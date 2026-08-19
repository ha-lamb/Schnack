# Schnack – Voice-to-Text Tray-Tool

Internes Windows-11-Tray-Tool für persönliche Nutzung. Nimmt gesprochenen deutschen Text per globalem Hotkey oder schwebendem Button auf, transkribiert ihn und fügt das Ergebnis — zurückhaltend korrigiert oder ins Englische übersetzt — automatisch ins zuvor aktive Textfeld ein.

**Zwei wählbare Backends** (Einstellungen → Backend):

| Backend | Spracherkennung | Textverarbeitung | Privacy |
|---------|-----------------|------------------|---------|
| **OpenAI** (Standard) | OpenAI Cloud-STT | OpenAI Chat Completions | Audio + Transkript gehen an OpenAI |
| **Claude** | Whisper lokal (Whisper.net) | Anthropic Claude API | Audio bleibt lokal, nur Transkript geht an Anthropic |

---

## Voraussetzungen

- Windows 11 (x64)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64) — wird beim Start geprüft
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (nur für Entwicklung)
- Mikrofon
- Je nach Backend: OpenAI-API-Key **oder** Anthropic-API-Key
- Beim Claude-Backend: einmaliger Whisper-Modell-Download (ca. 1,6 GB für `large-v3-turbo`)

---

## Setup

### API-Key setzen (je nach Backend-Wahl)

```powershell
# OpenAI-Backend (Standard):
setx OPENAI_API_KEY "sk-..."

# Claude-Backend:
setx ANTHROPIC_API_KEY "sk-ant-..."

# Danach Terminal und VS Code neu starten
```

Alternativ: Einstellungen öffnen → API-Key eingeben → **Speichern** (wird DPAPI-verschlüsselt in `%APPDATA%\Schnack\` abgelegt).

---

## Build und Start

```powershell
git clone <repo-url>
cd Schnack

dotnet restore
dotnet build

# Starten (Entwicklung):
dotnet run --project Schnack

# Tests:
dotnet test
```

---

## Updates

Schnack prüft beim Start automatisch im Hintergrund auf neue Versionen (GitHub Releases).

- Bei verfügbarem Update: Tray-Notification + Menüeintrag „Update vX.Y.Z installieren".
- Klick auf den Eintrag lädt das Delta-Update (~5–20 MB) herunter und startet die App neu.
- Manueller Check über **Tray-Menü → Auf Updates prüfen**.
- Der App-Start wird durch den Update-Check nicht verzögert.

**Hinweis:** Solange das GitHub-Repo privat ist, schlägt der anonyme Update-Check still fehl — Updates funktionieren erst mit einem öffentlichen Repo.

**Privacy:** Der Update-Check sendet einen anonymen HTTPS-Request an `github.com/ha-lamb/Schnack`. Keine Telemetrie, keine persönlichen Daten.

Release bauen (für Maintainer): siehe [RELEASE.md](RELEASE.md).

---

## Bedienung

### Hotkey (Standard: `Ctrl+Alt+S`)

1. Cursor in ein beliebiges Textfeld setzen (Notepad, Browser, E-Mail, …)
2. `Ctrl+Alt+S` drücken → Aufnahme startet
3. Sprechen
4. `Ctrl+Alt+S` erneut drücken → Transkription + Verarbeitung → Text wird eingefügt

### Schwebender Aufnahme-Button

Über das Tray-Menü ein-/ausblendbar (Häkchen-Eintrag). Klick startet/stoppt die Aufnahme, Farben zeigen den Status (rot = Aufnahme, gelb = Verarbeitung). Per Maus frei verschiebbar; die Position bleibt über App-Neustarts erhalten.

### Tray-Menü

| Eintrag | Funktion |
|---------|---------|
| *Hinweis: Aufnahme über Hotkey oder schwebenden Button* | — |
| Deutsch korrigieren | Modus `de_correct` |
| Deutsch → Englisch | Modus `de_to_en` |
| Einstellungen… | Einstellungs-Dialog |
| Schwebender Aufnahme-Button | Button ein-/ausblenden (Häkchen) |
| Über Schnack… | Version, Datum, Lizenz |
| Auf Updates prüfen | Manueller Update-Check |
| Beenden | Programm beenden |

### Modi

| Modus | Beschreibung |
|-------|-------------|
| `de_correct` | Korrigiert Rechtschreibung, Zeichensetzung und offensichtliche Diktierfehler. Inhalt bleibt unverändert. |
| `de_to_en` | Übersetzt in natürliches, klares Englisch. Bedeutung bleibt vollständig erhalten. |

---

## Einstellungen

Einstellungsdatei: `%APPDATA%\Schnack\settings.json` (Schema-Version 2, automatische Migration)

| Feld | Standard | Beschreibung |
|------|---------|-------------|
| `backendProvider` | `openai` | Gewählter Stack: `openai` oder `claude` |
| `defaultMode` | `de_correct` | Aktiver Modus beim Start |
| `openAiTranscriptionModel` | `gpt-4o-mini-transcribe` | OpenAI-STT-Modell |
| `openAiChatModel` | `gpt-4o-mini` | OpenAI-Chat-Modell (Textverarbeitung) |
| `openAiChatMaxTokens` | `4096` | Maximale Ausgabelänge (OpenAI) |
| `claudeModel` | `claude-haiku-4-5` | Claude-Modell (Textverarbeitung) |
| `claudeMaxTokens` | `4096` | Maximale Ausgabelänge (Claude) |
| `whisperModel` | `large-v3-turbo` | Lokales Whisper-Modell (Claude-Backend) |
| `whisperUseGpu` | `false` | CUDA-GPU für Whisper nutzen |
| `hotkey` | `Ctrl+Alt+S` | Globaler Aufnahme-Hotkey |
| `restoreClipboard` | `true` | Vorherigen Clipboard-Text wiederherstellen |
| `preferClipboardFreeInsertion` | `true` | Unicode-Tastatur statt Clipboard+Strg+V (empfohlen) |
| `debugLogging` | `false` | Ausführliches Log (ohne Transkripte). Alternativ Env `SCHNACK_DEBUG=1`. Wirkt sofort. |
| `microphoneDeviceId` | `null` | Mikrofon (null = System-Standard) |

Logs: `%APPDATA%\Schnack\logs\schnack-<datum>.log` (7 Tage Aufbewahrung)

**Hotkey reagiert nicht:** meist ist die Kombination schon belegt (zweite Schnack-Instanz oder anderes Programm) — andere Instanz beenden oder in den Einstellungen einen anderen Hotkey wählen.

---

## Bekannte Einschränkungen

- **Nur Text-Clipboard:** Beim Clipboard-Einfügeweg wird nur vorheriger Text gesichert/wiederhergestellt; Bilder und Dateien im Clipboard gehen verloren (`restoreClipboard = true`).
- **Erststart-Download (Claude-Backend):** Das Whisper-Modell muss einmalig heruntergeladen werden (Einstellungen → Herunterladen).
- **Kein Auto-Stop:** Keine Voice Activity Detection — Aufnahme wird manuell gestoppt.
- **Single-Instance:** Nur eine Instanz gleichzeitig.
- **SetForegroundWindow:** In seltenen Fällen kann das automatische Fokussieren des Zielfensters fehlschlagen. Der Text liegt dann in der Zwischenablage und wird per Tray-Hinweis zum manuellen Einfügen (`Strg+V`) angeboten.

---

## Privacy

- **OpenAI-Backend:** Die WAV-Aufnahme geht zur Transkription an OpenAI; das Transkript wird dort auch korrigiert/übersetzt.
- **Claude-Backend:** Audio bleibt vollständig lokal (Whisper.net); nur das Transkript geht zur Korrektur/Übersetzung an Anthropic.
- Logs enthalten keine Transkripte, keine API-Keys, keine Audiodaten.
- Temporäre WAV-Dateien (`%TEMP%\Schnack\`) werden nach der Verarbeitung gelöscht.

---

## NuGet-Abhängigkeiten

| Paket | Lizenz | Zweck |
|-------|--------|-------|
| H.NotifyIcon.Wpf | MIT | WPF Tray-Icon |
| NHotkey.Wpf | MIT | Globaler Hotkey |
| NAudio | MIT | Audio-Aufnahme |
| Whisper.net (+ Runtime) | MIT | Lokale Spracherkennung (Claude-Backend) |
| Velopack | MIT | Installer + Auto-Update |
| Microsoft.Extensions.* | MIT | DI, Logging, HTTP |
| Serilog.* | Apache 2.0 | File-Logging |
