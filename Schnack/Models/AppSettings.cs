namespace Schnack.Models;

public record AppSettings
{
    /// <summary>4 = aktuelles Schema; kleiner oder fehlend = ältere Datei (Migration in JsonSettingsService).</summary>
    public int SettingsSchema { get; init; } = 4;

    /// <summary>KI-Dienst für die Nachbearbeitung. Die Spracherkennung läuft immer lokal.
    /// Bewusst ein eigener JSON-Name: das frühere Feld backendProvider kannte den Wert "local",
    /// den dieses Enum nicht abbilden kann — ein Mapping darauf würde beim Laden werfen.</summary>
    public AiService AiService { get; init; } = AiService.OpenAi;

    /// <summary>Transkript durch den KI-Dienst glätten (und im Übersetzungsmodus übersetzen) lassen.
    /// Aus oder ohne hinterlegten Schlüssel: der Rohtext der Spracherkennung wird eingefügt.</summary>
    public bool TextSmoothing { get; init; } = true;

    /// <summary>Sprache der Oberfläche (Tray, Dialoge, Meldungen).</summary>
    public AppLanguage UiLanguage { get; init; } = AppLanguage.De;

    /// <summary>Sprache, in der diktiert wird. Steuert STT und die Übersetzungsrichtung.</summary>
    public AppLanguage DictationLanguage { get; init; } = AppLanguage.De;

    /// <summary>Aktiver Modus beim Start: "correct" oder "translate".</summary>
    public string DefaultMode { get; init; } = "correct";

    /// <summary>OpenAI Chat-Modell für die Nachbearbeitung (API chat/completions), nur bei AiService.OpenAi.</summary>
    public string OpenAiChatModel { get; init; } = "gpt-4o-mini";

    /// <summary>Anthropic Claude-Modell für die Nachbearbeitung, nur bei AiService.Claude.</summary>
    public string ClaudeModel { get; init; } = "claude-haiku-4-5";
    public int ClaudeMaxTokens { get; init; } = 4096;

    /// <summary>Maximale Ausgabelänge für OpenAI Chat (chat/completions max_tokens).</summary>
    public int OpenAiChatMaxTokens { get; init; } = 4096;

    /// <summary>Whisper.net Modell-Dateiname ohne Präfix/Extension: large-v3-turbo, medium, base.</summary>
    public string WhisperModel { get; init; } = "large-v3-turbo";

    /// <summary>GPU (Vulkan) für die Whisper-Inferenz nutzen. Wirkung ist treiberabhängig;
    /// Whisper.net fällt selbsttätig auf CPU zurück, wenn keine Vulkan-Runtime lädt.</summary>
    public bool WhisperUseGpu { get; init; } = false;

    /// <summary>Whisper-Modell beim App-Start vorladen und einmal aufwärmen, damit das erste
    /// Diktat nicht auf das Laden der Modelldatei wartet.</summary>
    public bool WhisperPreload { get; init; } = true;

    public int? MicrophoneDeviceId { get; init; } = null;
    public string Hotkey { get; init; } = "Ctrl+Alt+S";
    public bool RestoreClipboard { get; init; } = true;

    /// <summary>Wenn true: Text per SendInput Unicode (kein Clipboard für den Inhalt). Empfohlen.</summary>
    public bool PreferClipboardFreeInsertion { get; init; } = true;

    /// <summary>Eigennamen und Fachbegriffe, die die Spracherkennung bevorzugt erkennen soll.
    /// Wirkt als Vorab-Kontext der Erkennung und als Schreibvorgabe in der Nachbearbeitung.</summary>
    public string[] Vocabulary { get; init; } = [];

    public bool DebugLogging { get; init; } = false;
    public string? TempAudioPath { get; init; } = null;

    /// <summary>Letzte Position des schwebenden Aufnahme-Buttons (Pixel, Bildschirmkoordinaten).</summary>
    public double? FloatingButtonLeft { get; init; }
    public double? FloatingButtonTop { get; init; }
}
