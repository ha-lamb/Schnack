using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Schnack.Models;
using Schnack.Services.Internal;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace Schnack.Services;

/// <summary>
/// Lokale Transkription via Whisper.net (kein Cloud-Aufruf; Audio bleibt auf dem Gerät).
/// </summary>
public sealed class WhisperLocalTranscriptionService : ITranscriptionService, IWhisperWarmup
{
    // 16 kHz mono 16 bit — das Format, das NAudioRecordingService aufnimmt.
    private const int SampleRate = 16000;
    private const int BytesPerSecond = SampleRate * 2;
    private const int WavHeaderBytes = 44;

    private readonly ISettingsService _settings;
    private readonly IWhisperModelDownloadService _downloadService;
    private readonly ILogger<WhisperLocalTranscriptionService> _logger;

    // Lazily created; guarded by _factoryLock. Disposed in DisposeAsync.
    private WhisperFactory? _factory;
    private string? _loadedModelName;
    private bool _loadedUseGpu;
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
        var settings = _settings.Settings;
        var modelName = settings.WhisperModel;

        if (!_downloadService.IsModelDownloaded(modelName))
            throw new SchnackException(SchnackError.WhisperModelMissing,
                $"Whisper model {modelName} not downloaded");

        var factory = await GetOrCreateFactoryAsync(modelName, settings.WhisperUseGpu, ct);

        _logger.LogInformation("Whisper local transcription start, model: {Model}", modelName);

        await using var processor = BuildProcessor(factory, settings);
        await using var fileStream = File.OpenRead(wavFilePath);

        var audioSeconds = Math.Max(0, fileStream.Length - WavHeaderBytes) / (double)BytesPerSecond;
        var started = Stopwatch.GetTimestamp();

        var segments = new System.Text.StringBuilder();
        await foreach (var segment in processor.ProcessAsync(fileStream, ct))
            segments.Append(segment.Text);

        var elapsed = Stopwatch.GetElapsedTime(started);
        var text = segments.ToString().Trim();

        // Der Realtime-Faktor ist der einzige Wert, der Konfigurationen vergleichbar macht.
        // Die Audiodauer stammt aus der Dateigröße und hat keinen Inhaltsbezug.
        _logger.LogInformation(
            "Whisper transcription took {Ms} ms for {Seconds:F1} s audio (xRT {Factor:F2})",
            (long)elapsed.TotalMilliseconds, audioSeconds,
            audioSeconds > 0 ? elapsed.TotalSeconds / audioSeconds : 0);

        if (settings.DebugLogging)
            _logger.LogDebug("Whisper transcript length: {Length} chars", text.Length);

        return text;
    }

    private WhisperProcessor BuildProcessor(WhisperFactory factory, AppSettings settings)
    {
        // whisper.cpp skaliert bis etwa acht Threads; darüber bringt mehr Parallelität nichts
        // und kostet durch Synchronisation eher Zeit.
        var threads = Math.Max(1, Math.Min(8, Environment.ProcessorCount));

        // Greedy statt Beam Search: deutlich schneller. WithGreedySamplingStrategy gibt das
        // Interface zurueck, deshalb der Cast auf den konkreten Builder fuer WithBestOf.
        var builder = (GreedySamplingStrategyBuilder)factory.CreateBuilder()
            .WithLanguage(settings.DictationLanguage.ToIsoCode())
            .WithThreads(threads)
            .WithGreedySamplingStrategy();
        var processorBuilder = builder.WithBestOf(1).ParentBuilder;

        var vocabulary = VocabularyPrompt.Normalize(settings.Vocabulary);
        if (vocabulary.Length > 0)
        {
            var speechPrompt = VocabularyPrompt.ForSpeech(
                vocabulary, settings.DictationLanguage, out var droppedTerms);
            if (droppedTerms > 0)
                _logger.LogWarning("Vocabulary too long for the speech prompt, {Count} term(s) omitted", droppedTerms);

            // CarryInitialPrompt: ohne das wirkt die Begriffsliste nur im ersten 30-Sekunden-Fenster
            // einer Aufnahme. Bei Qualitätsproblemen ist diese Zeile der erste Kandidat zum Entfernen.
            processorBuilder = processorBuilder
                .WithPrompt(speechPrompt)
                .WithCarryInitialPrompt(true);
            _logger.LogInformation("Whisper vocabulary terms: {Terms}", vocabulary.Length);
        }

        return processorBuilder.Build();
    }

    /// <summary>
    /// Lädt das Modell und rechnet eine Sekunde Stille durch. Der zweite Teil ist der wichtigere:
    /// er erzwingt Graph-Allokation und — bei aktiver GPU — die Shader-Übersetzung, also genau den
    /// Aufwand, der sonst im ersten echten Diktat steckt.
    /// Scheitert bewusst still: ein fehlgeschlagenes Vorladen darf den Start nicht stören.
    /// </summary>
    public async Task WarmUpAsync(CancellationToken ct = default)
    {
        try
        {
            var settings = _settings.Settings;
            if (!_downloadService.IsModelDownloaded(settings.WhisperModel))
                return;

            var started = Stopwatch.GetTimestamp();
            var factory = await GetOrCreateFactoryAsync(settings.WhisperModel, settings.WhisperUseGpu, ct);

            await using var processor = BuildProcessor(factory, settings);
            await using var silence = new MemoryStream(CreateSilenceWav(seconds: 1));
            await foreach (var _ in processor.ProcessAsync(silence, ct)) { }

            _logger.LogInformation("Whisper warmup completed in {Ms} ms",
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            // Beenden während des Vorladens ist kein Fehler.
        }
        catch (ObjectDisposedException)
        {
            // Der Dienst wurde beim Herunterfahren bereits entsorgt.
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Whisper warmup skipped: {Type}", ex.GetType().Name);
        }
    }

    /// <summary>Minimaler PCM-WAV mit Stille — spart das Schreiben einer Datei fürs Vorladen.</summary>
    private static byte[] CreateSilenceWav(int seconds)
    {
        var dataSize = BytesPerSecond * seconds;
        var buffer = new byte[WavHeaderBytes + dataSize];

        using var writer = new BinaryWriter(new MemoryStream(buffer));
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);                       // Größe des fmt-Blocks
        writer.Write((short)1);                 // PCM
        writer.Write((short)1);                 // mono
        writer.Write(SampleRate);
        writer.Write(BytesPerSecond);
        writer.Write((short)2);                 // Blockausrichtung
        writer.Write((short)16);                // Bit pro Sample
        writer.Write("data"u8);
        writer.Write(dataSize);
        // Die Nutzdaten bleiben null — das ist die Stille.

        return buffer;
    }

    private async Task<WhisperFactory> GetOrCreateFactoryAsync(string modelName, bool useGpu, CancellationToken ct)
    {
        await _factoryLock.WaitAsync(ct);
        try
        {
            // useGpu gehört in die Cache-Bedingung: sonst wirkt ein Umschalten erst nach Neustart.
            if (_factory != null && _loadedModelName == modelName && _loadedUseGpu == useGpu)
                return _factory;

            _factory?.Dispose();
            _factory = null;

            var modelPath = _downloadService.GetModelPath(modelName);

            // Muss vor dem ersten FromPath stehen. Whisper.net probt die Liste der Reihe nach und
            // fällt selbsttätig zurück — ein eigener Fallback wäre schädliche Doppelung.
            RuntimeOptions.RuntimeLibraryOrder = useGpu
                ? [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx]
                : [RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx];

            var started = Stopwatch.GetTimestamp();
            _factory = useGpu
                ? WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = true })
                : WhisperFactory.FromPath(modelPath);

            _logger.LogInformation(
                "Whisper model {Model} loaded in {Ms} ms (gpu requested: {Gpu}, runtime in use: {Runtime})",
                modelName, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, useGpu,
                RuntimeOptions.LoadedLibrary?.ToString() ?? "unknown");

            _loadedModelName = modelName;
            _loadedUseGpu = useGpu;
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
