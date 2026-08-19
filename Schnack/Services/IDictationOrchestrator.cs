using Schnack.Models;

namespace Schnack.Services;

/// <summary>
/// Kapselt State-Machine (Idle ⇄ Recording ⇄ Processing) und Diktier-Pipeline
/// (Aufnahme → Transkription → Postprocessing → Texteinfügung).
/// </summary>
public interface IDictationOrchestrator : IDisposable
{
    /// <summary>Aktiver Modus (de_correct / de_to_en); wird pro Pipeline-Lauf gelesen.</summary>
    DictationMode CurrentMode { get; set; }

    RecordingState State { get; }

    /// <summary>
    /// Startet aus Idle die Aufnahme (Ziel-HWND wird gecacht) bzw. stoppt aus Recording
    /// und verarbeitet. Rückgabe-Task endet, wenn die Pipeline des Stop-Aufrufs fertig ist
    /// (bei Start/No-op sofort). Aufrufer dürfen fire-and-forget verwenden.
    /// </summary>
    Task ToggleRecordingAsync(nint foregroundHwnd);
}
