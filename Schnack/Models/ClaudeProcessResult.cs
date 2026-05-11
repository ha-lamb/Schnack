namespace Schnack.Models;

/// <summary>
/// Ergebnis der Claude-Nachbearbeitung inkl. Hinweis auf mögliche Kürzung.
/// </summary>
public record ClaudeProcessResult(string Text, bool IsPossiblyTruncated);
