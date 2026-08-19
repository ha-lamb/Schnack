using System.Windows;
using Schnack.Models;
using Schnack.Views;

namespace Schnack.Services;

public sealed class FloatingRecordUiService : IFloatingRecordUi
{
    private readonly ISettingsService _settingsService;
    private FloatingRecordWindow? _window;
    private DateTime _lastPositionSaveUtc;
    private bool _disposed;

    public FloatingRecordUiService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public event EventHandler? ToggleRecordingRequested;
    public event EventHandler? VisibilityChanged;

    public bool IsVisible => _window?.IsVisible ?? false;

    public void ShowOrActivate()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_window == null)
            {
                _window = new FloatingRecordWindow();
                _window.ToggleRecording += (_, _) => ToggleRecordingRequested?.Invoke(this, EventArgs.Empty);
                _window.HideRequested += (_, _) => Hide();
                _window.DragCompleted += (_, _) => SavePosition();
                _window.LocationChanged += OnLocationChanged;
                ApplyInitialPosition();
                _window.Closed += (_, _) =>
                {
                    _window!.LocationChanged -= OnLocationChanged;
                    _window = null;
                    VisibilityChanged?.Invoke(this, EventArgs.Empty);
                };
            }

            _window.Show();
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void Hide()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _window?.Hide();
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void ApplyInitialPosition()
    {
        if (_window == null)
            return;

        var s = _settingsService.Settings;
        var wa = SystemParameters.WorkArea;
        _window.Left = s.FloatingButtonLeft ?? (wa.Right - _window.Width - 20);
        _window.Top = s.FloatingButtonTop ?? (wa.Bottom - _window.Height - 20);
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (_window == null)
            return;

        var now = DateTime.UtcNow;
        if ((now - _lastPositionSaveUtc).TotalMilliseconds < 400)
            return;
        _lastPositionSaveUtc = now;
        SavePosition();
    }

    private void SavePosition()
    {
        if (_window == null) return;
        var updated = _settingsService.Settings with
        {
            FloatingButtonLeft = _window.Left,
            FloatingButtonTop = _window.Top
        };
        _settingsService.UpdateSettings(updated);
        _ = _settingsService.SaveAsync();
    }

    public void ApplyLanguage()
    {
        Application.Current.Dispatcher.Invoke(() => _window?.ApplyLocalization());
    }

    public void SetRecordingState(RecordingState state)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_window == null)
                return;

            _window.SetRecordingVisual(
                isRecording: state == RecordingState.Recording,
                isProcessing: state == RecordingState.Processing);
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_window != null)
                {
                    _window.LocationChanged -= OnLocationChanged;
                    _window.Close();
                    _window = null;
                }
            });
        }
        catch
        {
            // Shutdown: Dispatcher kann bereits beendet sein
        }
    }
}
