using System.Text;
using Schnack.Models;

namespace Schnack.Services.Internal;

/// <summary>
/// Formatiert die nutzerdefinierte Begriffsliste für die beiden Stellen, an denen sie wirkt:
/// als Vorab-Kontext für die Spracherkennung und als Anweisung für die Nachbearbeitung.
/// Reine Funktionen ohne Abhängigkeiten — die Formulierungen sind funktionale Prompts,
/// keine UI-Texte, und gehören daher nicht in die Ressourcen.
/// </summary>
internal static class VocabularyPrompt
{
    /// <summary>Obergrenze für den Erkennungs-Prompt. Beide Engines fassen nur ~224 Tokens;
    /// darüber wird stillschweigend abgeschnitten, deshalb kappen wir selbst.</summary>
    internal const int MaxSpeechPromptChars = 700;

    /// <summary>Schutz gegen versehentlich riesige Listen (Copy-Paste ganzer Dokumente).</summary>
    internal const int MaxTerms = 500;

    /// <summary>Trimmt, verwirft Leerzeilen und Duplikate, deckelt die Anzahl.</summary>
    internal static string[] Normalize(IEnumerable<string>? terms)
    {
        if (terms == null)
            return [];

        return terms
            .Select(t => t?.Trim() ?? string.Empty)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTerms)
            .ToArray();
    }

    /// <summary>
    /// Prompt für die Spracherkennung: Sprachhinweis plus so viele Begriffe, wie in das
    /// Kontextfenster passen. <paramref name="truncatedCount"/> meldet, wie viele nicht mehr passten.
    /// </summary>
    internal static string ForSpeech(string[] terms, AppLanguage language, out int truncatedCount)
    {
        var hint = language == AppLanguage.En ? "Dictation in English." : "Diktat auf Deutsch.";
        truncatedCount = 0;

        if (terms.Length == 0)
            return hint;

        var lead = language == AppLanguage.En
            ? " Proper nouns and technical terms: "
            : " Eigennamen und Fachbegriffe: ";

        var builder = new StringBuilder(hint).Append(lead);
        var budget = MaxSpeechPromptChars - builder.Length - 1; // -1 für den Schlusspunkt
        var used = 0;

        for (var i = 0; i < terms.Length; i++)
        {
            var addition = (used == 0 ? "" : ", ") + terms[i];
            if (used > 0 && addition.Length > budget)
            {
                truncatedCount = terms.Length - i;
                break;
            }

            builder.Append(addition);
            budget -= addition.Length;
            used++;
        }

        return builder.Append('.').ToString();
    }

    /// <summary>Anweisungsblock für die Nachbearbeitung; leer, wenn keine Begriffe hinterlegt sind.</summary>
    internal static string ForPostProcessing(string[] terms, AppLanguage promptLanguage)
    {
        if (terms.Length == 0)
            return string.Empty;

        var list = string.Join(", ", terms);

        // Die Schlussklausel bremst Übereifer: ohne sie verbiegt das Modell auch entfernt
        // ähnliche Wörter auf Listeneinträge.
        return promptLanguage == AppLanguage.En
            ? $"""
               The following proper nouns and technical terms may occur. Spell them exactly like this, even if the transcript renders them differently:
               {list}

               Do not insert them on your own — only replace wording that clearly refers to one of them.
               """
            : $"""
               Die folgenden Eigennamen und Fachbegriffe können vorkommen. Schreibe sie exakt so, auch wenn das Transkript sie anders wiedergibt:
               {list}

               Füge sie nicht von dir aus ein — ersetze nur, was offensichtlich einen davon meint.
               """;
    }
}
