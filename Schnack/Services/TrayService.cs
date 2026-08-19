using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Extensions.Logging;
using Schnack.Localization;
using Schnack.Models;

namespace Schnack.Services;

public sealed class TrayService : ITrayService
{
    private readonly ILogger<TrayService> _logger;
    private readonly ISettingsService _settings;
    private TaskbarIcon? _taskbarIcon;
    private readonly Dictionary<DictationChoice, MenuItem> _choiceItems = [];
    private MenuItem? _floatingItem;
    private MenuItem? _updateItem;
    private bool _disposed;

    // Zustand, der einen Menü-Neuaufbau (Sprachwechsel) überleben muss
    private DictationChoice _currentChoice = DictationChoice.All[0];
    private RecordingState _currentState = RecordingState.Idle;
    private bool _floatingVisible;
    private string? _pendingUpdateVersion;

    public event EventHandler<DictationChoice>? ModeChangeRequested;
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

        _taskbarIcon.TrayLeftMouseDoubleClick += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        _taskbarIcon.ContextMenu = BuildContextMenu();
        _taskbarIcon.ForceCreate();
        UpdateState(_currentState);

        _logger.LogInformation("Tray initialized");
    }

    /// <summary>
    /// Baut das Kontextmenü in der aktuellen Sprache neu auf. Nötig, weil die Menü-Header
    /// beim Erzeugen fest in die MenuItems geschrieben werden — ein Sprachwechsel greift sonst nicht.
    /// </summary>
    public void RebuildMenu()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_taskbarIcon == null) return;
            _taskbarIcon.ContextMenu = BuildContextMenu();
            UpdateState(_currentState);
            _logger.LogInformation("Tray menu rebuilt");
        });
    }

    private ContextMenu BuildContextMenu()
    {
        var hintItem = new MenuItem
        {
            Header = string.Format(Strings.Tray_Hint, _settings.Settings.Hotkey),
            IsEnabled = false
        };

        _choiceItems.Clear();
        foreach (var choice in DictationChoice.All)
        {
            var item = new MenuItem
            {
                Header = choice.DisplayName,
                IsCheckable = true,
                IsChecked = choice == _currentChoice
            };
            var captured = choice;
            item.Click += (_, _) => ModeChangeRequested?.Invoke(this, captured);
            _choiceItems[choice] = item;
        }

        var settingsItem = new MenuItem { Header = Strings.Tray_Settings };
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        _floatingItem = new MenuItem
        {
            Header = Strings.Tray_FloatingButton,
            IsCheckable = true,
            IsChecked = _floatingVisible
        };
        _floatingItem.Click += (_, _) => ToggleFloatingRecorderRequested?.Invoke(this, EventArgs.Empty);

        var aboutItem = new MenuItem { Header = Strings.Tray_About };
        aboutItem.Click += (_, _) => AboutRequested?.Invoke(this, EventArgs.Empty);

        _updateItem = new MenuItem
        {
            Header = _pendingUpdateVersion is null
                ? string.Empty
                : string.Format(Strings.Tray_UpdateInstall, _pendingUpdateVersion),
            Visibility = _pendingUpdateVersion is null ? Visibility.Collapsed : Visibility.Visible
        };
        _updateItem.Click += (_, _) => ApplyUpdateRequested?.Invoke(this, EventArgs.Empty);

        var checkUpdatesItem = new MenuItem { Header = Strings.Tray_CheckUpdates };
        checkUpdatesItem.Click += (_, _) => CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty);

        var exitItem = new MenuItem { Header = Strings.Tray_Exit };
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new ContextMenu();
        menu.Items.Add(hintItem);
        menu.Items.Add(new Separator());
        foreach (var item in _choiceItems.Values)
            menu.Items.Add(item);
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
        _currentState = state;
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_taskbarIcon == null) return;

            _taskbarIcon.ToolTipText = state switch
            {
                RecordingState.Idle => Strings.Tray_TooltipIdle,
                RecordingState.Recording => Strings.Tray_TooltipRecording,
                RecordingState.Processing => Strings.Tray_TooltipProcessing,
                _ => "Schnack"
            };
        });
    }

    public void UpdateMode(DictationChoice choice)
    {
        _currentChoice = choice;
        Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var (candidate, item) in _choiceItems)
                item.IsChecked = candidate == choice;
        });
    }

    public void UpdateFloatingButtonVisibility(bool visible)
    {
        _floatingVisible = visible;
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_floatingItem != null)
                _floatingItem.IsChecked = visible;
        });
    }

    public void ShowUpdateMenuItem(string version)
    {
        _pendingUpdateVersion = version;
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_updateItem == null) return;
            _updateItem.Header = string.Format(Strings.Tray_UpdateInstall, version);
            _updateItem.Visibility = Visibility.Visible;
        });
    }

    public void HideUpdateMenuItem()
    {
        _pendingUpdateVersion = null;
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
