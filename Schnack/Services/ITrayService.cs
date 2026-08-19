using Schnack.Models;

namespace Schnack.Services;

public interface ITrayService : IDisposable
{
    void Initialize();

    /// <summary>Baut das Kontextmenü in der aktuellen Sprache neu auf (Zustände bleiben erhalten).</summary>
    void RebuildMenu();

    void UpdateState(RecordingState state);
    void UpdateMode(DictationChoice choice);
    void ShowBalloonTip(string title, string message);

    event EventHandler<DictationChoice>? ModeChangeRequested;
    event EventHandler? SettingsRequested;
    event EventHandler? AboutRequested;
    event EventHandler? ToggleFloatingRecorderRequested;
    void UpdateFloatingButtonVisibility(bool visible);
    event EventHandler? ExitRequested;

    void ShowUpdateMenuItem(string version);
    void HideUpdateMenuItem();
    event EventHandler? ApplyUpdateRequested;
    event EventHandler? CheckForUpdatesRequested;
}
