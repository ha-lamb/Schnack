using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Moq;
using Schnack.Models;
using Schnack.Services;

namespace Schnack.Tests;

public class OpenAiChatServiceTests
{
    private static OpenAiChatService BuildService(
        HttpMessageHandler handler,
        string? apiKey = "test-key")
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com/")
        };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("OpenAi")).Returns(httpClient);

        var secretService = new Mock<ISecretService>();
        secretService.Setup(s => s.GetOpenAiApiKey()).Returns(apiKey);

        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.Settings).Returns(new AppSettings());

        return new OpenAiChatService(
            factory.Object,
            secretService.Object,
            settingsService.Object,
            Mock.Of<ILogger<OpenAiChatService>>());
    }

    [Fact]
    public async Task ProcessAsync_ValidResponse_ReturnsCorrectedText()
    {
        const string responseJson = """
            {
              "choices": [
                {
                  "message": { "role": "assistant", "content": "Korrigierter Text." },
                  "finish_reason": "stop"
                }
              ]
            }
            """;

        var sut = BuildService(new FakeMessageHandler(OkResponse(responseJson)));
        var result = await sut.ProcessAsync("test transkript", DictationMode.Correct);

        Assert.Equal("Korrigierter Text.", result.Text);
        Assert.False(result.IsPossiblyTruncated);
    }

    [Fact]
    public async Task ProcessAsync_FinishReasonLength_SetsTruncatedFlag()
    {
        const string responseJson = """
            {
              "choices": [
                {
                  "message": { "role": "assistant", "content": "Abgeschnittener Text" },
                  "finish_reason": "length"
                }
              ]
            }
            """;

        var sut = BuildService(new FakeMessageHandler(OkResponse(responseJson)));
        var result = await sut.ProcessAsync("test", DictationMode.Correct);

        Assert.Equal("Abgeschnittener Text", result.Text);
        Assert.True(result.IsPossiblyTruncated);
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
    public async Task ProcessAsync_MissingApiKey_ThrowsMissingOpenAiKey()
    {
        var sut = BuildService(new FakeMessageHandler(OkResponse("{}")), apiKey: null);

        var ex = await Assert.ThrowsAsync<SchnackException>(
            () => sut.ProcessAsync("test", DictationMode.Correct));
        Assert.Equal(SchnackError.MissingOpenAiKey, ex.Code);
    }

    [Fact]
    public async Task ProcessAsync_TooManyRequestsResponse_ThrowsRateLimit()
    {
        var sut = BuildService(new FakeMessageHandler(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));

        var ex = await Assert.ThrowsAsync<SchnackException>(
            () => sut.ProcessAsync("test", DictationMode.Correct));
        Assert.Equal(SchnackError.RateLimit, ex.Code);
    }

    [Fact]
    public async Task ProcessAsync_ServiceUnavailableThenOk_RetriesAndReturns()
    {
        const string responseJson = """
            {
              "choices": [
                {
                  "message": { "role": "assistant", "content": "Nach Retry" },
                  "finish_reason": "stop"
                }
              ]
            }
            """;

        var handler = new FakeSequentialHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            OkResponse(responseJson));

        var sut = BuildService(handler);
        var result = await sut.ProcessAsync("test", DictationMode.Translate);

        Assert.Equal("Nach Retry", result.Text);
        Assert.Equal(2, handler.SendCount);
    }

    private static HttpResponseMessage OkResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}
