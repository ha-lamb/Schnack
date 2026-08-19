# CLAUDE.md – Projektkontext Schnack

> **Erste Datei, die Claude Code in jeder Session liest.** Halte sie aktuell, wenn sich Architektur, Tools oder Konventionen ändern. Sie beschreibt den **Ist-Zustand** des Codes und ist zusammen mit `PROJEKT_STATUS.md` (aktueller Arbeitsstand) die Single Source of Truth.

## Was ist Schnack?

Internes Windows-11-Tray-Tool (.NET 10 / WPF) für persönliche Nutzung. Nimmt gesprochene Sprache via Mikrofon auf, transkribiert sie und fügt den geglätteten oder übersetzten Text in das zuvor aktive Windows-Textfeld ein.

**Zweisprachig (Deutsch/Englisch).** Zwei voneinander unabhängige Dinge:

- **Oberflächensprache** (`UiLanguage`) — Tray, Dialoge, Meldungen. Wechsel wirkt sofort.
- **Diktat-Modus** — eine von vier Optionen: `Deutsch`, `Englisch`, `Deutsch → Englisch`, `Englisch → Deutsch`. Geglättet wird immer; die Pfeil-Varianten übersetzen zusätzlich.

Die vier Optionen sind intern die Kombinationen aus `DictationLanguage` × `DictationMode` (`Correct`/`Translate`), gebündelt in `Models/DictationChoice.cs` — **die einzige Quelle** für Tray-Menü und Einstellungen, damit beide nicht auseinanderlaufen. Jede Kombination hat einen eigenen Prompt in `DictationPrompts`. Die Auswahl wird sofort persistiert (Tray wie Dialog), weil die Services die Diktiersprache pro Lauf aus den Settings lesen.

**Zwei Backend-Stacks** (Nutzer wählt einen in den Einstellungen — Entweder-oder, kein Mischbetrieb):

| Backend | STT (Audio → Text) | Textverarbeitung | Privacy |
|---------|--------------------|--------------------|---------|
| **OpenAI** | OpenAI `v1/audio/transcriptions` (Cloud) | OpenAI `v1/chat/completions` (Cloud) | Audio + Transkript gehen an OpenAI |
| **Claude** | Whisper.net **lokal** | Anthropic `v1/messages` (Cloud) | Audio bleibt lokal, nur Transkript geht an Anthropic |

**Default-Backend bei Erststart:** OpenAI (kein Whisper-Modell-Download nötig, schneller einsatzbereit). Backend-Wechsel wirkt ab dem nächsten Pipeline-Lauf ohne App-Neustart (Keyed-DI-Auflösung pro Lauf).

**Kein kommerzielles Produkt.** Kein Enterprise-Rollout. Kein Mehrbenutzer-Setup.

## Tech-Stack (verbindlich)

- **C# 14 / .NET 10 / WPF / x64** (`TargetFramework: net10.0-windows`)
- **WPF-Tray:** `H.NotifyIcon.Wpf` (kein Mischen mit WinForms-NotifyIcon)
- **Globaler Hotkey:** `NHotkey.Wpf` (Default `Ctrl+Alt+S`)
- **Audio-Aufnahme:** `NAudio` (16 kHz mono PCM WAV)
- **STT (OpenAI-Backend):** `HttpClient` + `IHttpClientFactory` gegen `v1/audio/transcriptions`. Kein OpenAI-SDK.
- **STT (Claude-Backend):** `Whisper.net` + `Whisper.net.Runtime` (CPU). Modelle in `%APPDATA%\Schnack\models\`, Download via `IWhisperModelDownloadService` aus `huggingface.co/ggerganov/whisper.cpp`.
- **Postprocessing (Claude-Backend):** `HttpClient` gegen Anthropic `v1/messages`. Kein Anthropic-SDK.
- **Postprocessing (OpenAI-Backend):** `HttpClient` gegen OpenAI `v1/chat/completions`. Gleiches Interface (`IPostProcessingService`).
- **Installer + Auto-Update:** `Velopack` (NuGet) + `vpk` CLI. Updates via GitHub Releases.
- **DI:** `Microsoft.Extensions.DependencyInjection` (inkl. Keyed Services für die Backend-Wahl)
- **Logging:** `Microsoft.Extensions.Logging` + Serilog File-Sink (`Serilog.Sinks.File`)
- **Secrets:** Windows DPAPI (`ProtectedData`, seit .NET 10 ohne separates NuGet-Paket)
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

## Architektur-Überblick

- **`App.xaml.cs`**: eigene `Main()` (Velopack-Bootstrap), Single-Instance-Mutex, DI-Container, Serilog-Setup, Event-Wiring zwischen Tray/Hotkey/Floating-Button und dem Orchestrator, Fenster-Dialoge, Cleanup.
- **`Services/DictationOrchestrator.cs`** (`IDictationOrchestrator`): kapselt die State-Machine `Idle ⇄ Recording ⇄ Processing` (thread-safe via `Interlocked.CompareExchange`) und die Pipeline Aufnahme → Transkription → Postprocessing → Texteinfügung. Löst `ITranscriptionService`/`IPostProcessingService` pro Lauf per Keyed DI anhand `BackendProvider` auf (bewusste Ausnahme von der Konstruktor-Injection, damit der Backend-Wechsel ohne Neustart wirkt). Cacht das Ziel-HWND beim Aufnahme-Start.
- **Bedienpfade:** Hotkey und schwebender Button (beide rufen `ToggleRecordingAsync` mit dem aktuellen Foreground-HWND). Das Tray-Menü bietet bewusst **keine** Aufnahme-Steuerung — Win32-Foreground-Tracking durch Tray-Menü-Interaktion ist unzuverlässig; stattdessen steht dort ein Hinweis-Eintrag.

## Kritische Architekturregeln (verletzungssicher)

### Win32-Interop

- `SendInput` ist die **einzige** erlaubte Methode für Tastendrücke. Niemals `SendKeys`, niemals `keybd_event`.
- `SetForegroundWindow` immer mit dem `AttachThreadInput`-Trick kombinieren (Pattern siehe `TextInsertionService`). Direktaufruf scheitert oft.
- Zwischen `SetForegroundWindow` und `SendInput` ca. **80–150 ms** Verzögerung, damit der Fokus settled.
- **Standard-Texteinfügen:** Setting `PreferClipboardFreeInsertion = true` (Default). Zeichen werden per `SendInput` mit `KEYEVENTF_UNICODE` direkt in das Zielfenster getippt — kein Clipboard nötig, zuverlässiger am Cursor, keine Win+V-Historie.
- **Alternative:** Clipboard + `SendInput` Strg+V mit `KEYEVENTF_SCANCODE` + `MapVirtualKey`. Wird verwendet, wenn `PreferClipboardFreeInsertion = false` oder als automatischer Fallback, wenn `SetForegroundWindow` fehlschlägt (Tray-Notification fordert Nutzer dann zu manuellem Strg+V auf).
- **Schwebender Aufnahme-Button:** `FloatingRecordWindow` setzt nach `SourceInitialized` `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, damit Klicks keinen Fokus stehlen und `GetForegroundWindow` weiter die Ziel-App liefert. Drag per eigener Delta-Logik (nicht `DragMove()` — hat mit `WS_EX_NOACTIVATE` Edge-Cases), Toggle über Tray-Häkchen.
- **`BringWindowToTop` nicht verwenden** — macht bei Fullscreen-Apps und UAC-Dialogen Probleme.
- Alle P/Invoke-Signaturen ausschließlich in `Interop/Win32.cs`. Keine Duplikate. `MOUSEINPUT` dort ist nötig für die korrekte Union-Größe von `INPUT` — nicht entfernen.

### Threading

- Recording-Stop-Callback läuft auf einem NAudio-Background-Thread.
- **Clipboard-Operationen** ausschließlich über `Application.Current.Dispatcher.Invoke(...)` auf den UI-Thread (STA-Anforderung).
- Kein `.Result` / `.Wait()` auf Tasks aus UI-Code → Deadlock-Gefahr. Immer `await`.
- **Ausnahme:** `NAudioRecordingService.StopRecording()` blockt bewusst mit `_stopTcs.Task.Wait(TimeSpan.FromSeconds(5))`, weil NAudio async signalisiert und der WAV-Writer vor der STT-Phase geschlossen sein muss. **Mit Timeout**, damit ein hängendes Mikrofon nicht die App blockiert.
- Die Pipeline läuft im `DictationOrchestrator` per `Task.Run` auf einem Background-Thread, damit `StopRecording()` den UI-Thread nicht verklemmt. Der `CancellationTokenSource` des Vorlaufs wird vor jedem neuen Lauf disposed.

### Velopack & App-Lifecycle

- **Eigene `Main`-Methode** in `App.xaml.cs`. `App.xaml` ist als `<Page>` deklariert (nicht `<ApplicationDefinition>`), `StartupObject` gesetzt.
- `VelopackApp.Build().Run()` muss als allererster Aufruf in `Main` laufen, **vor** dem WPF-Bootstrap (verarbeitet Update-Hooks `--veloapp-*` ohne UI-Stack).
- **Single-Instance-Mutex** (`Schnack.Singleton.{Environment.UserName}`): `ReleaseMutex()` immer in try-catch (Cross-Thread-Release wirft sonst).
- Vor `ApplyUpdatesAndRestart` feuert `IUpdateService.BeforeApplyRestart` — `App` gibt dort den Mutex frei, sonst blockiert die neu gestartete Instanz.
- **Update-Quelle:** `VelopackUpdateService.RepoUrl` (Konstante) → `https://github.com/ha-lamb/Schnack`. Das Repo ist seit 08/2026 **public** — der anonyme Update-Check funktioniert. Würde es wieder privat gestellt, schlüge der Check still fehl (nur Warning-Log).

### Secrets

- **Anthropic:** Env `ANTHROPIC_API_KEY` oder DPAPI-Datei `%APPDATA%\Schnack\secrets.dat`.
- **OpenAI:** Env `OPENAI_API_KEY` oder DPAPI-Datei `%APPDATA%\Schnack\openai-secrets.dat`.
- DPAPI-Scope: `DataProtectionScope.CurrentUser`. **Niemals** Keys in Code, Logs, Plain-Text-Settings oder Git.

### Logging-Verbote

**Niemals loggen:** Audiodateien/-pfade mit Inhaltsbezug, Transkripte, korrigierte/übersetzte Texte, API-Keys, API-Request/-Response-Bodies, `error.message`-Felder (können User-Daten enthalten).

**Erlaubt:** Recording-Start/Stop-Events, Audiodateigröße, HTTP-Statuscodes, Exception-Typen (ohne Message bei Verdacht auf User-Daten), Modus/Modellname/Hotkey/Backend, aus Fehler-Bodies nur `error.type` + `error.code` (zentral via `Services/Internal/ApiErrorLog.cs`).

**Debug-Logging** (Setting `DebugLogging` oder Env `SCHNACK_DEBUG=1`) hebt das Log-Level an — Verbote gelten weiter. Maximal Zeichenanzahl von Transkripten loggen, nie Inhalt. Level-Wechsel wirkt ohne Neustart (`LoggingLevelSwitch`).

### HTTP-Client-Konventionen

- Alle drei HTTP-Services (`OpenAiTranscriptionService`, `OpenAiChatService`, `ClaudeService`) nutzen `IHttpClientFactory` mit named clients (`"OpenAi"`, `"Claude"`).
- **Retry-Logik zentral** in `Services/Internal/HttpRetry.cs`: 3 Attempts, exponential backoff 250/500/1000 ms. Retry nur bei `RequestTimeout`, 5xx-Transienten (`InternalServerError`, `BadGateway`, `ServiceUnavailable`, `GatewayTimeout`) und `TaskCanceledException` (ohne echte Cancellation). **Niemals** bei 401/403/429 — direkt freundliche Fehlermeldung.
- **Fehler-Logging zentral** in `Services/Internal/ApiErrorLog.cs` (sanitisiert).
- JSON: Anthropic + OpenAI erwarten **snake_case**. Maßgeblich sind die `[JsonPropertyName]`-Attribute an allen DTO-Properties; als konsistentes Fallback steht die Policy überall auf `JsonNamingPolicy.SnakeCaseLower`. Neue DTO-Properties bekommen immer ein Attribut.

### Settings & Schema-Migration

- `AppSettings` ist ein `record` mit `with`-Updates. Persistenz: `%APPDATA%\Schnack\settings.json`.
- **Schema-Versionierung** über `SettingsSchema` (aktuell **3**). Schema 2 brachte `BackendProvider`, Schema 3 die Sprachen (`UiLanguage`, `DictationLanguage`) und sprachneutrale Modi (`de_correct`/`de_to_en` → `correct`/`translate`).
- Die Migration ist eine Kaskade aus `if (schema < N)`-Blöcken; **zurückgeschrieben wird einmal am Ende**, wenn `schema < CurrentSchema`. Bei neuen Feldern: Default in `AppSettings` setzen, `CurrentSchema` erhöhen, Block ergänzen, Test in `JsonSettingsServiceTests` nachziehen.
- Bestandsnutzer bleiben bei der Migration bewusst auf Deutsch; nur Neuinstallationen übernehmen die Windows-Sprache.

### Lokalisierung

- Texte liegen in `Localization/Strings.resx` (Deutsch, neutral) und `Strings.en.resx`. `NeutralResourcesLanguage=de` in der csproj; **`SatelliteResourceLanguages` nicht setzen**, sonst fällt Englisch aus dem Publish und damit aus dem Velopack-Paket.
- Zugriff über die handgeschriebene Klasse `Localization/Strings.cs` (eine Property je Schlüssel). Sie ist bewusst nicht generiert — die MSBuild-Generierung kollidiert mit WPFs Markup-Kompilierung.
- **Neuer Text heißt: Eintrag in beide `.resx` UND eine Zeile in `Strings.cs`.** `LocalizationTests` schlägt sonst fehl.
- XAML bindet per `{x:Static loc:Strings.Key}`. Das genügt für `SettingsWindow`/`AboutWindow`, weil sie transient sind und pro Öffnen neu entstehen.
- **Ausnahmen, die den Sprachwechsel nicht automatisch mitbekommen:** Das Tray-Menü schreibt seine Header beim Erzeugen fest (`ITrayService.RebuildMenu()`), und `FloatingRecordWindow` wird zwischengespeichert (`IFloatingRecordUi.ApplyLanguage()`). Beide hängen am `ILocalizationService.LanguageChanged`-Event, verdrahtet in `App.OnLanguageChanged`.
- **Logs bleiben englisch** und werden nie lokalisiert.

### Fehlerbehandlung

- Fehler mit eigener Nutzermeldung werden als `SchnackException` mit `SchnackError`-Code geworfen (`Models/SchnackError.cs`); der `DictationOrchestrator` übersetzt den Code in einen Balloon.
- **Niemals** Fehler über Exception-Texte zuordnen — das brach bei der Übersetzung still. Exception-Messages sind englisch und rein für Logs.

## Codestil

- **File-scoped namespaces**, **Nullable enabled**.
- **`async`/`await` durchgängig**, `CancellationToken` als letzter Parameter.
- **Konstruktor-Injection** für alle Services. Einzige dokumentierte Ausnahme: Keyed-Auflösung der Backend-Services im `DictationOrchestrator`.
- **Records** für DTOs. **`sealed`** für Service-Implementierungen (Ausnahme `JsonSettingsService` — Test-Subklasse, kommentiert).
- **Knappe Kommentare** an P/Invoke-Stellen, Threading-Workarounds und nicht-offensichtlichen Algorithmen. Keine XML-Docs an trivialen Methoden.

## Wo was liegt

```
C:\Dropbox\Cowork\Schnack\
├─ CLAUDE.md                    Diese Datei (Architektur, Konventionen)
├─ PROJEKT_STATUS.md            Aktueller Arbeitsstand, offene Punkte
├─ README.md                    Empfänger-Doku (Setup, Bedienung, Privacy)
├─ RELEASE.md                   Release-Workflow für Maintainer
├─ LICENSE                      MIT
├─ .gitignore / .claudeignore
├─ .claude/settings.local.json  Claude-Code-Allowlist (nicht committen)
├─ build-release.ps1            Velopack-Build + GitHub-Upload
├─ Schnack.slnx
├─ Schnack/
│  ├─ Schnack.csproj            net10.0-windows, x64, Version + ReleaseDate
│  ├─ App.xaml / App.xaml.cs    Main(), Velopack, DI, Mutex, Event-Wiring
│  ├─ Services/
│  │  ├─ Internal/              HttpRetry, ApiErrorLog, IUpdateChecker/VelopackUpdateChecker
│  │  ├─ IDictationOrchestrator / DictationOrchestrator   State-Machine + Pipeline
│  │  ├─ ILocalizationService / LocalizationService       Sprachwechsel zur Laufzeit
│  │  ├─ ITrayService / TrayService
│  │  ├─ IRecordingService / NAudioRecordingService
│  │  ├─ ITranscriptionService  → OpenAiTranscriptionService | WhisperLocalTranscriptionService
│  │  ├─ IPostProcessingService → OpenAiChatService | ClaudeService
│  │  ├─ ITextInsertionService / TextInsertionService
│  │  ├─ IHotkeyService / HotkeyService
│  │  ├─ ISettingsService / JsonSettingsService
│  │  ├─ ISecretService / DpapiSecretService
│  │  ├─ IFloatingRecordUi / FloatingRecordUiService
│  │  ├─ IUpdateService / VelopackUpdateService
│  │  ├─ IWhisperModelDownloadService / WhisperModelDownloadService
│  │  ├─ DictationPrompts       Modus-Prompts (de_correct, de_to_en)
│  │  └─ MicrophoneEnumerator (static)
│  ├─ ViewModels/SettingsViewModel.cs   Dirty-Tracking
│  ├─ Views/                    SettingsWindow, AboutWindow, FirstRunWindow, FloatingRecordWindow
│  ├─ Commands/RelayCommand.cs
│  ├─ Localization/             Strings.resx (de), Strings.en.resx, Strings.cs (Zugriff)
│  ├─ Models/                   AppSettings, AppLanguage, BackendProvider, DictationMode,
│  │                            DictationChoice (die vier Diktat-Optionen),
│  │                            RecordingState, SchnackError/SchnackException
│  │  ├─ Claude/                Anthropic Request/Response-DTOs
│  │  └─ OpenAi/                OpenAI Request/Response-DTOs
│  ├─ Interop/Win32.cs          P/Invoke gebündelt
│  └─ Resources/                tray-icon.ico, Schnack_Logo.png
├─ Schnack.Tests/               xUnit + Moq (Services, Settings, Orchestrator, Update)
└─ releases/                    Velopack-Output (gitignored; letztes Full-Paket für Delta-Updates behalten!)

Laufzeit-Pfade (NICHT im Repo):
%APPDATA%\Schnack\              settings.json, secrets.dat, openai-secrets.dat, models/, logs/
%LocalAppData%\Schnack\         Velopack-Installation (per-user)
%TEMP%\Schnack\                 temporäre WAV-Dateien
```

## Workflow-Konventionen für Claude Code

### Standard-Vorgehen

1. **Vor größeren Änderungen:** Plan Mode verwenden, Plan abwarten.
2. **Vor Commit:** `dotnet build` und `dotnet test` müssen grün sein, keine neuen Warnungen.
3. **Bei neuen NuGet-Paketen:** vorher nachfragen, Lizenz prüfen, in README dokumentieren.
4. **Bei Win32-Interop- oder Threading-Änderungen:** extra-vorsichtig, Begründung im Code-Kommentar.
5. **Anthropic-/OpenAI-Modellnamen:** wenn ein Name 404 zurückgibt, aktuelle Liste aus der jeweiligen Doku verifizieren statt zu raten.
6. **Wichtige Erkenntnisse/Statuswechsel** in `PROJEKT_STATUS.md` nachziehen.

### Autonomie

`.claude/settings.local.json` definiert eine Allowlist für Routine-Befehle. Permission-Mode: **Accept Edits**.

**Selbstständig ohne Nachfrage:** Datei-Edits im Workspace; `dotnet build/test/run/restore`; Build-Fehler und neue Warnungen selbst beheben; Lese-Befehle; Refactorings innerhalb der vorgegebenen Architektur.

**Mit Nachfrage:** neue NuGet-Pakete; Architektur-Entscheidungen außerhalb dieser Datei; Änderungen außerhalb des Workspaces (Env-Variablen, globale Konfiguration); `git commit` und `git push` — Nutzer committet selbst.

### Session-Ende

1. Zusammenfassung der Änderungen.
2. `dotnet build` und `dotnet test` grün, keine neuen Warnungen.
3. **Versionierung:** Bei Codeänderungen fragt Claude per Auswahlfrage, welche Versionsstelle erhöht wird — **Major / Minor / Patch / keine Änderung** — und aktualisiert dann in `Schnack/Schnack.csproj` das `<Version>`-Element und `<AssemblyMetadata Include="ReleaseDate" Value="…"/>` auf das heutige Datum. Der Über-Dialog zeigt beide Werte automatisch aus den Assembly-Metadaten.
4. **Nicht committen.** Nutzer prüft und committet selbst.

## Repo-Hygiene & Sicherheit

- `.gitignore` deckt ab: Build-Output (`bin/`, `obj/`, `publish/`, `releases/`, `*.nupkg`), Secrets (`secrets.dat`, `openai-secrets.dat`, `.env`), Laufzeitdaten (`models/`, `logs/`, `*.wav`, `*.log`), Editor-/Temp-Dateien (`*.user`, `*.suo`, `*.bak`, `*.tmp`, `*.swp`, `~$*`, `.vs/`), Dropbox-Conflict-Dateien (`*conflict*`, `*Conflict*`) und `.claude/settings.local.json`.
- `.claudeignore` ist eine Obermenge davon (zusätzlich `*.bin` für Whisper-Modelle).
- **Vor jedem `git push`** Schlüssel-Scan:
  ```pwsh
  git grep -E "sk-ant-[a-zA-Z0-9_-]{20,}|sk-proj-[a-zA-Z0-9_-]{20,}|ghp_[a-zA-Z0-9]{36,}"
  ```
  Treffer = nicht pushen.
- **GitHub Secret Scanning + Push Protection** in den Repo-Settings aktivieren.

## Versionierung

- **Single source of truth:** `<Version>` in `Schnack/Schnack.csproj`; `ReleaseDate` als Assembly-Metadatum daneben.
- Pflege über die Session-Ende-Regel (siehe oben); `build-release.ps1` parsed `<Version>` für `vpk pack`.
- GitHub-Release-Tag: `v<version>`. SemVer: Patch für Bug-Fixes, Minor für Features, Major für Breaking Changes.

## Out of Scope (dauerhaft)

- Hybrid-Backend-Modi (z.B. OpenAI-STT + Claude-Postprocessing). Nutzer wählt einen kompletten Stack.
- Streaming-STT, Voice Activity Detection, Auto-Stop bei Stille, Live-Vorschau.
- Weitere Sprachen als Deutsch und Englisch; automatische Spracherkennung des Diktats.
- Weitere Modi außer `Correct` und `Translate`.
- Code-Signing. Multi-User. Multi-Channel-Releases (nur `win`).
- Auto-Apply von Updates ohne Nutzer-Bestätigung; Background-Polling für Update-Checks (nur beim Start + manuell).
- Private-Repo-Support für den Update-Check (würde DPAPI-Token in der App erfordern).
- Tray-Menü-Aufnahmesteuerung (Foreground-Tracking über Tray unzuverlässig — entfernt, Hinweis-Eintrag stattdessen).
