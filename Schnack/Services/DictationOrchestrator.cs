using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schnack.Localization;
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

    public DictationMode CurrentMode { get; set; } = DictationMode.Correct;

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
                _trayService.ShowBalloonTip(Strings.Balloon_AppTitle, Strings.Balloon_NoSpeech);
                return;
            }

            _logger.LogDebug("Post-processing phase");
            var result = await postProcessingService.ProcessAsync(transcript, CurrentMode, ct);
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
