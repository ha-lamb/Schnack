# Änderungs-Prompt: Schnack v2 – Floating-Button-Toggle + Installer

> Folge-Iteration nach `CHANGES.md`. Diese Datei behandelt **zwei eigenständige Features** und kann unabhängig von offenen Punkten der ersten CHANGES-Datei umgesetzt werden.

## Arbeitsweise (verbindlich)

1. **Stufe 1 – Plan:** Erstelle einen kurzen Plan: betroffene Dateien, vorgesehene Änderungen pro Datei, ggf. neue Dateien, Test-Anpassungen, manuelle Verifikationsschritte. **Noch keinen Code.** Warte auf Freigabe.
2. **Stufe 2 – Implementierung:** Nach Freigabe vollständig und kompilierbar umsetzen. `dotnet build` und `dotnet test` müssen grün sein.

Lies zuerst `CLAUDE.md`, `PROMPT.md` und (falls noch relevant) `CHANGES.md` für Architektur-Kontext.

## Autonomie-Konventionen
Identisch zur ersten `CHANGES.md`:
- Datei-Edits, Datei-Erstellung, NuGet-Pakete aus dieser Datei, dotnet/git-Lese-Befehle und Builds: ohne Nachfrage.
- Build-Fehler und neue Compiler-Warnungen: selbst beheben.
- Nachfragen bei: NuGet-Paketen außerhalb dieser Datei, Architektur-Entscheidungen jenseits des Dokumentierten, `git commit`/`git push`.
- Konflikt-Hierarchie: `CHANGES_v2.md` > `CHANGES.md` > `CLAUDE.md` > `PROMPT.md`.
- Am Ende: Zusammenfassung, **nicht committen**.

---

## Änderung 1 – Floating-Button: Verschieben verifizieren + Toggle ein/aus

### Hintergrund
Der schwebende Aufnahme-Button (`FloatingRecordWindow`) hat aktuell zwei UX-Defizite:

1. **Verschieben funktioniert beim Nutzer nicht zuverlässig**, obwohl der Code in `FloatingRecordWindow.xaml.cs` bereits `DragMove()` mit 4-Pixel-Schwelle und Mouse-Capture implementiert. Die Position wird in `FloatingRecordUiService.OnLocationChanged` persistiert. Symptom unklar – möglicherweise reagiert nichts auf Click+Drag, oder der Klick triggert versehentlich die Aufnahme statt zu draggen, oder die Position springt zurück.
2. **Kein Ausschalten möglich**: Der Tray-Menüeintrag „Schwebender Aufnahme-Button…" ruft `ShowOrActivate()` und macht das Window sichtbar. Es gibt keinen Weg, das Window wieder auszublenden – außer durch App-Beenden. Es soll als **Toggle** funktionieren.

### 1.1 Drag-Verhalten verifizieren und ggf. fixen

**Diagnose-Schritt zuerst:**
- Lies `FloatingRecordWindow.xaml` und `FloatingRecordWindow.xaml.cs` und prüfe konkret folgende Verdachts-Stellen:
  - Greift `MouseLeftButtonDown` tatsächlich auf das `RootBorder`-Element? Oder fängt ein darüberliegendes Control den Event ab (z.B. ein Button, der den ganzen Window-Bereich abdeckt)?
  - Wird `DragMove()` aufgerufen, **bevor** `MouseCapture` zugewiesen ist? Reihenfolge: erst `CaptureMouse()`, dann `DragMove()` (DragMove blockiert bis MouseUp und löst die Capture intern).
  - Wird `_suppressToggle` korrekt gesetzt? Aktuell wird es in `OnBorderMouseMove` gesetzt, sobald gedraggt wurde. Dann darf `OnBorderMouseLeftButtonUp` nicht doch noch `ToggleRecording` auslösen.
  - Ist die 4-Pixel-Schwelle vielleicht zu hoch oder zu niedrig? Bei Touch-Displays oder hoher DPI kann das problematisch sein.
  - Beim Setzen von `WS_EX_NOACTIVATE` in `OnSourceInitialized`: verhindert das, dass das Fenster Mouse-Events sauber empfängt? `WS_EX_NOACTIVATE` betrifft Aktivierung/Fokus, nicht Mouse-Events – sollte also okay sein.
- Aktiviere temporär Debug-Logging im Drag-Pfad: `MouseDown`, jeder `MouseMove` während Drag, `MouseUp` mit aktuellem `_dragging`/`_suppressToggle`-Status. Das Log darf nach erfolgreichem Fix wieder entfernt werden – aber **mindestens als `LogDebug`** drin lassen, damit Diagnose künftig möglich ist.

**Fix-Schritt:**
- Wenn die Diagnose ein konkretes Problem zeigt: gezielt fixen, kurze Begründung im Commit/Code-Kommentar.
- Wenn nichts Offensichtliches schief läuft: 
  - **`DragMove()` durch eigene Drag-Logik ersetzen**, weil `DragMove()` in WPF mit `WS_EX_NOACTIVATE` bekanntermaßen Edge-Cases hat. Eigene Implementierung: in `OnBorderMouseMove` direkt `Left += dx; Top += dy;` setzen, auf Basis der `Mouse.GetPosition`-Differenz seit MouseDown.
  - Mouse-Capture explizit auf das `RootBorder` setzen statt auf `(UIElement)sender`.
  - 3-Pixel-Schwelle (statt 4) testen.

**Akzeptanz für Drag:**
- Linke Maustaste auf den schwebenden Button drücken, Maus bewegen → Button folgt der Maus.
- Maustaste loslassen → Button bleibt an neuer Position.
- Bei Drag wird **keine Aufnahme** gestartet/gestoppt.
- Bei kurzem Klick ohne Bewegung wird die Aufnahme weiterhin getoggelt.
- Position überlebt App-Neustart (war schon implementiert, aber bitte verifizieren – `FloatingButtonLeft` und `FloatingButtonTop` in `AppSettings`).

### 1.2 Toggle ein/aus für den Floating-Button

#### UX-Änderungen
- Tray-Menü-Eintrag „Schwebender Aufnahme-Button…" → umbenennen zu „**Schwebender Aufnahme-Button**" (ohne Ellipsis, da kein Dialog mehr aufgeht).
- Eintrag wird **als `IsCheckable = true`** umgesetzt mit `IsChecked` synchron zur tatsächlichen Sichtbarkeit des Fensters.
- Klick auf den Eintrag toggelt: sichtbar → ausblenden; ausgeblendet → einblenden an der zuletzt gespeicherten Position.
- Default beim App-Start: Floating-Button **nicht** sichtbar (so wie aktuell). Sichtbarkeit wird nicht über App-Sessions hinweg persistiert (Position aber schon).
- Falls in einer späteren Iteration die Sichtbarkeit doch persistiert werden soll: Setting `FloatingButtonVisibleOnStartup` (bool, default `false`) vorbereiten – aber **nicht** im MVP umsetzen, nur als Kommentar im `AppSettings`-Code erwähnen.

#### Technische Umsetzung

**`IFloatingRecordUi`-Interface erweitern:**
```csharp
public interface IFloatingRecordUi : IDisposable
{
    event EventHandler? ToggleRecordingRequested;
    event EventHandler? VisibilityChanged;  // NEU

    void ShowOrActivate();
    void Hide();                             // NEU
    bool IsVisible { get; }                  // NEU
    void SetRecordingState(RecordingState state);
}
```

**`FloatingRecordUiService.cs`:**
- `Hide()`-Methode: prüft ob `_window != null && _window.IsVisible`, dann `_window.Hide()`. Wirft `VisibilityChanged` aus.
- `IsVisible`-Property: `_window?.IsVisible ?? false`.
- In `ShowOrActivate()`: nach `_window.Show()` ebenfalls `VisibilityChanged` werfen.
- `_window` darf **nicht** auf `null` gesetzt werden, wenn nur ausgeblendet wird – das Fenster wird wiederverwendet, damit Position und State erhalten bleiben.
- Window-`Closed`-Handler bleibt für den Fall, dass das Fenster explizit zerstört wird (z.B. App-Shutdown).
- **Important**: Da `WS_EX_NOACTIVATE` und kein Close-Button gesetzt sind, kann der Nutzer das Fenster nicht über die Standard-Wege schließen. Trotzdem `Hide()` und nicht `Close()` verwenden, damit der Window-State (Position, Größe) erhalten bleibt.

**`TrayService.cs`:**
- `floatingItem` als `MenuItem { IsCheckable = true, Header = "Schwebender Aufnahme-Button" }`.
- Click-Handler: nicht mehr direkt `ShowFloatingRecorderRequested` werfen, sondern ein neues Event `ToggleFloatingRecorderRequested`.
- Neue Methode `UpdateFloatingButtonVisibility(bool visible)`: setzt `_floatingItem.IsChecked = visible` (über Dispatcher).
- Bestehendes Event `ShowFloatingRecorderRequested` umbenennen → `ToggleFloatingRecorderRequested`.

**`App.xaml.cs`:**
- Neuer Handler `OnToggleFloatingRecorderRequested`:
  ```csharp
  if (_floatingRecordUi.IsVisible)
      _floatingRecordUi.Hide();
  else
      _floatingRecordUi.ShowOrActivate();
  ```
- Subscribe auf `_floatingRecordUi.VisibilityChanged` → ruft `_trayService.UpdateFloatingButtonVisibility(_floatingRecordUi.IsVisible)` auf.
- Nach dem ersten Setzen von `_floatingRecordUi` initial `_trayService.UpdateFloatingButtonVisibility(false)` aufrufen, damit das Häkchen korrekt startet.

#### Akzeptanz für Toggle:
- App starten → Tray-Menü öffnen → „Schwebender Aufnahme-Button" hat **kein Häkchen**, Button ist nicht sichtbar.
- Klick auf den Eintrag → Häkchen erscheint, Button wird sichtbar an gespeicherter (oder Default-) Position.
- Klick erneut → Häkchen weg, Button verschwindet.
- Position bleibt zwischen Aus-Ein-Wechseln erhalten.
- Toggle funktioniert auch während Aufnahme/Verarbeitung – aber: wenn der Button gerade in `Recording`- oder `Processing`-State ist und ausgeblendet wird, erscheint er beim nächsten Einblenden im richtigen State (über `SetRecordingState`-Aufruf in `ShowOrActivate`).

---

---

## Änderung 2 – Installer + Auto-Update per Velopack

### Ziel
Eine professionelle Setup-EXE plus **eingebauter Auto-Update-Mechanismus**: Schnack prüft beim Start im Hintergrund auf neue Versionen, lädt nur das Delta (~ein paar MB statt 80 MB) und installiert es nach Bestätigung des Nutzers mit anschließendem App-Restart. Saubere Deinstallation. Kein Admin-Recht (UAC) nötig. Updates werden über **GitHub Releases** verteilt (kostenlos).

### Architektur-Entscheidungen (fix)

| Aspekt | Entscheidung | Begründung |
|--------|-------------|-----------|
| Tool | **Velopack** (NuGet `Velopack` + `vpk` CLI) | Aktiv gepflegter Squirrel-Nachfolger; Installer + Updates in einem Tool |
| Publish-Variante | **Self-contained, win-x64** | Empfänger braucht kein .NET-Runtime |
| Install-Scope | **Per-User** (von Velopack default in `%LocalAppData%\Schnack`) | Kein UAC, kein Admin |
| Update-Hosting | **GitHub Releases** | Kostenlos, vom `vpk upload`-CLI direkt unterstützt |
| Repo-Sichtbarkeit | **Public empfohlen, private möglich** | Public = anonymer Update-Check; private = GitHub-Token nötig (siehe 2.3) |
| Code-Signing | **Nein** im MVP | SmartScreen-Warnung beim Erststart akzeptabel; Velopack später leicht nachrüstbar |
| Update-Channel | **`win`** (Velopack-Default) | Kein Multi-Channel-Bedarf |
| Update-Check-Frequenz | **Beim App-Start** im Hintergrund + manueller Tray-Eintrag | Pragmatisch, keine ständigen Polls |
| Update-UX | Tray-Notification „Update verfügbar – jetzt installieren" | Nicht-aufdringlich; Nutzer entscheidet |
| `%APPDATA%\Schnack\` (Settings, Logs, Secrets) | Bleibt bei Update **und** Deinstallation erhalten | Nutzer-Daten nicht verlieren |

### Voraussetzungen, die der Nutzer (Hauke) einmalig erfüllen muss
Diese Schritte führt **Hauke selbst** vor der Implementierung aus, **nicht Claude Code**:

1. **GitHub-Account** (falls noch nicht vorhanden).
2. **GitHub-Repo `Schnack` anlegen** (private oder public). Vorschlag: `https://github.com/<username>/Schnack`.
3. **Lokales Git-Remote setzen**:
   ```pwsh
   cd C:\Projekte\Schnack
   git remote add origin https://github.com/<username>/Schnack.git
   git push -u origin main
   ```
4. **`vpk` CLI installieren** (einmalig, global):
   ```pwsh
   dotnet tool install -g vpk
   ```
   (Verifizieren mit `vpk --help`. Falls vpk-Version anders ist als das Velopack-NuGet-Paket: beide auf gleichen Stand bringen.)
5. **GitHub Personal Access Token** erzeugen (für `vpk upload github`):
   - GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic) → Generate new
   - Scope: `repo` (für private Repos) oder `public_repo` (für public)
   - Token kopieren und als Umgebungsvariable setzen:
     ```pwsh
     setx VPK_GITHUB_TOKEN "ghp_..."
     ```
   - PowerShell schließen und neu öffnen, damit die Variable greift.

Claude Code soll diese fünf Punkte in der README dokumentieren und beim Plan explizit darauf hinweisen, dass sie vor dem ersten Release-Build erledigt sein müssen.

### 2.1 Velopack-Integration in den App-Code

#### NuGet-Paket
- `Velopack` (aktuelle Version, in `Schnack.csproj`).

#### `App.xaml` umbauen (Velopack-WPF-Anforderung)
WPF generiert standardmäßig eine `Main`-Methode aus `App.xaml`. Velopack muss aber **vor** dem WPF-Bootstrap laufen, damit Update-Apply-Aufrufe nicht den ganzen UI-Stack hochfahren. Daher:

1. In `Schnack.csproj`:
   ```xml
   <ItemGroup>
     <ApplicationDefinition Remove="App.xaml"/>
     <Page Include="App.xaml"/>
   </ItemGroup>
   <PropertyGroup>
     <StartupObject>Schnack.App</StartupObject>
   </PropertyGroup>
   ```
2. In `App.xaml.cs` neue `Main`-Methode am Anfang der Klasse:
   ```csharp
   [STAThread]
   private static void Main(string[] args)
   {
       // MUSS als erstes laufen — Velopack handled hier ggf. Update-Hooks
       // (--veloapp-install, --veloapp-updated, --veloapp-obsolete, etc.)
       VelopackApp.Build()
           .OnFirstRun(v =>
           {
               // Optional: First-Run-Logik (z.B. Hinweis auf API-Key-Setup).
               // Für Schnack reicht der bestehende FirstRunWindow-Mechanismus.
           })
           .Run();

       var app = new App();
       app.InitializeComponent();
       app.Run();
   }
   ```
3. Bestehender `OnStartup`-Code bleibt unverändert.

#### Neuer Service `IUpdateService` (in `Services/`)
```csharp
public interface IUpdateService
{
    /// <summary>Beim App-Start: Update-Check im Hintergrund. Wirft kein Exception bei Netzfehler.</summary>
    Task CheckOnStartupAsync(CancellationToken ct = default);

    /// <summary>Manueller Trigger aus dem Tray-Menü. Zeigt selbst Tray-Notifications.</summary>
    Task CheckAndPromptAsync(CancellationToken ct = default);

    event EventHandler<UpdateAvailableEventArgs>? UpdateAvailable;
}
```
- Implementierung `VelopackUpdateService` mit `UpdateManager(new GithubSource(repoUrl, accessToken: null, prerelease: false))`.
- `repoUrl` aus einer Konstante oder einem Setting (`AppSettings.UpdateRepoUrl`, default `"https://github.com/<username>/Schnack"` — als Platzhalter eintragen, finalen URL setzt Hauke selbst).
- Bei Update verfügbar: `_trayService.ShowBalloonTip("Update verfügbar", "Schnack v{newVersion} ist verfügbar. Klick im Tray-Menü auf 'Jetzt installieren'.")`. Plus internes Event, damit das Tray-Menü einen neuen Eintrag „Jetzt installieren" zeigt.
- Bei Klick auf „Jetzt installieren": `await mgr.DownloadUpdatesAsync(updateInfo); mgr.ApplyUpdatesAndRestart(updateInfo);` — die App beendet sich, Velopack startet sie nach Update neu.
- DI-Registrierung in `App.xaml.cs.BuildServiceProvider()`: `services.AddSingleton<IUpdateService, VelopackUpdateService>();`.

#### Tray-Menü erweitern
- Neuer Eintrag „**Auf Updates prüfen**" im Tray-Menü (oberhalb von „Beenden", unter „Über Schnack"). Klick → `_updateService.CheckAndPromptAsync()`.
- Wenn ein Update verfügbar ist: dynamisch ein zweiter Eintrag „**Update v1.x.x installieren**" eingeblendet. Klick → Download + Apply + Restart.
- Beide Einträge brauchen Status-Updates über den `Dispatcher` (siehe bestehende `UpdateState`-Pattern in `TrayService`).

#### `App.xaml.cs.OnStartup`
- Nach erfolgreichem Bootstrap: `_ = _updateService.CheckOnStartupAsync();` (fire-and-forget, **nicht** awaiten — App-Start darf nicht blockieren). 
- Bei aktiver Aufnahme oder laufender Pipeline: Update-Notification erscheint trotzdem, Installation bleibt aber Nutzer-Trigger (kein automatisches Apply mitten in einer Aufnahme).

#### Single-Instance-Mutex und Velopack
- Velopack startet während Update einen separaten Updater-Prozess, der die alte App killt und nach Apply neu startet.
- **Risiko**: Mutex `Schnack.Singleton.{User}` blockiert den Restart, wenn die alte Instanz nicht sauber freigegeben hat.
- Lösung: In `OnExit`/`CleanupAndShutdown` sicherstellen, dass `_mutex.Dispose()` läuft (ist bereits drin, mit Mutex-Try-Catch-Fix aus erster `CHANGES.md` 5.4 robust).
- Zusätzlich: kurze Verzögerung im Velopack-Restart abwarten — das macht Velopack intern (`ApplyUpdatesAndRestart` kennt das Problem). **Verifizieren** beim Test.

### 2.2 Build- und Release-Pipeline

#### Datei `build-release.ps1` im Repo-Root
PowerShell-Script, das den kompletten Release-Vorgang automatisiert. Aufruf:
```pwsh
.\build-release.ps1                       # nimmt Version aus csproj
.\build-release.ps1 -Version 1.4.0        # Version explizit überschreiben
.\build-release.ps1 -SkipUpload           # nur lokal packen, nicht zu GitHub pushen
```

Verhalten:
1. **Vorbedingungen prüfen**:
   - `vpk` installiert? (Aufruf `vpk --help`, bei Fehler: Anleitung ausgeben und mit `exit 1` beenden.)
   - `VPK_GITHUB_TOKEN` gesetzt? (Bei `-SkipUpload` egal, sonst Pflicht.)
   - Git-Repo sauber? (`git status --porcelain` muss leer sein, sonst Warnung — Nutzer kann mit `-Force` überschreiben.)
2. **Version bestimmen**:
   - Wenn `-Version` übergeben: nehmen.
   - Sonst: aus `Schnack/Schnack.csproj` `<Version>` parsen.
3. **Build & Publish**:
   ```pwsh
   dotnet publish Schnack -c Release -r win-x64 --self-contained true `
     /p:PublishSingleFile=false `
     -o publish/win-x64
   ```
   (Velopack rät von SingleFile ab — die einzelnen Dateien werden für Delta-Updates gebraucht.)
4. **Velopack pack**:
   ```pwsh
   vpk pack `
     --packId Schnack `
     --packVersion $version `
     --packDir publish/win-x64 `
     --mainExe Schnack.exe `
     --packTitle "Schnack" `
     --packAuthors "Hauke Lamb" `
     --icon Schnack/Resources/Schnack_favicon.ico `
     --outputDir releases
   ```
5. **Verifikation**: `releases/Schnack-Setup.exe`, `releases/Schnack-<version>-full.nupkg` und (ab 2. Release) `releases/Schnack-<version>-delta.nupkg` müssen existieren.
6. **Upload zu GitHub** (übersprungen bei `-SkipUpload`):
   ```pwsh
   vpk upload github `
     --repoUrl https://github.com/<username>/Schnack `
     --publish `
     --releaseName "v$version" `
     --tag "v$version" `
     --token $env:VPK_GITHUB_TOKEN
   ```
7. **Konsolen-Output**: finale URLs (Release-Page auf GitHub) und lokaler Pfad zur Setup-EXE.

Bei Fehler in irgendeinem Schritt: klare Fehlermeldung, `exit 1`. Idempotent neu ausführbar.

### 2.3 Update-Quelle: GitHub-Repo

- **Public-Repo**: Update-Check funktioniert anonym, kein Token in der Schnack-App nötig. Empfohlen.
- **Private-Repo**: Schnack-App müsste einen GitHub-Token kennen. Speicherung analog API-Keys per DPAPI. Im MVP **nicht implementieren** — wenn Hauke private will, separates Setting in einer späteren Iteration.
- Empfehlung in der README: **Public-Repo** für Schnack, weil keine Geschäftsgeheimnisse drin sind. Im Repo selbst stehen weiterhin keine API-Keys (siehe `.gitignore`-Regeln).

### 2.4 Update-UX im Detail

- **Beim App-Start (Hintergrund)**: 
  - Wenn Update verfügbar: dezente Tray-Notification **einmal** anzeigen („Update auf v1.4.0 verfügbar – im Tray-Menü installieren").
  - Tray-Menü-Eintrag „**Update v1.4.0 installieren**" wird sichtbar (oberhalb „Auf Updates prüfen").
  - Notification verschwindet nach 5–10 s automatisch (System-Default), Menü-Eintrag bleibt persistent bis Update installiert oder App neu gestartet wurde.
- **Manuell „Auf Updates prüfen"** im Menü:
  - Kein Update verfügbar: Tray-Notification „Schnack ist auf dem neuesten Stand (v1.3.0)".
  - Update verfügbar: identisches Verhalten wie automatischer Check.
  - Netzwerkfehler: Tray-Notification „Update-Check fehlgeschlagen – keine Verbindung zu GitHub".
- **„Update v1.4.0 installieren"** Klick:
  - Tray-Notification „Update wird heruntergeladen…".
  - Bei Erfolg: `ApplyUpdatesAndRestart()` — App beendet sich, Velopack führt Apply durch, neue App-Version startet automatisch.
  - Bei Fehler: Tray-Notification „Update-Installation fehlgeschlagen — Details siehe Log".
- **Während Aufnahme/Verarbeitung**: Update-Klick bleibt erlaubt, aber mit MessageBox-Hinweis „Schnack startet jetzt neu — laufende Aufnahme geht verloren. Fortfahren?". Bei „Nein" Abbruch.

### 2.5 Repo-Struktur

- `build-release.ps1` im Repo-Root.
- `releases/` (in `.gitignore`) — lokaler Output-Ordner.
- `publish/` (in `.gitignore`) — `dotnet publish`-Output.
- Neuer kleiner README-Anhang `RELEASE.md` im Repo-Root mit:
  - Voraussetzungs-Checkliste (vpk installiert, Token gesetzt, Repo angelegt).
  - Release-Workflow Schritt für Schritt.
  - Troubleshooting-Hinweise (häufige Fehler von vpk, etc.).

### 2.6 `.gitignore` ergänzen
```
# Velopack
publish/
releases/
*.nupkg
```

### 2.7 README.md ergänzen

Zwei neue Abschnitte einfügen, **zwischen** „Build und Start" und „Bedienung":

#### „Updates"
- Erklärung für **Empfänger**: Schnack prüft beim Start automatisch auf Updates. Bei verfügbarem Update erscheint eine Tray-Notification und ein Menüeintrag. Klick installiert das Update (~5–20 MB Delta) und startet die App neu.
- Manueller Check über Tray-Menü → „Auf Updates prüfen".
- Privacy: beim Update-Check wird ein anonymer HTTP-Request an `github.com/<username>/Schnack` gestellt. Keine Telemetrie, keine Nutzungs-Daten.

#### „Release bauen (für Maintainer)"
- Voraussetzungen-Checkliste (siehe 2.0 oben).
- Befehl: `.\build-release.ps1` ausführen.
- Output: GitHub-Release mit Setup-EXE und Update-Paketen.
- Erst-Verteilung: GitHub-Release-URL teilen, Empfänger laden Setup-EXE und installieren einmal manuell — alle weiteren Updates kommen automatisch über die App.

### 2.8 Versionsnummer-Synchronisation

- **Single source of truth**: `Schnack.csproj` `<Version>` (wie schon vorher).
- `build-release.ps1` parsed das Element per simpler Regex.
- Velopack tagged GitHub-Release mit `v<version>`.
- Beim nächsten Release **vorher** `<Version>` und `<AssemblyMetadata Include="ReleaseDate" Value="..."/>` in csproj manuell hochsetzen, dann `build-release.ps1`.

### 2.9 Manuelle Verifikation nach Implementierung

Diese Schritte führt **Hauke** aus, **nicht Claude Code**:

1. Voraussetzungen erfüllen (Repo, vpk, Token — siehe 2.0).
2. Erste Version 1.3.0 builden: `.\build-release.ps1`.
3. GitHub-Release prüfen: `Schnack-Setup.exe` und `.nupkg`-Dateien angehängt.
4. `Schnack-Setup.exe` von einem **anderen** (oder bereinigtem) Windows-11-Rechner runterladen, ausführen → Installation läuft per-user durch.
5. Schnack starten, Tray-Icon erscheint, Workflow funktioniert.
6. Version in csproj auf 1.3.1 hochsetzen, kleine sichtbare Änderung im Code (z.B. Tray-Tooltip-Text), `.\build-release.ps1` erneut.
7. Auf dem Test-Rechner Schnack neu starten → Tray-Notification „Update v1.3.1 verfügbar" erscheint binnen ~10 s.
8. „Update installieren" klicken → App beendet sich, kommt nach ~5 s neu hoch mit der Änderung sichtbar.
9. Über Windows-Apps & Features deinstallieren → `%LocalAppData%\Schnack\` weg, `%APPDATA%\Schnack\` (Settings, Logs) bleibt.

Wenn Schritt 7 oder 8 nicht funktioniert: Logs unter `%LocalAppData%\Schnack\` (Velopack-Updater-Log) und `%APPDATA%\Schnack\logs\` (App-Log) prüfen.

---

## Akzeptanzkriterien

1. `dotnet build` und `dotnet test` grün, keine neuen Warnungen.
2. **Floating-Button verschieben**: linke Maustaste halten + ziehen funktioniert zuverlässig. Position überlebt App-Neustart.
3. **Floating-Button toggle**: Tray-Menü-Eintrag mit Häkchen, Klick blendet ein/aus, Position und State bleiben erhalten.
4. **Floating-Button Klick ohne Drag** startet/stoppt Aufnahme wie bisher.
5. **Velopack-Integration im Code**: `VelopackApp.Build().Run()` als allererster Aufruf in `Main`. App startet weiterhin normal ohne Velopack-Argumente.
6. **Build-Script `build-release.ps1`**: läuft sauber durch, prüft Voraussetzungen, baut, packt, lädt zu GitHub hoch (es sei denn `-SkipUpload`).
7. **Setup-EXE**:
   - Größe im Bereich 70–120 MB (self-contained).
   - Per-User-Installation ohne UAC.
   - Start-Menü-Eintrag erstellt.
   - Saubere Deinstallation, **ohne** `%APPDATA%\Schnack\` zu löschen.
8. **Update-Check beim App-Start** läuft im Hintergrund und blockt den Start nicht.
9. **Update-Verfügbar**-UX: dezente Tray-Notification + Menüeintrag „Update v.x.y installieren".
10. **Update-Apply**: Klick auf Menüeintrag installiert, App startet neu, neue Version aktiv. Singleton-Mutex blockiert den Restart nicht.
11. **Manueller Update-Check** über Tray-Menü mit klaren Status-Notifications für „aktuell", „verfügbar", „Netzfehler".
12. **README** dokumentiert: Update-Mechanismus aus Empfänger-Sicht, Release-Workflow aus Maintainer-Sicht, GitHub-Token-Setup.
13. **Versionsnummer** kommt aus `Schnack.csproj` und landet automatisch in Setup-EXE und GitHub-Release-Tag.

## Out of Scope (nicht umsetzen)

- Code-Signing (kommerzielles Cert oder self-signed). Spätere Iteration; Velopack unterstützt es out-of-the-box, wenn ein Cert vorhanden ist.
- Private-Repo-Support für Update-Check (würde DPAPI-gespeicherten GitHub-Token in der Schnack-App erfordern).
- Multi-Channel (z.B. „beta" / „stable"). Aktuell nur `win`-Channel.
- Auto-Apply ohne Nutzer-Bestätigung. Update bleibt opt-in pro Release.
- Background-Polling während die App läuft (alle X Minuten Update-Check). Aktuell nur beim Start + manuell.
- Migration alter Settings-Versionen beim Update-Lauf. Macht die App selbst beim Start (siehe Schema-Migration aus erster `CHANGES.md`).
- Floating-Button-Sichtbarkeit über App-Sessions persistieren (Setting `FloatingButtonVisibleOnStartup` nur als Kommentar vorbereiten).

---

**Beginne jetzt mit Stufe 1 (Plan). Code erst nach Freigabe.**
