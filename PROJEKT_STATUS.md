# PROJEKT_STATUS.md – Schnack

> Lebendige Status-Übersicht. Wird bei Statuswechseln, neuen Aufgaben oder Architektur-Entscheidungen aktualisiert. **Nicht** mit `CLAUDE.md` (Architektur, dauerhaft) oder `TEST_RESULTS.md` (Bug-Befunde) verwechseln.

**Stand:** 11. Mai 2026
**Aktuelle Version:** 1.3.0 (in `Schnack.csproj`)
**Repo:** `ha-lamb/Schnack` (private, MIT)
**Build-Status:** lokal grün, Velopack-Pipeline implementiert, erster Release noch nicht durch

---

## Was Schnack ist

Internes Windows-11-Diktier-Tool. Tray-App, .NET 10 / WPF / x64. Nimmt deutsche Sprache via Mikrofon auf, transkribiert sie, fügt korrigierten Text oder englische Übersetzung ins zuvor aktive Textfeld ein.

**Backend wählbar (entweder-oder):**
- **OpenAI** (Cloud-STT + Cloud-Postprocessing) — Default, einsatzbereit ohne Modell-Download
- **Claude** (lokales Whisper.net + Anthropic-API für Postprocessing) — Audio bleibt lokal

**Bedienpfade:**
- Globaler Hotkey `Ctrl+Alt+S` ✅
- Schwebender Aufnahme-Button ✅
- Tray-Menü ⚠️ (Bug, siehe TEST_RESULTS.md Befund 1)

---

## Implementierungs-Stand

### ✅ Umgesetzt
- WPF-Tray-App mit `H.NotifyIcon.Wpf`, globaler Hotkey via `NHotkey.Wpf`
- Audio-Aufnahme mit NAudio (16 kHz mono PCM)
- OpenAI-Backend (STT via `v1/audio/transcriptions`, Postprocessing via `v1/chat/completions`)
- Claude-Backend (Whisper.net lokal, Anthropic `v1/messages`)
- Backend-Auswahl in den Settings als Entweder-oder-Radio
- DPAPI-verschlüsselte Secrets in `%APPDATA%\Schnack\`
- Win32-Texteinfügung über `SendInput` mit `AttachThreadInput`-Trick
- Schwebender Button mit Status-Farben (gelb/rot)
- Settings-Dialog mit Dirty-Tracking und `[Abbrechen][Speichern]`-Layout
- Über-Dialog mit Logo-Hintergrund
- Single-Instance-Mutex
- Logging via Serilog mit Logging-Verboten (siehe CLAUDE.md)
- Schema-Migration für `settings.json`
- README für Empfänger
- xUnit-Tests für ClaudeService, OpenAiChatService, JsonSettingsService
- Velopack-Integration (eigene `Main`, `App.xaml` als `<Page>`, `IUpdateService`, Tray-Update-Menü)
- `build-release.ps1` für Velopack-Pack + GitHub-Upload
- GitHub-MCP in Claude Desktop eingerichtet (Schreib-/Lesezugriff aufs private Repo verifiziert)

### 🟡 In Arbeit / offen

**Aus TEST_RESULTS.md** (manuelle End-to-End-Test-Befunde, höchste Priorität):
- **Befund 1** Tray-Menü-Pfad fügt keinen Text ein — Foreground-Caching beim Tray-Klick fehlerhaft
- **Befund 2** Schwebender Button lässt sich nicht per Drag verschieben
- **Befund 3** Schwebender Button kann nicht über Tray ausgeblendet werden (Toggle fehlt)
- **Befund 4** Settings-Dialog → „Speichern" lässt App abstürzen 🔥 kritisch

**Velopack-Anpassung für Private-Repo:**
- `VELOPACK_PROMPT.md` setzt aktuell Public-Repo voraus (anonymer Update-Check)
- Repo ist aber privat → eingebauter Read-Only-Token muss ergänzt werden
- Token-Build-Pipeline in `build-release.ps1` nachziehen

### ⏳ Geplant / nice to have
- GitHub-Issues für Befund 1–4 anlegen (per GitHub-MCP, jetzt möglich)
- Erster echter Velopack-Release über GitHub-Releases (manuelle Verifikation)
- Update-Mechanismus auf zweitem Test-Rechner durchspielen
- README ggf. Privacy-Sektion finalisieren je nach Backend-Default
- **bin/ und obj/ aus Git-History entfernen:** Commit 3d0419e hat 39 Dateien (Test-Assemblies in Schnack.Tests/bin/ und Schnack.Tests/obj/) versehentlich committed. .gitignore prüfen, `git rm -r --cached bin/ obj/` nachholen — separater Task, nicht eilig.

### ❌ Out of Scope (dauerhaft)
- Hybrid-Backend-Modi
- Streaming-STT, VAD, Auto-Stop bei Stille
- Andere STT-Sprachen als Deutsch
- Code-Signing
- Multi-User auf einem Rechner
- Auto-Apply von Updates ohne Bestätigung

---

## Aktueller Fokus

**Reihenfolge der nächsten Schritte:**

1. **Befund 4 (Settings-Crash)** — kritisch, Showstopper, zuerst.
2. **Befund 3 (Floating-Toggle)** — kleinster Aufwand, klar spezifiziert.
3. **Befund 2 (Floating-Drag)** — mittlerer Aufwand, gut diagnostizierbar.
4. **Befund 1 (Tray-Pfad)** — höchstes Risiko, Eskalations-Regel beachten (Fix-Versuch, bei Scheitern Funktion entfernen mit Bestätigung).
5. **VELOPACK_PROMPT.md** auf Private-Repo anpassen.
6. **Erster Velopack-Release** als 1.3.1 nach Bugfix bzw. 1.4.0 wenn Toggle/Drag drin.

Parallel möglich: GitHub-Issues für Befund 1–4 anlegen (per GitHub-MCP).

---

## Datei-Hierarchie für Konflikte

Höher gewinnt:
1. `TEST_RESULTS.md` — aktuelle Bug-Befunde
2. `VELOPACK_PROMPT.md` — aktuelle Velopack-Spezifikation
3. `PROJEKT_STATUS.md` — aktueller Projekt-Stand
4. `CLAUDE.md` — Architektur, Konventionen, Soll-Zustand
5. `CHANGES.md` / `CHANGES_v2.md` — historische Aufgabenpakete (weitgehend umgesetzt, nur noch Referenz)
6. `PROMPT.md` — historischer Initial-Prompt

---

## Chat-Konvention

- **Hauptchat** beginnt mit `HUB - <Thema>` (z.B. `HUB - Schnack Übersicht`). Strategie, Übersicht, „was als Nächstes"-Entscheidungen. Keine Detail-Specs.
- **Alle anderen Chats** sind Nebenchats mit sprechenden Titeln ohne Emoji (z.B. `Bugfix Settings-Crash`, `Velopack Private-Repo-Anpassung`).
- Vor inhaltlicher Antwort in JEDEM Chat: PROJEKT_STATUS.md lesen.
- Nebenchats schreiben wichtige Erkenntnisse, Entscheidungen, Statuswechsel in PROJEKT_STATUS.md zurück.
- Konkrete Specs als Markdown-Datei ins Repo, nicht im Chat lassen.

---

## Tooling-Setup

- **Code:** VS Code + Claude Code (Plan-Mode + Accept-Edits)
- **Spec/Doku:** Claude Desktop + Filesystem-MCP auf `C:\Projekte\Schnack`
- **Issues:** GitHub Issues, Zugriff per GitHub-MCP (Classic-PAT mit `repo`-Scope, Config-Vorlage in `claude_desktop_config.example.json`)
- **Git:** lokal + private GitHub-Repo `ha-lamb/Schnack`
- **Update:** Velopack über GitHub Releases (privat, Token-basiert)

---

## Manuell durchzuführende Aufgaben (für Hauke)

Sammelliste, die nur ich (Hauke) erledigen kann, nicht Claude:

- [x] GitHub-MCP in Claude Desktop einrichten (Token classic mit `repo`-Scope) — erledigt 11.05.2026
- [ ] Erster `build-release.ps1`-Lauf nach Fix der TEST_RESULTS-Befunde
- [ ] Velopack-Update-Mechanismus auf zweitem Rechner verifizieren
- [ ] Read-Only-Token für Update-Check generieren (separater PAT mit `repo`-Scope, lange Laufzeit)

---

## Änderungs-Protokoll

- **11.05.2026** — Erster Velopack-Probebuild (`.\build-release.ps1 -SkipUpload -Version 1.3.0`) erfolgreich. Setup-EXE: `releases/Schnack-win-Setup.exe`, 80,4 MB. Zwei Fixes nötig waren: (1) `DOTNET_ROLL_FORWARD=Major` dauerhaft im User-ENV gesetzt + ins Skript geschrieben (vpk 0.0.1298 zielt auf .NET 9, Rechner hat nur .NET 10); (2) Verifikationspfad im Skript von `Schnack-Setup.exe` auf `Schnack-win-Setup.exe` korrigiert (vpk-Namenskonvention).
- **11.05.2026** — GitHub-MCP in Claude Desktop eingerichtet und getestet. Read-Zugriff aufs private Repo `ha-lamb/Schnack` funktioniert (leere Issues-Liste erwartungsgemäß). Config-Vorlage `claude_desktop_config.example.json` im Repo abgelegt (ohne Token). GitHub-Issues für Befund 1–4 können jetzt per MCP angelegt werden.
- **11.05.2026** — Hub-and-Spoke-Chat-Konvention etabliert. Alter unübersichtlicher Sammelchat wird verlassen, neuer HUB-Chat angelegt. Chat-Konvention in Project Instructions und PROJEKT_STATUS.md verankert.
- **10.05.2026** — PROJEKT_STATUS.md angelegt nach Wechsel zu Claude Desktop + Filesystem-MCP-Setup.
