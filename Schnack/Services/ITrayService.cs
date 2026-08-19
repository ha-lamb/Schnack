using Schnack.Models;

namespace Schnack.Services;

public interface ITrayService : IDisposable
{
    void Initialize();
    void UpdateState(RecordingState state);
    void UpdateMode(DictationMode mode);
    void ShowBalloonTip(string title, string message);

    event EventHandler<DictationMode>? ModeChangeRequested;
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
