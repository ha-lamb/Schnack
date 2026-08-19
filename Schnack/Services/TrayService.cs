using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Extensions.Logging;
using Schnack.Models;

namespace Schnack.Services;

public sealed class TrayService : ITrayService
{
    private readonly ILogger<TrayService> _logger;
    private readonly ISettingsService _settings;
    private TaskbarIcon? _taskbarIcon;
    private MenuItem? _modeCorrectItem;
    private MenuItem? _modeTranslateItem;
    private MenuItem? _floatingItem;
    private MenuItem? _updateItem;
    private bool _disposed;

    public event EventHandler<DictationMode>? ModeChangeRequested;
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

        _taskbarIcon.TrayLeftMouseDoubleClick += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        _taskbarIcon.ContextMenu = BuildContextMenu();
        _taskbarIcon.ForceCreate();

        _logger.LogInformation("Tray initialized");
    }

    private ContextMenu BuildContextMenu()
    {
        var hintItem = new MenuItem
        {
            Header = $"Aufnahme über Hotkey ({_settings.Settings.Hotkey}) oder schwebenden Button",
            IsEnabled = false
        };

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
        menu.Items.Add(hintItem);
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
