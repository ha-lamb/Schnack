using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Schnack.Models;
using Schnack.Models.Claude;
using Schnack.Services.Internal;

namespace Schnack.Services;

public sealed class ClaudeService : IPostProcessingService
{
    // Anthropic erwartet snake_case; die DTO-Attribute ([JsonPropertyName]) sind maßgeblich,
    // die Policy hier nur als konsistentes Fallback (identisch zu OpenAiChatService).
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecretService _secretService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<ClaudeService> _logger;

    public ClaudeService(
        IHttpClientFactory httpClientFactory,
        ISecretService secretService,
        ISettingsService settingsService,
        ILogger<ClaudeService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _secretService = secretService;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<ClaudeProcessResult> ProcessAsync(string transcript, DictationMode mode, CancellationToken ct = default)
    {
        var apiKey = _secretService.GetApiKey()
            ?? throw new SchnackException(SchnackError.MissingAnthropicKey, "ANTHROPIC_API_KEY not set");

        var settings = _settingsService.Settings;
        var prompt = DictationPrompts.Build(settings.DictationLanguage, mode, transcript, settings.Vocabulary);

        using var client = _httpClientFactory.CreateClient("Claude");
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        _logger.LogInformation("Sending request to Claude API, model: {Model}", settings.ClaudeModel);

        using var response = await SendWithTemperatureFallbackAsync(client, settings, prompt, ct);

        _logger.LogInformation("Claude API response status: {StatusCode}", (int)response.StatusCode);

        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            throw new SchnackException(SchnackError.ApiKeyInvalid, "Anthropic rejected the API key");

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new SchnackException(SchnackError.RateLimit, "Anthropic rate limit reached");

        if (!response.IsSuccessStatusCode)
        {
            await ApiErrorLog.LogSanitizedAsync(response, _logger, "Anthropic", ct);
            response.EnsureSuccessStatusCode();
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var parsed = JsonSerializer.Deserialize<MessagesResponse>(responseJson, JsonOptions)
            ?? throw new SchnackException(SchnackError.EmptyApiResponse, "Anthropic returned an empty response");

        if (parsed.StopReason == "max_tokens")
            _logger.LogWarning("Claude stop_reason=max_tokens; response may be truncated");

        var text = string.Concat(parsed.Content
            .Where(b => b.Type == "text" && b.Text != null)
            .Select(b => b.Text!));

        var truncated = string.Equals(parsed.StopReason, "max_tokens", StringComparison.Ordinal);
        return new ClaudeProcessResult(text, truncated);
    }

    /// <summary>
    /// Schickt die Anfrage mit <c>temperature: 0</c> — Nachbearbeitung ist eine analytische
    /// Aufgabe, und ohne Angabe läge der Wert bei 1,0.
    /// Opus 4.7 und neuer haben den Parameter entfernt und antworten mit HTTP 400. Weil das
    /// Modell ein freies Textfeld in den Einstellungen ist, wird der Aufruf dann einmal ohne
    /// Temperatur wiederholt, statt jedes Diktat scheitern zu lassen.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithTemperatureFallbackAsync(
        HttpClient client, AppSettings settings, DictationPrompt prompt, CancellationToken ct)
    {
        var response = await SendOnceAsync(client, settings, prompt, temperature: 0, ct);

        if (response.StatusCode != HttpStatusCode.BadRequest)
            return response;

        // Nur den Hinweis auf das Feld suchen — der Fehlertext selbst wird nie geloggt.
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!body.Contains("temperature", StringComparison.OrdinalIgnoreCase))
            return response;

        response.Dispose();
        _logger.LogWarning(
            "Model {Model} rejected the temperature parameter, retrying without it", settings.ClaudeModel);
        return await SendOnceAsync(client, settings, prompt, temperature: null, ct);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpClient client, AppSettings settings, DictationPrompt prompt, double? temperature, CancellationToken ct)
    {
        var request = new MessagesRequest
        {
            Model = settings.ClaudeModel,
            MaxTokens = settings.ClaudeMaxTokens,
            System = prompt.System,
            Temperature = temperature,
            Messages = [new MessageItem { Role = "user", Content = prompt.UserContent }]
        };
        var requestJson = JsonSerializer.Serialize(request, JsonOptions);

        return await HttpRetry.SendAsync(
            async innerCt =>
            {
                using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                return await client.PostAsync("v1/messages", content, innerCt);
            },
            _logger, "Claude", ct);
    }

}
