using System.Threading;
using System.Windows;
using System.Windows.Threading;
using H.NotifyIcon.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Velopack;
using Schnack.Interop;
using Schnack.Localization;
using Schnack.Services.Internal;
using Schnack.Models;
using Schnack.Services;
using Schnack.ViewModels;
using Schnack.Views;

namespace Schnack;

public partial class App : Application
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (Environment.Version.Major < 10)
        {
            // Läuft vor dem Laden der Settings — nutzt daher die Windows-Sprache
            var r = System.Windows.MessageBox.Show(
                Strings.Format(nameof(Strings.Startup_DotNetRequired), Environment.Version),
                Strings.Startup_DotNetRequiredTitle,
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Error);
            if (r == System.Windows.MessageBoxResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://dotnet.microsoft.com/download/dotnet/10.0",
                    UseShellExecute = true
                });
            return;
        }

        // Muss allererster Aufruf sein — verarbeitet Velopack Update-Hooks ohne WPF-Stack
        VelopackApp.Build()
            .OnFirstRun(_ => { /* FirstRunWindow wird in OnStartup gezeigt */ })
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    private Mutex? _mutex;
    private ServiceProvider? _serviceProvider;
    private Microsoft.Extensions.Logging.ILogger? _logger;
    private ITrayService? _trayService;
    private IHotkeyService? _hotkeyService;
    private ISettingsService? _settingsService;
    private IRecordingService? _recordingService;
    private IFloatingRecordUi? _floatingRecordUi;
    private IUpdateService? _updateService;
    private IDictationOrchestrator? _orchestrator;
    private ILocalizationService? _localization;
    private LoggingLevelSwitch? _logLevelSwitch;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Nur Typ + Stacktrace loggen, nie ex.Message (kann User-Daten enthalten — Logging-Verbote).
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            _logger?.LogError("UnhandledException: {Type}\n{Stack}", ex?.GetType().Name, ex?.StackTrace);
        };
        DispatcherUnhandledException += (_, args) =>
        {
            _logger?.LogError("DispatcherUnhandledException: {Type}\n{Stack}",
                args.Exception?.GetType().Name, args.Exception?.StackTrace);
            args.Handled = true;
        };

        // Single-instance guard via named Mutex
        _mutex = new Mutex(true, $"Schnack.Singleton.{Environment.UserName}", out bool isNew);
        if (!isNew)
        {
            ShowTemporaryBalloon(Strings.Balloon_AlreadyRunning);
            _mutex.Dispose();
            Shutdown(0);
            return;
        }

        _serviceProvider = BuildServiceProvider();
        _logger = _serviceProvider.GetRequiredService<ILogger<App>>();
        _logger.LogInformation("Schnack starting");

        _settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
        await _settingsService.LoadAsync();
        ApplyDebugLogLevelFromSettings();

        var settings = _settingsService.Settings;

        // Muss vor dem Tray-Aufbau laufen: dessen Menü-Header werden beim Erzeugen festgeschrieben
        _localization = _serviceProvider.GetRequiredService<ILocalizationService>();
        _localization.Apply(settings.UiLanguage);
        _localization.LanguageChanged += OnLanguageChanged;

        _recordingService = _serviceProvider.GetRequiredService<IRecordingService>();

        _trayService = _serviceProvider.GetRequiredService<ITrayService>();
        _trayService.ModeChangeRequested += OnModeChangeRequested;
        _trayService.SettingsRequested += OnSettingsRequested;
        _trayService.AboutRequested += OnAboutRequested;
        _trayService.ToggleFloatingRecorderRequested += OnToggleFloatingRecorderRequested;
        _trayService.ExitRequested += OnExitRequested;
        _trayService.ApplyUpdateRequested     += OnApplyUpdateRequested;
        _trayService.CheckForUpdatesRequested += OnCheckForUpdatesRequested;
        _trayService.Initialize();
        _trayService.UpdateState(RecordingState.Idle);
        _trayService.UpdateFloatingButtonVisibility(false);

        _floatingRecordUi = _serviceProvider.GetRequiredService<IFloatingRecordUi>();
        _floatingRecordUi.ToggleRecordingRequested += OnFloatingToggleRecordingRequested;
        _floatingRecordUi.VisibilityChanged += OnFloatingVisibilityChanged;
        _floatingRecordUi.SetRecordingState(RecordingState.Idle);

        _orchestrator = _serviceProvider.GetRequiredService<IDictationOrchestrator>();
        var startupChoice = DictationChoice.FromSettings(settings);
        _orchestrator.CurrentMode = startupChoice.Mode;
        _trayService.UpdateMode(startupChoice);

        _hotkeyService = _serviceProvider.GetRequiredService<IHotkeyService>();
        try
        {
            _hotkeyService.Register(settings.Hotkey, OnHotkeyPressed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.GetType().Name + ": Failed to register hotkey");
            _trayService.ShowBalloonTip(Strings.Balloon_AppTitle,
                Strings.Format(nameof(Strings.Startup_HotkeyFailed), settings.Hotkey));
        }

        // Startup validations
        var secretService = _serviceProvider.GetRequiredService<ISecretService>();
        if (secretService.GetApiKey() == null && settings.BackendProvider == BackendProvider.Claude)
        {
            _trayService.ShowBalloonTip(Strings.Error_MissingAnthropicKeyTitle, Strings.Error_MissingAnthropicKey);
        }

        if (secretService.GetOpenAiApiKey() == null && settings.BackendProvider == BackendProvider.OpenAi)
        {
            _trayService.ShowBalloonTip(Strings.Error_MissingOpenAiKeyTitle, Strings.Error_MissingOpenAiKey);
        }

        if (_settingsService.CreatedDefaultSettingsOnLastLoad)
            _ = Dispatcher.BeginInvoke(new Action(ShowFirstRunDialog), DispatcherPriority.ApplicationIdle);

        _updateService = _serviceProvider.GetRequiredService<IUpdateService>();
        _updateService.UpdateAvailable    += OnUpdateAvailable;
        _updateService.BeforeApplyRestart += OnBeforeApplyRestart;
        _ = _updateService.CheckOnStartupAsync();

        _logger.LogInformation("Schnack ready. Mode: {Mode}, Backend: {Backend}", _orchestrator.CurrentMode, settings.BackendProvider);
    }

    private void ShowFirstRunDialog()
    {
        try
        {
            var window = _serviceProvider!.GetRequiredService<FirstRunWindow>();
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex.GetType().Name + ": First-run dialog failed");
        }
    }

    /// <summary>Wird u.a. nach Schließen der Einstellungen aufgerufen, damit Debug-Logging ohne Neustart greift.</summary>
    internal void ApplyDebugLogLevelFromSettings()
    {
        if (_logLevelSwitch == null || _settingsService == null)
            return;

        var wantDebug = Environment.GetEnvironmentVariable("SCHNACK_DEBUG") == "1"
            || _settingsService.Settings.DebugLogging;
        _logLevelSwitch.MinimumLevel = wantDebug ? LogEventLevel.Debug : LogEventLevel.Information;
    }

    private void OnHotkeyPressed() => TryToggleRecordingUserAction();

    private void OnFloatingToggleRecordingRequested(object? sender, EventArgs e) => TryToggleRecordingUserAction();

    private void OnToggleFloatingRecorderRequested(object? sender, EventArgs e)
    {
        if (_floatingRecordUi == null) return;
        if (_floatingRecordUi.IsVisible)
            _floatingRecordUi.Hide();
        else
            _floatingRecordUi.ShowOrActivate();
    }

    /// <summary>Tray-Menü und schwebender Button schreiben ihre Texte beim Erzeugen fest — beide neu bestücken.</summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _trayService?.RebuildMenu();
        _floatingRecordUi?.ApplyLanguage();
    }

    private void OnFloatingVisibilityChanged(object? sender, EventArgs e) =>
        _trayService?.UpdateFloatingButtonVisibility(_floatingRecordUi?.IsVisible ?? false);

    private void OnUpdateAvailable(object? sender, UpdateAvailableEventArgs e) =>
        _trayService?.ShowUpdateMenuItem(e.NewVersion);

    private void OnBeforeApplyRestart(object? sender, EventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        _mutex = null;
    }

    private void OnCheckForUpdatesRequested(object? sender, EventArgs e) =>
        _ = _updateService?.CheckAndPromptAsync();

    private void OnApplyUpdateRequested(object? sender, EventArgs e)
    {
        if (_orchestrator != null && _orchestrator.State != RecordingState.Idle)
        {
            var r = MessageBox.Show(
                Strings.Update_RestartConfirm,
                Strings.Update_RestartConfirmTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
        }
        _ = _updateService?.ApplyKnownUpdateAsync();
    }

    /// <summary>Hotkey und schwebender Button: Vordergrund-Fenster sofort lesen (NOACTIVATE-Floater stiehlt keinen Fokus).</summary>
    private void TryToggleRecordingUserAction() =>
        _ = _orchestrator?.ToggleRecordingAsync(Win32.GetForegroundWindow());

    private void OnModeChangeRequested(object? sender, DictationChoice choice)
    {
        if (_orchestrator != null)
            _orchestrator.CurrentMode = choice.Mode;
        _trayService?.UpdateMode(choice);

        // Persistieren: die Transkriptions- und Postprocessing-Services lesen die Diktiersprache
        // pro Lauf aus den Settings, und die Wahl soll den Neustart überleben.
        if (_settingsService != null)
        {
            _settingsService.UpdateSettings(_settingsService.Settings with
            {
                DictationLanguage = choice.Language,
                DefaultMode = choice.ModeValue
            });
            _ = _settingsService.SaveAsync();
        }

        _logger?.LogInformation("Dictation choice changed to {Language}/{Mode}", choice.Language, choice.Mode);
    }

    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var oldHotkey = _settingsService?.Settings.Hotkey;
            var oldLanguage = _settingsService?.Settings.UiLanguage;
            var window = _serviceProvider?.GetRequiredService<SettingsWindow>();
            if (window == null) return;
            window.ShowDialog();

            var newLanguage = _settingsService?.Settings.UiLanguage;
            if (newLanguage != null && newLanguage != oldLanguage)
                _localization?.Apply(newLanguage.Value);

            // Diktat-Modus kann auch im Dialog geändert worden sein — Tray-Häkchen und Pipeline nachziehen
            if (_settingsService != null)
            {
                var choice = DictationChoice.FromSettings(_settingsService.Settings);
                if (_orchestrator != null)
                    _orchestrator.CurrentMode = choice.Mode;
                _trayService?.UpdateMode(choice);
            }

            var newHotkey = _settingsService?.Settings.Hotkey;
            if (_hotkeyService != null && newHotkey != null && newHotkey != oldHotkey)
            {
                _hotkeyService.Unregister();
                try
                {
                    _hotkeyService.Register(newHotkey, OnHotkeyPressed);
                    _logger?.LogInformation("Hotkey re-registered: {Hotkey}", newHotkey);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex.GetType().Name + ": Failed to re-register hotkey");
                    _trayService?.ShowBalloonTip("Schnack", $"Hotkey '{newHotkey}' konnte nicht registriert werden.");
                }
            }
        });
    }

    private void OnAboutRequested(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var window = _serviceProvider?.GetRequiredService<AboutWindow>();
            window?.ShowDialog();
        });
    }

    private void OnExitRequested(object? sender, EventArgs e)
    {
        CleanupAndShutdown();
    }

    private ServiceProvider BuildServiceProvider()
    {
        _logLevelSwitch = new LoggingLevelSwitch(
            Environment.GetEnvironmentVariable("SCHNACK_DEBUG") == "1"
                ? LogEventLevel.Debug
                : LogEventLevel.Information);

        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Schnack", "logs", "schnack-.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(_logLevelSwitch)
            .WriteTo.File(logPath,
                rollingInterval: Serilog.RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSerilog(dispose: true));

        services.AddHttpClient("Claude", client =>
        {
            client.BaseAddress = new Uri("https://api.anthropic.com/");
            client.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddHttpClient("OpenAi", client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/");
            client.Timeout = TimeSpan.FromMinutes(3);
        });

        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<ISecretService, DpapiSecretService>();
        services.AddSingleton<IRecordingService, NAudioRecordingService>();
        services.AddSingleton<ITextInsertionService, TextInsertionService>();
        services.AddSingleton<IHotkeyService, HotkeyService>();
        services.AddSingleton<ITrayService, TrayService>();
        services.AddSingleton<IFloatingRecordUi, FloatingRecordUiService>();
        services.AddSingleton<IWhisperModelDownloadService, WhisperModelDownloadService>();

        // Keyed singletons per BackendProvider: "OpenAi" / "Claude"
        services.AddKeyedSingleton<ITranscriptionService, OpenAiTranscriptionService>(BackendProvider.OpenAi.ToString());
        services.AddKeyedSingleton<ITranscriptionService, WhisperLocalTranscriptionService>(BackendProvider.Claude.ToString());

        services.AddKeyedSingleton<IPostProcessingService, OpenAiChatService>(BackendProvider.OpenAi.ToString());
        services.AddKeyedSingleton<IPostProcessingService, ClaudeService>(BackendProvider.Claude.ToString());

        services.AddSingleton<IUpdateChecker>(
            _ => new VelopackUpdateChecker(VelopackUpdateService.RepoUrl));
        services.AddSingleton<IUpdateService, VelopackUpdateService>();
        services.AddSingleton<IDictationOrchestrator, DictationOrchestrator>();

        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<AboutWindow>();
        services.AddTransient<FirstRunWindow>();

        return services.BuildServiceProvider();
    }

    private void CleanupAndShutdown()
    {
        _logger?.LogInformation("Schnack shutting down");
        _orchestrator?.Dispose();
        _hotkeyService?.Unregister();
        _floatingRecordUi?.Dispose();
        _trayService?.Dispose();

        _recordingService?.Dispose();

        // ServiceProvider muss per DisposeAsync freigegeben werden, wenn Singletons IAsyncDisposable implementieren.
        if (_serviceProvider is IAsyncDisposable asyncProvider)
            asyncProvider.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
        else
            _serviceProvider?.Dispose();
        _serviceProvider = null;

        try { _mutex?.ReleaseMutex(); } catch { /* not owned by this thread */ }
        _mutex?.Dispose();

        Shutdown(0);
    }

    private static void ShowTemporaryBalloon(string message)
    {
        // Brief tray icon to show the "already running" balloon, then dispose
        using var icon = new H.NotifyIcon.TaskbarIcon();
        try
        {
            var iconStream = Application.GetResourceStream(
                new Uri("pack://application:,,,/Resources/tray-icon.ico"))?.Stream;
            if (iconStream != null)
                icon.Icon = new System.Drawing.Icon(iconStream);
            icon.ForceCreate();
            icon.ShowNotification("Schnack", message, NotificationIcon.Info);
            Thread.Sleep(2000);
        }
        catch { /* ignore if balloon fails */ }
    }
}
