namespace Schnack.Services.Internal;

/// <summary>Ein von Whisper geliefertes Segment, reduziert auf das, was für die Prüfung zählt.</summary>
internal readonly record struct SpeechSegment(string Text, float Probability);

internal readonly record struct FilterResult(string Text, IReadOnlyList<float> DroppedProbabilities);

/// <summary>
/// Verwirft Segmente, die Whisper aus Stille oder Raumrauschen erfunden hat.
///
/// Whisper halluziniert auf sprachfreien Abschnitten Floskeln aus seinen Untertitel-Trainingsdaten
/// — im Deutschen typischerweise „Vielen Dank." Bei langen Diktaten mit Sprechpausen passiert das
/// zuverlässig; bei reinem Rauschen kam sogar der Vokabel-Prompt als Transkript zurück.
///
/// **Gemessen** (large-v3-turbo, synthetische Sprache mit Raumklang-Anhang):
/// echte Sprache 0,947–0,992, Halluzinationen 0,647–0,779. Dazwischen liegt kein einziger Messwert.
///
/// Nicht verwendet werden bewusst:
/// - <c>NoSpeechProbability</c> — in Whisper.net 1.9.0 auf diesem Weg immer 0, also wertlos.
/// - <c>MinProbability</c> und Zeichen-pro-Sekunde — im Fall „leise Rede plus Rauschen" lag beides
///   über den Werten echter Sprache; sie hätten die Halluzination durchgelassen.
/// - Wortlisten — die würden auch ein tatsächlich gesprochenes „vielen Dank" verwerfen.
/// </summary>
internal static class SegmentFilter
{
    /// <summary>
    /// Schwelle bewusst näher an den Halluzinationen (≤ 0,779) als an echter Sprache (≥ 0,947):
    /// Eine durchgerutschte Floskel sieht der Nutzer und löscht sie. Verworfene echte Sprache
    /// wäre still verloren — das ist der teurere Fehler.
    /// </summary>
    internal const float MinProbability = 0.80f;

    internal static FilterResult Apply(IReadOnlyList<SpeechSegment> segments)
    {
        var kept = new System.Text.StringBuilder();
        var dropped = new List<float>();

        foreach (var segment in segments)
        {
            if (segment.Probability < MinProbability)
                dropped.Add(segment.Probability);
            else
                kept.Append(segment.Text);
        }

        // Bewusst kein Rückfall auf „dann eben alles behalten": Liegt jedes Segment unter der
        // Schwelle, wurde mit hoher Wahrscheinlichkeit gar nicht gesprochen. Ein leeres Transkript
        // meldet der Orchestrator als „keine Sprache erkannt" — das ist ehrlicher, als erfundenen
        // Text einzufügen.
        return new FilterResult(kept.ToString(), dropped);
    }
}
