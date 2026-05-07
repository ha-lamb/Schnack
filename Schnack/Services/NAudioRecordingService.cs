using NAudio.Wave;
using Microsoft.Extensions.Logging;
using Schnack.Models;

namespace Schnack.Services;

public sealed class NAudioRecordingService : IRecordingService
{
    private readonly ISettingsService _settings;
    private readonly ILogger<NAudioRecordingService> _logger;

    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _currentFilePath;
    private TaskCompletionSource<string>? _stopTcs;
    private bool _disposed;

    public bool IsRecording => _waveIn != null;

    public NAudioRecordingService(ISettingsService settings, ILogger<NAudioRecordingService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public void StartRecording(string filePath)
    {
        if (_waveIn != null)
            throw new InvalidOperationException("Already recording");

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        _currentFilePath = filePath;
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1),
            DeviceNumber = _settings.Settings.MicrophoneDeviceId ?? -1
        };

        _writer = new WaveFileWriter(filePath, _waveIn.WaveFormat);
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStopped;
        _waveIn.StartRecording();

        _logger.LogInformation("Recording started");
    }

    public string StopRecording()
    {
        if (_waveIn == null || _currentFilePath == null)
            throw new InvalidOperationException("Not recording");

        var path = _currentFilePath;
        _stopTcs = new TaskCompletionSource<string>();

        if (_settings.Settings.DebugLogging)
            _logger.LogDebug("StopRecording: StopRecording() vor Wait, thread {Thread}", Environment.CurrentManagedThreadId);

        // This triggers OnRecordingStopped asynchronously on NAudio's internal thread
        _waveIn.StopRecording();

        // Block until OnRecordingStopped has flushed and closed the WaveFileWriter
        // so the STT backend receives a complete WAV file.
        // 5s Timeout: Schutz gegen hängenden NAudio-Treiber (z.B. abgestecktes USB-Mikrofon).
        if (!_stopTcs.Task.Wait(TimeSpan.FromSeconds(5)))
        {
            _logger.LogError("Recording stop timeout after 5s — NAudio did not signal completion");
            throw new InvalidOperationException("Aufnahme konnte nicht sauber beendet werden.");
        }

        if (_settings.Settings.DebugLogging)
            _logger.LogDebug("StopRecording: Wait beendet, thread {Thread}", Environment.CurrentManagedThreadId);

        return path;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _writer?.Write(e.Buffer, 0, e.BytesRecorded);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // Close writer BEFORE signalling completion — transcription needs the complete file
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;

        _waveIn?.Dispose();
        _waveIn = null;

        var bytes = _currentFilePath != null && File.Exists(_currentFilePath)
            ? new FileInfo(_currentFilePath).Length
            : 0;
        _logger.LogInformation("Recording stopped, file size: {Bytes} bytes", bytes);

        if (e.Exception != null)
            _logger.LogError(e.Exception.GetType().Name + ": Recording stopped with error");

        _stopTcs?.TrySetResult(_currentFilePath ?? string.Empty);
        _stopTcs = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _waveIn?.StopRecording();
        // Give OnRecordingStopped time to flush
        _stopTcs?.Task.Wait(TimeSpan.FromSeconds(2));
        _writer?.Dispose();
        _waveIn?.Dispose();
    }
}
