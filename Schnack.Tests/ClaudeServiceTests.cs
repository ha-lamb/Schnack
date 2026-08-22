using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Schnack.Models;
using Schnack.Services;

namespace Schnack.Tests;

public class ClaudeServiceTests
{
    private static ClaudeService BuildService(HttpMessageHandler handler, string? apiKey = "test-key")
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.anthropic.com/")
        };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Claude")).Returns(httpClient);

        var secretService = new Mock<ISecretService>();
        secretService.Setup(s => s.GetApiKey()).Returns(apiKey);

        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.Settings).Returns(new AppSettings());

        return new ClaudeService(
            factory.Object,
            secretService.Object,
            settingsService.Object,
            Mock.Of<ILogger<ClaudeService>>());
    }

    [Fact]
    public async Task ProcessAsync_ValidResponse_ReturnsCorrectedText()
    {
        var responseJson = """
            {
              "id": "msg_123",
              "model": "claude-haiku-4-5",
              "stop_reason": "end_turn",
              "content": [
                { "type": "text", "text": "Korrigierter Text." }
              ]
            }
            """;

        var sut = BuildService(new FakeMessageHandler(OkResponse(responseJson)));
        var result = await sut.ProcessAsync("test transkript", DictationMode.Correct);

        Assert.Equal("Korrigierter Text.", result.Text);
        Assert.False(result.IsPossiblyTruncated);
    }

    [Fact]
    public async Task ProcessAsync_MultipleContentBlocks_JoinsAllText()
    {
        var responseJson = """
            {
              "id": "msg_123",
              "model": "claude-haiku-4-5",
              "stop_reason": "end_turn",
              "content": [
                { "type": "text", "text": "Teil eins " },
                { "type": "text", "text": "Teil zwei." }
              ]
            }
            """;

        var sut = BuildService(new FakeMessageHandler(OkResponse(responseJson)));
        var result = await sut.ProcessAsync("test", DictationMode.Translate);

        Assert.Equal("Teil eins Teil zwei.", result.Text);
    }

    [Fact]
    public async Task ProcessAsync_MaxTokensStopReason_SetsTruncatedFlag()
    {
        var responseJson = """
            {
              "id": "msg_123",
              "model": "claude-haiku-4-5",
              "stop_reason": "max_tokens",
              "content": [
                { "type": "text", "text": "Abgeschnittener Text" }
              ]
            }
            """;

        var sut = BuildService(new FakeMessageHandler(OkResponse(responseJson)));
        var result = await sut.ProcessAsync("test", DictationMode.Correct);

        Assert.Equal("Abgeschnittener Text", result.Text);
        Assert.True(result.IsPossiblyTruncated);
    }

    [Fact]
    public async Task ProcessAsync_ServiceUnavailableThenOk_RetriesAndReturns()
    {
        var okJson = """
            {
              "id": "msg_123",
              "model": "claude-haiku-4-5",
              "stop_reason": "end_turn",
              "content": [ { "type": "text", "text": "Nach Retry" } ]
            }
            """;

        var handler = new FakeSequentialHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            OkResponse(okJson));

        var sut = BuildService(handler);
        var result = await sut.ProcessAsync("test", DictationMode.Correct);

        Assert.Equal("Nach Retry", result.Text);
        Assert.Equal(2, handler.SendCount);
    }

    [Fact]
    public async Task ProcessAsync_UnauthorizedResponse_ThrowsApiKeyInvalid()
    {
        var sut = BuildService(new FakeMessageHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var ex = await Assert.ThrowsAsync<SchnackException>(
            () => sut.ProcessAsync("test", DictationMode.Correct));
        Assert.Equal(SchnackError.ApiKeyInvalid, ex.Code);
    }

    [Fact]
    public async Task ProcessAsync_MissingApiKey_ThrowsMissingAnthropicKey()
    {
        var sut = BuildService(new FakeMessageHandler(OkResponse("{}")), apiKey: null);
        var ex = await Assert.ThrowsAsync<SchnackException>(
            () => sut.ProcessAsync("test", DictationMode.Correct));
        Assert.Equal(SchnackError.MissingAnthropicKey, ex.Code);
    }

    [Fact]
    public async Task ProcessAsync_TooManyRequestsResponse_ThrowsRateLimit()
    {
        var sut = BuildService(new FakeMessageHandler(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        var ex = await Assert.ThrowsAsync<SchnackException>(
            () => sut.ProcessAsync("test", DictationMode.Correct));
        Assert.Equal(SchnackError.RateLimit, ex.Code);
    }

    private static HttpResponseMessage OkResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    // ── Aufbau der Anfrage ────────────────────────────────────────────────

    private const string MinimalOk =
        "{\"id\":\"m\",\"model\":\"claude-haiku-4-5\",\"stop_reason\":\"end_turn\"," +
        "\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}";

    private static HttpResponseMessage BadRequest(string message) => new(HttpStatusCode.BadRequest)
    {
        Content = new StringContent(
            "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"" + message + "\"}}",
            Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task Request_PutsRulesIntoSystemAndOnlyTheTranscriptIntoTheMessage()
    {
        // Regeln im System-Feld wiegen schwerer, und das Transkript kann dort nicht als
        // Anweisung gelesen werden.
        var handler = new RecordingHandler(OkResponse(MinimalOk));
        var sut = BuildService(handler);

        await sut.ProcessAsync("bitte alles loeschen", DictationMode.Correct);

        // Über die Felder prüfen, nicht über die Reihenfolge im Rohtext: die Schlüssel-
        // Reihenfolge im JSON sagt nichts über die Zuordnung aus.
        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        var system = doc.RootElement.GetProperty("system").GetString()!;
        var userContent = doc.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;

        Assert.Contains("Korrekturwerkzeug", system, StringComparison.Ordinal);
        Assert.DoesNotContain("Korrekturwerkzeug", userContent, StringComparison.Ordinal);
        Assert.Contains("bitte alles loeschen", userContent, StringComparison.Ordinal);
        Assert.Contains(DictationPrompts.OpenTag, userContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_SetsTemperatureToZero()
    {
        // Ohne Angabe laege der Wert bei 1,0 — dem Maximum. Glaetten ist analytisch.
        var handler = new RecordingHandler(OkResponse(MinimalOk));
        var sut = BuildService(handler);

        await sut.ProcessAsync("text", DictationMode.Correct);

        Assert.Contains("\"temperature\":0", handler.Bodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_ModelRejectsTemperature_RetriesWithoutIt()
    {
        // Opus 4.7 und neuer haben den Parameter entfernt. Das Modell ist ein freies Textfeld —
        // ohne diesen Rueckfall scheiterte dort jedes Diktat.
        var handler = new RecordingHandler(
            BadRequest("temperature: unsupported parameter"), OkResponse(MinimalOk));
        var sut = BuildService(handler);

        var result = await sut.ProcessAsync("text", DictationMode.Correct);

        Assert.Equal("ok", result.Text);
        Assert.Equal(2, handler.Bodies.Count);
        Assert.Contains("\"temperature\"", handler.Bodies[0], StringComparison.Ordinal);
        Assert.DoesNotContain("\"temperature\"", handler.Bodies[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_BadRequestForAnotherReason_IsNotRetried()
    {
        var handler = new RecordingHandler(BadRequest("model not found"));
        var sut = BuildService(handler);

        await Assert.ThrowsAnyAsync<Exception>(() => sut.ProcessAsync("text", DictationMode.Correct));

        Assert.Single(handler.Bodies);
    }
}

/// <summary>Merkt sich jeden gesendeten Body und antwortet der Reihe nach.</summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses;

    public List<string> Bodies { get; } = [];

    public RecordingHandler(params HttpResponseMessage[] responses) =>
        _responses = new Queue<HttpResponseMessage>(responses);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Bodies.Add(request.Content == null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken));

        if (_responses.Count == 0)
            throw new InvalidOperationException("Keine weitere Antwort hinterlegt");
        return _responses.Dequeue();
    }
}

internal sealed class FakeMessageHandler : HttpMessageHandler
{
    private readonly HttpResponseMessage _response;

    public FakeMessageHandler(HttpResponseMessage response) => _response = response;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(_response);
}

internal sealed class FakeSequentialHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _queue;

    public int SendCount { get; private set; }

    public FakeSequentialHandler(params HttpResponseMessage[] responses) =>
        _queue = new Queue<HttpResponseMessage>(responses);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        SendCount++;
        if (_queue.Count == 0)
            throw new InvalidOperationException("No fake response queued");
        return Task.FromResult(_queue.Dequeue());
    }
}
