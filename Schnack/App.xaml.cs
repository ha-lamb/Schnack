using System.Net;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using H.NotifyIcon.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Schnack.Interop;
using Schnack.Models;
using Schnack.Services;
using Schnack.ViewModels;
using Schnack.Views;

namespace Schnack;

public partial class App : Application
{
    private Mutex? _mutex;
    private ServiceProvider? _serviceProvider;
    private Microsoft.Extensions.Logging.ILogger? _logger;
    private ITrayService? _trayService;
    private IHotkeyService? _hotkeyService;
    private ISettingsService? _settingsService;
    private IRecordingService? _recordingService;
    private ITextInsertionService? _textInsertionService;
    private IFloatingRecordUi? _floatingRecordUi;

    // Thread-safe state: values from RecordingState enum
    private int _recordingState;
    private nint _cachedTargetHwnd;
    private CancellationTokenSource? _pipelineCts;
    private DictationMode _currentMode = DictationMode.DeCorrect;
    private LoggingLevelSwitch? _logLevelSwitch;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _logger?.LogError("UnhandledException: {Type}", args.ExceptionObject?.GetType().Name);
        DispatcherUnhandledException += (_, args) =>
        {
            _logger?.LogError("DispatcherUnhandledException: {Type}", args.Exception?.GetType().Name);
            args.Handled = true;
        };

        // Single-instance guard via named Mutex
        _mutex = new Mutex(true, $"Schnack.Singleton.{Environment.UserName}", out bool isNew);
        if (!isNew)
        {
            ShowTemporaryBalloon("Schnack läuft bereits");
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
        _currentMode = settings.DefaultMode == "de_to_en" ? DictationMode.DeToEn : DictationMode.DeCorrect;

        _recordingService = _serviceProvider.GetRequiredService<IRecordingService>();
        _textInsertionService = _serviceProvider.GetRequiredService<ITextInsertionService>();

        _trayService = _serviceProvider.GetRequiredService<ITrayService>();
        _trayService.StartRecordingRequested += OnStartRecordingRequested;
        _trayService.StopRecordingRequested += OnStopRecordingRequested;
        _trayService.CancelProcessingRequested += OnCancelProcessingRequested;
        _trayService.ModeChangeRequested += OnModeChangeRequested;
        _trayService.SettingsRequested += OnSettingsRequested;
        _trayService.AboutRequested += OnAboutRequested;
        _trayService.ToggleFloatingRecorderRequested += OnToggleFloatingRecorderRequested;
        _trayService.ExitRequested += OnExitRequested;
        _trayService.Initialize();
        _trayService.UpdateState(RecordingState.Idle);
        _trayService.UpdateFloatingButtonVisibility(false);

        _floatingRecordUi = _serviceProvider.GetRequiredService<IFloatingRecordUi>();
        _floatingRecordUi.ToggleRecordingRequested += OnFloatingToggleRecordingRequested;
        _floatingRecordUi.VisibilityChanged += OnFloatingVisibilityChanged;
        _floatingRecordUi.SetRecordingState(RecordingState.Idle);

        _hotkeyService = _serviceProvider.GetRequiredService<IHotkeyService>();
        try
        {
            _hotkeyService.Register(settings.Hotkey, OnHotkeyPressed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.GetType().Name + ": Failed to register hotkey");
            _trayService.ShowBalloonTip("Schnack", $"Hotkey '{settings.Hotkey}' konnte nicht registriert werden.");
        }

        // Startup validations
        var secretService = _serviceProvider.GetRequiredService<ISecretService>();
        if (secretService.GetApiKey() == null && settings.BackendProvider == BackendProvider.Claude)
        {
            _trayService.ShowBalloonTip("Anthropic API-Key fehlt",
                "ANTHROPIC_API_KEY setzen oder in den Einstellungen speichern, dann neu starten.");
        }

        if (secretService.GetOpenAiApiKey() == null && settings.BackendProvider == BackendProvider.OpenAi)
        {
            _trayService.ShowBalloonTip("OpenAI API-Key fehlt",
                "OPENAI_API_KEY setzen oder OpenAI-Key in den Einstellungen speichern.");
        }

        if (_settingsService.CreatedDefaultSettingsOnLastLoad)
            _ = Dispatcher.BeginInvoke(new Action(ShowFirstRunDialog), DispatcherPriority.ApplicationIdle);

        _logger.LogInformation("Schnack ready. Mode: {Mode}, Backend: {Backend}", _currentMode, settings.BackendProvider);
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

    private void OnCancelProcessingRequested(object? sender, EventArgs e) => _pipelineCts?.Cancel();

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

    private void OnFloatingVisibilityChanged(object? sender, EventArgs e) =>
        _trayService?.UpdateFloatingButtonVisibility(_floatingRecordUi?.IsVisible ?? false);

    /// <summary>Hotkey und schwebender Button: Vordergrund-Fenster sofort lesen (NOACTIVATE-Floater stiehlt keinen Fokus).</summary>
    private void TryToggleRecordingUserAction()
    {
        var hwnd = Win32.GetForegroundWindow();

        int prev = Interlocked.CompareExchange(ref _recordingState, (int)RecordingState.Recording, (int)RecordingState.Idle);
        if (prev == (int)RecordingState.Idle)
        {
            _cachedTargetHwnd = hwnd;
            StartRecording();
            return;
        }

        prev = Interlocked.CompareExchange(ref _recordingState, (int)RecordingState.Processing, (int)RecordingState.Recording);
        if (prev == (int)RecordingState.Recording)
            StopAndProcess();
    }

    private void OnStartRecordingRequested(object? sender, EventArgs e)
    {
        if (_trayService != null)
            _cachedTargetHwnd = _trayService.CachedForegroundHwnd;

        if (_cachedTargetHwnd == 0)
        {
            _trayService?.ShowBalloonTip("Kein Zielfenster erkannt",
                "Cursor bitte vorher in ein Textfeld positionieren, dann erneut starten.");
            return;
        }

        int prev = Interlocked.CompareExchange(ref _recordingState, (int)RecordingState.Recording, (int)RecordingState.Idle);
        if (prev == (int)RecordingState.Idle)
            StartRecording();
    }

    private void OnStopRecordingRequested(object? sender, EventArgs e)
    {
        int prev = Interlocked.CompareExchange(ref _recordingState, (int)RecordingState.Processing, (int)RecordingState.Recording);
        if (prev == (int)RecordingState.Recording)
            StopAndProcess();
    }

    private void OnModeChangeRequested(object? sender, DictationMode mode)
    {
        _currentMode = mode;
        _logger?.LogInformation("Mode changed to {Mode}", mode);
    }

    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var oldHotkey = _settingsService?.Settings.Hotkey;
            var window = _serviceProvider?.GetRequiredService<SettingsWindow>();
            if (window == null) return;
            window.ShowDialog();

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

    private void StartRecording()
    {
        try
        {
            var tempDir = _settingsService?.Settings.TempAudioPath
                ?? Path.Combine(Path.GetTempPath(), "Schnack");
            var wavPath = Path.Combine(tempDir, $"rec_{DateTime.UtcNow:yyyyMMdd_HHmmss}.wav");

            _recordingService!.StartRecording(wavPath);
            _trayService?.UpdateState(RecordingState.Recording);
            _floatingRecordUi?.SetRecordingState(RecordingState.Recording);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex.GetType().Name + ": Failed to start recording");
            Interlocked.Exchange(ref _recordingState, (int)RecordingState.Idle);
            _trayService?.UpdateState(RecordingState.Idle);
            _floatingRecordUi?.SetRecordingState(RecordingState.Idle);
            _trayService?.ShowBalloonTip("Mikrofon-Fehler",
                "Aufnahme konnte nicht gestartet werden. Einstellungen prüfen.");
        }
    }

    private void StopAndProcess()
    {
        _pipelineCts = new CancellationTokenSource();
        var token = _pipelineCts.Token;
        // Pipeline darf nicht auf dem UI-Thread laufen: StopRecording() blockiert mit Wait() und
        // verklemmt sich mit Tray/WPF und NAudio-Callbacks.
        _ = Task.Run(async () =>
        {
            try
            {
                await RunPipelineAsync(token);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex.GetType().Name + ": Pipeline task faulted");
            }
        }, token);
    }

    private async Task RunPipelineAsync(CancellationToken ct)
    {
        string? wavPath = null;
        try
        {
            _logger?.LogDebug("Pipeline start, thread {Thread}", Environment.CurrentManagedThreadId);
            wavPath = _recordingService!.StopRecording();
            _logger?.LogDebug("Recording stop completed, wav path length {Len}", wavPath.Length);
            _trayService?.UpdateState(RecordingState.Processing);
            _floatingRecordUi?.SetRecordingState(RecordingState.Processing);

            // Per-run service resolution based on current BackendProvider setting
            var backend = _settingsService!.Settings.BackendProvider;
            var transcriptionService = _serviceProvider!.GetRequiredKeyedService<ITranscriptionService>(backend.ToString());
            var postProcessingService = _serviceProvider!.GetRequiredKeyedService<IPostProcessingService>(backend.ToString());

            _logger?.LogDebug("Transcription phase, backend: {Backend}", backend);
            var transcript = await transcriptionService.TranscribeAsync(wavPath, ct);
            _logger?.LogDebug("Transcription finished, empty: {Empty}", string.IsNullOrWhiteSpace(transcript));

            if (string.IsNullOrWhiteSpace(transcript))
            {
                _trayService?.ShowBalloonTip("Schnack", "Keine Sprache erkannt.");
                return;
            }

            _logger?.LogDebug("Post-processing phase");
            var result = await postProcessingService.ProcessAsync(transcript, _currentMode, ct);
            _logger?.LogDebug("Text insertion phase");
            if (_cachedTargetHwnd == 0)
            {
                await Application.Current.Dispatcher.InvokeAsync(() => Clipboard.SetText(result.Text));
                _trayService?.ShowBalloonTip("Kein Zielfenster",
                    "Text liegt in der Zwischenablage – bitte mit Strg+V einfügen.");
                return;
            }
            await _textInsertionService!.InsertTextAsync(_cachedTargetHwnd, result.Text, ct);

            if (result.IsPossiblyTruncated)
            {
                _trayService?.ShowBalloonTip(
                    "Hinweis",
                    "Die Antwort könnte abgeschnitten sein. 'Max Tokens' in den Einstellungen erhoehen.");
            }
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("Pipeline cancelled");
        }
        catch (HttpRequestException ex) when (ex.Message.StartsWith("OpenAI:", StringComparison.Ordinal))
        {
            _trayService?.ShowBalloonTip("OpenAI", "OpenAI-Anfrage abgelehnt oder API-Key ungültig.");
        }
        catch (HttpRequestException ex) when (
            ex.StatusCode == HttpStatusCode.Unauthorized ||
            ex.StatusCode == HttpStatusCode.Forbidden)
        {
            _trayService?.ShowBalloonTip("API-Key ungültig", "API-Key ungültig oder abgelaufen.");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _trayService?.ShowBalloonTip("Rate Limit", "Rate Limit erreicht – kurz warten.");
        }
        catch (HttpRequestException)
        {
            _trayService?.ShowBalloonTip("Netzwerk", "Keine Verbindung zum API-Backend.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Aufnahme konnte nicht", StringComparison.Ordinal))
        {
            _trayService?.ShowBalloonTip("Mikrofon antwortet nicht",
                "Mikrofon prüfen – Verbindung oder Treiber wurde unterbrochen.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Kein Zielfenster", StringComparison.Ordinal))
        {
            _trayService?.ShowBalloonTip("Kein Zielfenster",
                "Zielfenster konnte nicht erkannt werden. Text liegt in der Zwischenablage – bitte mit Strg+V einfügen.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("OPENAI_API_KEY", StringComparison.Ordinal))
        {
            _trayService?.ShowBalloonTip("OpenAI API-Key fehlt",
                "OPENAI_API_KEY setzen oder OpenAI-Key in den Einstellungen speichern.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("ANTHROPIC_API_KEY", StringComparison.Ordinal))
        {
            _trayService?.ShowBalloonTip("Anthropic API-Key fehlt",
                "ANTHROPIC_API_KEY nicht gesetzt. Umgebungsvariable setzen und neu starten.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("nicht heruntergeladen", StringComparison.Ordinal))
        {
            _trayService?.ShowBalloonTip("Whisper-Modell fehlt",
                "Whisper-Modell in den Einstellungen herunterladen.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex.GetType().Name + ": Pipeline error");
            _trayService?.ShowBalloonTip("Fehler", "Verarbeitung fehlgeschlagen.");
        }
        finally
        {
            _logger?.LogDebug("Pipeline finally, resetting state");
            if (wavPath != null && File.Exists(wavPath))
            {
                try { File.Delete(wavPath); }
                catch { /* ignore cleanup failures */ }
            }
            Interlocked.Exchange(ref _recordingState, (int)RecordingState.Idle);
            _trayService?.UpdateState(RecordingState.Idle);
            _floatingRecordUi?.SetRecordingState(RecordingState.Idle);
        }
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

        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<AboutWindow>();
        services.AddTransient<FirstRunWindow>();

        return services.BuildServiceProvider();
    }

    private void CleanupAndShutdown()
    {
        _logger?.LogInformation("Schnack shutting down");
        _pipelineCts?.Cancel();
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
