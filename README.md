# Schnack – Voice-to-Text Tray-Tool

Internes Windows-11-Tray-Tool für persönliche Nutzung. Nimmt gesprochenen deutschen Text per globalem Hotkey auf, transkribiert ihn per **OpenAI Whisper API** (Cloud-STT) und schickt das Ergebnis zur zurückhaltenden Korrektur oder Übersetzung ins Englische an die **Anthropic Claude API**. Der finale Text wird automatisch ins zuvor aktive Textfeld eingefügt.

---

## Voraussetzungen

- Windows 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Mikrofon
- Anthropic API-Key (Claude-Verarbeitung)
- OpenAI API-Key (Spracherkennung)

---

## Setup

### 1. API-Keys setzen

**Anthropic (Claude – Textkorrektur / Übersetzung):**

```powershell
# In der aktuellen PowerShell-Session (nur temporär):
$env:ANTHROPIC_API_KEY = “sk-ant-...”

# Dauerhaft für den aktuellen Benutzer:
setx ANTHROPIC_API_KEY “sk-ant-...”
# Danach Terminal und VS Code neu starten
```

**OpenAI (Whisper – Spracherkennung):**

```powershell
$env:OPENAI_API_KEY = “sk-...”
# oder: setx OPENAI_API_KEY “sk-...”
```

Alternativ: Einstellungen öffnen → jeweiligen API-Key eingeben → **Speichern** (DPAPI-verschlüsselt).

---

## Build und Start

```powershell
git clone <repo-url>
cd Schnack

dotnet restore
dotnet build

# Starten (Entwicklung):
dotnet run --project Schnack

# Release-Build für lokale Nutzung:
dotnet publish Schnack -c Release -r win-x64 --self-contained false
```

Tests ausführen:

```powershell
dotnet test Schnack.Tests/Schnack.Tests.csproj
```

---

## Bedienung

### Hotkey (Standard: `Ctrl+Alt+S`)

1. Cursor in ein beliebiges Textfeld setzen (z.B. Notepad, Browser, E-Mail)
2. `Ctrl+Alt+S` drücken → Aufnahme startet (Tray-Icon zeigt „Aufnahme läuft…")
3. Sprechen
4. `Ctrl+Alt+S` erneut drücken → Transkription + Verarbeitung → Text wird eingefügt

### Tray-Menü

Rechtsklick auf das Tray-Icon:

| Eintrag | Funktion |
|---------|---------|
| Aufnahme starten | Startet die Aufnahme |
| Aufnahme stoppen | Stoppt und verarbeitet |
| Verarbeitung abbrechen | Nur während Transkription/API: bricht die laufende Pipeline ab |
| Deutsch korrigieren | Wechselt zu Modus `de_correct` |
| Deutsch → Englisch | Wechselt zu Modus `de_to_en` |
| Einstellungen… | Öffnet den Einstellungs-Dialog |
| Beenden | Beendet das Programm |

### Modi

| Modus | Beschreibung |
|-------|-------------|
| `de_correct` | Korrigiert Rechtschreibung, Zeichensetzung und offensichtliche Diktierfehler. Inhalt bleibt unverändert. |
| `de_to_en` | Übersetzt in natürliches, klares Englisch. Bedeutung bleibt vollständig erhalten. |

---

## Einstellungen

Einstellungsdatei: `%APPDATA%\Schnack\settings.json`

| Feld | Standard | Beschreibung |
|------|---------|-------------|
| `settingsSchema` | `1` | Interne Version der Einstellungsdatei (Migration) |
| `defaultMode` | `de_correct` | Aktiver Modus beim Start |
| `openAiTranscriptionModel` | `gpt-4o-mini-transcribe` | OpenAI Whisper-Modell für STT |
| `claudeModel` | `claude-haiku-4-5` | Claude-Modell für die Verarbeitung |
| `claudeMaxTokens` | `4096` | Maximale Ausgabelänge |
| `hotkey` | `Ctrl+Alt+S` | Globaler Aufnahme-Hotkey |
| `restoreClipboard` | `true` | Vorherigen Clipboard-Inhalt wiederherstellen |
| `preferClipboardFreeInsertion` | `true` | Unicode-Tastatur statt Clipboard+Strg+V (empfohlen) |
| `debugLogging` | `false` | Serilog auf **Debug** (inkl. Pipeline-/NAudio-Phasen ohne Transkripte). Alternativ Umgebungsvariable `SCHNACK_DEBUG=1`. Nach Änderung in den Einstellungen wirkt die Stufe sofort (kein App-Neustart nötig). |
| `microphoneDeviceId` | `null` | Mikrofon-ID (null = System-Standard) |

Logs: `%APPDATA%\Schnack\logs\schnack-<datum>.log` (7 Tage Aufbewahrung)

**Hotkey reagiert nicht:** oft `HotkeyAlreadyRegisteredException` (zweite Schnack-Instanz oder anderes Programm mit derselben Kombination) — andere Instanz beenden oder in den Einstellungen einen anderen Hotkey wählen.

**Tray „Aufnahme stoppen“ hängt:** wurde behoben, indem die Verarbeitungs-Pipeline nicht mehr auf dem UI-Thread blockiert.

---

## Bekannte Einschränkungen

- **Nur Text-Clipboard**: Beim Einfügen des Textes wird nur der vorherige Text-Inhalt des Clipboards gesichert und wiederhergestellt. Bilder, Dateien und andere Clipboard-Formate gehen verloren, wenn `restoreClipboard = true` aktiv ist.
- **Erststart-Download**: Das Whisper-Modell (ca. 1,6 GB für `large-v3-turbo`) muss einmalig heruntergeladen werden.
- **Kein Auto-Stop**: Es gibt kein Voice Activity Detection (VAD). Die Aufnahme muss manuell gestoppt werden.
- **Single-Instance**: Nur eine Instanz von Schnack kann gleichzeitig laufen.
- **SetForegroundWindow**: In seltenen Fällen (z.B. bestimmte Sicherheits-Software) kann das automatische Fokussieren des Zielfensters fehlschlagen. Der Text liegt dann im Clipboard und muss manuell mit `Strg+V` eingefügt werden.

---

## Privacy-Hinweis

Schnack verwendet derzeit das **OpenAI-Backend**:

- **Audio wird an OpenAI gesendet.** Die WAV-Aufnahme wird zur Transkription an die OpenAI Whisper API übertragen.
- **Das Transkript wird an Anthropic gesendet.** Der transkribierte Text wird zur Korrektur bzw. Übersetzung an die Anthropic Claude API übermittelt.
- Logs enthalten keine Transkripte, keine API-Keys und keine Audiodaten.
- Temporäre WAV-Dateien (`%TEMP%\Schnack\`) werden nach der Verarbeitung automatisch gelöscht.

> Ein lokales Whisper-Backend (Audio bleibt auf dem Rechner, nur Transkript geht an Anthropic) ist für eine spätere Version geplant.

---

## NuGet-Abhängigkeiten

| Paket | Lizenz | Zweck |
|-------|--------|-------|
| H.NotifyIcon.Wpf | MIT | WPF Tray-Icon |
| NHotkey.Wpf | MIT | Globaler Hotkey |
| NAudio | MIT | Audio-Aufnahme |
| Microsoft.Extensions.* | MIT | DI, Logging, HTTP |
| Serilog.* | Apache 2.0 | File-Logging |
| System.Security.Cryptography.ProtectedData | MIT | DPAPI-Verschlüsselung |
