using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Moq;
using Schnack.Models;
using Schnack.Services;

namespace Schnack.Tests;

/// <summary>
/// Prüft, dass die Begriffsliste im Nachbearbeitungs-Prompt landet. Ihre zweite Wirkung —
/// als Vorab-Kontext der lokalen Spracherkennung — deckt VocabularyPromptTests ab; dort ist
/// sie ohne die native Whisper-Abhängigkeit prüfbar.
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
