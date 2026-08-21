# Schnack – Release-Anleitung (Maintainer)

Diese Datei richtet sich ausschließlich an den Maintainer (Hauke).

---

## Einmalige Voraussetzungen

Nur einmalig nötig, danach per Release-Workflow.

### 1. GitHub-Repo

- Repo `ha-lamb/Schnack` existiert und ist seit 19.08.2026 **public**, Remote ist eingerichtet.
- Der Update-Check in der App läuft anonym und funktioniert deshalb. Würde das Repo wieder privat gestellt, schlüge er still fehl (nur Warning im Log).

### 2. `vpk` CLI installieren

```pwsh
dotnet tool install -g vpk
```

Verifikation: `vpk --help` (in neuem Terminal nach Installation).

### 3. GitHub Personal Access Token generieren

1. GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic) → Generate new (classic).
2. Scope: `public_repo` (oder `repo` für private Repos).
3. Token kopieren und dauerhaft setzen:
   ```pwsh
   setx VPK_GITHUB_TOKEN "ghp_..."
   ```
4. Terminal / VS Code neu starten (damit `setx`-Wert wirksam wird).

### 4. Repo-URL anpassen (falls nötig)

Wenn der GitHub-Username oder Repo-Name vom Platzhalter abweicht:
- `Schnack/Services/VelopackUpdateService.cs` → `RepoUrl`-Konstante anpassen.
- `build-release.ps1` → `$RepoUrl`-Variable anpassen.

---

## Release-Workflow

Für jeden neuen Release folgende Schritte ausführen:

### 1. Version hochsetzen

In `Schnack/Schnack.csproj`:
```xml
<Version>1.6.0</Version>
<AssemblyMetadata Include="ReleaseDate" Value="2026-08-21" />
```

Commit der Versionsänderung:
```pwsh
git add Schnack/Schnack.csproj
git commit -m "chore: bump version to 1.6.0"
```

### 2. Build und Tests prüfen

```pwsh
dotnet build
dotnet test
```

Muss grün sein, keine neuen Warnungen.

### 3. Release bauen und hochladen

```pwsh
# Version aus csproj, Upload zu GitHub
.\build-release.ps1

# Oder explizite Version angeben:
.\build-release.ps1 -Version 1.4.0

# Nur lokal bauen, kein Upload (z.B. zum Testen):
.\build-release.ps1 -SkipUpload
```

### 4. GitHub-Release prüfen

- Auf `https://github.com/ha-lamb/Schnack/releases` prüfen:
  - `Schnack-win-Setup.exe` angehängt? (vpk-Namenskonvention: mit `win`-Channel)
  - `Schnack-1.6.0-full.nupkg` angehängt?
  - Ab dem 2. Release auch `Schnack-1.6.0-delta.nupkg`?
- Ggf. Release-Notes manuell ergänzen.

### 5. Verteilung (Erstinstallation)

- GitHub-Release-URL teilen.
- Empfänger lädt `Schnack-win-Setup.exe` herunter und führt sie aus.
- Installation erfolgt per-user in `%LocalAppData%\Schnack\` (kein UAC-Prompt).

---

## Troubleshooting

**„vpk: Der Begriff 'vpk' wird nicht erkannt"**
→ `dotnet tool install -g vpk` ausführen, dann neues Terminal öffnen.
→ Sicherstellen, dass `%USERPROFILE%\.dotnet\tools` im `PATH` ist.

**vpk startet nicht (falsche .NET-Version)**
→ vpk 0.0.1298 zielt auf .NET 9; der Rechner hat .NET 10. `build-release.ps1` setzt dafür `DOTNET_ROLL_FORWARD=Major` — bei manuellen `vpk`-Aufrufen ggf. selbst setzen.

**„VPK_GITHUB_TOKEN ist nicht gesetzt"**
→ Token neu generieren (oben Schritt 3), `setx VPK_GITHUB_TOKEN "ghp_..."`, Terminal neu starten.

**„dotnet publish fehlgeschlagen"**
→ `dotnet build` vorher grün? Fehlermeldung genau lesen.

**Update wird auf Test-Rechner nicht erkannt**
→ Ist das Repo public? Ist der Tag wirklich `v1.6.0` (mit `v` Präfix)?
→ GitHub-Release als „Published" gesetzt (nicht „Draft")?
→ Beide Rechner haben Internet-Verbindung zu GitHub?

**„App läuft bereits"-Balloon nach Update-Restart**
→ Singleton-Mutex wurde nicht sauber freigegeben. Prüfen, ob `BeforeApplyRestart`-Event
   in `App.xaml.cs` korrekt den Mutex freigibt. Log unter `%APPDATA%\Schnack\logs\` prüfen.

**Velopack-Updater-Fehler nach Installation**
→ Logs unter `%LocalAppData%\Schnack\` (Velopack-eigene Logs) prüfen.
→ Vorherige Schnack-Instanz vollständig beenden (Task-Manager).

**SmartScreen-Warnung beim Erststart der Setup-EXE**
→ Erwartet (kein Code-Signing im MVP).
→ „Weitere Informationen" → „Trotzdem ausführen" klicken.

---

## Datei-Pfade nach Installation

| Pfad | Inhalt |
|------|--------|
| `%LocalAppData%\Schnack\` | Velopack-Installation (App-Binaries, Update-Dateien) |
| `%AppData%\Schnack\` | Settings, Logs, API-Keys — bleibt bei Update **und** Deinstallation |
| `%Temp%\Schnack\` | Temporäre WAV-Dateien (werden automatisch gelöscht) |

---

## Versionierung

- **Single source of truth:** `<Version>` in `Schnack/Schnack.csproj`.
- `build-release.ps1` liest die Version automatisch und übergibt sie an `vpk`.
- GitHub-Release-Tag: `v<version>` (z.B. `v1.6.0`).
- SemVer: `MAJOR.MINOR.PATCH`.
