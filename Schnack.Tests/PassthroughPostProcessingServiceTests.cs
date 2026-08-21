using Microsoft.Extensions.Logging;
using Moq;
using Schnack.Models;
using Schnack.Services;

namespace Schnack.Tests;

/// <summary>
/// Der Passthrough muss den Text wortgleich zurückgeben. Jede Korrektur hier wäre stille
/// Nachbearbeitung — genau das, was im lokalen Betrieb nicht passieren soll.
/// </summary>
public class PassthroughPostProcessingServiceTests
{
    private static PassthroughPostProcessingService CreateSut() =>
        new(Mock.Of<ILogger<PassthroughPostProcessingService>>());

    [Theory]
    [InlineData("Ein ganz normaler Satz.")]
    [InlineData("  führende und folgende Leerzeichen  ")]
    [InlineData("Zeile eins\r\nZeile zwei\nZeile drei")]
    [InlineData("Umlaute äöü, Straße, Emoji 🎙, Anführungszeichen „so“")]
    [InlineData("")]
    [InlineData("ähm also ähm doppelt doppelt")]
    public async Task ProcessAsync_ReturnsTheTranscriptUnchanged(string transcript)
    {
        var result = await CreateSut().ProcessAsync(transcript, DictationMode.Correct);

        Assert.Equal(transcript, result.Text);
    }

    [Theory]
    [InlineData(DictationMode.Correct)]
    [InlineData(DictationMode.Translate)]
    public async Task ProcessAsync_IgnoresTheMode(DictationMode mode)
    {
        // Übersetzt wird in dieser Konfiguration schon bei der Erkennung, nicht mehr hier.
        var result = await CreateSut().ProcessAsync("unverändert", mode);

        Assert.Equal("unverändert", result.Text);
    }

    [Fact]
    public async Task ProcessAsync_NeverReportsTruncation()
    {
        // Das Signal stammt ausschließlich aus einer Chat-Antwort — hier gibt es keine.
        var result = await CreateSut().ProcessAsync(new string('x', 100_000), DictationMode.Correct);

        Assert.False(result.IsPossiblyTruncated);
    }
}
