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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
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
            ?? throw new InvalidOperationException("ANTHROPIC_API_KEY ist nicht gesetzt");

        var prompt = BuildPrompt(transcript, mode);
        var settings = _settingsService.Settings;

        var request = new MessagesRequest
        {
            Model = settings.ClaudeModel,
            MaxTokens = settings.ClaudeMaxTokens,
            Messages = [new MessageItem { Role = "user", Content = prompt }]
        };
        var requestJson = JsonSerializer.Serialize(request, JsonOptions);

        using var client = _httpClientFactory.CreateClient("Claude");
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        _logger.LogInformation("Sending request to Claude API, model: {Model}", settings.ClaudeModel);

        using var response = await HttpRetry.SendAsync(
            async innerCt =>
            {
                using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                return await client.PostAsync("v1/messages", content, innerCt);
            },
            _logger, "Claude", ct);

        _logger.LogInformation("Claude API response status: {StatusCode}", (int)response.StatusCode);

        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            throw new HttpRequestException("API-Key ungültig oder abgelaufen", null, response.StatusCode);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("Rate Limit erreicht", null, response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            await LogSanitizedApiErrorAsync(response, ct);
            response.EnsureSuccessStatusCode();
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var parsed = JsonSerializer.Deserialize<MessagesResponse>(responseJson, JsonOptions)
            ?? throw new InvalidOperationException("Leere Antwort von Claude");

        if (parsed.StopReason == "max_tokens")
            _logger.LogWarning("Claude stop_reason=max_tokens; Antwort möglicherweise abgeschnitten");

        var text = string.Concat(parsed.Content
            .Where(b => b.Type == "text" && b.Text != null)
            .Select(b => b.Text!));

        var truncated = string.Equals(parsed.StopReason, "max_tokens", StringComparison.Ordinal);
        return new ClaudeProcessResult(text, truncated);
    }

    private static string BuildPrompt(string transcript, DictationMode mode) =>
        DictationPrompts.Build(
            mode == DictationMode.DeCorrect ? DictationPrompts.DeCorrect : DictationPrompts.DeToEn,
            transcript);

    private async Task LogSanitizedApiErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var err = doc.RootElement.GetProperty("error");
            var type = err.TryGetProperty("type", out var t) ? t.GetString() : null;
            var code = err.TryGetProperty("code", out var c) ? c.GetString() : null;
            _logger.LogWarning("Anthropic API error type={Type} code={Code} status={Status}", type, code, (int)response.StatusCode);
        }
        catch
        {
            _logger.LogWarning("Anthropic API error status={Status}", (int)response.StatusCode);
        }
    }
}
