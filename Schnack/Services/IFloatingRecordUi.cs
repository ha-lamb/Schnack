using Schnack.Models;

namespace Schnack.Services;

public interface IFloatingRecordUi : IDisposable
{
    event EventHandler? ToggleRecordingRequested;
    event EventHandler? VisibilityChanged;

    bool IsVisible { get; }

    void ShowOrActivate();
    void Hide();
    void SetRecordingState(RecordingState state);

    /// <summary>Übernimmt die aktuelle Sprache ins bereits erzeugte Fenster (Kontextmenü).</summary>
    void ApplyLanguage();
}
