using Microsoft.Extensions.Logging;
using Schnack.Models;
using Whisper.net;
using Whisper.net.Ggml;

namespace Schnack.Services;

/// <summary>
/// Lokale Transkription via Whisper.net (kein Cloud-Aufruf; Audio bleibt auf dem Gerät).
/// </summary>
public sealed class WhisperLocalTranscriptionService : ITranscriptionService
{
    private readonly ISettingsService _settings;
    private readonly IWhisperModelDownloadService _downloadService;
    private readonly ILogger<WhisperLocalTranscriptionService> _logger;

    // Lazily created; guarded by _factoryLock. Disposed in DisposeAsync.
    private WhisperFactory? _factory;
    private string? _loadedModelName;
    private readonly SemaphoreSlim _factoryLock = new(1, 1);

    public WhisperLocalTranscriptionService(
        ISettingsService settings,
        IWhisperModelDownloadService downloadService,
        ILogger<WhisperLocalTranscriptionService> logger)
    {
        _settings = settings;
        _downloadService = downloadService;
        _logger = logger;
    }

    public async Task<string> TranscribeAsync(string wavFilePath, CancellationToken ct = default)
    {
        var modelName = _settings.Settings.WhisperModel;
        var useGpu = _settings.Settings.WhisperUseGpu;

        if (!_downloadService.IsModelDownloaded(modelName))
            throw new InvalidOperationException(
                $"Whisper-Modell '{modelName}' ist nicht heruntergeladen. Bitte in den Einstellungen herunterladen.");

        var factory = await GetOrCreateFactoryAsync(modelName, useGpu, ct);

        _logger.LogInformation("Whisper local transcription start, model: {Model}", modelName);

        await using var processor = factory.CreateBuilder()
            .WithLanguage("de")
            .Build();

        await using var fileStream = File.OpenRead(wavFilePath);
        var segments = new System.Text.StringBuilder();

        await foreach (var segment in processor.ProcessAsync(fileStream, ct))
            segments.Append(segment.Text);

        var text = segments.ToString().Trim();
        if (_settings.Settings.DebugLogging)
            _logger.LogDebug("Whisper transcript length: {Length} chars", text.Length);

        return text;
    }

    private async Task<WhisperFactory> GetOrCreateFactoryAsync(string modelName, bool useGpu, CancellationToken ct)
    {
        await _factoryLock.WaitAsync(ct);
        try
        {
            if (_factory != null && _loadedModelName == modelName)
                return _factory;

            _factory?.Dispose();
            _factory = null;

            var modelPath = _downloadService.GetModelPath(modelName);
            _logger.LogInformation("Loading Whisper model from {Path}", modelPath);

            _factory = useGpu
                ? WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = true })
                : WhisperFactory.FromPath(modelPath);

            _loadedModelName = modelName;
            return _factory;
        }
        finally
        {
            _factoryLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _factoryLock.WaitAsync();
        try
        {
            _factory?.Dispose();
            _factory = null;
        }
        finally
        {
            _factoryLock.Release();
            _factoryLock.Dispose();
        }
    }
}
