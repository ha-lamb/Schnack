using Schnack.Models;

namespace Schnack.Services;

public interface ITrayService : IDisposable
{
    void Initialize();
    void UpdateState(RecordingState state);
    void ShowBalloonTip(string title, string message);

    event EventHandler<DictationMode>? ModeChangeRequested;
    event EventHandler? StartRecordingRequested;
    event EventHandler? StopRecordingRequested;
    event EventHandler? CancelProcessingRequested;
    event EventHandler? SettingsRequested;
    event EventHandler? AboutRequested;
    event EventHandler? ToggleFloatingRecorderRequested;
    void UpdateFloatingButtonVisibility(bool visible);
    event EventHandler? ExitRequested;

    void ShowUpdateMenuItem(string version);
    void HideUpdateMenuItem();
    event EventHandler? ApplyUpdateRequested;
    event EventHandler? CheckForUpdatesRequested;

    /// <summary>Ziel-HWND für Tray-gestartete Aufnahme: vor Menü (Mousedown) und nach „Start” per Idle erneut ermittelt.</summary>
    nint CachedForegroundHwnd { get; }
}
