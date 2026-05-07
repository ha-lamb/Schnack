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

    internal static string Build(string promptTemplate, string transcript) =>
        promptTemplate.Replace("{{TRANSCRIPT}}", transcript);
}
