namespace Schnack.Models;

public record AppSettings
{
    /// <summary>2 = aktuelles Schema; 0/1 oder fehlend = ältere Datei (Migration in JsonSettingsService).</summary>
    public int SettingsSchema { get; init; } = 2;

    /// <summary>Gewählter Backend-Stack: OpenAi (Cloud-STT + OpenAI-Chat) oder Claude (Whisper.net + Anthropic).</summary>
    public BackendProvider BackendProvider { get; init; } = BackendProvider.OpenAi;

    public string DefaultMode { get; init; } = "de_correct";

    /// <summary>OpenAI Speech-to-Text Modell (API audio/transcriptions).</summary>
    public string OpenAiTranscriptionModel { get; init; } = "gpt-4o-mini-transcribe";

    /// <summary>OpenAI Chat-Modell für Textverarbeitung (API chat/completions), nur bei BackendProvider.OpenAi.</summary>
    public string OpenAiChatModel { get; init; } = "gpt-4o-mini";

    /// <summary>Anthropic Claude-Modell für Textverarbeitung, nur bei BackendProvider.Claude.</summary>
    public string ClaudeModel { get; init; } = "claude-haiku-4-5";
    public int ClaudeMaxTokens { get; init; } = 4096;

    /// <summary>Maximale Ausgabelänge für OpenAI Chat (chat/completions max_tokens).</summary>
    public int OpenAiChatMaxTokens { get; init; } = 4096;

    /// <summary>Whisper.net Modell-Dateiname ohne Präfix/Extension: large-v3-turbo, medium, base.</summary>
    public string WhisperModel { get; init; } = "large-v3-turbo";

    /// <summary>CUDA-GPU für Whisper-Inferenz nutzen (erfordert Whisper.net.Runtime.Cuda.Windows).</summary>
    public bool WhisperUseGpu { get; init; } = false;

    public int? MicrophoneDeviceId { get; init; } = null;
    public string Hotkey { get; init; } = "Ctrl+Alt+S";
    public bool RestoreClipboard { get; init; } = true;

    /// <summary>Wenn true: Text per SendInput Unicode (kein Clipboard für den Inhalt). Empfohlen.</summary>
    public bool PreferClipboardFreeInsertion { get; init; } = true;

    public bool DebugLogging { get; init; } = false;
    public string? TempAudioPath { get; init; } = null;

    /// <summary>Letzte Position des schwebenden Aufnahme-Buttons (Pixel, Bildschirmkoordinaten).</summary>
    public double? FloatingButtonLeft { get; init; }
    public double? FloatingButtonTop { get; init; }
}
