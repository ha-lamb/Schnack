using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Extensions.Logging;
using Schnack.Interop;
using Schnack.Models;

namespace Schnack.Services;

public sealed class TrayService : ITrayService
{
    private readonly ILogger<TrayService> _logger;
    private readonly ISettingsService _settings;
    private TaskbarIcon? _taskbarIcon;
    private MenuItem? _startItem;
    private MenuItem? _stopItem;
    private MenuItem? _cancelItem;
    private MenuItem? _modeCorrectItem;
    private MenuItem? _modeTranslateItem;
    private MenuItem? _floatingItem;
    private MenuItem? _updateItem;
    private bool _disposed;

    public nint CachedForegroundHwnd { get; private set; }

    public event EventHandler<DictationMode>? ModeChangeRequested;
    public event EventHandler? StartRecordingRequested;
    public event EventHandler? StopRecordingRequested;
    public event EventHandler? CancelProcessingRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? AboutRequested;
    public event EventHandler? ToggleFloatingRecorderRequested;
    public event EventHandler? ApplyUpdateRequested;
    public event EventHandler? CheckForUpdatesRequested;
    public event EventHandler? ExitRequested;

    public TrayService(ILogger<TrayService> logger, ISettingsService settings)
    {
        _logger = logger;
        _settings = settings;
    }

    public void Initialize()
    {
        _taskbarIcon = new TaskbarIcon();

        var iconStream = Application.GetResourceStream(
            new Uri("pack://application:,,,/Resources/tray-icon.ico"))?.Stream;

        if (iconStream != null)
            _taskbarIcon.Icon = new System.Drawing.Icon(iconStream);

        _taskbarIcon.ToolTipText = "Schnack – Bereit";

        // Vordergrund-Fenster erfassen, bevor das Kontextmenü den Fokus stiehlt (Links- und Rechtsklick).
        _taskbarIcon.TrayRightMouseDown += OnTrayMouseDownBeforeMenu;
        _taskbarIcon.TrayLeftMouseDown += OnTrayMouseDownBeforeMenu;
        _taskbarIcon.TrayLeftMouseDoubleClick += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        _taskbarIcon.ContextMenu = BuildContextMenu();
        _taskbarIcon.ForceCreate();

        _logger.LogInformation("Tray initialized");
    }

    private void OnTrayMouseDownBeforeMenu(object? sender, RoutedEventArgs e) =>
        CachedForegroundHwnd = Win32.GetForegroundWindow();

    private ContextMenu BuildContextMenu()
    {
        _startItem = new MenuItem { Header = "Aufnahme starten" };
        _startItem.Click += (_, _) =>
        {
            // Nach Menü-Schließen: GetForegroundWindow liefert oft wieder die Ziel-App (wie beim Schwebebutton).
            // Vorheriger Cache (Mousedown auf dem Tray) als Fallback, falls Fokus noch auf Explorer/Taskleiste hängt.
            var backup = CachedForegroundHwnd;
            _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyForegroundCacheAfterTrayMenuClosed(backup);
                StartRecordingRequested?.Invoke(this, EventArgs.Empty);
            }), DispatcherPriority.ApplicationIdle);
        };

        _stopItem = new MenuItem { Header = "Aufnahme stoppen", IsEnabled = false };
        _stopItem.Click += (_, _) => StopRecordingRequested?.Invoke(this, EventArgs.Empty);

        _cancelItem = new MenuItem { Header = "Verarbeitung abbrechen", IsEnabled = false };
        _cancelItem.Click += (_, _) => CancelProcessingRequested?.Invoke(this, EventArgs.Empty);

        _modeCorrectItem = new MenuItem { Header = "Deutsch korrigieren", IsCheckable = true, IsChecked = true };
        _modeCorrectItem.Click += (_, _) => ModeChangeRequested?.Invoke(this, DictationMode.DeCorrect);

        _modeTranslateItem = new MenuItem { Header = "Deutsch → Englisch", IsCheckable = true };
        _modeTranslateItem.Click += (_, _) => ModeChangeRequested?.Invoke(this, DictationMode.DeToEn);

        var settingsItem = new MenuItem { Header = "Einstellungen…" };
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        _floatingItem = new MenuItem { Header = "Schwebender Aufnahme-Button", IsCheckable = true };
        _floatingItem.Click += (_, _) => ToggleFloatingRecorderRequested?.Invoke(this, EventArgs.Empty);

        var aboutItem = new MenuItem { Header = "Über Schnack…" };
        aboutItem.Click += (_, _) => AboutRequested?.Invoke(this, EventArgs.Empty);

        _updateItem = new MenuItem { Visibility = Visibility.Collapsed };
        _updateItem.Click += (_, _) => ApplyUpdateRequested?.Invoke(this, EventArgs.Empty);

        var checkUpdatesItem = new MenuItem { Header = "Auf Updates prüfen" };
        checkUpdatesItem.Click += (_, _) => CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty);

        var exitItem = new MenuItem { Header = "Beenden" };
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new ContextMenu();
        menu.Items.Add(_startItem);
        menu.Items.Add(_stopItem);
        menu.Items.Add(_cancelItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(_modeCorrectItem);
        menu.Items.Add(_modeTranslateItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(_floatingItem);
        menu.Items.Add(aboutItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(_updateItem);
        menu.Items.Add(checkUpdatesItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        return menu;
    }

    private void ApplyForegroundCacheAfterTrayMenuClosed(nint backupFromMouseDown)
    {
        nint fg = Win32.GetForegroundWindow();
        bool debugLog = _settings.Settings.DebugLogging
            || Environment.GetEnvironmentVariable("SCHNACK_DEBUG") == "1";

        bool backupIsSchnack = IsWindowOfCurrentProcess(backupFromMouseDown);
        if (debugLog)
        {
            _logger.LogDebug(
                "TrayFg backup={Backup} fg={Fg} backupProc={BackupProc} fgProc={FgProc} backupIsSchnack={BackupIsSchnack}",
                backupFromMouseDown, fg,
                GetProcessName(backupFromMouseDown),
                GetProcessName(fg),
                backupIsSchnack);
        }

        // Bevorzuge backupFromMouseDown, wenn fg auf Explorer oder den eigenen Prozess zeigt.
        if (fg != 0 && !IsWindowOfCurrentProcess(fg))
        {
            Win32.GetWindowThreadProcessId(fg, out uint fgPid);
            bool fgIsExplorer = IsExplorerProcessId(fgPid);

            if (fgIsExplorer && backupFromMouseDown != 0 && !backupIsSchnack)
            {
                Win32.GetWindowThreadProcessId(backupFromMouseDown, out uint backupPid);
                if (!IsExplorerProcessId(backupPid))
                {
                    CachedForegroundHwnd = backupFromMouseDown;
                    if (debugLog)
                        _logger.LogDebug("TrayFg: using backup={Hwnd} (fg was Explorer)", backupFromMouseDown);
                    return;
                }
            }

            CachedForegroundHwnd = fg;
            if (debugLog)
                _logger.LogDebug("TrayFg: using fg={Hwnd}", fg);
            return;
        }

        // fg == 0 oder eigener Prozess: Backup bevorzugen, sofern es nicht auch Schnack ist
        if (backupFromMouseDown != 0 && !backupIsSchnack)
        {
            CachedForegroundHwnd = backupFromMouseDown;
            if (debugLog)
                _logger.LogDebug("TrayFg: fg invalid, using backup={Hwnd}", backupFromMouseDown);
        }
        else
        {
            // Kein gültiges Ziel gefunden — TextInsertionService wird mit 0 umgehen
            CachedForegroundHwnd = 0;
            if (debugLog)
                _logger.LogDebug("TrayFg: no valid target window found (backup={Backup} fg={Fg})", backupFromMouseDown, fg);
        }
    }

    private static string GetProcessName(nint hwnd)
    {
        if (hwnd == 0) return "(none)";
        try
        {
            Win32.GetWindowThreadProcessId(hwnd, out uint pid);
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return "(unknown)"; }
    }

    private static bool IsWindowOfCurrentProcess(nint hwnd)
    {
        if (hwnd == 0)
            return false;
        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        return (int)pid == Environment.ProcessId;
    }

    private static bool IsExplorerProcessId(uint pid)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public void UpdateState(RecordingState state)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_taskbarIcon == null) return;

            _taskbarIcon.ToolTipText = state switch
            {
                RecordingState.Idle => "Schnack – Bereit",
                RecordingState.Recording => "Schnack – Aufnahme läuft…",
                RecordingState.Processing => "Schnack – Verarbeite…",
                _ => "Schnack"
            };

            if (_startItem != null)
                _startItem.IsEnabled = state == RecordingState.Idle;
            if (_stopItem != null)
                _stopItem.IsEnabled = state == RecordingState.Recording;
            if (_cancelItem != null)
                _cancelItem.IsEnabled = state == RecordingState.Processing;
        });
    }

    public void UpdateMode(DictationMode mode)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_modeCorrectItem != null)
                _modeCorrectItem.IsChecked = mode == DictationMode.DeCorrect;
            if (_modeTranslateItem != null)
                _modeTranslateItem.IsChecked = mode == DictationMode.DeToEn;
        });
    }

    public void UpdateFloatingButtonVisibility(bool visible)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_floatingItem != null)
                _floatingItem.IsChecked = visible;
        });
    }

    public void ShowUpdateMenuItem(string version)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_updateItem == null) return;
            _updateItem.Header = $"Update v{version} installieren";
            _updateItem.Visibility = Visibility.Visible;
        });
    }

    public void HideUpdateMenuItem()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_updateItem != null)
                _updateItem.Visibility = Visibility.Collapsed;
        });
    }

    public void ShowBalloonTip(string title, string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _taskbarIcon?.ShowNotification(title, message, NotificationIcon.Info);
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _taskbarIcon?.Dispose();
    }
}
