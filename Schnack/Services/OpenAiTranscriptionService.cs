using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Schnack.Models.OpenAi;
using Schnack.Services.Internal;

namespace Schnack.Services;

/// <summary>
/// Transkription per OpenAI <c>v1/audio/transcriptions</c> (Cloud, kein lokales Modell).
/// </summary>
public sealed class OpenAiTranscriptionService : ITranscriptionService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecretService _secretService;
    private readonly ISettingsService _settings;
    private readonly ILogger<OpenAiTranscriptionService> _logger;

    public OpenAiTranscriptionService(
        IHttpClientFactory httpClientFactory,
        ISecretService secretService,
        ISettingsService settings,
        ILogger<OpenAiTranscriptionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _secretService = secretService;
        _settings = settings;
        _logger = logger;
    }

    public async Task<string> TranscribeAsync(string wavFilePath, CancellationToken ct = default)
    {
        var apiKey = _secretService.GetOpenAiApiKey()
            ?? throw new InvalidOperationException("OPENAI_API_KEY ist nicht gesetzt und kein OpenAI-Key in den Einstellungen gespeichert.");

        var model = _settings.Settings.OpenAiTranscriptionModel;
        _logger.LogInformation("OpenAI transcription request, model: {Model}", model);

        using var client = _httpClientFactory.CreateClient("OpenAi");

        using var response = await HttpRetry.SendAsync(
            async innerCt =>
            {
                await using var fileStream = File.OpenRead(wavFilePath);
                using var form = new MultipartFormDataContent();
                var fileContent = new StreamContent(fileStream);
                form.Add(fileContent, "file", Path.GetFileName(wavFilePath));
                form.Add(new StringContent(model, Encoding.UTF8), "model");
                form.Add(new StringContent("de", Encoding.UTF8), "language");
                form.Add(new StringContent("json", Encoding.UTF8), "response_format");

                using var req = new HttpRequestMessage(HttpMethod.Post, "v1/audio/transcriptions") { Content = form };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                return await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, innerCt);
            },
            _logger, "OpenAI STT", ct);

        _logger.LogInformation("OpenAI transcription response status: {Status}", (int)response.StatusCode);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new HttpRequestException("OpenAI: API-Key ungültig", null, response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            await LogSanitizedApiErrorAsync(response, ct);
            response.EnsureSuccessStatusCode();
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var parsed = JsonSerializer.Deserialize<TranscriptionResponse>(json);
        var text = parsed?.Text?.Trim() ?? string.Empty;
        if (_settings.Settings.DebugLogging)
            _logger.LogDebug("OpenAI transcript length: {Length} chars", text.Length);
        return text;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask; // keine Ressourcen freizugeben

    private async Task LogSanitizedApiErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var err = doc.RootElement.GetProperty("error");
            var type = err.TryGetProperty("type", out var t) ? t.GetString() : null;
            var code = err.TryGetProperty("code", out var c) ? c.GetString() : null;
            _logger.LogWarning("OpenAI API error type={Type} code={Code} status={Status}", type, code, (int)response.StatusCode);
        }
        catch
        {
            _logger.LogWarning("OpenAI API error status={Status}", (int)response.StatusCode);
        }
    }
}
