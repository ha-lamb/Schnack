using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Moq;
using Schnack.Models;
using Schnack.Services;

namespace Schnack.Tests;

/// <summary>
/// Prüft, dass die Begriffsliste tatsächlich in beiden ausgehenden Anfragen landet —
/// im Erkennungs-Prompt und im Nachbearbeitungs-Prompt.
/// </summary>
public class VocabularyIntegrationTests
{
    private static readonly string[] Terms = ["Kubernetes", "Krzysztof"];

    private static (OpenAiChatService Service, CapturingHandler Handler) BuildChatService(string[] vocabulary)
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
        settings.Setup(s => s.Settings).Returns(new AppSettings { Vocabulary = vocabulary });

        return (new OpenAiChatService(factory.Object, secrets.Object, settings.Object,
            Mock.Of<ILogger<OpenAiChatService>>()), handler);
    }

    private static (OpenAiTranscriptionService Service, CapturingHandler Handler) BuildSttService(string[] vocabulary)
    {
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
        settings.Setup(s => s.Settings).Returns(new AppSettings { Vocabulary = vocabulary });

        return (new OpenAiTranscriptionService(factory.Object, secrets.Object, settings.Object,
            Mock.Of<ILogger<OpenAiTranscriptionService>>()), handler);
    }

    // ── Spracherkennung ─────────────────────────────────────────────

    [Fact]
    public async Task Transcription_SendsVocabularyInPrompt()
    {
        var wav = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(wav, new byte[44]);
            var (sut, handler) = BuildSttService(Terms);

            await sut.TranscribeAsync(wav);

            Assert.Contains("Kubernetes", handler.Body!, StringComparison.Ordinal);
            Assert.Contains("Krzysztof", handler.Body!, StringComparison.Ordinal);
            Assert.Contains("Diktat auf Deutsch.", handler.Body!, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(wav)) File.Delete(wav);
        }
    }

    [Fact]
    public async Task Transcription_EmptyVocabulary_KeepsPlainLanguageHint()
    {
        var wav = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(wav, new byte[44]);
            var (sut, handler) = BuildSttService([]);

            await sut.TranscribeAsync(wav);

            Assert.Contains("Diktat auf Deutsch.", handler.Body!, StringComparison.Ordinal);
            Assert.DoesNotContain("Eigennamen", handler.Body!, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(wav)) File.Delete(wav);
        }
    }

    // ── Nachbearbeitung ─────────────────────────────────────────────

    [Fact]
    public async Task PostProcessing_SendsVocabularyBlock()
    {
        var (sut, handler) = BuildChatService(Terms);

        await sut.ProcessAsync("test", DictationMode.Correct);

        Assert.Contains("Kubernetes", handler.Body!, StringComparison.Ordinal);
        Assert.Contains("Krzysztof", handler.Body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostProcessing_EmptyVocabulary_LeavesNoPlaceholderOrBlankSection()
    {
        var (sut, handler) = BuildChatService([]);

        await sut.ProcessAsync("test", DictationMode.Correct);

        Assert.DoesNotContain("{{VOCABULARY}}", handler.Body!, StringComparison.Ordinal);
        Assert.DoesNotContain("Eigennamen und Fachbegriffe", handler.Body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostProcessing_TranslationMode_AlsoCarriesVocabulary()
    {
        var (sut, handler) = BuildChatService(Terms);

        await sut.ProcessAsync("test", DictationMode.Translate);

        Assert.Contains("Kubernetes", handler.Body!, StringComparison.Ordinal);
    }
}
