using System.Net.Http;
using Microsoft.Extensions.Logging;
using Schnack.Services;

namespace Schnack.Services;

public interface IWhisperModelDownloadService
{
    /// <summary>Gibt true zurück wenn das Modell bereits lokal vorhanden ist.</summary>
    bool IsModelDownloaded(string modelName);

    /// <summary>Lädt das Whisper-Modell herunter; meldet Fortschritt via <paramref name="progress"/>.</summary>
    Task DownloadModelAsync(string modelName, IProgress<double>? progress = null, CancellationToken ct = default);

    string GetModelPath(string modelName);
}

public sealed class WhisperModelDownloadService : IWhisperModelDownloadService
{
    // HuggingFace-Pfad für ggml-Modelle (Whisper.net-Format)
    private const string HuggingFaceBaseUrl =
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{0}.bin";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WhisperModelDownloadService> _logger;

    private string ModelsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Schnack", "models");

    public WhisperModelDownloadService(
        IHttpClientFactory httpClientFactory,
        ILogger<WhisperModelDownloadService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string GetModelPath(string modelName) =>
        Path.Combine(ModelsDir, $"ggml-{modelName}.bin");

    public bool IsModelDownloaded(string modelName) =>
        File.Exists(GetModelPath(modelName));

    public async Task DownloadModelAsync(string modelName, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var destPath = GetModelPath(modelName);
        if (File.Exists(destPath))
        {
            _logger.LogInformation("Whisper model {Model} already present, skipping download", modelName);
            return;
        }

        Directory.CreateDirectory(ModelsDir);
        var url = string.Format(HuggingFaceBaseUrl, modelName);
        _logger.LogInformation("Downloading Whisper model {Model} from {Url}", modelName, url);

        using var client = _httpClientFactory.CreateClient();
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        var tmpPath = destPath + ".tmp";

        try
        {
            await using var src = await response.Content.ReadAsStreamAsync(ct);
            await using var dest = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;
                if (totalBytes > 0)
                    progress?.Report((double)downloaded / totalBytes);
            }
        }
        catch
        {
            if (File.Exists(tmpPath))
                File.Delete(tmpPath);
            throw;
        }

        File.Move(tmpPath, destPath, overwrite: true);
        _logger.LogInformation("Whisper model {Model} downloaded to {Path}", modelName, destPath);
    }
}
