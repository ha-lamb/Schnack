using Schnack.Models;

namespace Schnack.Services.Internal;

/// <summary>
/// Beantwortet an genau einer Stelle, ob nachbearbeitet wird und wer es tut.
/// Die Frage stellen Pipeline, Tray-Menü und Einstellungsdialog — liefen die auseinander,
/// böte die Oberfläche Optionen an, die die Pipeline nicht bedienen kann.
/// Bewusst pur: der Aufrufer beschafft <paramref name="keyAvailable"/> über
/// <see cref="ISecretService.HasKeyFor"/>.
/// </summary>
internal static class SmoothingPolicy
{
    /// <summary>Keyed-DI-Schlüssel für die Nachbearbeitung, die nichts tut.</summary>
    internal const string Passthrough = "None";

    /// <summary>
    /// Wird tatsächlich geglättet? Nur wenn der Nutzer es will UND der gewählte Dienst
    /// einen hinterlegten Schlüssel hat — ohne Schlüssel gäbe es sonst bei jedem Diktat
    /// einen Fehler statt eines brauchbaren Ergebnisses.
    /// </summary>
    internal static bool IsActive(AppSettings settings, bool keyAvailable) =>
        settings.TextSmoothing && keyAvailable;

    /// <summary>
    /// Schlüssel, unter dem der <see cref="IPostProcessingService"/> aufgelöst wird. Ohne
    /// Glättung der Passthrough — dadurch bleibt die Auflösung im Orchestrator einheitlich.
    /// </summary>
    internal static string PostProcessingKey(AppSettings settings, bool keyAvailable) =>
        IsActive(settings, keyAvailable) ? settings.AiService.ToString() : Passthrough;
}
