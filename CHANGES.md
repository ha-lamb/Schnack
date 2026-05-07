# Änderungs-Prompt: Schnack v1.x → nächste Iteration

## Arbeitsweise (verbindlich)

1. **Stufe 1 – Plan:** Erstelle zuerst einen kurzen Plan: betroffene Dateien, vorgesehene Änderungen pro Datei, ggf. neue Dateien, Test-Anpassungen. **Noch keinen Code.** Warte auf Freigabe.
2. **Stufe 2 – Implementierung:** Nach Freigabe vollständig und kompilierbar umsetzen. `dotnet build` und `dotnet test` müssen am Ende grün sein.

Lies zuerst `CLAUDE.md` und `PROMPT.md` für Architektur-Kontext und Codestil-Regeln. Halte alle dort definierten Regeln ein (SendInput statt SendKeys, AttachThreadInput, Dispatcher für Clipboard, Logging-Verbote, DPAPI für Secrets).

## Autonomie-Konventionen

Ziel: möglichst wenige Unterbrechungen. Eine Allowlist für `dotnet`/`git`-Lese-/Build-Befehle ist in `.claude/settings.local.json` hinterlegt; der Permission-Mode steht auf **Accept Edits**.

**Selbstständig handeln (nicht nachfragen):**
- Dateien anlegen, ändern, löschen im Workspace.
- `dotnet build` nach jeder logischen Teiländerung; bei Fehler selbst beheben, nicht nachfragen, nicht aufgeben.
- `dotnet test` nach jedem abgeschlossenen Komponenten-Set; bei Fehler selbst beheben.
- Build- oder Compiler-Warnungen, die durch eigene Änderungen entstehen: selbst beheben.
- NuGet-Pakete installieren, **die in dieser Datei explizit genannt sind** (z.B. `Whisper.net`, `Whisper.net.Runtime`).
- Refactorings innerhalb der vorgegebenen Architektur, solange keine neuen Abstraktionen jenseits dieser Datei eingeführt werden.
- Lese-Befehle (`dir`, `ls`, `cat`, `Get-Content`, `Get-ChildItem`, `Select-String`, `dotnet list`, `git status`, `git diff`, `git log`).

**Trotzdem nachfragen bei:**
- NuGet-Paketen außerhalb der hier genannten.
- Architektur-Entscheidungen, die weder in `PROMPT.md`, `CLAUDE.md` noch in dieser Datei beantwortet sind.
- Auflösung von Konflikten zwischen `PROMPT.md`/`CLAUDE.md`/`CHANGES.md` (in dieser Reihenfolge wachsende Priorität – `CHANGES.md` gewinnt im Konflikt).
- Aktionen, die Daten außerhalb des Workspaces verändern (Env-Variablen, globale Git-Config, Systemordner).
- `git commit` und `git push` (nicht von Claude Code ausführen, der Nutzer committet selbst).

**Am Ende der Session:**
- Kurze Zusammenfassung der gemachten Änderungen, gegliedert nach Änderung 1–7.
- `dotnet build` und `dotnet test` müssen grün sein.
- **Nicht committen** – der Nutzer prüft und committet selbst.
- Aktualisierte Versionsnummer (siehe Änderung 5.8) in der Zusammenfassung erwähnen.

---

## Änderung 1 – Bug: Tray-Aufnahme fügt keinen Text ein

### Symptom
Aufnahme über Rechtsklick auf Tray-Icon → „Aufnahme starten" → sprechen → „Aufnahme stoppen" funktioniert vom Ablauf her (Aufnahme läuft, Verarbeitung läuft), aber **der finale Text wird nicht in das zuvor aktive Textfeld eingefügt**.

Der schwebende Aufnahme-Button und der globale Hotkey funktionieren korrekt – nur der Tray-Menü-Pfad ist betroffen.

### Vermutete Ursache
Beim Tray-Pfad geht der Foreground-Window-Cache verloren oder enthält das falsche HWND. Aktuelle Logik in `TrayService`:
- Cache wird in `OnTrayMouseDownBeforeMenu` gesetzt (vor Menü-Öffnen).
- Beim Klick auf „Aufnahme starten" wird in `_startItem.Click` über `Dispatcher.BeginInvoke` mit `DispatcherPriority.ApplicationIdle` versucht, den Cache nach Menü-Schließen erneut zu prüfen (`ApplyForegroundCacheAfterTrayMenuClosed`).
- `App.OnStartRecordingRequested` liest `_trayService.CachedForegroundHwnd` und übernimmt es.

Mögliche Probleme, die zu prüfen sind:
- HWND zeigt am Ende auf das Schnack-Tray-Icon-Fenster oder auf Explorer.
- HWND ist `0`, aber `TextInsertionService.InsertTextAsync` returnt früh nur mit Warnung-Log → Nutzer sieht nichts.
- Das HWND ist gültig, aber `SetForegroundWindow` schlägt fehl, weil zwischen Menü-Schließen und Pipeline-Ende zu viel Zeit vergeht und Windows das Foreground-Lock greift.

### Aufgabe
1. **Diagnose ermöglichen**: Bei Debug-Logging (`SCHNACK_DEBUG=1` oder Setting) protokolliere für den Tray-Pfad zusätzlich:
   - HWND beim MouseDown (Backup)
   - HWND nach Menü-Schließen (von `ApplyForegroundCacheAfterTrayMenuClosed`)
   - HWND, der finally an `TextInsertionService` übergeben wird
   - Ergebnis von `SetForegroundWindow` im Insertion-Pfad
   - Process-Name des Ziel-HWND (zur Verifikation, dass es nicht Schnack/Explorer ist)
2. **Fix umsetzen**:
   - Sicherstellen, dass das im MouseDown-Handler erfasste HWND **nicht** überschrieben wird, falls die Nach-Menü-Heuristik ein Schnack-eigenes oder Explorer-Fenster liefert.
   - Wenn nach allen Fallbacks kein gültiges HWND vorliegt: dem Nutzer **klar** per Tray-Tipp mitteilen („Kein Zielfenster erkannt – Text liegt in der Zwischenablage, bitte mit Strg+V einfügen"), nicht still scheitern.
   - Im Tray-Pfad zusätzlich den `PreferClipboardFreeInsertion`-Modus berücksichtigen: Unicode-SendInput braucht zwingend ein gültig fokussiertes Zielfenster, sonst landet die Eingabe ins Leere. Falls Ziel-HWND nicht zuverlässig fokussiert werden kann, **automatischen Fallback auf Clipboard+Strg+V** verwenden, damit der Nutzer manuell einfügen kann.
3. **Testen**: Nach dem Fix manuell verifizieren mit Notepad, Browser-Adressleiste und einem WPF-Eingabefeld.

---

## Änderung 2 – Über-Dialog: Hintergrundfarbe an Logo angleichen

### Aktuell
`AboutWindow.xaml` hat weißen Hintergrund. Das Schnack-Logo hat einen warmen weiß-beigen Hintergrund, der visuell vom reinen Weiß absticht.

### Aufgabe
1. Den dominanten Hintergrund-Farbton aus `Resources/Schnack_Logo.png` ermitteln (warmes Weiß-Beige).
2. Diesen Farbton als `<SolidColorBrush>` in `App.xaml` als Resource definieren (Schlüssel z.B. `SchnackBackgroundBrush`), damit er global wiederverwendbar ist.
3. `AboutWindow.xaml` als Hintergrund diese Brush verwenden, damit Logo-Bereich und Fenster-Hintergrund visuell verschmelzen.
4. **Nicht** auf andere Dialoge anwenden – nur Über-Dialog. Settings-Dialog bleibt wie er ist.

### Wenn Farbton-Ermittlung schwierig ist
Verwende einen sinnvollen warmen Off-White als Default (z.B. `#FAF7F2` oder `#F8F4EC`) und kommentiere im XAML, dass der Wert ggf. an das Logo angepasst werden sollte.

---

## Änderung 3 – Einstellungen: STT-Anbieter als Entweder-oder-Auswahl

### Aktuelles Verhalten
- `OpenAiTranscriptionService` ist die einzige `ITranscriptionService`-Implementierung und wird immer verwendet.
- In den Settings gibt es API-Key-Felder für OpenAI **und** Anthropic, was suggeriert, dass beide gleichzeitig genutzt werden – tatsächlich ist OpenAI nur für STT, Anthropic für Textverarbeitung.

### Gewünschtes Verhalten
Der Nutzer soll **eine** von zwei kompletten Stacks wählen:

| Modus | STT (Audio → Text) | Textverarbeitung (Korrigieren / Übersetzen) |
|-------|--------------------|--------------------------------------------|
| **OpenAI** | OpenAI `v1/audio/transcriptions` (wie heute) | OpenAI Chat Completions (`v1/chat/completions`, gpt-4.1-mini oder gpt-4o-mini, konfigurierbar) |
| **Claude** | Lokales Whisper.net (siehe Hinweis unten) | Anthropic Claude API (wie heute) |

### Aufgabe – Architektur

1. **Neues Setting `BackendProvider`** in `AppSettings`:
   - Werte: `"openai"` oder `"claude"`
   - Default: `"openai"` (kein Bruch für bestehende Nutzer, die OpenAI-STT bereits konfiguriert haben)
2. **Neues Interface `IPostProcessingService`** mit Methode `ProcessAsync(transcript, mode, ct) → (text, isPossiblyTruncated)`.
   - `ClaudeService` implementiert es bereits faktisch – bring es unter dieses Interface.
   - Neue Implementierung `OpenAiChatService : IPostProcessingService` für `v1/chat/completions`. Verwendet die bestehenden Modus-Prompts (de_correct, de_to_en) als User-Message.
3. **Neue Implementierung `WhisperLocalTranscriptionService : ITranscriptionService`**:
   - NuGet `Whisper.net`, `Whisper.net.Runtime`, optional `Whisper.net.Runtime.Cuda`.
   - Modell-Pfad: `%APPDATA%\Schnack\models\ggml-<model>.bin`.
   - Modell-Download-Logik (HTTPS aus `huggingface.co/ggerganov/whisper.cpp`) als separater `IWhisperModelDownloadService` mit Fortschritts-Event für die UI.
   - Bei fehlendem Modell: kein Crash, sondern aussagekräftige Fehlermeldung mit Hinweis auf den Download-Button in den Settings.
4. **Service-Auswahl per Factory-Pattern oder Keyed-DI**:
   - `App.xaml.cs` liest `BackendProvider` und löst die passenden `ITranscriptionService` + `IPostProcessingService` auf.
   - Bei Wechsel des Providers in den Settings: die Pipeline nutzt ab dem nächsten Lauf automatisch den neuen Provider (kein App-Neustart nötig). Realisierbar über `IServiceProvider.GetKeyedService<...>` mit dem aktuellen Setting-Wert oder über eine Resolver-Klasse.

### Aufgabe – Settings-UI

5. Neuer Abschnitt **ganz oben** in `SettingsWindow.xaml`:
   - Überschrift: „Backend"
   - Erklärungstext (kleiner, grauer Hinweis): „Wählen Sie einen Anbieter. Beide Wege liefern dasselbe Ergebnis – sprachgerechte Korrektur oder Übersetzung."
   - Zwei `RadioButton`s:
     - **OpenAI** – „Cloud-STT + Cloud-Textverarbeitung. Schnell, qualitativ hochwertig. Audio und Transkript werden an OpenAI gesendet."
     - **Claude** – „Lokales Whisper für Spracherkennung + Anthropic Claude für Textverarbeitung. Audio bleibt lokal, nur Transkript geht an Anthropic. Erfordert einmaligen Whisper-Modell-Download (~1,6 GB)."
6. **Provider-abhängige Sichtbarkeit** der weiteren Sektionen:
   - Wenn **OpenAI** gewählt: zeige nur OpenAI-API-Key-Sektion + OpenAI-STT-Modell-Dropdown + OpenAI-Chat-Modell-Dropdown.
   - Wenn **Claude** gewählt: zeige nur Anthropic-API-Key-Sektion + Whisper-Modell-Dropdown (`large-v3-turbo`, `medium`, `base`) + Whisper-Modell-Download-Button + Whisper-GPU-Toggle (wenn CUDA-Runtime referenziert ist) + Claude-Modell-Dropdown.
   - Realisieren mit `Visibility`-Bindings auf eine `IsOpenAi` / `IsClaude` Property im ViewModel.
7. Beim Wechsel des Providers: bestehende Werte (API-Keys, Modell-Auswahlen des anderen Stacks) **nicht löschen**, nur ausblenden. Wechsel zurück soll vorherige Werte wiederherstellen.
8. **Unsichtbare aber gespeicherte Settings**: Hotkey, Mikrofon, Clipboard-Einstellungen, Debug-Logging, Modus (de_correct / de_to_en) bleiben global und werden in beiden Provider-Modi angezeigt.

### Aufgabe – Models / DTOs

9. Neues Enum `Models/BackendProvider.cs`: `OpenAi`, `Claude` mit JSON-String-Mapping über `JsonStringEnumConverter`.
10. `AppSettings` um folgende Felder erweitern (alle mit sinnvollen Defaults):
    - `BackendProvider` (default: `OpenAi`)
    - `OpenAiChatModel` (z.B. `"gpt-4o-mini"` oder `"gpt-4.1-mini"` – aktuelles Modell verifizieren)
    - `WhisperModel` (default: `"large-v3-turbo"`)
    - `WhisperUseGpu` (default: `false`)
11. **Settings-Schema-Migration**: `SettingsSchema` von `1` auf `2` hochzählen, Migration für Bestandsnutzer (fehlt `backendProvider` → setze `"openai"`, schreibe Datei zurück).

### Hinweis zu Whisper.net
- Whisper.net wurde im ursprünglichen PROMPT.md spezifiziert, später entfernt zugunsten OpenAI-only. Mit dieser Änderung kommt es als Option zurück.
- Wenn die Implementierung von Whisper-Download + GPU-Erkennung den Scope sprengt: implementiere Stufe 1 (Settings-UI + Backend-Auswahl + OpenAiChatService) vollständig und liefere Whisper-Integration als zweiten Schritt nach.

---

## Änderung 4 – Einstellungen: Button-Verhalten am unteren Rand

### Aktuelles Verhalten
- `[Speichern]` und `[Schließen]` als getrennte Buttons.
- Schließen über X oder „Schließen"-Button speichert ebenfalls (aktuelles `OnClosing`-Verhalten in `SettingsWindow.xaml.cs` ruft `PersistAndRefreshLogLevel()` auf).

### Gewünschtes Verhalten
- Zwei Buttons in dieser Reihenfolge (Windows-Standard, primary rechts):
  - **`[Abbrechen]`** (links): Verwirft alle ungespeicherten Änderungen. Wenn ungespeicherte Änderungen vorliegen: kurze Rückfrage „Änderungen verwerfen?" → bei „Ja" wird das Fenster geschlossen ohne zu speichern, bei „Nein" bleibt das Fenster offen. Wenn keine Änderungen vorliegen: kommentarlos schließen.
  - **`[Speichern]`** (rechts): Persistiert die Settings, wendet `ApplyDebugLogLevelFromSettings()` an, schließt das Fenster.
- **Beim Klick auf X**: identisch zu **[Abbrechen]** (mit Rückfrage bei ungespeicherten Änderungen).

### Aufgabe
1. **Dirty-Tracking** im `SettingsViewModel`:
   - Beim Konstruktor: Kopie der initialen Werte als Vergleichs-Baseline merken.
   - Property `IsDirty` (read-only), die `true` wird, sobald sich irgendein Feld vom Baseline-Wert unterscheidet.
   - In jedem `set`-Setter: nach `OnPropertyChanged` zusätzlich `OnPropertyChanged(nameof(IsDirty))` auslösen.
2. `SettingsWindow.xaml`:
   - Vorhandene Buttons-Zeile umbauen: `[Abbrechen]` links, `[Speichern]` rechts. Spacing wie bisher.
   - `[Speichern]` als `IsDefault="True"` (Enter-Taste).
   - `[Abbrechen]` als `IsCancel="True"` (Escape-Taste).
3. `SettingsWindow.xaml.cs`:
   - Button-Handler für Abbrechen, Speichern, OnClosing-Override: alle drei Pfade gehen über eine zentrale Methode, die `IsDirty` prüft und ggf. `MessageBox.Show` mit Frage zeigt.
   - **Speichern-Pfad**: `ViewModel.SaveCommand.Execute(null)`, `app.ApplyDebugLogLevelFromSettings()`, `Hide()` (oder `Close()` – je nachdem ob das Fenster transient via DI ist; aktuell ist es transient, daher `Close()` korrekt).
   - **Abbrechen-Pfad**: bei `IsDirty` Bestätigung, dann `Close()` ohne Save.
4. Bestehende Methoden `OnSaveAnthropicApiKeyClick` und `OnSaveOpenAiApiKeyClick` bleiben funktional unverändert (API-Key-Save ist sofort wirksam und unabhängig von „Speichern" – das ist vom Nutzer so gewollt, weil DPAPI-Speicherung atomar ist). Die Tatsache, dass API-Key-Speichern immer sofort wirkt, sollte in der UI durch den dortigen `Speichern`-Button neben dem PasswordBox bereits klar sein.

---

## Akzeptanzkriterien für die ursprünglichen Änderungen 1–4

(Vollständige, erweiterte Akzeptanzkriterien siehe Ende der Datei nach Änderung 7.)

---

## Änderung 5 – Hot Fixes & Doku-Hygiene (kleine, schnelle Korrekturen)

Sammlung aus dem letzten Code-Review. Alle einzeln klein, in Summe 30–60 min.

### 5.1 README-Privacy-Sektion korrigieren
Die README behauptet aktuell „Audiodaten verlassen den Rechner nicht. Die Spracherkennung (Whisper) läuft vollständig lokal." Das ist seit dem Wechsel auf OpenAI-STT **falsch**. Sektion neu schreiben, sodass sie das tatsächliche Verhalten der gewählten Backend-Variante (siehe Änderung 3) abbildet:
- Bei **OpenAI-Backend**: Audio + Transkript gehen an OpenAI.
- Bei **Claude-Backend**: Audio bleibt lokal (Whisper.net), nur Transkript geht an Anthropic.

### 5.2 README NuGet-Tabelle bereinigen
Die NuGet-Tabelle listet aktuell `Whisper.net`, `Whisper.net.Runtime`, `Whisper.net.Runtime.Cuda` – keines davon ist in `Schnack.csproj` referenziert. Mit Änderung 3 kommen diese Pakete zurück; bis dahin Tabelle an die tatsächlichen Referenzen anpassen oder mit dem Hinweis „nur bei Claude-Backend" markieren.

### 5.3 MainWindow-Leftover entfernen
- `App.xaml`: falls `StartupUri="MainWindow.xaml"` drin ist – entfernen. Beim Start öffnet sonst kurz ein leeres Fenster, weil `OnStartup` schon eigenständig bootstrappt.
- `MainWindow.xaml` und `MainWindow.xaml.cs` löschen – wird nirgendwo mehr referenziert.
- `Schnack.csproj` ggf. anpassen, falls die Dateien dort als Item gelistet sind.

### 5.4 Mutex-Release fault-tolerant
In `App.CleanupAndShutdown` wirft `_mutex.ReleaseMutex()` eine `ApplicationException`, wenn der Aufruf von einem anderen Thread kommt als der ursprüngliche `OnStartup`-Thread. Umbauen auf:
```csharp
try { _mutex?.ReleaseMutex(); } catch { /* not owned by this thread */ }
_mutex?.Dispose();
```

### 5.5 `BringWindowToTop` entfernen
In `TextInsertionService` wird nach `SetForegroundWindow` zusätzlich `BringWindowToTop` gerufen. Das umgeht die normale Z-Order und kann bei Fullscreen-Apps oder UAC-Dialogen Probleme machen. `SetForegroundWindow` reicht – der `BringWindowToTop`-Call (und die P/Invoke-Deklaration in `Win32.cs`, falls sonst nirgends genutzt) ersatzlos streichen.

### 5.6 `settings.local.json` sauber positionieren
Die Datei liegt aktuell im Repo-Root mit Cursor-spezifischen PowerShell-Allowlist-Einträgen. Sie gehört nach `.claude/settings.local.json` und muss in `.gitignore` aufgenommen werden. Aus dem Repo-Root entfernen (`git rm`), Verzeichnis `.claude/` anlegen, Datei dorthin verschieben, `.gitignore` ergänzen mit:
```
.claude/settings.local.json
```

### 5.7 `RelayCommand` in eigene Datei
`RelayCommand` ist aktuell am Ende von `SettingsViewModel.cs` eingebettet. In neue Datei `Schnack/Commands/RelayCommand.cs` mit Namespace `Schnack.Commands` auslagern. Using-Statement in `SettingsViewModel.cs` ergänzen.

### 5.8 Versionierung klären
`Schnack.csproj` zeigt `<Version>1.1.0</Version>`, `STRUCTURED-REQUESTS.md` spricht von „Version 1.10". Auf eine semver-konforme Variante einigen – Vorschlag: aktueller Stand `1.2.0`, nach Umsetzung aller Änderungen aus dieser Datei `1.3.0`. `<AssemblyMetadata Include="ReleaseDate" Value="..."/>` entsprechend aktualisieren.

---

## Änderung 6 – Robustheit & Symmetrie (Pipeline-Hardening)

### 6.1 `OpenAiTranscriptionService`: Retry-Logik wie ClaudeService
Aktuell schickt der STT-Service einmalig und scheitert bei jedem 503/Timeout. Analog zur Implementierung in `ClaudeService`:
- `MaxAttempts = 3`
- Retries nur bei `RequestTimeout`, `InternalServerError`, `BadGateway`, `ServiceUnavailable`, `GatewayTimeout` und `TaskCanceledException` (nicht bei `OperationCanceledException` mit `ct.IsCancellationRequested`)
- Exponential Backoff 250/500/1000 ms
- Logging analog (`Status`, `Attempt`)
- 401/403 nie retry → `HttpRequestException` mit klarer Message
- 429 → eigene `HttpRequestException` mit „Rate Limit"-Message

Die Retry-Logik ist Boilerplate, die mit dem neuen `OpenAiChatService` (Änderung 3) faktisch dreimal existieren würde. **Empfehlung**: kleine Helper-Klasse `HttpRetry.SendWithRetryAsync(...)` in `Schnack/Services/Internal/HttpRetry.cs`, die alle drei Services nutzen. Wenn das den Scope sprengt: Copy-Paste, aber identisch und gleichzeitig ändern.

### 6.2 `NAudioRecordingService.StopRecording`: Wait mit Timeout
Aktuell:
```csharp
_stopTcs.Task.Wait();
```
Wenn NAudio das `RecordingStopped`-Event nie auslöst (Treiber-Bug, abgesteckes USB-Mikrofon), hängt die ganze Pipeline für immer. Umbauen auf:
```csharp
if (!_stopTcs.Task.Wait(TimeSpan.FromSeconds(5)))
{
    _logger.LogError("Recording stop timeout after 5s — NAudio did not signal completion");
    throw new InvalidOperationException("Aufnahme konnte nicht sauber beendet werden.");
}
```
Catch in `RunPipelineAsync` zeigt dann freundliche Tray-Meldung „Mikrofon antwortet nicht – bitte Verbindung prüfen".

### 6.3 `ITranscriptionService : IAsyncDisposable` aufräumen
Das Interface erbt von `IAsyncDisposable`, aber `OpenAiTranscriptionService` returnt nur `ValueTask.CompletedTask`. Mit Whisper.net (Änderung 3) wird Cleanup tatsächlich relevant (Whisper-Context, Modell-Speicher). Daher:
- Interface-Erbschaft beibehalten.
- `OpenAiTranscriptionService.DisposeAsync` bleibt no-op, aber mit kurzem Kommentar „nichts zu cleanen".
- `WhisperLocalTranscriptionService.DisposeAsync` (neu in Änderung 3) gibt Whisper-Context und Modell-Stream frei.
- `App.CleanupAndShutdown` muss die Service-Dispose-Reihenfolge korrekt einhalten: `await ((IAsyncDisposable)_transcriptionService).DisposeAsync()` vor `_serviceProvider.DisposeAsync()`.

### 6.4 OpenAI-Fehler-Body ins Log (sanitisiert)
Bei Non-Success-Status liest `OpenAiTranscriptionService` den Response-Body aktuell nicht. Für Diagnose: Body lesen, **nur** `error.type` und `error.code` aus dem JSON loggen, **nicht** `error.message` (kann User-Daten enthalten). Falls Body kein gültiges JSON ist: nur Statuscode loggen. Analog für `ClaudeService` und neuen `OpenAiChatService`.

### 6.5 Clipboard-Backup-Größenlimit
In `TextInsertionService.InsertOnUiThreadAsync` wird der alte Clipboard-Inhalt vor dem Setzen des neuen Texts gesichert. Bei großen Inhalten (z.B. mehrere MB Text aus Word) kann `Clipboard.GetText()` mehrere hundert ms blockieren. Limit einbauen:
```csharp
const int MaxBackupChars = 100_000;
if (_settings.Settings.RestoreClipboard && Clipboard.ContainsText())
{
    var existing = Clipboard.GetText();
    if (existing.Length <= MaxBackupChars)
        previousClipboard = existing;
    else
        _logger.LogDebug("Clipboard backup skipped, content too large ({Chars} chars)", existing.Length);
}
```

### 6.6 OpenAI-STT-Tests
Bisher null Tests für `OpenAiTranscriptionService`. Mindestens analog zu `ClaudeServiceTests`:
- erfolgreicher Roundtrip mit gemocktem 200-OK + JSON-Body
- 401 → `HttpRequestException`
- API-Key fehlt → `InvalidOperationException`
- 503 dann 200 (Retry-Verhalten nach Änderung 6.1)

---

## Änderung 7 – Refactoring (mittelfristig, optional in dieser Iteration)

Diese drei Punkte sind keine Bugs, sondern Wartbarkeit. Können auch in eine spätere Session geschoben werden.

### 7.1 `App.xaml.cs` aufteilen
Aktuell ~400 Zeilen / 17 KB: DI-Bootstrap, Pipeline-Orchestrierung, Event-Wiring, Mutex, Logging-Setup. Vorschlag:
- **`Services/DictationOrchestrator.cs`** (neu): kapselt State Machine (`_recordingState`, `_cachedTargetHwnd`, `_pipelineCts`), `TryToggleRecordingUserAction`, `StartRecording`, `StopAndProcess`, `RunPipelineAsync`. Bekommt Recording/Transcription/PostProcessing/TextInsertion per DI.
- **`App.xaml.cs`**: nur noch Mutex, ServiceProvider-Build, Logging-Setup, Tray/Hotkey/Floating-Event-Wiring auf `IDictationOrchestrator`-Methoden, Cleanup.

Das macht zukünftige Tests für die Pipeline-Logik überhaupt erst sinnvoll möglich (aktuell ist die Pipeline-Logik nicht testbar, weil sie in `App` lebt).

### 7.2 `sealed`-Konsistenz
`FloatingRecordUiService` und `OpenAiTranscriptionService` sind `sealed`, alle anderen Services nicht. Vorschlag: alle Services `sealed` machen, **außer**:
- `JsonSettingsService` bleibt offen (Test-Subklasse `TestableJsonSettingsService` leitet ab) – mit Kommentar `// virtual SettingsFilePath für Test-Subklasse, daher nicht sealed`.

### 7.3 JSON-Property-Naming für Anthropic & OpenAI verifizieren
**Wichtig vor Implementierung von Änderung 3**: Anthropic erwartet snake_case (`max_tokens`, `stop_reason`, `content`), OpenAI ebenfalls (`response_format`, `chat.completions`). `ClaudeService` setzt aktuell `JsonNamingPolicy.CamelCase`, was die DTOs als `maxTokens` etc. serialisieren würde – dann wäre die App nicht funktionsfähig.

Zwei mögliche Lösungen:
- (a) Auf `JsonNamingPolicy.SnakeCaseLower` umstellen (System.Text.Json ab .NET 8 verfügbar).
- (b) `[JsonPropertyName("max_tokens")]` etc. an allen DTO-Properties.

(a) ist sauberer und weniger fehleranfällig. Bitte prüfen, was im aktuellen Code (in den nicht im Snapshot enthaltenen `Models/Claude/*.cs`-Dateien) verwendet wird und konsistent durchziehen.

---

## Akzeptanzkriterien (erweitert)

Nach Abschluss aller Änderungen:

1. `dotnet build` und `dotnet test` grün, **keine neuen Warnungen**.
2. **Tray-Pfad**: Cursor in Notepad → Rechtsklick Tray → „Aufnahme starten" → sprechen → Rechtsklick Tray → „Aufnahme stoppen" → Text erscheint in Notepad. Wenn nicht zuverlässig: klare Tray-Notification + Text im Clipboard.
3. **Über-Dialog** zeigt Hintergrund im Logo-Farbton, Logo verschmilzt visuell mit dem Fenster.
4. **Settings**: Radio-Button-Auswahl OpenAI/Claude blendet die jeweils irrelevante Konfiguration aus. Wechsel zur Laufzeit funktioniert ohne App-Neustart.
5. **Settings-Buttons**: `[Abbrechen]` links, `[Speichern]` rechts. Speichern schließt das Fenster automatisch. Abbrechen mit ungespeicherten Änderungen fragt nach Bestätigung. Enter = Speichern, Escape = Abbrechen.
6. **Schema-Migration**: Bestehende `settings.json` (Schema 1) wird automatisch auf Schema 2 migriert mit `backendProvider: "openai"`.
7. **README** stimmt mit dem Code überein – insbesondere Privacy-Sektion und NuGet-Tabelle.
8. **Kein leeres MainWindow** beim Start.
9. **App-Beenden** wirft keine Exceptions mehr (Mutex-Fix).
10. **Mikrofon-Disconnect** während Aufnahme: App hängt nicht, sondern zeigt nach max. 5 s eine Fehlermeldung.
11. **Tests**: 
    - `JsonSettingsServiceTests`: neuer Test für Schema-1→2-Migration.
    - `ClaudeServiceTests`: weiterhin grün (ggf. Interface-Umbenennung berücksichtigen).
    - Neue `OpenAiChatServiceTests` analog (Retry, 401, Multi-Block, max_tokens-Äquivalent).
    - Neue `OpenAiTranscriptionServiceTests`: API-Key fehlt, 401, Roundtrip, Retry nach 503.
    - Falls Änderung 7.1 umgesetzt: erste Tests für `DictationOrchestrator` (State-Machine-Übergänge, leerer Transcript-Pfad).
12. **Repo-Hygiene**: `settings.local.json` nicht mehr im Repo-Root, `MainWindow`-Dateien gelöscht, `.gitignore` aktualisiert.

## Out of Scope (nicht umsetzen)

- Hybrid-Modi (z.B. OpenAI-STT + Claude-Postprocessing): bewusst nicht angeboten. Der Nutzer wählt einen kompletten Stack.
- Streaming-STT, VAD, Auto-Stop.
- Auto-Update.
- Code-Signing / Installer.
- Magic-Number-Konstanten in zentrale Config-Klasse extrahieren (S2 aus dem Review): kann später kommen, aktuell nicht wichtig genug.

---

## Reihenfolge-Empfehlung

Wenn Änderung 3 (Backend-Auswahl) zu groß für eine Session ist:

**Session A (1–2 h):** Änderungen 1, 2, 4, 5 vollständig + Änderung 6 (außer 6.1 für den noch nicht existierenden ChatService).

**Session B (2–3 h):** Änderung 3 vollständig + Änderung 6.1 (Retry-Helper für alle drei HTTP-Services) + Änderung 6.6 (STT-Tests).

**Session C (1–2 h, optional):** Änderung 7 (Refactoring).

---

**Beginne jetzt mit Stufe 1 (Plan). Code erst nach meiner Freigabe. Wenn du beim Plan denkst, dass die Reihenfolge oder das Splitting anders sinnvoller ist, schlage es vor.**
