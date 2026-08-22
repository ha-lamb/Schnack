using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Schnack.Models;
using Schnack.Models.OpenAi;
using Schnack.Services.Internal;

namespace Schnack.Services;

/// <summary>
/// Nachbearbeitung per OpenAI <c>v1/chat/completions</c> (OpenAI-Backend-Stack).
/// </summary>
public sealed class OpenAiChatService : IPostProcessingService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecretService _secretService;
    private readonly ISettingsService _settings;
    private readonly ILogger<OpenAiChatService> _logger;

    public OpenAiChatService(
        IHttpClientFactory httpClientFactory,
        ISecretService secretService,
        ISettingsService settings,
        ILogger<OpenAiChatService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _secretService = secretService;
        _settings = settings;
        _logger = logger;
    }

    public async Task<ClaudeProcessResult> ProcessAsync(string transcript, DictationMode mode, CancellationToken ct = default)
    {
        var apiKey = _secretService.GetOpenAiApiKey()
            ?? throw new SchnackException(SchnackError.MissingOpenAiKey, "OPENAI_API_KEY not set and no stored key");

        var model = _settings.Settings.OpenAiChatModel;
        var prompt = DictationPrompts.Build(_settings.Settings.DictationLanguage, mode, transcript, _settings.Settings.Vocabulary);

        var requestBody = new ChatRequest
        {
            Model = model,
            // Regeln als System-Nachricht, Transkript als Nutzernachricht — so kann der diktierte
            // Text nicht als Anweisung gelesen werden.
            Messages =
            [
                new ChatMessage { Role = "system", Content = prompt.System },
                new ChatMessage { Role = "user", Content = prompt.UserContent }
            ],
            MaxTokens = _settings.Settings.OpenAiChatMaxTokens,
            // 0 statt 0,1: Glätten ist eine analytische Aufgabe, keine kreative.
            Temperature = 0f
        };
        var requestJson = JsonSerializer.Serialize(requestBody, JsonOptions);

        _logger.LogInformation("OpenAI Chat request, model: {Model}", model);

        using var client = _httpClientFactory.CreateClient("OpenAi");

        using var response = await HttpRetry.SendAsync(
            async innerCt =>
            {
                using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                using var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions") { Content = content };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                return await client.SendAsync(req, innerCt);
            },
            _logger, "OpenAI Chat", ct);

        _logger.LogInformation("OpenAI Chat response status: {Status}", (int)response.StatusCode);

        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            throw new SchnackException(SchnackError.ApiKeyInvalid, "OpenAI rejected the API key");

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new SchnackException(SchnackError.RateLimit, "OpenAI rate limit reached");

        if (!response.IsSuccessStatusCode)
        {
            await ApiErrorLog.LogSanitizedAsync(response, _logger, "OpenAI", ct);
            response.EnsureSuccessStatusCode();
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var chatResponse = JsonSerializer.Deserialize<ChatResponse>(json, JsonOptions)!;
        var choice = chatResponse.Choices[0];
        var truncated = string.Equals(choice.FinishReason, "length", StringComparison.Ordinal);
        if (truncated)
            _logger.LogWarning("OpenAI Chat finish_reason=length; response may be truncated");

        return new ClaudeProcessResult(choice.Message.Content.Trim(), truncated);
    }
}
