# Implementierungs-Prompt: Schnack (Voice-to-Text Tray-App, Windows 11, .NET 10, Claude)

## Rolle
Du bist ein erfahrener Senior Windows/.NET-Entwickler mit Erfahrung in WPF, Win32-Interop, Audio-Verarbeitung und LLM-APIs. Du arbeitest in **Claude Code** in **VS Code** unter Windows 11.

## Arbeitsweise — zweistufig (verbindlich)
1. **Stufe 1 – Plan:** Erstelle zuerst einen detaillierten Plan: vollständige Projektstruktur, NuGet-Pakete mit Versionen, Reihenfolge der Implementierung, kritische Win32-Calls inklusive P/Invoke-Signaturen. **Noch keinen Code.** Warte auf mein Go.
2. **Stufe 2 – Implementierung:** Nach Freigabe implementiere vollständig und kompilierbar.

Lies zu Beginn jeder Session `CLAUDE.md` im Repo-Root.

## Ziel
Ein internes Windows-11-Tool (.NET 10, WPF), das als Tray-App läuft und gesprochenen deutschen Text per globalem Hotkey oder Tray-Menü aufnimmt, lokal transkribiert (Whisper.net) und per Anthropic-Claude-API zurückhaltend korrigiert oder ins Englische übersetzt. Der finale Text wird automatisch ins zuvor aktive Windows-Textfeld eingefügt.

Ausschließlich für persönliche interne Nutzung. Kein kommerzielles Produkt, kein Enterprise-Rollout.

## Workflow aus Nutzersicht
1. Cursor in beliebiges Windows-Textfeld setzen.
2. Hotkey drücken (Default `Ctrl+Alt+Space`) **oder** Tray-Menü → "Aufnahme starten".
3. Sprechen.
4. Hotkey erneut drücken **oder** Tray-Menü → "Aufnahme stoppen".
5. Tool transkribiert lokal, schickt Transkript an Claude zur Nachbearbeitung, fügt finalen Text ins Ursprungsfeld ein.

Kein Live-Text. Keine Vorschau. Kein Hauptfenster im Vordergrund (nur Settings-Dialog auf Wunsch).

## Inhaltliche Vorgaben für die Nachbearbeitung
- Inhalt des Gesprochenen muss erhalten bleiben.
- Erlaubt: Rechtschreibung, Interpunktion, Groß-/Kleinschreibung, offensichtliche Diktierfehler, leichte Reduktion von Füllwörtern und Doppelnennungen.
- Nicht erlaubt: starke stilistische Umformulierung, neue Inhalte, Veränderung von Namen/Zahlen/Terminen/URLs/E-Mails/Fachbegriffen.

---

## Architekturentscheidungen (verbindlich)

### Speech-to-Text: lokal via Whisper.net
- NuGet: `Whisper.net` + `Whisper.net.Runtime` (CPU). Optional `Whisper.net.Runtime.Cuda` als Erweiterung später.
- Default-Modell: `ggml-large-v3-turbo.bin` (gute deutsche Qualität, akzeptable Geschwindigkeit auf CPU).
- Konfigurierbar: `large-v3-turbo`, `medium`, `base`.
- Modell-Download: einmalig beim ersten Start nach `%APPDATA%\Schnack\models\` (HTTPS von Hugging Face Repo `ggerganov/whisper.cpp`). Fortschrittsanzeige im Settings-Dialog.
- STT-Sprache fest auf `"de"`.
- **Keine Cloud-STT.** Audiodaten verlassen den Rechner nicht.

### Textverarbeitung: Anthropic Claude API
- Endpoint: `POST https://api.anthropic.com/v1/messages`
- Header: `x-api-key: <KEY>`, `anthropic-version: 2023-06-01`, `content-type: application/json`
- Default-Modell: `claude-haiku-4-5` (schnell, günstig, für Korrektur völlig ausreichend). Konfigurierbar auf `claude-sonnet-4-6` für höhere Übersetzungsqualität.
- Request-Body:
  ```json
  {
    "model": "claude-haiku-4-5",
    "max_tokens": 4096,
    "messages": [{ "role": "user", "content": "<PROMPT_MIT_TRANSCRIPT>" }]
  }
  ```
- Response-Parsing: `response.content` ist ein Array von Content-Blöcken. Sammle alle `text`-Blöcke, joine sie. Robust gegen `stop_reason` ungleich `"end_turn"` (logge Warnung bei `max_tokens`).
- API-Key:
  - Primär aus Umgebungsvariable `ANTHROPIC_API_KEY`.
  - Optional zusätzlich verschlüsselt in `%APPDATA%\Schnack\secrets.dat` via **Windows DPAPI** (`ProtectedData.Protect`, `DataProtectionScope.CurrentUser`).
  - **Niemals** API-Key in Code, Logs, Repo oder Plain-Text-Settings.
- Hinweis: gegebenenfalls aktuelle Modellnamen vor Build verifizieren (Anthropic-Doku).

### Tray, UI, Hotkey
- Tray-Icon: **`H.NotifyIcon.Wpf`** (NuGet) – sauber WPF-nativ, kein WinForms-Mischen.
- Globaler Hotkey: **`NHotkey.Wpf`** (NuGet). Default `Ctrl+Alt+Space` toggelt Aufnahme. In Settings änderbar.
- Hauptfenster existiert, bleibt aber standardmäßig verborgen. Wird nur als Settings-Dialog angezeigt.
- Tray-Menü zeigt den aktuell aktiven Modus per Häkchen.

### Audio
- NuGet: `NAudio`
- Format: **16 kHz, mono, 16-bit PCM WAV** (Whisper-Native-Sample-Rate, kein Resampling nötig).
- Speicherort: `%TEMP%\Schnack\rec_<timestamp>.wav`. Nach Verarbeitung (Erfolg oder Fehler) löschen.
- **Kein VAD, kein Auto-Stop, kein Stille-Trimming im MVP** – Whisper kommt mit Stille am Anfang/Ende sehr gut klar.
- Manueller Start/Stop ausschließlich.

### Win32-Interop für Textinjection (kritisch — bitte sorgfältig)
- **Foreground-Window cachen** *vor* Tray-Klick / Hotkey, je nach Trigger:
  - Hotkey: GetForegroundWindow direkt nach Hotkey-Event (Fokus ist noch beim Zielfenster).
  - Tray-Klick: GetForegroundWindow im `MouseDown`-Handler, *bevor* das Menü Fokus nimmt; ggf. abonnieren von Maus-Hover/Open-Events.
- **`SetForegroundWindow` ist restriktiv** und schlägt oft fehl, wenn der eigene Prozess nicht Foreground ist. Verwende den **`AttachThreadInput`-Trick**:
  1. `GetWindowThreadProcessId(targetHwnd, ...)` → Ziel-Thread-ID.
  2. `GetCurrentThreadId()` → eigener Thread.
  3. `AttachThreadInput(currentThreadId, targetThreadId, true)`.
  4. `SetForegroundWindow(targetHwnd)`, ggf. `BringWindowToTop`.
  5. `AttachThreadInput(currentThreadId, targetThreadId, false)`.
- **Ctrl+V ausschließlich via `SendInput`** (P/Invoke `user32.dll` mit `INPUT[]`-Struct). **Kein `SendKeys`**, **kein `keybd_event`**.
  - Sequenz: KeyDown VK_CONTROL → KeyDown 'V' → KeyUp 'V' → KeyUp VK_CONTROL.
  - `KEYEVENTF_SCANCODE` mit `MapVirtualKey` für Robustheit gegenüber Layouts.
  - Verzögerung **30–50 ms** zwischen `SetForegroundWindow` und `SendInput`.
- **Clipboard-Zugriff nur auf STA-UI-Thread.** Recording-Stop-Callback läuft auf Background-Thread → Clipboard-Operationen ausschließlich via `Application.Current.Dispatcher.Invoke(...)`.
- **Clipboard-Backup**: nur `Clipboard.GetText()` sichern (Text). Andere Formate (Bilder, Files, RTF) werden nicht zurückgesichert — Einschränkung in README dokumentieren. Nach 500 ms Delay alten Text wiederherstellen. Hinter Settings-Toggle `RestoreClipboard` (Default `true`).

### Single-Instance
- Beim Start: `new Mutex(true, $"Schnack.Singleton.{Environment.UserName}", out var created)`.
- Wenn `!created`: Tray-Tipp "Schnack läuft bereits" anzeigen und sauber beenden (kein Crash, kein Fehler).

### DI + Logging
- `Microsoft.Extensions.DependencyInjection` → ServiceProvider in `App.xaml.cs`. Alle Services per Konstruktor-Injection.
- `Microsoft.Extensions.Logging` mit Serilog-File-Sink (`Serilog.Extensions.Logging`, `Serilog.Sinks.File`).
- Log-Pfad: `%APPDATA%\Schnack\logs\schnack-.log`, Daily Rolling, 7 Tage Retention.
- **Niemals loggen**: Audiodateien, Transkripte, korrigierte Texte, API-Keys, Anthropic-Request/Response-Bodies.
- **Erlaubt zu loggen**: Recording-Start/Stop-Events, Audiodateigröße in Bytes, HTTP-Statuscodes, Exception-Typen ohne Message-Inhalt, gewählter Modus, Modellname.
- Settings-Toggle `DebugLogging` (Default `false`): zusätzlich Zeichenanzahl von Transkripten loggen, **nie** Inhalt.

### Settings
- Pfad: `%APPDATA%\Schnack\settings.json`. Bei Erststart mit Defaults erzeugen.
- Felder:
  - `DefaultMode`: `"de_correct"` | `"de_to_en"`
  - `WhisperModel`: `"large-v3-turbo"` | `"medium"` | `"base"`
  - `ClaudeModel`: z.B. `"claude-haiku-4-5"`
  - `ClaudeMaxTokens`: `4096`
  - `MicrophoneDeviceId`: `null` (Default-Mic) oder konkrete WaveIn-Device-ID
  - `Hotkey`: z.B. `"Ctrl+Alt+Space"`
  - `RestoreClipboard`: `true`
  - `DebugLogging`: `false`
  - `TempAudioPath`: Default `null` → `%TEMP%\Schnack`

---

## Modus-Prompts (an Claude senden)

### Modus "Deutsch korrigieren" (`de_correct`)

```
Korrigiere den folgenden diktierten deutschen Text sehr zurückhaltend.

Erlaubt:
- Rechtschreibung korrigieren
- Zeichensetzung ergänzen
- Groß- und Kleinschreibung korrigieren
- offensichtliche Diktierfehler beheben
- Füllwörter leicht reduzieren
- doppelte Formulierungen entfernen

Nicht erlaubt:
- Inhalt ändern
- neue Informationen hinzufügen
- Informationen entfernen
- Aussagen abschwächen oder verstärken
- Namen, Zahlen, Termine, URLs, E-Mail-Adressen oder Fachbegriffe verändern
- den Stil stark umformulieren
- aus Stichpunkten Fließtext machen, außer der Nutzer hat offensichtlich Fließtext diktiert

Gib ausschließlich den finalen korrigierten Text aus, ohne Erklärung, ohne Markdown, ohne Anführungszeichen.

Text:
{{TRANSCRIPT}}
```

### Modus "Deutsch → Englisch" (`de_to_en`)

```
Der folgende Text wurde auf Deutsch diktiert. Übersetze ihn in natürliches, klares Englisch.

Wichtig:
- Bedeutung vollständig erhalten
- keine Informationen hinzufügen
- keine Informationen entfernen
- Namen, Zahlen, Termine, URLs, E-Mail-Adressen und Fachbegriffe erhalten
- offensichtliche Diktierfehler vorsichtig korrigieren
- Füllwörter und doppelte Formulierungen leicht glätten
- professionell und natürlich formulieren, aber nicht überformulieren
- keine Erklärung, kein Markdown, keine Anführungszeichen

Gib ausschließlich den finalen englischen Text aus.

Text:
{{TRANSCRIPT}}
```

`{{TRANSCRIPT}}` wird durch das Whisper-Transkript ersetzt.

---

## Komponenten-Struktur (verbindlich)

```
Schnack/
├─ Schnack.sln
├─ Schnack/
│  ├─ Schnack.csproj            (TargetFramework: net10.0-windows, WPF, x64)
│  ├─ App.xaml / App.xaml.cs       (DI-Container, Single-Instance, Tray-Init)
│  ├─ Views/
│  │  └─ SettingsWindow.xaml(.cs)
│  ├─ ViewModels/
│  │  └─ SettingsViewModel.cs
│  ├─ Services/
│  │  ├─ ITrayService.cs              / TrayService.cs
│  │  ├─ IRecordingService.cs         / NAudioRecordingService.cs
│  │  ├─ ITranscriptionService.cs     / WhisperTranscriptionService.cs
│  │  ├─ IClaudeService.cs            / ClaudeService.cs
│  │  ├─ ITextInsertionService.cs     / TextInsertionService.cs
│  │  ├─ IHotkeyService.cs            / HotkeyService.cs
│  │  ├─ ISettingsService.cs          / JsonSettingsService.cs
│  │  ├─ ISecretService.cs            / DpapiSecretService.cs
│  │  └─ IModelDownloadService.cs     / WhisperModelDownloadService.cs
│  ├─ Models/
│  │  ├─ AppSettings.cs
│  │  ├─ DictationMode.cs (enum)
│  │  ├─ RecordingState.cs (enum: Idle, Recording, Processing)
│  │  └─ Claude/ (Request/Response Records: MessagesRequest, MessagesResponse, ContentBlock)
│  ├─ Interop/
│  │  └─ Win32.cs (P/Invoke: GetForegroundWindow, SetForegroundWindow,
│  │               AttachThreadInput, GetWindowThreadProcessId,
│  │               SendInput, INPUT, KEYBDINPUT, MapVirtualKey)
│  └─ Resources/
│     └─ tray-icon.ico
├─ Schnack.Tests/
│  ├─ Schnack.Tests.csproj (xUnit, Moq)
│  ├─ ClaudeServiceTests.cs       (HttpClient mocking via DelegatingHandler)
│  └─ JsonSettingsServiceTests.cs (Temp-Dir-IO)
├─ CLAUDE.md
├─ README.md
├─ .gitignore
└─ .claudeignore
```

## State Machine
`RecordingState`: `Idle` → `Recording` → `Processing` → `Idle`.
- Thread-safe via `Interlocked.CompareExchange<int>` auf einem `int`-State-Feld.
- Aufnahme-Start nur erlaubt aus `Idle`.
- Aufnahme-Stop nur erlaubt aus `Recording`.
- Race-Conditions zwischen Hotkey + Tray-Klick müssen abgefangen werden (z.B. zwei schnelle Hotkey-Drücker hintereinander).
- Tray-Menü-Items aktivieren/deaktivieren und Tray-Icon-Tooltip pro State aktualisieren: "Bereit" / "Aufnahme läuft" / "Verarbeite…".

## Async / Cancellation
- Alle IO/HTTP/STT/Audio-Operationen `async`. UI-Thread niemals blockieren.
- `CancellationTokenSource` pro Verarbeitungslauf. Bei "Beenden" / Window-Close: cancel & cleanup.

## Fehlerbehandlung (verständliche Tray-Ballontipps oder MessageBox)
- Mikrofon nicht verfügbar / kein Default-Device → Hinweis + Settings öffnen.
- `ANTHROPIC_API_KEY` fehlt → Hinweis mit Anleitung (`setx ANTHROPIC_API_KEY ...` und VS Code neu starten).
- Whisper-Modell fehlt → Settings öffnen + Download-Button.
- HTTP 401/403 von Anthropic → "API-Key ungültig oder abgelaufen".
- HTTP 429 → "Rate Limit – kurz warten".
- HTTP 5xx / Netzwerkfehler → "Keine Verbindung zur Anthropic-API".
- Leeres Transkript → Hinweis "Keine Sprache erkannt", nichts einfügen.
- "Stoppen" ohne laufende Aufnahme → still ignorieren.
- Win32 SetForegroundWindow fehlgeschlagen → trotzdem versuchen Clipboard zu setzen, Hinweis "Bitte manuell mit Strg+V einfügen".

## Sicherheit & Privacy
- API-Key niemals in Code, Logs, oder Git.
- `.gitignore`: `bin/`, `obj/`, `*.user`, `*.suo`, `appsettings.local.json`, `secrets.dat`, `models/`, `*.wav`.
- `.claudeignore`: zusätzlich `logs/`, `*.log`, `.env`.
- Audiodateien werden lokal verarbeitet und nach Transkription gelöscht.
- Nur Transkript-Text geht an Anthropic-Cloud.

---

## Akzeptanzkriterien
1. Projekt lässt sich in VS Code öffnen.
2. `dotnet restore` und `dotnet build` laufen ohne Warnungen außer bekannten NuGet-Hinweisen.
3. `dotnet run --project Schnack` startet die App ohne sichtbares Hauptfenster, Tray-Icon erscheint.
4. Hotkey `Ctrl+Alt+Space` startet/stoppt Aufnahme aus jedem fokussierten Textfeld.
5. Tray-Menü ermöglicht denselben Workflow inkl. Modus-Umschaltung.
6. Im Modus `de_correct` wird zurückhaltend korrigierter deutscher Text in das zuvor aktive Textfeld eingefügt.
7. Im Modus `de_to_en` wird natürliches Englisch eingefügt.
8. Temporäre WAV-Dateien werden nach Verarbeitung (Erfolg oder Fehler) gelöscht.
9. Fehlender API-Key, fehlendes Whisper-Modell und Mikrofonfehler werden verständlich gemeldet.
10. Single-Instance funktioniert: zweiter Start zeigt Hinweis und beendet sich.
11. Logs enthalten keine Transkript-Inhalte, keine API-Keys.
12. xUnit-Tests für `ClaudeService` und `JsonSettingsService` laufen grün.

## Lieferumfang
1. Vollständige Projektstruktur wie oben.
2. Lauffähige MVP-Implementierung.
3. `README.md` (auf **Deutsch**) mit:
   - Zweck des Tools
   - Voraussetzungen (.NET 10 SDK, Windows 11, Mikrofon)
   - Setup (`ANTHROPIC_API_KEY` setzen, Whisper-Modell-Erststart)
   - Build- und Startanleitung (`dotnet restore`, `dotnet build`, `dotnet run`)
   - Bedienung (Hotkey, Tray-Menü, Modi)
   - Bekannte Einschränkungen (nur Text-Clipboard wird gesichert; Erststart-Modell-Download, etc.)
   - Privacy-Hinweis (Audio bleibt lokal, Transkript geht an Anthropic)
4. Knappe Kommentare an kritischen Stellen (Win32-Interop, AttachThreadInput-Trick, Mutex). Nicht überkommentieren.
5. Falls Pakete nicht verfügbar: pragmatische Alternative wählen und in der README dokumentieren.

## Out of Scope (explizit nicht im MVP)
- Stille-Trimming, VAD, Auto-Stop
- Streaming-Transkription
- Live-Vorschau
- Andere STT-Sprachen als Deutsch
- Modi außer `de_correct` und `de_to_en`
- Auto-Update-Mechanismus
- Installer (MSIX/Setup) — manuelles `dotnet publish -c Release -r win-x64 --self-contained false` reicht
- Code-Signing
- Cloud-STT-Backend

---

**Beginne jetzt mit Stufe 1 (Plan). Code erst nach meiner Freigabe.**
