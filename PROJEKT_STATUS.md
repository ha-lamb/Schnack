# PROJEKT_STATUS.md – Schnack

> Lebendige Status-Übersicht: aktueller Arbeitsstand und offene Punkte. Architektur und Konventionen stehen in `CLAUDE.md`.

**Stand:** 19. August 2026
**Version:** siehe `<Version>` in `Schnack/Schnack.csproj`
**Repo:** `ha-lamb/Schnack` (privat, MIT), Remote verifiziert, `main` synchron
**Build/Tests:** grün (37 Tests), keine Warnungen

---

## Aktueller Zustand

- App ist funktional komplett: Hotkey- und Floating-Button-Bedienung, beide Backends (OpenAI-Cloud / Claude mit lokalem Whisper), Settings-Dialog mit Backend-Umschaltung und Dirty-Tracking, DPAPI-Secrets, Schema-Migration, Velopack-Integration mit Update-UX.
- Komplett-Review 08/2026 abgeschlossen: Redundanzen zentralisiert (`ApiErrorLog`, Update-Check-Kern), Tray-Modus-Häkchen-Bug behoben, Pipeline in `DictationOrchestrator` extrahiert (mit State-Machine-Tests), tote Dateien und historische Arbeitsdokumente entfernt, Doku auf Ist-Zustand neu geschrieben.
- Lokaler Velopack-Probebuild erfolgreich (`releases/` enthält Setup-EXE + Full-Paket 1.3.1 — Basis für künftige Delta-Updates, nicht löschen).

## Offene Punkte

- [ ] **Erster echter GitHub-Release** über `.\build-release.ps1` (Hauke; Voraussetzungen laut RELEASE.md).
- [ ] **Update-Check funktioniert erst mit public Repo:** `GithubSource` läuft anonym; gegen das private Repo schlägt der Check still fehl. Entscheidung offen: Repo public stellen oder (spätere Iteration) Token-Lösung.
- [ ] **Update-Mechanismus auf zweitem Rechner verifizieren** (nach erstem Release).

## Bekannte, bewusst offene Punkte aus dem Review 08/2026

- Exception-Zuordnung der Pipeline matcht auf Message-Strings (funktioniert; typed Exceptions wären reine Kosmetik).
- `SaveAsync`-Fehler der Settings sind nur im Log sichtbar (fire-and-forget).
- Theoretisches Schreibrennen zwischen Floating-Button-Positionsspeicherung und Settings-Dialog (last-write-wins, praktisch irrelevant).
- `ClaudeProcessResult` heißt backend-lastig, wird aber von beiden Postprocessing-Services geliefert (Umbenennung wäre Geschmacksfrage).

---

## Tooling-Setup

- **Code:** VS Code + Claude Code (Plan-Mode + Accept-Edits), Ordner `C:\Dropbox\Cowork\Schnack`
- **Issues:** GitHub Issues per GitHub-MCP (Classic-PAT mit `repo`-Scope)
- **Update-Verteilung:** Velopack über GitHub Releases

## Chat-Konvention

- **Hauptchat** beginnt mit `HUB - <Thema>`: Strategie, Übersicht, „was als Nächstes".
- **Nebenchats** mit sprechenden Titeln ohne Emoji (z.B. `Bugfix Settings-Crash`).
- Vor inhaltlicher Antwort in jedem Chat: diese Datei lesen; wichtige Erkenntnisse hierher zurückschreiben.
- Konkrete Specs als Markdown-Datei ins Repo, nicht im Chat lassen.
