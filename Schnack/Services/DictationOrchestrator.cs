using System.Net;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schnack.Models;

namespace Schnack.Services;

public sealed class DictationOrchestrator : IDictationOrchestrator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISettingsService _settingsService;
    private readonly IRecordingService _recordingService;
    private readonly ITextInsertionService _textInsertionService;
    private readonly ITrayService _trayService;
    private readonly IFloatingRecordUi _floatingRecordUi;
    private readonly ILogger<DictationOrchestrator> _logger;

    // Thread-safe state: values from RecordingState enum
    private int _recordingState;
    private nint _cachedTargetHwnd;
    private CancellationTokenSource? _pipelineCts;
    private bool _disposed;

    public DictationMode CurrentMode { get; set; } = DictationMode.DeCorrect;

    public RecordingState State => (RecordingState)_recordingState;

    public DictationOrchestrator(
        IServiceProvider serviceProvider,
        ISettingsService settingsService,
        IRecordingService recordingService,
        ITextInsertionService textInsertionService,
        ITrayService trayService,
        IFloatingRecordUi floatingRecordUi,
        ILogger<DictationOrchestrator> logger)
    {
        _serviceProvider = serviceProvider;
        _settingsService = settingsService;
        _recordingService = recordingService;
        _textInsertionService = textInsertionService;
        _trayService = trayService;
        _floatingRecordUi = floatingRecordUi;
        _logger = logger;
    }

    public Task ToggleRecordingAsync(nint foregroundHwnd)
    {
        int prev = Interlocked.CompareExchange(ref _recordingState, (int)RecordingState.Recording, (int)RecordingState.Idle);
        if (prev == (int)RecordingState.Idle)
        {
            _cachedTargetHwnd = foregroundHwnd;
            StartRecording();
            return Task.CompletedTask;
        }

        prev = Interlocked.CompareExchange(ref _recordingState, (int)RecordingState.Processing, (int)RecordingState.Recording);
        if (prev == (int)RecordingState.Recording)
            return StopAndProcessAsync();

        // Processing läuft bereits — Toggle wird ignoriert
        return Task.CompletedTask;
    }

    private void StartRecording()
    {
        try
        {
            var tempDir = _settingsService.Settings.TempAudioPath
                ?? Path.Combine(Path.GetTempPath(), "Schnack");
            var wavPath = Path.Combine(tempDir, $"rec_{DateTime.UtcNow:yyyyMMdd_HHmmss}.wav");

            _recordingService.StartRecording(wavPath);
            _trayService.UpdateState(RecordingState.Recording);
            _floatingRecordUi.SetRecordingState(RecordingState.Recording);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.GetType().Name + ": Failed to start recording");
            Interlocked.Exchange(ref _recordingState, (int)RecordingState.Idle);
            _trayService.UpdateState(RecordingState.Idle);
            _floatingRecordUi.SetRecordingState(RecordingState.Idle);
            _trayService.ShowBalloonTip("Mikrofon-Fehler",
                "Aufnahme konnte nicht gestartet werden. Einstellungen prüfen.");
        }
    }

    private Task StopAndProcessAsync()
    {
        // Vorheriger Lauf ist sicher beendet (State-Machine erlaubt Stop nur nach Idle→Recording),
        // daher kann der alte CTS hier gefahrlos entsorgt werden.
        _pipelineCts?.Dispose();
        _pipelineCts = new CancellationTokenSource();
        var token = _pipelineCts.Token;

        // Pipeline darf nicht auf dem UI-Thread laufen: StopRecording() blockiert mit Wait() und
        // verklemmt sich mit Tray/WPF und NAudio-Callbacks.
        return Task.Run(async () =>
        {
            try
            {
                await RunPipelineAsync(token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.GetType().Name + ": Pipeline task faulted");
            }
        }, token);
    }

    private async Task RunPipelineAsync(CancellationToken ct)
    {
        string? wavPath = null;
        try
        {
            _logger.LogDebug("Pipeline start, thread {Thread}", Environment.CurrentManagedThreadId);
            wavPath = _recordingService.StopRecording();
            _logger.LogDebug("Recording stop completed, wav path length {Len}", wavPath.Length);
            _trayService.UpdateState(RecordingState.Processing);
            _floatingRecordUi.SetRecordingState(RecordingState.Processing);

            // Auflösung pro Lauf anhand des aktuellen BackendProvider-Settings (Keyed DI,
            // bewusste Ausnahme von der Konstruktor-Injection — Backend ist zur Laufzeit wechselbar).
            var backend = _settingsService.Settings.BackendProvider;
            var transcriptionService = _serviceProvider.GetRequiredKeyedService<ITranscriptionService>(backend.ToString());
            var postProcessingService = _serviceProvider.GetRequiredKeyedService<IPostProcessingService>(backend.ToString());

            _logger.LogDebug("Transcription phase, backend: {Backend}", backend);
            var transcript = await transcriptionService.TranscribeAsync(wavPath, ct);
            _logger.LogDebug("Transcription finished, empty: {Empty}", string.IsNullOrWhiteSpace(transcript));

            if (string.IsNullOrWhiteSpace(transcript))
            {
                _trayService.ShowBalloonTip("Schnack", "Keine Sprache erkannt.");
                return;
            }

            _logger.LogDebug("Post-processing phase");
            var result = await postProcessingService.ProcessAsync(transcript, CurrentMode, ct);
            _logger.LogDebug("Text insertion phase");
            if (_cachedTargetHwnd == 0)
            {
                await Application.Current.Dispatcher.InvokeAsync(() => Clipboard.SetText(result.Text));
                _trayService.ShowBalloonTip("Kein Zielfenster",
                    "Text liegt in der Zwischenablage – bitte mit Strg+V einfügen.");
                return;
            }
            await _textInsertionService.InsertTextAsync(_cachedTargetHwnd, result.Text, ct);

            if (result.IsPossiblyTruncated)
            {
                _trayService.ShowBalloonTip(
                    "Hinweis",
                    "Die Antwort könnte abgeschnitten sein. 'Max Tokens' in den Einstellungen erhoehen.");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Pipeline cancelled");
        }
        catch (HttpRequestException ex) when (ex.Message.StartsWith("OpenAI:", StringComparison.Ordinal))
        {
            _trayService.ShowBalloonTip("OpenAI", "OpenAI-Anfrage abgelehnt oder API-Key ungültig.");
        }
        catch (HttpRequestException ex) when (
            ex.StatusCode == HttpStatusCode.Unauthorized ||
            ex.StatusCode == HttpStatusCode.Forbidden)
        {
            _trayService.ShowBalloonTip("API-Key ungültig", "API-Key ungültig oder abgelaufen.");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _trayService.ShowBalloonTip("Rate Limit", "Rate Limit erreicht – kurz warten.");
        }
        catch (HttpRequestException)
        {
            _trayService.ShowBalloonTip("Netzwerk", "Keine Verbindung zum API-Backend.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Aufnahme konnte nicht", StringComparison.Ordinal))
        {
            _trayService.ShowBalloonTip("Mikrofon antwortet nicht",
                "Mikrofon prüfen – Verbindung oder Treiber wurde unterbrochen.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Kein Zielfenster", StringComparison.Ordinal))
        {
            _trayService.ShowBalloonTip("Kein Zielfenster",
                "Zielfenster konnte nicht erkannt werden. Text liegt in der Zwischenablage – bitte mit Strg+V einfügen.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("OPENAI_API_KEY", StringComparison.Ordinal))
        {
            _trayService.ShowBalloonTip("OpenAI API-Key fehlt",
                "OPENAI_API_KEY setzen oder OpenAI-Key in den Einstellungen speichern.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("ANTHROPIC_API_KEY", StringComparison.Ordinal))
        {
            _trayService.ShowBalloonTip("Anthropic API-Key fehlt",
                "ANTHROPIC_API_KEY nicht gesetzt. Umgebungsvariable setzen und neu starten.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("nicht heruntergeladen", StringComparison.Ordinal))
        {
            _trayService.ShowBalloonTip("Whisper-Modell fehlt",
                "Whisper-Modell in den Einstellungen herunterladen.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.GetType().Name + ": Pipeline error");
            _trayService.ShowBalloonTip("Fehler", "Verarbeitung fehlgeschlagen.");
        }
        finally
        {
            _logger.LogDebug("Pipeline finally, resetting state");
            if (wavPath != null && File.Exists(wavPath))
            {
                try { File.Delete(wavPath); }
                catch { /* ignore cleanup failures */ }
            }
            Interlocked.Exchange(ref _recordingState, (int)RecordingState.Idle);
            _trayService.UpdateState(RecordingState.Idle);
            _floatingRecordUi.SetRecordingState(RecordingState.Idle);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pipelineCts?.Cancel();
        _pipelineCts?.Dispose();
        _pipelineCts = null;
    }
}
