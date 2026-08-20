using Schnack.Models;
using Schnack.Services.Internal;

namespace Schnack.Tests;

public class VocabularyPromptTests
{
    // ── Normalisierung ──────────────────────────────────────────────

    [Fact]
    public void Normalize_TrimsDropsEmptyAndDeduplicates()
    {
        var result = VocabularyPrompt.Normalize(
            ["  Kubernetes  ", "", "   ", "Krzysztof", "kubernetes", "Krzysztof"]);

        Assert.Equal(["Kubernetes", "Krzysztof"], result);
    }

    [Fact]
    public void Normalize_Null_ReturnsEmpty()
    {
        Assert.Empty(VocabularyPrompt.Normalize(null));
    }

    [Fact]
    public void Normalize_CapsAtMaxTerms()
    {
        var many = Enumerable.Range(0, VocabularyPrompt.MaxTerms + 50).Select(i => $"Term{i}");

        Assert.Equal(VocabularyPrompt.MaxTerms, VocabularyPrompt.Normalize(many).Length);
    }

    // ── Erkennungs-Prompt ───────────────────────────────────────────

    [Theory]
    [InlineData(AppLanguage.De, "Diktat auf Deutsch.")]
    [InlineData(AppLanguage.En, "Dictation in English.")]
    public void ForSpeech_EmptyList_ReturnsOnlyLanguageHint(AppLanguage language, string expected)
    {
        var result = VocabularyPrompt.ForSpeech([], language, out var dropped);

        Assert.Equal(expected, result);
        Assert.Equal(0, dropped);
    }

    [Fact]
    public void ForSpeech_KeepsLanguageHintAndListsTerms()
    {
        var result = VocabularyPrompt.ForSpeech(["Kubernetes", "Krzysztof"], AppLanguage.De, out var dropped);

        Assert.StartsWith("Diktat auf Deutsch.", result, StringComparison.Ordinal);
        Assert.Contains("Kubernetes", result, StringComparison.Ordinal);
        Assert.Contains("Krzysztof", result, StringComparison.Ordinal);
        Assert.Equal(0, dropped);
    }

    [Fact]
    public void ForSpeech_LongList_TruncatesAndReportsCount()
    {
        var terms = Enumerable.Range(0, 200).Select(i => $"Begriff{i:D3}").ToArray();

        var result = VocabularyPrompt.ForSpeech(terms, AppLanguage.De, out var dropped);

        Assert.True(result.Length <= VocabularyPrompt.MaxSpeechPromptChars,
            $"Prompt zu lang: {result.Length} Zeichen");
        Assert.True(dropped > 0, "Kappung hätte gemeldet werden müssen");
        Assert.Contains("Begriff000", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ForSpeech_SingleOversizedTerm_StillProducesPrompt()
    {
        var result = VocabularyPrompt.ForSpeech([new string('X', 2000)], AppLanguage.De, out _);

        Assert.StartsWith("Diktat auf Deutsch.", result, StringComparison.Ordinal);
    }

    // ── Nachbearbeitungs-Block ──────────────────────────────────────

    [Fact]
    public void ForPostProcessing_EmptyList_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, VocabularyPrompt.ForPostProcessing([], AppLanguage.De));
        Assert.Equal(string.Empty, VocabularyPrompt.ForPostProcessing([], AppLanguage.En));
    }

    [Fact]
    public void ForPostProcessing_German_ListsTermsWithGuardClause()
    {
        var result = VocabularyPrompt.ForPostProcessing(["Kubernetes"], AppLanguage.De);

        Assert.Contains("Kubernetes", result, StringComparison.Ordinal);
        Assert.Contains("Eigennamen", result, StringComparison.Ordinal);
        // Schutzklausel gegen Übereifer muss enthalten bleiben
        Assert.Contains("nicht von dir aus ein", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ForPostProcessing_English_UsesEnglishWording()
    {
        var result = VocabularyPrompt.ForPostProcessing(["Kubernetes"], AppLanguage.En);

        Assert.Contains("proper nouns", result, StringComparison.Ordinal);
        Assert.Contains("Do not insert them", result, StringComparison.Ordinal);
    }
}
