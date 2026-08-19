using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Moq;
using Schnack.Models;
using Schnack.Services;

namespace Schnack.Tests;

/// <summary>
/// Prüft, dass Diktiersprache und Modus den richtigen Prompt bzw. Sprachparameter erzeugen —
/// über die echten Services, nicht über die interne Prompt-Klasse.
/// </summary>
public class DictationLanguageTests
{
    private static (OpenAiChatService Service, CapturingHandler Handler) BuildChatService(
        AppLanguage dictationLanguage)
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}""",
                Encoding.UTF8, "application/json")
        });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("OpenAi"))
            .Returns(new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/") });

        var secrets = new Mock<ISecretService>();
        secrets.Setup(s => s.GetOpenAiApiKey()).Returns("test-key");

        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Settings).Returns(new AppSettings { DictationLanguage = dictationLanguage });

        return (new OpenAiChatService(factory.Object, secrets.Object, settings.Object,
            Mock.Of<ILogger<OpenAiChatService>>()), handler);
    }

    [Fact]
    public async Task GermanCorrect_UsesGermanCleanupPrompt()
    {
        var (sut, handler) = BuildChatService(AppLanguage.De);

        await sut.ProcessAsync("hallo", DictationMode.Correct);

        Assert.Contains("Korrigiere den folgenden diktierten deutschen Text", handler.Body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnglishCorrect_UsesEnglishCleanupPrompt()
    {
        var (sut, handler) = BuildChatService(AppLanguage.En);

        await sut.ProcessAsync("hello", DictationMode.Correct);

        Assert.Contains("Correct the following dictated English text", handler.Body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GermanTranslate_TargetsEnglish()
    {
        var (sut, handler) = BuildChatService(AppLanguage.De);

        await sut.ProcessAsync("hallo", DictationMode.Translate);

        // Umlautfreie Teilkette: System.Text.Json escapt Nicht-ASCII im Body
        Assert.Contains("klares Englisch", handler.Body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnglishTranslate_TargetsGerman()
    {
        var (sut, handler) = BuildChatService(AppLanguage.En);

        await sut.ProcessAsync("hello", DictationMode.Translate);

        Assert.Contains("Translate it into natural, clear German", handler.Body!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AppLanguage.De, "de")]
    [InlineData(AppLanguage.En, "en")]
    public async Task Transcription_SendsConfiguredLanguage(AppLanguage language, string expectedCode)
    {
        var wavPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(wavPath, new byte[44]);

            var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("text", Encoding.UTF8, "text/plain")
            });

            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient("OpenAi"))
                .Returns(new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/") });

            var secrets = new Mock<ISecretService>();
            secrets.Setup(s => s.GetOpenAiApiKey()).Returns("test-key");

            var settings = new Mock<ISettingsService>();
            settings.Setup(s => s.Settings).Returns(new AppSettings { DictationLanguage = language });

            var sut = new OpenAiTranscriptionService(factory.Object, secrets.Object, settings.Object,
                Mock.Of<ILogger<OpenAiTranscriptionService>>());

            await sut.TranscribeAsync(wavPath);

            // Multipart-Body enthält den language-Part
            Assert.Contains($"name=language", handler.Body!, StringComparison.Ordinal);
            Assert.Contains(expectedCode, handler.Body!, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(wavPath)) File.Delete(wavPath);
        }
    }
}

internal sealed class CapturingHandler : HttpMessageHandler
{
    private readonly HttpResponseMessage _response;

    public string? Body { get; private set; }

    public CapturingHandler(HttpResponseMessage response) => _response = response;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content != null)
            Body = await request.Content.ReadAsStringAsync(cancellationToken);
        return _response;
    }
}
