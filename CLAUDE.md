# CLAUDE.md – Projektkontext Schnack

> **Erste Datei, die Claude Code in jeder Session liest.** Halte sie aktuell, wenn sich Architektur, Tools oder Konventionen ändern. Sie beschreibt den **Ist-Zustand** des Codes und ist zusammen mit `PROJEKT_STATUS.md` (aktueller Arbeitsstand) die Single Source of Truth.

## Was ist Schnack?

Internes Windows-11-Tray-Tool (.NET 10 / WPF) für persönliche Nutzung. Nimmt gesprochene Sprache via Mikrofon auf, transkribiert sie und fügt den geglätteten oder übersetzten Text in das zuvor aktive Windows-Textfeld ein.

**Zweisprachig (Deutsch/Englisch).** Zwei voneinander unabhängige Dinge:

- **Oberflächensprache** (`UiLanguage`) — Tray, Dialoge, Meldungen. Wechsel wirkt sofort.
- **Diktat-Modus** — eine von vier Optionen: `Deutsch`, `Englisch`, `Deutsch → Englisch`, `Englisch → Deutsch`. Geglättet wird immer; die Pfeil-Varianten übersetzen zusätzlich.

Die vier Optionen sind intern die Kombinationen aus `DictationLanguage` × `DictationMode` (`Correct`/`Translate`), gebündelt in `Models/DictationChoice.cs` — **die einzige Quelle** für Tray-Menü und Einstellungen, damit beide nicht auseinanderlaufen. Jede Kombination hat einen eigenen Prompt in `DictationPrompts`. Die Auswahl wird sofort persistiert (Tray wie Dialog), weil die Services die Diktiersprache pro Lauf aus den Settings lesen.

**Zwei Schichten, kein Stack-Wechsel.** Das ist die tragende Struktur — wer sie als „Backend-Wahl" missversteht, baut die Oberfläche falsch:

| Schicht | Wer | Privacy |
|---------|-----|---------|
| **Spracherkennung** | Whisper.net **lokal**, immer | nichts verlässt das Gerät |
| **Nachbearbeitung** (optional) | OpenAI `v1/chat/completions` **oder** Anthropic `v1/messages` | nur das Transkript geht an den gewählten Dienst |

Eine Cloud-Spracherkennung gibt es nicht mehr — sie war mit Vulkan langsamer als lokal und weniger privat.

**Der Schalter `TextSmoothing` („Text glätten")** entscheidet, ob die zweite Schicht läuft. Aus oder ohne hinterlegten Schlüssel: der Rohtext der Erkennung wird eingefügt. Die Regel steht an genau einer Stelle — `Services/Internal/SmoothingPolicy.cs` — und lautet `TextSmoothing && keyAvailable`. Sie wird von Pipeline, Tray-Menü und Einstellungsdialog gleichermaßen gestellt.

**Übersetzt wird ausschließlich vom KI-Dienst.** Ohne aktive Glättung bietet `DictationChoice.Available` deshalb nur die beiden reinen Diktiersprachen an. Whisper übersetzt nicht selbst (siehe „Lokale Spracherkennung: Leistung und Grenzen").

**Erststart** (`Services/Internal/FirstRunDefaults.cs`): OpenAI-Schlüssel → OpenAI mit Glättung, sonst Anthropic-Schlüssel → Claude mit Glättung, sonst Glättung aus. Ein fehlender Schlüssel braucht **keinen** Rückfallmechanismus mehr — `SmoothingPolicy` wertet die Verfügbarkeit bei jedem Lauf neu aus.

Dienst- und Glättungswechsel wirken ab dem nächsten Pipeline-Lauf ohne App-Neustart (Keyed-DI-Auflösung pro Lauf).

**Kein kommerzielles Produkt.** Kein Enterprise-Rollout. Kein Mehrbenutzer-Setup.

## Tech-Stack (verbindlich)

- **C# 14 / .NET 10 / WPF / x64** (`TargetFramework: net10.0-windows`)
- **WPF-Tray:** `H.NotifyIcon.Wpf` (kein Mischen mit WinForms-NotifyIcon)
- **Globaler Hotkey:** `NHotkey.Wpf` (Default `Ctrl+Alt+S`)
- **Audio-Aufnahme:** `NAudio` (16 kHz mono PCM WAV)
- **STT:** `Whisper.net` + `Whisper.net.Runtime` (CPU) + `Whisper.net.Runtime.Vulkan` (GPU, optional über `WhisperUseGpu`). Modelle in `%APPDATA%\Schnack\models\`, Download via `IWhisperModelDownloadService` aus `huggingface.co/ggerganov/whisper.cpp`.
- **Nachbearbeitung (Claude):** `HttpClient` gegen Anthropic `v1/messages`. Kein Anthropic-SDK.
- **Nachbearbeitung (OpenAI):** `HttpClient` gegen OpenAI `v1/chat/completions`. Gleiches Interface (`IPostProcessingService`).
- **Installer + Auto-Update:** `Velopack` (NuGet) + `vpk` CLI. Updates via GitHub Releases.
- **DI:** `Microsoft.Extensions.DependencyInjection` (inkl. Keyed Services für die Wahl des KI-Dienstes)
- **Logging:** `Microsoft.Extensions.Logging` + Serilog File-Sink (`Serilog.Sinks.File`)
- **Secrets:** Windows DPAPI (`ProtectedData`, seit .NET 10 ohne separates NuGet-Paket)
- **Tests:** xUnit + Moq

**Tool-Substitutionen ohne explizite Diskussion sind nicht erlaubt.** Insbesondere kein `SendKeys`, kein `keybd_event`, kein WinForms-NotifyIcon, kein Azure-STT, kein OpenAI-/Anthropic-SDK, kein anderes Update-Framework als Velopack.

## Build & Run

```pwsh
# Optional: API-Key für die Nachbearbeitung (nur einer nötig, je nach gewähltem Dienst)
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
- **`Services/DictationOrchestrator.cs`** (`IDictationOrchestrator`): kapselt die State-Machine `Idle ⇄ Recording ⇄ Processing` (thread-safe via `Interlocked.CompareExchange`) und die Pipeline Aufnahme → Transkription → Postprocessing → Texteinfügung. Löst den `IPostProcessingService` pro Lauf per Keyed DI über `SmoothingPolicy.PostProcessingKey` auf (bewusste Ausnahme von der Konstruktor-Injection, damit Dienst- und Glättungswechsel ohne Neustart wirken). Den effektiven Diktat-Modus leitet er aus Settings und Glättungszustand ab, nicht aus der mutablen `CurrentMode`-Property. Cacht das Ziel-HWND beim Aufnahme-Start.
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

### Tray-Kontextmenü: Platzierung selbst gebaut

`H.NotifyIcon` setzt das Menü in `ShowContextMenu` stur auf die Cursorposition (`Placement = AbsolutePoint`) und überlässt die Korrektur WPFs Popup-Automatik. Die greift nur, wenn die Menühöhe beim Öffnen schon bekannt ist — sonst rutscht das Menü hinter die Taskleiste. `ShowContextMenu` ist nicht `virtual`, und `PopupPlacement`/`PopupOffset`/`CustomPopupPosition` wirken nur für Balloons.

Deshalb bricht `TrayService.OnPreviewContextMenuOpen` das Öffnen per `e.Handled = true` ab und übernimmt es selbst: messen, Position über `Services/Internal/TrayMenuPlacement.cs` gegen den Arbeitsbereich klemmen, öffnen, `SetForegroundWindow` nachziehen. **Der Handler ist kein überflüssiger Ballast — ohne ihn kommt der Fehler zurück.** Der `SetForegroundWindow`-Aufruf am Ende ist ebenfalls Pflicht, sonst schließt das Menü nicht beim Klick daneben.

Der Arbeitsbereich kommt bewusst vom Monitor unter dem Cursor (`MonitorFromPoint`/`GetMonitorInfo`), nicht aus `SystemParameters.WorkArea` — das liefert nur den Primärmonitor.

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
- **`CleanupAndShutdown`: Mutex-Freigabe und `Shutdown(0)` stehen in einem `finally`.** `ShutdownMode` ist `OnExplicitShutdown`; bleibt `Shutdown(0)` aus, läuft die Nachrichtenschleife weiter, obwohl das Tray-Symbol schon entsorgt ist — ein unsichtbarer Prozess, der den Mutex hält und jeden neuen Start abweist. Genau das passierte, als `DisposeAsync` des Whisper-Dienstes warf. **Hinter das Aufräumen gehört nie etwas, das werfen kann.**
- **Weitergeleitete DI-Registrierungen entsorgen mehrfach.** `ITranscriptionService` und `IWhisperWarmup` zeigen per Fabrik auf dieselbe `WhisperLocalTranscriptionService`-Instanz (sonst läge das Modell doppelt im Speicher). Der Container erfasst jede realisierte Fabrik-Instanz einzeln und ruft `DisposeAsync` entsprechend mehrfach auf — die Methode ist deshalb per `Interlocked` gegen Zweitaufrufe gesperrt. Gilt für jeden Dienst, der so registriert wird.
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

- Beide HTTP-Services (`OpenAiChatService`, `ClaudeService`) nutzen `IHttpClientFactory` mit named clients (`"OpenAi"`, `"Claude"`).
- **Retry-Logik zentral** in `Services/Internal/HttpRetry.cs`: 3 Attempts, exponential backoff 250/500/1000 ms. Retry nur bei `RequestTimeout`, 5xx-Transienten (`InternalServerError`, `BadGateway`, `ServiceUnavailable`, `GatewayTimeout`) und `TaskCanceledException` (ohne echte Cancellation). **Niemals** bei 401/403/429 — direkt freundliche Fehlermeldung.
- **Fehler-Logging zentral** in `Services/Internal/ApiErrorLog.cs` (sanitisiert).
- JSON: Anthropic + OpenAI erwarten **snake_case**. Maßgeblich sind die `[JsonPropertyName]`-Attribute an allen DTO-Properties; als konsistentes Fallback steht die Policy überall auf `JsonNamingPolicy.SnakeCaseLower`. Neue DTO-Properties bekommen immer ein Attribut.

### Settings & Schema-Migration

- `AppSettings` ist ein `record` mit `with`-Updates. Persistenz: `%APPDATA%\Schnack\settings.json`.
- **Schema-Versionierung** über `SettingsSchema` (aktuell **4**). Schema 2 brachte die Backend-Wahl, Schema 3 die Sprachen und sprachneutrale Modi, Schema 4 den Wechsel von der Stack-Wahl zum Schichtenmodell (`backendProvider` → `aiService` + `TextSmoothing`).
- **Schema 4 liest den alten Wert aus dem Roh-JSON** (`ReadLegacyBackendProvider`), weil `backendProvider` in `AppSettings` nicht mehr existiert. Das neue Feld heißt bewusst anders: ein `[JsonPropertyName("backendProvider")]` auf `AiService` würfe beim alten Wert `"local"`, und der `catch` in `LoadAsync` setzte daraufhin **alle** Einstellungen auf Default zurück — stillschweigend.
- Die Migration ist eine Kaskade aus `if (schema < N)`-Blöcken; **zurückgeschrieben wird einmal am Ende**, wenn `schema < CurrentSchema`. Bei neuen Feldern: Default in `AppSettings` setzen und Test in `JsonSettingsServiceTests` nachziehen. `CurrentSchema` **nur** erhöhen, wenn bestehende Werte umgeschrieben werden müssen — rein additive Felder erhalten in alten Dateien automatisch den Record-Default.
- Bestandsnutzer bleiben bei der Migration bewusst auf Deutsch; nur Neuinstallationen übernehmen die Windows-Sprache.

### Lokale Spracherkennung: Leistung und Grenzen

- **Runtime-Wahl** über `RuntimeOptions.RuntimeLibraryOrder` (statisch, `Whisper.net.LibraryLoader`), gesetzt in `GetOrCreateFactoryAsync` **unmittelbar vor** `WhisperFactory.FromPath` — nicht im Startup, wo die Settings teils noch nicht geladen sind. Whisper.net probt die Liste der Reihe nach und fällt selbsttätig zurück; ein eigener try/catch-Fallback wäre schädliche Doppelung. `RuntimeOptions.LoadedLibrary` verrät danach, welche Runtime tatsächlich gewonnen hat — wird geloggt.
- **Gemessen (RTX 5070 Ti, large-v3-turbo, 26,9 s Audio):** CPU 6757 ms, Vulkan 295 ms — Faktor 23 bei wortgleichem Transkript. Vulkan ist deshalb die Empfehlung, bleibt aber optional, weil die Wirkung treiberabhängig ist.
- **`useGpu` gehört in den Cache-Schlüssel der Factory**, sonst wirkt die Umschaltung erst nach Neustart.
- **Vorladen** (`WhisperPreload`, Default an) lädt beim Start das Modell **und** rechnet eine Sekunde Stille durch. Der zweite Teil ist der wichtigere: er erzwingt Graph-Allokation und Shader-Übersetzung. Gemessen: 4749 ms, die sonst das erste Diktat bezahlt. Fire-and-Forget, scheitert still; der `CancellationTokenSource` wird in `CleanupAndShutdown` **vor** der Entsorgung des Providers gecancelt, sonst läuft der Warmup in ein entsorgtes Semaphor.
- **Whisper erfindet auf Stille Floskeln — Segmente werden nach `Probability` gefiltert.** Auf sprachfreien Abschnitten halluziniert das Modell Wendungen aus seinen Untertitel-Trainingsdaten, im Deutschen typischerweise „Vielen Dank."; bei reinem Rauschen kam sogar der Vokabel-Prompt als Transkript zurück. `Services/Internal/SegmentFilter.cs` verwirft Segmente unter **0,80**.
  Gemessen (large-v3-turbo, synthetische Sprache mit Raumklang-Anhang): echte Sprache **0,947–0,992**, Halluzinationen **0,647–0,779** — dazwischen kein einziger Messwert.
  Drei Alternativen wurden gemessen und verworfen: `NoSpeechProbability` ist auf diesem Weg immer 0; `MinProbability` und Zeichen-pro-Sekunde lagen im Fall „leise Rede plus Rauschen" **über** den Werten echter Sprache. Wortlisten scheiden aus, weil sie auch ein gesprochenes „vielen Dank" verwürfen.
  **`WithProbabilities()` ist Voraussetzung** — ohne die Option bleibt `SegmentData.Probability` auf 0.
  Liegt jedes Segment unter der Schwelle, bleibt der Text **leer**. Das meldet der Orchestrator als „keine Sprache erkannt" — ehrlicher, als erfundenen Text einzufügen.
- **Whisper übersetzt nicht** — bewusste Entscheidung, nicht Unvermögen der Bibliothek. Zwei gemessene Gründe: `large-v3-turbo` (das Standardmodell) ignoriert das Translate-Flag und liefert still die Quellsprache, während dieselbe Aufnahme mit `base` sauber übersetzt; und Whisper kann grundsätzlich **nur ins Englische**. Eine Übersetzungsoption, die je nach Modell wirkt oder nicht und nur in eine Richtung geht, ist schlechter als keine. Übersetzt wird deshalb ausschließlich vom KI-Dienst.

### Nachbearbeitung: Regeln in den System-Teil, Temperatur auf 0

Beides zusammen entscheidet darüber, ob geglättet oder umgeschrieben wird — im Alltagsgebrauch fiel auf, dass der Text zunehmend inhaltlich abwich.

- **`temperature: 0`.** Anthropic setzt ohne Angabe **1,0**, das Maximum des Bereichs 0–1; die Doku empfiehlt ausdrücklich Werte nahe 0 für analytische Aufgaben. Ohne den Parameter war jedes Diktat ein neuer Würfelwurf. `ClaudeService` und `OpenAiChatService` setzen jetzt beide 0.
- **Opus 4.7 und neuer lehnen `temperature` mit HTTP 400 ab** — der Parameter wurde dort entfernt. Weil das Modell ein freies Textfeld in den Einstellungen ist, wiederholt `ClaudeService.SendWithTemperatureFallbackAsync` den Aufruf einmal ohne Temperatur, statt jedes Diktat scheitern zu lassen. **Diesen Rückfall nicht entfernen.**
- **Die Regeln stehen im `system`-Feld**, nicht in der Nutzernachricht. Dort wiegen sie schwerer, und das Transkript kann nicht als Anweisung gelesen werden. `DictationPrompts.Build` liefert deshalb ein `DictationPrompt`-Paar aus `System` und `UserContent`.
- **Das Transkript ist mit `<diktat>`-Markierungen eingefasst**, und die Regeln sagen ausdrücklich, dass der Inhalt dazwischen keine Anweisung ist. Belegt: Ein diktiertes „kannst du mir erklären, was der Unterschied zwischen TCP und UDP ist" kommt als korrigierte Frage zurück, nicht als Erklärung.
- **Die Prompts erteilen keine Umschreib-Lizenzen mehr.** Frühere Formulierungen wie „Füllwörter leicht reduzieren" oder „professionell und natürlich formulieren" luden genau zu dem ein, was vermieden werden soll. Grundregel ist jetzt: im Zweifel unverändert lassen.
- `DictationOrchestrator.WarnIfLengthDeviates` protokolliert eine Warnung, wenn die Glättung die Zeichenzahl um mehr als 40 % verändert — dann hat das Modell vermutlich geantwortet statt korrigiert. Nur im Korrektur-Modus; eine Übersetzung darf die Länge verschieben.

### Vokabular

- `AppSettings.Vocabulary` (`string[]`) hält Eigennamen und Fachbegriffe. `Services/Internal/VocabularyPrompt.cs` formatiert sie für die zwei Stellen, an denen sie wirken: als Vorab-Kontext der Spracherkennung (gekappt auf ~700 Zeichen wegen des 224-Token-Fensters) und als Anweisungsblock im Nachbearbeitungs-Prompt (`{{VOCABULARY}}`-Platzhalter in allen vier Templates).
- Die Formulierungen dort sind **funktionale Prompts, keine UI-Texte** — sie gehören nicht in die `.resx`, sondern folgen der Sprache des jeweiligen Prompt-Templates.
- Im lokalen Whisper-Pfad zusätzlich `WithCarryInitialPrompt(true)`, sonst wirkt die Liste nur im ersten 30-Sekunden-Fenster.
- **Begriffe nie im Klartext loggen** — nur ihre Anzahl.
- **Ohne Glättung wirkt das Vokabular nur einfach** (als Vorab-Kontext der Erkennung); die Schreibvorgabe im Nachbearbeitungs-Prompt entfällt mit dem Schritt, der sie ausgewertet hätte.

### Logo und Icons

- **Vektor-Master** sind `Resources/Schnack_Logo.svg` und `Schnack_Logo_White.svg` — Änderungen dort beginnen, PNG/ICO daraus neu exportieren (nicht umgekehrt).
- Zwei Varianten: farbig (Petrol `#055859`, Welle `#FEF9ED`) für helle Untergründe, weiße Silhouette für den Aufnahme-Knopf in Rot (`#DC3545`) und Gelb (`#FFC107`) — dort hat Petrol zu wenig Kontrast. `FloatingRecordWindow.SetRecordingVisual` schaltet um.
- Alle Grafiken sind **freigestellt** (transparent). Das frühere cremefarbene Quadrat ist entfallen; `SchnackBackgroundBrush` bleibt davon unberührt.
- `tray-icon.ico` enthält 16/32/48/**256** px — die 256er braucht Windows für Alt+Tab, große Explorer-Ansicht und den Velopack-Installer.

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
- **Konstruktor-Injection** für alle Services. Einzige dokumentierte Ausnahme: die Keyed-Auflösung des `IPostProcessingService` im `DictationOrchestrator` — der Dienst ist zur Laufzeit umschaltbar. Die Spracherkennung wird normal injiziert, seit es nur noch eine gibt.
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
│  │  ├─ Internal/              HttpRetry, ApiErrorLog, IUpdateChecker/VelopackUpdateChecker,
│  │  │                        SmoothingPolicy (Glättungsregel + Keyed-Schlüssel),
│  │  │                        FirstRunDefaults (Vorbelegung beim Erststart)
│  │  ├─ IDictationOrchestrator / DictationOrchestrator   State-Machine + Pipeline
│  │  ├─ ILocalizationService / LocalizationService       Sprachwechsel zur Laufzeit
│  │  ├─ ITrayService / TrayService
│  │  ├─ IRecordingService / NAudioRecordingService
│  │  ├─ ITranscriptionService  → WhisperLocalTranscriptionService (einzige Implementierung)
│  │  ├─ IPostProcessingService → OpenAiChatService | ClaudeService | PassthroughPostProcessingService
│  │  ├─ IWhisperWarmup         Vorladen, nur von WhisperLocalTranscriptionService implementiert
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
│  ├─ Models/                   AppSettings, AppLanguage, AiService, DictationMode,
│  │                            DictationChoice (die vier Diktat-Optionen),
│  │                            RecordingState, SchnackError/SchnackException
│  │  ├─ Claude/                Anthropic Request/Response-DTOs
│  │  └─ OpenAi/                OpenAI Request/Response-DTOs
│  ├─ Interop/Win32.cs          P/Invoke gebündelt
│  └─ Resources/                tray-icon.ico (16/32/48/256), Schnack_Logo.png (farbig),
│                              Schnack_Logo_White.png (weiße Silhouette für rot/gelb),
│                              *.svg = Vektor-Master für künftige Änderungen
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
- **Abgebrochene Builds** hinterlassen mitunter eine `Schnack_*_wpftmp.csproj` im Projektordner (WPF-Zwischenprojekt, wird nur bei Erfolg aufgeräumt). `dotnet run --project Schnack` scheitert dann mit „mehrere Projektdateien" — die Datei ist reines Artefakt und kann gelöscht werden. Beide Ignore-Dateien decken das Muster ab.
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

- Cloud-Spracherkennung. Sie wurde 08/2026 entfernt: lokal ist mit Vulkan schneller und privater.
- Lokales Sprachmodell für Glättung/Übersetzung ohne Cloud (Ollama o.ä.). Ohne Glättung bleibt es beim Rohtext.
- Übersetzung durch Whisper selbst — siehe „Lokale Spracherkennung: Leistung und Grenzen".
- Streaming-STT, Voice Activity Detection, Auto-Stop bei Stille, Live-Vorschau.
- Weitere Sprachen als Deutsch und Englisch; automatische Spracherkennung des Diktats.
- Weitere Modi außer `Correct` und `Translate`.
- Code-Signing. Multi-User. Multi-Channel-Releases (nur `win`).
- Auto-Apply von Updates ohne Nutzer-Bestätigung; Background-Polling für Update-Checks (nur beim Start + manuell).
- Private-Repo-Support für den Update-Check (würde DPAPI-Token in der App erfordern).
- Tray-Menü-Aufnahmesteuerung (Foreground-Tracking über Tray unzuverlässig — entfernt, Hinweis-Eintrag stattdessen).
