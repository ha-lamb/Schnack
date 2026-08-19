using Schnack.Models;

namespace Schnack.Services;

internal static class DictationPrompts
{
    internal const string DeCorrect = """
        Korrigiere den folgenden diktierten deutschen Text sehr zurückhaltend.

        Erlaubt:
        - Rechtschreibung korrigieren
        - Zeichensetzung ergänzen
        - Groß- und Kleinschreibung korrigieren
        - offensichtliche Diktierfehler beheben
        - Füllwörter leicht reduzieren
        - doppelte Formulierungen entfernen

        Nicht erlaubt:
        - Inhalt ändern
        - neue Informationen hinzufügen
        - Informationen entfernen
        - Aussagen abschwächen oder verstärken
        - Namen, Zahlen, Termine, URLs, E-Mail-Adressen oder Fachbegriffe verändern
        - den Stil stark umformulieren
        - aus Stichpunkten Fließtext machen, außer der Nutzer hat offensichtlich Fließtext diktiert

        Gib ausschließlich den finalen korrigierten Text aus, ohne Erklärung, ohne Markdown, ohne Anführungszeichen.

        Text:
        {{TRANSCRIPT}}
        """;

    internal const string EnCorrect = """
        Correct the following dictated English text very conservatively.

        Allowed:
        - fix spelling
        - add punctuation
        - fix capitalisation
        - repair obvious dictation errors
        - slightly reduce filler words
        - remove duplicated phrasings

        Not allowed:
        - change the content
        - add new information
        - remove information
        - weaken or strengthen statements
        - alter names, numbers, dates, URLs, e-mail addresses or technical terms
        - heavily rephrase the style
        - turn bullet points into prose, unless the user clearly dictated prose

        Output only the final corrected text, without explanation, without Markdown, without quotation marks.

        Text:
        {{TRANSCRIPT}}
        """;

    internal const string DeToEn = """
        Der folgende Text wurde auf Deutsch diktiert. Übersetze ihn in natürliches, klares Englisch.

        Wichtig:
        - Bedeutung vollständig erhalten
        - keine Informationen hinzufügen
        - keine Informationen entfernen
        - Namen, Zahlen, Termine, URLs, E-Mail-Adressen und Fachbegriffe erhalten
        - offensichtliche Diktierfehler vorsichtig korrigieren
        - Füllwörter und doppelte Formulierungen leicht glätten
        - professionell und natürlich formulieren, aber nicht überformulieren
        - keine Erklärung, kein Markdown, keine Anführungszeichen

        Gib ausschließlich den finalen englischen Text aus.

        Text:
        {{TRANSCRIPT}}
        """;

    internal const string EnToDe = """
        The following text was dictated in English. Translate it into natural, clear German.

        Important:
        - preserve the meaning completely
        - do not add information
        - do not remove information
        - keep names, numbers, dates, URLs, e-mail addresses and technical terms
        - carefully correct obvious dictation errors
        - lightly smooth filler words and duplicated phrasings
        - phrase it professionally and naturally, but do not over-formulate
        - no explanation, no Markdown, no quotation marks

        Output only the final German text.

        Text:
        {{TRANSCRIPT}}
        """;

    /// <summary>Wählt den Prompt anhand Diktiersprache und Modus; Übersetzen zielt stets auf die andere Sprache.</summary>
    internal static string Build(AppLanguage language, DictationMode mode, string transcript)
    {
        var template = (language, mode) switch
        {
            (AppLanguage.De, DictationMode.Correct) => DeCorrect,
            (AppLanguage.De, DictationMode.Translate) => DeToEn,
            (AppLanguage.En, DictationMode.Correct) => EnCorrect,
            _ => EnToDe
        };
        return template.Replace("{{TRANSCRIPT}}", transcript);
    }
}
