using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schnack.Localization;
using Schnack.Models;
using Schnack.Services.Internal;

namespace Schnack.Services;

public sealed class DictationOrchestrator : IDictationOrchestrator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISettingsService _settingsService;
    private readonly ISecretService _secretService;
    private readonly ITranscriptionService _transcriptionService;
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

    // Ohne Schlüssel läuft die Pipeline still über den Passthrough. Damit der Nutzer nicht
    // rätselt, warum der Text ungeglättet ankommt, gibt es genau einen Hinweis pro Sitzung.
    private bool _missingKeyReported;

    public DictationMode CurrentMode { get; set; } = DictationMode.Correct;

    public RecordingState State => (RecordingState)_recordingState;

    public DictationOrchestrator(
        IServiceProvider serviceProvider,
        ISettingsService settingsService,
        ISecretService secretService,
        ITranscriptionService transcriptionService,
        IRecordingService recordingService,
        ITextInsertionService textInsertionService,
        ITrayService trayService,
        IFloatingRecordUi floatingRecordUi,
        ILogger<DictationOrchestrator> logger)
    {
        _serviceProvider = serviceProvider;
        _settingsService = settingsService;
        _secretService = secretService;
        _transcriptionService = transcriptionService;
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
            // InvariantCulture: der Dateiname darf nicht von der eingestellten Kultur abhängen
            var wavPath = Path.Combine(tempDir,
                $"rec_{DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture)}.wav");

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
            _trayService.ShowBalloonTip(Strings.Balloon_MicErrorTitle, Strings.Balloon_MicErrorStart);
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

            // Nachbearbeitung pro Lauf auflösen (Keyed DI, bewusste Ausnahme von der
            // Konstruktor-Injection — Dienst und Glättung sind zur Laufzeit umschaltbar).
            // Ohne Glättung liefert SmoothingPolicy den Passthrough-Schlüssel; die Auflösung
            // bleibt dadurch einheitlich, ohne Sonderpfad in der State-Machine.
            var settings = _settingsService.Settings;
            var keyAvailable = _secretService.HasKeyFor(settings.AiService);
            var smoothing = SmoothingPolicy.IsActive(settings, keyAvailable);
            var postProcessingService = _serviceProvider.GetRequiredKeyedService<IPostProcessingService>(
                SmoothingPolicy.PostProcessingKey(settings, keyAvailable));

            if (settings.TextSmoothing && !keyAvailable)
                ReportMissingKeyOnce(settings.AiService);

            _logger.LogDebug("Transcription phase, smoothing: {Smoothing}, service: {Service}",
                smoothing, settings.AiService);
            var phaseStarted = Stopwatch.GetTimestamp();
            var transcript = await _transcriptionService.TranscribeAsync(wavPath, ct);
            _logger.LogDebug("Transcription finished in {Ms} ms, empty: {Empty}",
                (long)Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds,
                string.IsNullOrWhiteSpace(transcript));

            if (string.IsNullOrWhiteSpace(transcript))
            {
                _trayService.ShowBalloonTip(Strings.Balloon_AppTitle, Strings.Balloon_NoSpeech);
                return;
            }

            _logger.LogDebug("Post-processing phase");
            phaseStarted = Stopwatch.GetTimestamp();
            // Effektiver Modus aus den Settings ableiten statt aus der mutablen CurrentMode-
            // Property: ohne Glättung kann niemand übersetzen, und die Property wird von drei
            // Stellen gesetzt — ein vergessener Pfad hieße sonst still nicht übersetzt.
            var effectiveMode = smoothing
                ? DictationChoice.FromSettings(settings).Mode
                : DictationMode.Correct;
            var result = await postProcessingService.ProcessAsync(transcript, effectiveMode, ct);
            _logger.LogDebug("Post-processing finished in {Ms} ms",
                (long)Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds);

            if (effectiveMode == DictationMode.Correct)
                WarnIfLengthDeviates(transcript, result.Text);

            _logger.LogDebug("Text insertion phase");
            if (_cachedTargetHwnd == 0)
            {
                await Application.Current.Dispatcher.InvokeAsync(() => Clipboard.SetText(result.Text));
                _trayService.ShowBalloonTip(Strings.Error_NoTargetWindowTitle, Strings.Error_NoTargetWindowClipboard);
                return;
            }
            await _textInsertionService.InsertTextAsync(_cachedTargetHwnd, result.Text, ct);

            if (result.IsPossiblyTruncated)
            {
                _trayService.ShowBalloonTip(Strings.Balloon_HintTitle, Strings.Balloon_Truncated);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Pipeline cancelled");
        }
        catch (SchnackException ex)
        {
            _logger.LogWarning("Pipeline error: {Code}", ex.Code);
            ShowErrorBalloon(ex.Code);
        }
        catch (HttpRequestException)
        {
            _trayService.ShowBalloonTip(Strings.Error_NetworkTitle, Strings.Error_Network);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.GetType().Name + ": Pipeline error");
            _trayService.ShowBalloonTip(Strings.Error_GenericTitle, Strings.Error_Generic);
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

    /// <summary>
    /// Glätten darf die Länge kaum verändern. Weicht sie stark ab, hat das Sprachmodell
    /// vermutlich geantwortet statt korrigiert oder etwas weggelassen — beim Gegenlesen ist das
    /// leicht zu übersehen, im Log nicht.
    /// Nur bei Korrektur sinnvoll: eine Übersetzung darf die Länge verschieben.
    /// Geloggt werden ausschließlich Zeichenzahlen, nie Inhalt.
    /// </summary>
    private void WarnIfLengthDeviates(string transcript, string result)
    {
        // Kurze Diktate schwanken relativ stark; der Sockel verhindert Fehlalarme.
        const int MinLength = 40;
        if (transcript.Length < MinLength)
            return;

        var ratio = result.Length / (double)transcript.Length;
        if (ratio is >= 0.6 and <= 1.4)
            return;

        _logger.LogWarning(
            "Smoothing changed the length unexpectedly: {Before} -> {After} chars (factor {Ratio:F2}). " +
            "The model may have answered instead of correcting.",
            transcript.Length, result.Length, ratio);
    }

    /// <summary>Einmal pro Sitzung darauf hinweisen, dass ohne Schlüssel nicht geglättet wird.</summary>
    private void ReportMissingKeyOnce(AiService service)
    {
        if (_missingKeyReported)
            return;
        _missingKeyReported = true;

        _logger.LogWarning("Smoothing requested but no key for {Service}; inserting raw text", service);
        _trayService.ShowBalloonTip(Strings.Balloon_AppTitle, Strings.Balloon_NoSmoothingWithoutKey);
    }

    private void ShowErrorBalloon(SchnackError code)
    {
        var (title, message) = code switch
        {
            SchnackError.MicrophoneStopTimeout => (Strings.Error_MicTimeoutTitle, Strings.Error_MicTimeout),
            SchnackError.NoTargetWindow => (Strings.Error_NoTargetWindowTitle, Strings.Error_NoTargetWindow),
            SchnackError.MissingOpenAiKey => (Strings.Error_MissingOpenAiKeyTitle, Strings.Error_MissingOpenAiKey),
            SchnackError.MissingAnthropicKey => (Strings.Error_MissingAnthropicKeyTitle, Strings.Error_MissingAnthropicKey),
            SchnackError.WhisperModelMissing => (Strings.Error_WhisperModelMissingTitle, Strings.Error_WhisperModelMissing),
            SchnackError.ApiKeyInvalid => (Strings.Error_ApiKeyInvalidTitle, Strings.Error_ApiKeyInvalid),
            SchnackError.RateLimit => (Strings.Error_RateLimitTitle, Strings.Error_RateLimit),
            SchnackError.EmptyApiResponse => (Strings.Error_GenericTitle, Strings.Error_EmptyResponse),
            _ => (Strings.Error_GenericTitle, Strings.Error_Generic)
        };
        _trayService.ShowBalloonTip(title, message);
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
