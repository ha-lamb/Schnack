# Implementierungs-Prompt: Velopack Auto-Update für Schnack

> Eigenständige Aufgabenbeschreibung. Übernimmt und konsolidiert die Velopack-Spezifikation aus `CHANGES_v2.md`. Bei Konflikten gilt **diese Datei** als maßgeblich. `CLAUDE.md` bleibt für Architektur und Konventionen verbindlich.

## Arbeitsweise (verbindlich)

1. **Stufe 1 – Plan:** Detaillierter Plan: betroffene Dateien, Reihenfolge der Änderungen, neue Dateien, Test-Anpassungen, manuelle Verifikationsschritte. **Noch keinen Code.** Warte auf Freigabe.
2. **Stufe 2 – Implementierung:** Nach Freigabe vollständig und kompilierbar. `dotnet build` und `dotnet test` müssen am Ende grün sein, ohne neue Warnungen.

Lies zuerst `CLAUDE.md` für Architektur- und Konventions-Kontext (insbesondere Logging-Verbote, Threading-Regeln, Mutex-Behandlung, Codestil).

## Autonomie-Konventionen

`.claude/settings.local.json` definiert die Allowlist. Permission-Mode in VS Code: **Accept Edits**.

**Selbstständig ohne Nachfrage:**
- Datei-Edits, Datei-Erstellung, Datei-Löschung im Workspace.
- `dotnet build`, `dotnet test`, `dotnet run`, `dotnet restore` nach jedem logischen Teilschritt.
- `dotnet add package Velopack` (in dieser Datei explizit erlaubt).
- Build-Fehler und neue Compiler-Warnungen selbst beheben.
- Refactorings innerhalb der vorgegebenen Architektur.

**Mit Rückfrage:**
- NuGet-Pakete außerhalb von `Velopack`.
- Architektur-Entscheidungen, die hier nicht beantwortet sind.
- `git commit` und `git push` (nicht ausführen, ich committe selbst).
- Manuelle Verifikationsschritte — diese kann nur ich (Hauke) ausführen, nicht Claude Code.

**Am Ende der Session:**
- Zusammenfassung der gemachten Änderungen.
- Build und Tests grün.
- **Nicht committen.**
- Voraussetzungen, die ich (Hauke) noch erfüllen muss, klar auflisten (siehe Abschnitt „Voraussetzungen, die der Maintainer erfüllen muss").

---

## Ziel

Eine professionelle Setup-EXE plus **eingebauter Auto-Update-Mechanismus**: Schnack prüft beim App-Start im Hintergrund auf neue Versionen, lädt nur das Delta (~5–20 MB statt der vollen ~80 MB), zeigt eine Tray-Notification und installiert das Update nach manueller Bestätigung mit anschließendem App-Restart. Saubere Deinstallation. Kein UAC-Prompt. Kein Admin-Recht. Updates werden über **GitHub Releases** verteilt (kostenlos).

## Architektur-Entscheidungen (fix)

| Aspekt | Entscheidung |
|--------|-------------|
| Tool | Velopack (NuGet `Velopack` + `vpk` CLI) |
| Publish-Variante | Self-contained, win-x64, **kein** SingleFile (Velopack braucht einzelne Dateien für Delta-Updates) |
| Install-Scope | Per-User (`%LocalAppData%\Schnack`) — Velopack-Default |
| Update-Hosting | GitHub Releases (public Repo empfohlen) |
| Code-Signing | Nein (im MVP). SmartScreen-Warnung beim Erststart wird vom Empfänger durch „Trotzdem ausführen" akzeptiert. |
| Update-Channel | `win` (Velopack-Default) |
| Update-Check-Frequenz | Beim App-Start im Hintergrund + manueller Tray-Eintrag |
| Update-UX | Tray-Notification + dynamischer Tray-Menüeintrag „Update v.x.y installieren" |
| `%APPDATA%\Schnack\` | Bleibt bei Update **und** Deinstallation erhalten |

## Voraussetzungen, die der Maintainer erfüllen muss

Diese Schritte führt **Hauke selbst** vor dem ersten Release-Build aus, **nicht Claude Code**. Claude Code soll sie in einer neuen `RELEASE.md` dokumentieren.

1. **GitHub-Account** vorhanden.
2. **GitHub-Repo `Schnack` anlegen** (public empfohlen, MIT-Lizenz).
3. **Lokales Git-Remote setzen und initial pushen:**
   ```pwsh
   cd C:\Projekte\Schnack
   git remote add origin https://github.com/ha-lamb/Schnack.git
   git push -u origin main
   ```
4. **`vpk` CLI global installieren:**
   ```pwsh
   dotnet tool install -g vpk
   ```
   Verifikation: `vpk --help`.
5. **GitHub Personal Access Token (Classic)** generieren:
   - GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic) → Generate new (classic).
   - Scope: `public_repo` (oder `repo` für private Repos).
   - Token kopieren und als Umgebungsvariable setzen:
     ```pwsh
     setx VPK_GITHUB_TOKEN "ghp_..."
     ```
   - Terminal/VS Code danach neu starten.

Claude Code soll diese Liste in `RELEASE.md` aufnehmen und beim Plan in Stufe 1 explizit darauf hinweisen, dass diese Schritte vor dem ersten Release-Build erledigt sein müssen.

---

## 1. App-Code-Integration

### 1.1 NuGet-Paket
- `Velopack` zu `Schnack/Schnack.csproj` hinzufügen (aktuelle stabile Version aus NuGet).

### 1.2 `App.xaml` und `App.xaml.cs` umbauen

Velopack muss **vor** dem WPF-Bootstrap laufen können, damit Update-Hooks (`--veloapp-install`, `--veloapp-updated`, `--veloapp-obsolete`, etc.) ohne UI-Stack verarbeitet werden können. WPF generiert standardmäßig eine `Main`-Methode aus `App.xaml`, die müssen wir abschalten.

**`Schnack.csproj`:**
```xml
<ItemGroup>
  <ApplicationDefinition Remove="App.xaml" />
  <Page Include="App.xaml" />
</ItemGroup>
<PropertyGroup>
  <StartupObject>Schnack.App</StartupObject>
</PropertyGroup>
```

**`App.xaml.cs` neue `Main`-Methode** (vor allem anderen Code in der Klasse):
```csharp
[STAThread]
public static void Main(string[] args)
{
    // MUSS als allererstes laufen — Velopack handled hier ggf. Update-Hooks
    // und beendet den Prozess sauber, bevor das WPF-Hochfahren beginnt.
    VelopackApp.Build()
        .OnFirstRun(v =>
        {
            // Optional: First-Run-Hook. Schnack hat dafür schon FirstRunWindow,
            // also hier nur ein no-op oder ein Logger-Ping.
        })
        .Run();

    var app = new App();
    app.InitializeComponent();
    app.Run();
}
```

Bestehender `OnStartup`-Code bleibt unverändert.

### 1.3 Neuer Service `IUpdateService`

**Interface (`Schnack/Services/IUpdateService.cs`):**
```csharp
public interface IUpdateService
{
    /// <summary>Update-Check beim App-Start im Hintergrund. Wirft KEINE Exception bei Netzfehler.</summary>
    Task CheckOnStartupAsync(CancellationToken ct = default);

    /// <summary>Manueller Trigger aus dem Tray-Menü. Zeigt selbst Status-Notifications.</summary>
    Task CheckAndPromptAsync(CancellationToken ct = default);

    /// <summary>Lädt und installiert das zuletzt erkannte Update. App startet danach neu.</summary>
    Task ApplyKnownUpdateAsync(CancellationToken ct = default);

    event EventHandler<UpdateAvailableEventArgs>? UpdateAvailable;

    /// <summary>true, wenn ein Update bekannt ist, das bereit zur Installation ist.</summary>
    bool HasPendingUpdate { get; }

    /// <summary>Versionsstring des bekannten Updates, wenn verfügbar (sonst null).</summary>
    string? PendingUpdateVersion { get; }
}

public sealed class UpdateAvailableEventArgs : EventArgs
{
    public required string NewVersion { get; init; }
}
```

**Implementierung `Schnack/Services/VelopackUpdateService.cs`:**
- Sealed class.
- Konstruktor erhält `ILogger<VelopackUpdateService>`, `ITrayService` (für Notifications), und konfiguriertes `IOptions<UpdateOptions>` oder direkt eine `string repoUrl`-Konstante.
- Velopack `UpdateManager` mit `GithubSource(repoUrl, accessToken: null, prerelease: false)` initialisieren.
- `CheckOnStartupAsync`:
  - Try-Catch um den ganzen Block. Bei Exception: nur loggen (Exception-Typ + HTTP-Status falls verfügbar), keine Notification, App-Start läuft unbeeinträchtigt weiter.
  - `var updateInfo = await mgr.CheckForUpdatesAsync(ct);`
  - Wenn `updateInfo != null`: `_pendingUpdate = updateInfo`, Event `UpdateAvailable` werfen, Tray-Notification anzeigen („Update auf v{X} verfügbar – im Tray-Menü installieren").
- `CheckAndPromptAsync`: wie `CheckOnStartupAsync`, aber zeigt auch Notifications für „aktuell" und „Netzwerkfehler".
- `ApplyKnownUpdateAsync`:
  - Wenn kein pending Update: no-op mit Warn-Log.
  - Sonst: Tray-Notification „Update wird heruntergeladen…", `await mgr.DownloadUpdatesAsync(_pendingUpdate, progress: null, ct);`, `mgr.ApplyUpdatesAndRestart(_pendingUpdate);` — die App beendet sich selbst, Velopack handled den Restart.
- `HasPendingUpdate` und `PendingUpdateVersion` als computed properties aus `_pendingUpdate`.

**Repo-URL als Konstante** in `App.xaml.cs` oder einer `UpdateOptions`-Klasse (default `"https://github.com/<username>/Schnack"` als Platzhalter) — Maintainer setzt finalen Wert vor erstem Release. **Nicht** in `AppSettings` als nutzer-änderbares Setting (nicht relevant für Endnutzer).

**DI-Registrierung in `App.xaml.cs.OnStartup`:**
```csharp
services.AddSingleton<IUpdateService, VelopackUpdateService>();
```

### 1.4 Logging-Disziplin

Konsequent gemäß CLAUDE.md:
- ✅ Loggen: Exception-Typ, HTTP-Statuscode, „Update v1.x.y verfügbar", „Apply gestartet", „Apply abgeschlossen".
- ❌ Niemals loggen: Vollständige Exception-Messages aus dem Velopack-Stack (können URLs mit Tokens enthalten), Token, Repo-Inhalte.

### 1.5 `App.xaml.cs.OnStartup` integrieren

Nach erfolgreichem DI-Bootstrap (am Ende von `OnStartup`):
```csharp
_ = _updateService.CheckOnStartupAsync();
```
**Fire-and-forget.** Nicht awaiten — App-Start darf nicht durch einen Update-Check verzögert werden.

### 1.6 Single-Instance-Mutex und Velopack-Restart

Velopack startet beim Apply einen separaten Updater-Prozess, der die alte Schnack-Instanz killt und nach Apply die neue startet. **Risiko**: der Mutex `Schnack.Singleton.{Environment.UserName}` blockiert den neu gestarteten Prozess, wenn die alte Instanz nicht sauber freigegeben hat.

Vorgehen:
1. `_mutex.ReleaseMutex()` und `_mutex.Dispose()` müssen in `OnExit` / `CleanupAndShutdown` zuverlässig laufen, beide in try-catch (Mutex-Exception bei Cross-Thread-Release ignorieren).
2. Vor `mgr.ApplyUpdatesAndRestart` zusätzlich explizit `_mutex.Dispose()` rufen, damit der Updater den Singleton sicher übernehmen kann.
3. Beim ersten Tests des Update-Vorgangs verifizieren, dass die neu gestartete Instanz nicht in „App läuft schon"-Notification rennt.

---

## 2. Tray-Menü-Erweiterung

### 2.1 Neuer Menüeintrag „Auf Updates prüfen"

Position: zwischen „Über Schnack" und „Beenden".

Click-Handler ruft `_updateService.CheckAndPromptAsync()`.

### 2.2 Dynamischer Menüeintrag „Update v1.x.y installieren"

Wenn `IUpdateService.HasPendingUpdate == true`: zusätzlicher Eintrag oberhalb von „Auf Updates prüfen" mit Text `Update v{PendingUpdateVersion} installieren`. Klick ruft `_updateService.ApplyKnownUpdateAsync()`.

Eintrag verschwindet wieder, wenn das Update appliziert wurde (was den Prozess sowieso beendet) oder bei nächstem App-Start, falls schon aktuell.

Synchronisierung über das `UpdateAvailable`-Event: `TrayService` lauscht und ruft `Dispatcher.Invoke` zur Menüaktualisierung.

### 2.3 Status-Notifications

| Event | Notification |
|-------|-------------|
| Update beim Start gefunden | „Update auf v{X} verfügbar – im Tray-Menü installieren." (5–10 s) |
| Manueller Check, kein Update | „Schnack ist auf dem neuesten Stand (v{aktuell})." |
| Manueller Check, Update gefunden | identisch zum Start-Fall |
| Manueller Check, Netzfehler | „Update-Check fehlgeschlagen – keine Verbindung zu GitHub." |
| Apply-Klick startet | „Update wird heruntergeladen…" |
| Apply-Klick fehlgeschlagen | „Update-Installation fehlgeschlagen – Details siehe Log." |

### 2.4 Apply während laufender Aufnahme

Wenn `RecordingState != Idle`: vor dem Apply eine `MessageBox` zeigen:
> „Schnack startet jetzt neu — laufende Aufnahme oder Verarbeitung geht verloren. Fortfahren?"

Bei „Nein" Apply abbrechen.

---

## 3. Build-Pipeline

### 3.1 `build-release.ps1` im Repo-Root

PowerShell-Skript, das den kompletten Release-Vorgang automatisiert.

**Aufrufe:**
```pwsh
.\build-release.ps1                    # nimmt Version aus csproj
.\build-release.ps1 -Version 1.4.0     # explizite Version
.\build-release.ps1 -SkipUpload        # nur lokal packen, nicht zu GitHub pushen
```

**Ablauf:**
1. **Vorbedingungen prüfen:**
   - `vpk --help` muss laufen — sonst Hinweis: „Inno Setup… ähm, vpk ist nicht installiert. Anleitung: `dotnet tool install -g vpk`. Danach Skript erneut ausführen." → `exit 1`.
   - Bei nicht gesetztem `-SkipUpload`: `$env:VPK_GITHUB_TOKEN` muss gesetzt sein, sonst Hinweis und `exit 1`.
   - Git-Status sauber: `git status --porcelain` muss leer sein, sonst Warnung mit `-Force`-Override-Möglichkeit.
2. **Version bestimmen:**
   - Wenn `-Version` übergeben: nehmen.
   - Sonst: aus `Schnack/Schnack.csproj` `<Version>...</Version>` per Regex parsen.
3. **Publish:**
   ```pwsh
   dotnet publish Schnack -c Release -r win-x64 --self-contained true `
       /p:PublishSingleFile=false `
       -o publish/win-x64
   ```
4. **Velopack pack:**
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
5. **Verifikation:** `releases/Schnack-Setup.exe` und `releases/Schnack-<version>-full.nupkg` müssen existieren. Ab dem 2. Release auch `Schnack-<version>-delta.nupkg`.
6. **Upload zu GitHub** (übersprungen bei `-SkipUpload`):
   ```pwsh
   vpk upload github `
       --repoUrl https://github.com/<username>/Schnack `
       --publish `
       --releaseName "v$version" `
       --tag "v$version" `
       --token $env:VPK_GITHUB_TOKEN
   ```
7. **Konsole:** finale Release-URL und lokaler Pfad zur Setup-EXE ausgeben.

Bei jedem Fehler: klare Meldung, `exit 1`. Skript ist idempotent neu ausführbar.

**Hinweis für Claude Code:** Den exakten Icon-Dateinamen aus dem `Resources/`-Ordner des aktuellen Codes übernehmen (statt zu raten). Falls dort kein passendes ICO liegt: Hinweis im Plan, dass eine `.ico`-Datei für den Installer erzeugt werden muss.

---

## 4. Repo-Hygiene

### 4.1 `.gitignore` ergänzen

```
# Velopack
publish/
releases/
*.nupkg
```

### 4.2 README.md erweitern

Zwei neue Abschnitte zwischen „Build und Start" und „Bedienung":

#### „Updates" (für Empfänger)

- Schnack prüft beim Start automatisch auf Updates.
- Bei verfügbarem Update: Tray-Notification + Menüeintrag „Update v.x.y installieren".
- Klick installiert das Update (~5–20 MB Delta) und startet die App neu.
- Manueller Check über Tray-Menü → „Auf Updates prüfen".
- Privacy: anonymer HTTPS-Request an `github.com/<username>/Schnack` beim Update-Check. Keine Telemetrie.

#### „Release bauen (für Maintainer)"

Verweis auf `RELEASE.md` für die vollständige Anleitung.

### 4.3 Neue `RELEASE.md` im Repo-Root

Vollständige Maintainer-Anleitung:
- Voraussetzungen-Checkliste (siehe Abschnitt „Voraussetzungen, die der Maintainer erfüllen muss" oben).
- Schritt-für-Schritt Release-Workflow:
  1. `<Version>` und `ReleaseDate` in `Schnack.csproj` hochsetzen.
  2. `dotnet build && dotnet test` muss grün sein.
  3. `git commit` der Versionsänderung.
  4. `.\build-release.ps1` ausführen.
  5. GitHub-Release-Page checken, ggf. Release-Notes ergänzen.
  6. Erst-Verteilung: GitHub-Release-URL teilen, Empfänger laden Setup-EXE herunter und installieren einmalig.
- Troubleshooting:
  - „vpk: command not found" → `dotnet tool install -g vpk`, neues Terminal.
  - Token-Fehler → Token regenerieren, neuen Wert in `setx VPK_GITHUB_TOKEN`, Terminal neu.
  - Velopack-Errors mit Mutex → vorherige Schnack-Instanz beenden.
  - Test-Empfänger sieht Update nicht → Repo public? Tag wirklich v{neueste Version}?

### 4.4 Versionsnummer-Synchronisation

- **Single source of truth:** `<Version>` in `Schnack/Schnack.csproj`.
- `build-release.ps1` parsed sie und übergibt sie an `vpk pack` und `vpk upload`.
- GitHub-Release-Tag: `v<version>`.
- `<AssemblyMetadata Include="ReleaseDate" .../>` in csproj manuell mitpflegen.

---

## 5. Tests

### 5.1 `IUpdateService`-Tests

Wenn `VelopackUpdateService` gut von `UpdateManager` zu mocken ist (eigenes Adapter-Interface): `VelopackUpdateServiceTests` mit Szenarien:
- Kein Update verfügbar → kein Event, `HasPendingUpdate == false`.
- Update verfügbar → Event geworfen, `PendingUpdateVersion` gesetzt.
- `CheckOnStartupAsync` mit Netzfehler → keine Exception nach außen, Logger-Mock erhält Warn-Log.

Wenn `UpdateManager` schwer mockbar ist: stattdessen einen schmalen Adapter-Wrapper bauen (`IUpdateChecker`), der `UpdateManager` kapselt, und nur den Adapter testen. Das erspart hässliche Reflection-basierte Mocks.

### 5.2 Bestehende Tests
Weiterhin grün halten. Build-Output-Pfad muss bei Tests beachtet werden (Tests laufen aus `bin/`, nicht aus `publish/`).

---

## 6. Manuelle Verifikation (durch Hauke nach Implementierung)

Diese Schritte führt **Hauke selbst** aus, **nicht Claude Code**. Claude Code listet sie am Ende der Session-Zusammenfassung als To-Do für mich.

1. Voraussetzungen erfüllen (Abschnitt „Voraussetzungen, die der Maintainer erfüllen muss" oben).
2. Erste Version bauen und hochladen: `.\build-release.ps1 -Version 1.3.0` (oder aktueller Stand).
3. GitHub-Release prüfen: `Schnack-Setup.exe` und `.nupkg`-Dateien angehängt.
4. `Schnack-Setup.exe` von einem zweiten Test-Rechner herunterladen, ausführen, Installation läuft per-user durch.
5. Schnack starten, Tray-Icon erscheint, Hotkey funktioniert.
6. Version in csproj auf `1.3.1` hochsetzen, kleine sichtbare Änderung im Code (z.B. Tray-Tooltip-Text ergänzen).
7. `.\build-release.ps1` erneut.
8. Auf Test-Rechner Schnack neu starten → Tray-Notification „Update v1.3.1 verfügbar" innerhalb ~10 s.
9. „Update installieren" klicken → App beendet sich, kommt nach ~5 s neu hoch mit der Änderung.
10. Über Windows-Apps & Features deinstallieren → `%LocalAppData%\Schnack\` weg, `%APPDATA%\Schnack\` (Settings, Logs) bleibt.

Wenn Schritt 8 oder 9 nicht funktioniert: Logs unter `%LocalAppData%\Schnack\` (Velopack-Updater-Log) und `%APPDATA%\Schnack\logs\` (App-Log) prüfen.

---

## Akzeptanzkriterien

1. `dotnet build` und `dotnet test` grün, keine neuen Warnungen.
2. **Velopack-Bootstrap:** `VelopackApp.Build().Run()` als allererster Aufruf in `Main`. App startet weiterhin normal ohne Velopack-Argumente.
3. **`build-release.ps1`:** läuft sauber durch, prüft alle Voraussetzungen vorher, baut, packt, lädt zu GitHub hoch (wenn nicht `-SkipUpload`).
4. **Setup-EXE:** Größe 70–120 MB, Per-User-Install ohne UAC, Start-Menü-Eintrag, saubere Deinstallation **ohne** `%APPDATA%\Schnack\` zu löschen.
5. **Update-Check beim App-Start** läuft im Hintergrund, blockt den Start nicht, fängt Netzfehler still ab.
6. **Update-Verfügbar-UX:** Tray-Notification + dynamischer Tray-Menüeintrag „Update v.x.y installieren".
7. **Update-Apply:** Klick installiert, App startet neu, neue Version aktiv. Singleton-Mutex blockiert den Restart nicht.
8. **Manueller Update-Check** über Tray-Menü mit klaren Status-Notifications.
9. **Apply während Aufnahme:** MessageBox-Bestätigung erforderlich.
10. **README** aus Empfänger-Sicht aktualisiert; **`RELEASE.md`** vollständig für Maintainer.
11. **Versionsnummer** kommt aus csproj, landet automatisch in Setup-EXE und Release-Tag.
12. **Logging:** keine Verletzungen der CLAUDE.md-Logging-Verbote (keine Tokens, keine sensiblen Inhalte).

## Out of Scope

- Code-Signing.
- Private-Repo-Update-Check (würde DPAPI-gespeicherten GitHub-Token in der App erfordern).
- Multi-Channel (beta/stable). Nur `win`.
- Auto-Apply ohne Nutzer-Bestätigung.
- Background-Polling während App läuft. Nur Start + manuell.
- CI/CD-Pipeline (GitHub Actions). Build läuft lokal vom Maintainer.

---

**Beginne jetzt mit Stufe 1 (Plan). Code erst nach Freigabe.**
