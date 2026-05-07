# CLAUDE.md – Projektkontext Schnack

> **Erste Datei, die Claude Code in jeder Session liest.** Halte sie aktuell, wenn sich Architektur, Tools oder Konventionen ändern. Sie ist die Single Source of Truth für stabiles Projekt-Wissen — temporäre Aufgabenpakete kommen separat in `CHANGES.md` / `CHANGES_v2.md`.

> **Hinweis zum Stand:** Diese Datei beschreibt den **Soll-Zustand** nach Umsetzung aller offenen `CHANGES*.md`. Nicht jede hier beschriebene Komponente existiert bereits im Code — wenn du an einer noch nicht umgesetzten Stelle arbeitest (z.B. `IPostProcessingService`, `HttpRetry`, `IUpdateService`), prüfe immer zuerst den realen Code-Stand und implementiere gemäß den Anweisungen in der zugehörigen `CHANGES*.md`. Bei Unsicherheit: Plan-Mode + Rückfrage.

## Was ist Schnack?

Internes Windows-11-Tray-Tool (.NET 10 / WPF) für persönliche Nutzung. Nimmt deutsche Sprache via Mikrofon auf, transkribiert sie und fügt den korrigierten oder ins Englische übersetzten Text in das zuvor aktive Windows-Textfeld ein.

**Zwei Backend-Stacks** (Nutzer wählt einen in den Einstellungen — Entweder-oder, kein Mischbetrieb):

| Backend | STT (Audio → Text) | Textverarbeitung | Privacy |
|---------|--------------------|--------------------|---------|
| **OpenAI** | OpenAI `v1/audio/transcriptions` (Cloud) | OpenAI `v1/chat/completions` (Cloud) | Audio + Transkript gehen an OpenAI |
| **Claude** | Whisper.net **lokal** | Anthropic `v1/messages` (Cloud) | Audio bleibt lokal, nur Transkript geht an Anthropic |

**Default-Backend bei Erststart:** OpenAI (kein Whisper-Modell-Download nötig, schneller einsatzbereit).

**Kein kommerzielles Produkt.** Kein Enterprise-Rollout. Kein Mehrbenutzer-Setup.

## Tech-Stack (verbindlich)

- **C# 14 / .NET 10 / WPF / x64** (`TargetFramework: net10.0-windows`)
- **WPF-Tray:** `H.NotifyIcon.Wpf` (kein Mischen mit WinForms-NotifyIcon)
- **Globaler Hotkey:** `NHotkey.Wpf` (Default `Ctrl+Alt+S` — überschreibt den älteren Wert `Ctrl+Alt+Space` aus `PROMPT.md`)
- **Audio-Aufnahme:** `NAudio` (16 kHz mono PCM WAV)
- **STT (OpenAI-Backend):** `HttpClient` + `IHttpClientFactory` gegen `v1/audio/transcriptions`. Kein OpenAI-SDK.
- **STT (Claude-Backend):** `Whisper.net` + `Whisper.net.Runtime` (CPU), optional `Whisper.net.Runtime.Cuda` (GPU). Modelle in `%APPDATA%\Schnack\models\`, Download via `IWhisperModelDownloadService` aus `huggingface.co/ggerganov/whisper.cpp`.
- **Postprocessing (Claude-Backend):** `HttpClient` gegen Anthropic `v1/messages`. Kein Anthropic-SDK.
- **Postprocessing (OpenAI-Backend):** `HttpClient` gegen OpenAI `v1/chat/completions`. Gleiches Interface (`IPostProcessingService`) wie ClaudeService.
- **Installer + Auto-Update:** `Velopack` (NuGet) + `vpk` CLI. Updates via GitHub Releases.
- **DI:** `Microsoft.Extensions.DependencyInjection`
- **Logging:** `Microsoft.Extensions.Logging` + Serilog File-Sink (`Serilog.Sinks.File`)
- **Secrets:** Windows DPAPI (`System.Security.Cryptography.ProtectedData`)
- **Tests:** xUnit + Moq

**Tool-Substitutionen ohne explizite Diskussion sind nicht erlaubt.** Insbesondere kein `SendKeys`, kein `keybd_event`, kein WinForms-NotifyIcon, kein Azure-STT, kein OpenAI-/Anthropic-SDK, kein anderes Update-Framework als Velopack.

## Build & Run

```pwsh
# Einmalig: API-Keys setzen (mindestens einer der beiden, je nach Backend-Wahl)
setx ANTHROPIC_API_KEY "sk-ant-..."     # nur für Claude-Backend
setx OPENAI_API_KEY "sk-..."             # nur für OpenAI-Backend
# Terminal/VS Code danach neu starten

# Entwicklung
dotnet restore
dotnet build
dotnet run --project Schnack
dotnet test

# Release-Build mit Velopack-Pack + GitHub-Upload
.\build-release.ps1                      # Version aus csproj
.\build-release.ps1 -Version 1.4.0       # Version explizit
.\build-release.ps1 -SkipUpload          # nur lokal packen
```

API-Keys können alternativ über die Settings-UI hinterlegt werden — werden dann DPAPI-verschlüsselt in `%APPDATA%\Schnack\secrets.dat` (Anthropic) bzw. `openai-secrets.dat` (OpenAI) gespeichert.

## Kritische Architekturregeln (verletzungssicher)

### Win32-Interop

- `SendInput` ist die **einzige** erlaubte Methode für Tastendrücke. Niemals `SendKeys`, niemals `keybd_event`.
- `SetForegroundWindow` immer mit dem `AttachThreadInput`-Trick kombinieren (Pattern siehe `TextInsertionService`). Direktaufruf scheitert oft.
- Zwischen `SetForegroundWindow` und `SendInput` ca. **80–150 ms** Verzögerung, damit der Fokus settled.
- **Standard-Texteinfügen:** Setting `PreferClipboardFreeInsertion = true` (Default). Zeichen werden per `SendInput` mit `KEYEVENTF_UNICODE` direkt in das Zielfenster getippt — kein Clipboard nötig, zuverlässiger am Cursor, keine Win+V-Historie.
- **Alternative:** Clipboard + `SendInput` Strg+V mit `KEYEVENTF_SCANCODE` + `MapVirtualKey`. Wird verwendet, wenn `PreferClipboardFreeInsertion = false` oder als automatischer Fallback, wenn `SetForegroundWindow` fehlschlägt (Tray-Notification fordert Nutzer dann zu manuellem Strg+V auf).
- **Schwebender Aufnahme-Button:** `FloatingRecordWindow` setzt nach `SourceInitialized` `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` (`GetWindowLongPtr` / `SetWindowLongPtr`), damit Klicks keinen Fokus stehlen und `GetForegroundWindow` weiter die Ziel-App liefert. Drag + Toggle ein/aus implementiert.
- **`BringWindowToTop` nicht verwenden** — wurde im Hardening-Pass entfernt, weil es bei Fullscreen-Apps und UAC-Dialogen Probleme macht.
- Alle P/Invoke-Signaturen ausschließlich in `Interop/Win32.cs`. Keine Duplikate verstreut über Services.

### Threading

- Recording-Stop-Callback läuft auf einem NAudio-Background-Thread.
- **Clipboard-Operationen** (`Clipboard.SetText`, `Clipboard.GetText`) ausschließlich über `Application.Current.Dispatcher.Invoke(...)` auf den UI-Thread (STA-Anforderung).
- Kein `.Result` / `.Wait()` auf Tasks aus UI-Code → Deadlock-Gefahr. Immer `await`.
- **Ausnahme:** `NAudioRecordingService.StopRecording()` blockt bewusst mit `_stopTcs.Task.Wait(TimeSpan.FromSeconds(5))`, weil NAudio's `StopRecording` async signalisiert und der WAV-Writer vor der STT-Phase geschlossen sein muss. **Mit Timeout**, damit ein hängendes Mikrofon nicht die ganze App blockiert.
- State-Übergänge `Idle ⇄ Recording ⇄ Processing` mit `Interlocked.CompareExchange` thread-safe.
- Pipeline (`StopAndProcess`) läuft per `Task.Run` auf einem Background-Thread, damit `_stopTcs.Wait()` den UI-Thread nicht verklemmt.

### Velopack & App-Lifecycle

- **Eigene `Main`-Methode** in `App.xaml.cs` — nicht der WPF-Default. `App.xaml` ist als `<Page>` deklariert (nicht `<ApplicationDefinition>`).
- `VelopackApp.Build().Run()` muss als allererster Aufruf in `Main` laufen, **vor** dem WPF-Bootstrap. Velopack braucht das, um Update-Hooks (`--veloapp-install`, `--veloapp-updated`, etc.) zu verarbeiten ohne UI-Stack hochzufahren.
- **Single-Instance-Mutex** (`Schnack.Singleton.{Environment.UserName}`) muss `_mutex.ReleaseMutex()` in try-catch wrappen, sonst fliegt `ApplicationException`, wenn Cleanup auf einem anderen Thread läuft.
- Update-Apply triggert App-Restart über Velopack — der Mutex muss vorher sauber freigegeben sein, sonst blockiert die neu gestartete Instanz.

### Secrets

- **Anthropic:** Umgebungsvariable `ANTHROPIC_API_KEY` oder DPAPI-Datei `%APPDATA%\Schnack\secrets.dat`.
- **OpenAI:** Umgebungsvariable `OPENAI_API_KEY` oder DPAPI-Datei `%APPDATA%\Schnack\openai-secrets.dat`.
- DPAPI-Scope: `DataProtectionScope.CurrentUser`.
- **Niemals** in Code, Logs, Plain-Text-Settings oder Git.
- API-Keys werden **nicht** im Repo-Verzeichnis gespeichert (DPAPI-Files liegen in `%APPDATA%`), können also physisch nicht versehentlich committed werden.

### Logging-Verbote

**Niemals loggen:**
- Audiodateien oder Pfade, die Inhalte verraten
- Transkripte (STT-Output)
- Korrigierte/übersetzte Texte (Postprocessing-Output)
- API-Keys (auch nicht teilweise)
- Anthropic- oder OpenAI-Request- oder Response-Bodies
- Anthropic-/OpenAI-`error.message`-Felder (können User-Daten enthalten)

**Erlaubt zu loggen:**
- Recording-Start/Stop-Events
- Audiodateigröße in Bytes
- HTTP-Statuscodes
- Exception-Typen (ohne `.Message`-Inhalt bei Verdacht auf User-Daten)
- Modus, Modellname, gewählter Hotkey, gewähltes Backend
- Bei Fehler-Bodies: nur `error.type` und `error.code` extrahieren, **nie** `error.message`

**Debug-Logging** (Setting `DebugLogging` oder Env `SCHNACK_DEBUG=1`) hebt das Log-Level an — auch dort dürfen die obigen Verbote nicht verletzt werden. Maximal Zeichenanzahl von Transkripten loggen, nie deren Inhalt.

### HTTP-Client-Konventionen

- Alle drei HTTP-Services (`OpenAiTranscriptionService`, `OpenAiChatService`, `ClaudeService`) nutzen `IHttpClientFactory` mit named clients (`"OpenAi"`, `"Claude"`).
- **Retry-Logik gemeinsam** in `Services/Internal/HttpRetry.cs`: 3 Attempts, exponential backoff 250/500/1000 ms.
- Retry **nur bei** `RequestTimeout`, `InternalServerError`, `BadGateway`, `ServiceUnavailable`, `GatewayTimeout`, `TaskCanceledException` (mit `!ct.IsCancellationRequested`).
- **Niemals retry bei** 401/403 (API-Key ungültig) und 429 (Rate Limit) — direkt freundliche Fehlermeldung.
- JSON-Serialisierung: Anthropic + OpenAI erwarten **snake_case** (`max_tokens`, `stop_reason`, `response_format`). Entweder `JsonNamingPolicy.SnakeCaseLower` global oder `[JsonPropertyName(...)]` an jeder Property — **konsistent durchziehen**.

### Settings & Schema-Migration

- `AppSettings` ist ein `record` mit `with`-Updates.
- Persistenz: `%APPDATA%\Schnack\settings.json`.
- **Schema-Versionierung** über `SettingsSchema`-Feld:
  - Schema 1: erstes Format
  - Schema 2: `BackendProvider`, `OpenAiChatModel`, `WhisperModel`, `WhisperUseGpu` ergänzt
- Bei `LoadAsync`: fehlende oder veraltete Schema-Version → Migration ausführen, Datei zurückschreiben, im Log vermerken.
- Bei neuen Settings-Feldern: Default-Wert in `AppSettings` setzen UND Migration-Schritt im `JsonSettingsService` ergänzen.

## Codestil

- **File-scoped namespaces** überall.
- **Nullable enabled**, Warnings als Errors für Nullable-Verstöße.
- **`async`/`await` durchgängig**, `CancellationToken` als letzter Parameter bei async-Methoden.
- **Konstruktor-Injection** für alle Services. Keine Service-Locators, keine statischen Factory-Calls aus Service-Logik.
- **Records** für DTOs (Anthropic/OpenAI Request/Response, AppSettings, Result-Typen).
- **`sealed`** für Service-Implementierungen, außer offene Vererbung ist begründet (z.B. `JsonSettingsService` für Test-Subklasse — mit Kommentar).
- **Knappe Kommentare** an P/Invoke-Stellen, Threading-Workarounds und nicht-offensichtlichen Algorithmen. Keine XML-Doc-Kommentare bei trivialen Methoden.
- Keine `using static`-Spaghetti, keine globalen Variablen.

## Wo was liegt

```
C:\Projekte\Schnack\
├─ CLAUDE.md                    Diese Datei
├─ PROMPT.md                    Ursprünglicher Implementierungs-Prompt
├─ CHANGES.md                   Aufgabenpaket Iteration 1 (Backend-Auswahl, Tray-Bug, Hot-Fixes)
├─ CHANGES_v2.md                Aufgabenpaket Iteration 2 (Floating-Toggle, Velopack)
├─ RELEASE.md                   Release-Workflow für Maintainer (vom Build erzeugt)
├─ README.md                    Empfänger-Doku (Setup, Bedienung, Privacy)
├─ LICENSE                      MIT
├─ .gitignore
├─ .claudeignore
├─ .claude/
│  └─ settings.local.json       Claude-Code-Allowlist (NICHT committen)
├─ build-release.ps1            Velopack-Build + GitHub-Upload
├─ Schnack.slnx
├─ Schnack/
│  ├─ Schnack.csproj            net10.0-windows, x64, Velopack-Setup
│  ├─ App.xaml / App.xaml.cs    Eigene Main(), Velopack-Bootstrap, DI, Mutex, Tray-Init
│  ├─ Services/
│  │  ├─ Internal/HttpRetry.cs              gemeinsame Retry-Logik
│  │  ├─ ITrayService / TrayService
│  │  ├─ IRecordingService / NAudioRecordingService
│  │  ├─ ITranscriptionService              Interface (IAsyncDisposable)
│  │  │  ├─ OpenAiTranscriptionService     OpenAI-Backend
│  │  │  └─ WhisperLocalTranscriptionService Claude-Backend (lokal)
│  │  ├─ IPostProcessingService             Interface (Korrektur/Übersetzung)
│  │  │  ├─ ClaudeService                  Claude-Backend
│  │  │  └─ OpenAiChatService              OpenAI-Backend
│  │  ├─ ITextInsertionService / TextInsertionService
│  │  ├─ IHotkeyService / HotkeyService
│  │  ├─ ISettingsService / JsonSettingsService
│  │  ├─ ISecretService / DpapiSecretService
│  │  ├─ IFloatingRecordUi / FloatingRecordUiService
│  │  ├─ IUpdateService / VelopackUpdateService
│  │  ├─ IWhisperModelDownloadService / WhisperModelDownloadService
│  │  └─ MicrophoneEnumerator (static)
│  ├─ ViewModels/
│  │  └─ SettingsViewModel.cs   Mit Dirty-Tracking
│  ├─ Views/
│  │  ├─ SettingsWindow.xaml    Backend-Radio + sichtbarkeitsabhängige Sektionen, Buttons [Abbrechen][Speichern]
│  │  ├─ AboutWindow.xaml       Hintergrund im Logo-Farbton
│  │  ├─ FirstRunWindow.xaml
│  │  └─ FloatingRecordWindow.xaml  Drag + Toggle
│  ├─ Commands/RelayCommand.cs
│  ├─ Models/                   AppSettings, BackendProvider, DictationMode, RecordingState, DTOs
│  │  ├─ Claude/                Anthropic Request/Response
│  │  └─ OpenAi/                OpenAI Request/Response
│  ├─ Interop/Win32.cs          P/Invoke gebündelt
│  └─ Resources/                tray-icon.ico, Schnack_Logo.png
├─ Schnack.Tests/
│  └─ Schnack.Tests.csproj      net10.0-windows, xUnit + Moq
├─ publish/                     dotnet publish-Output (in .gitignore)
└─ releases/                    Velopack-Output, Setup.exe + .nupkg (in .gitignore)

Laufzeit-Pfade (NICHT im Repo):
%APPDATA%\Schnack\              settings.json, secrets.dat, openai-secrets.dat, models/, logs/
%LocalAppData%\Schnack\         Velopack-Installation (per-user)
%TEMP%\Schnack\                 temporäre WAV-Dateien
```

## Workflow-Konventionen für Claude Code

### Standard-Vorgehen

1. **Vor größeren Änderungen:** Plan Mode (`Shift+Tab`) verwenden, Plan abwarten.
2. **Dokumenten-Hierarchie bei Konflikten** (höher gewinnt):
   1. `CHANGES_v2.md` (aktuellstes Aufgabenpaket)
   2. `CHANGES.md`
   3. `CLAUDE.md` (diese Datei)
   4. `PROMPT.md`
3. **Vor Commit:** `dotnet build` und `dotnet test` müssen grün sein, keine neuen Warnungen.
4. **Bei neuen NuGet-Paketen:** Lizenz prüfen, in README dokumentieren, in CHANGES-Datei explizit erlaubt sein.
5. **Bei Win32-Interop- oder Threading-Änderungen:** extra-vorsichtig, Begründung im Code-Kommentar.
6. **Anthropic-/OpenAI-Modellnamen:** wenn ein Name 404 zurückgibt, aktuelle Liste aus der jeweiligen Doku verifizieren statt zu raten.

### Autonomie

`.claude/settings.local.json` definiert eine Allowlist für routine-mäßige Befehle. Permission-Mode in VS Code: **Accept Edits**.

**Selbstständig ohne Nachfrage:**
- Datei-Edits, Datei-Erstellung, Datei-Löschung im Workspace
- `dotnet build`, `dotnet test`, `dotnet run`, `dotnet restore` — auch nach jedem logischen Teilschritt
- `dotnet add package` für Pakete, die in CHANGES-Dateien explizit genannt sind
- Build-Fehler und neue Compiler-Warnungen selbst beheben
- Lese-Befehle (`dir`, `ls`, `cat`, `Get-ChildItem`, `Select-String`, `git status`, `git diff`, `git log`, `dotnet list`)
- Refactorings innerhalb der vorgegebenen Architektur

**Mit Nachfrage:**
- NuGet-Pakete außerhalb der CHANGES-Vorgaben
- Architektur-Entscheidungen, die nicht in PROMPT/CLAUDE/CHANGES dokumentiert sind
- Änderungen außerhalb des Workspaces (Env-Variablen, globale Git-Config, Systemordner)
- `git commit` und `git push` — Nutzer committet selbst

### Session-Ende

- Zusammenfassung der gemachten Änderungen, gegliedert nach Aufgabenpaket.
- `dotnet build` und `dotnet test` grün.
- **Nicht committen.** Nutzer prüft und committet selbst.
- Versionsnummer-Updates in `Schnack.csproj` in der Zusammenfassung erwähnen.

## Repo-Hygiene & Sicherheit

`.gitignore` muss enthalten:
```
bin/
obj/
*.user
*.suo
*.bak
*.tmp
*.swp
~$*
.vs/
appsettings.local.json
secrets.dat
openai-secrets.dat
models/
*.wav
logs/
*.log
.env
.claude/settings.local.json
publish/
releases/
*.nupkg
*conflict*
*Conflict*
```

`.claudeignore` ist eine Obermenge davon plus alles, was Claude Code beim Lesen verwirren oder leaken könnte.

**Vor jedem `git push`** Schlüssel-Scan:
```pwsh
git grep -E "sk-ant-[a-zA-Z0-9_-]{20,}|sk-proj-[a-zA-Z0-9_-]{20,}|ghp_[a-zA-Z0-9]{36,}"
```
Treffer = nicht pushen.

**GitHub Secret Scanning + Push Protection** im Repo-Settings aktivieren — fängt versehentliche Key-Pushs automatisch ab.

## Versionierung

- **Single source of truth:** `<Version>` in `Schnack.csproj`.
- `<AssemblyMetadata Include="ReleaseDate" Value="..."/>` manuell mitpflegen.
- `build-release.ps1` parsed `<Version>` und gibt sie an `vpk pack --packVersion` weiter.
- GitHub-Release-Tag: `v<version>` (z.B. `v1.4.0`).
- SemVer: `MAJOR.MINOR.PATCH`. Patch für Bug-Fixes, Minor für neue Features, Major für Breaking Changes.

## Akzeptanzkriterien (siehe PROMPT.md / CHANGES*.md)

Bei der Arbeit an einem Feature: vorab die entsprechenden Akzeptanzkriterien aus dem zugehörigen Aufgabenpaket prüfen und am Ende explizit verifizieren.

## Out of Scope (dauerhaft)

Diese Punkte werden bewusst nicht implementiert:

- Hybrid-Backend-Modi (z.B. OpenAI-STT + Claude-Postprocessing). Nutzer wählt einen kompletten Stack.
- Streaming-STT, Voice Activity Detection, Auto-Stop bei Stille.
- Live-Vorschau des Transkripts.
- Andere STT-Sprachen als Deutsch.
- Weitere Modi außer `de_correct` und `de_to_en`.
- Code-Signing (kommerziell oder self-signed). Spätere Iteration möglich.
- Multi-User auf einem Rechner.
- Multi-Channel-Releases (beta/stable). Nur `win`-Channel.
- Auto-Apply von Updates ohne Nutzer-Bestätigung.
- Background-Polling für Update-Checks während App läuft. Nur beim Start + manuell.
- Private-Repo-Support für Update-Check (würde DPAPI-Token in der App erfordern).
- Migration alter Settings-Versionen beim Velopack-Update. Erledigt die App selbst beim Start.
