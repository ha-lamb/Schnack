# PROJEKT_STATUS.md – Schnack

> Lebendige Status-Übersicht: aktueller Arbeitsstand und offene Punkte. Architektur und Konventionen stehen in `CLAUDE.md`.

**Stand:** 20. August 2026
**Version:** siehe `<Version>` in `Schnack/Schnack.csproj`
**Repo:** `ha-lamb/Schnack` (**public** seit 19.08.2026, MIT), `main` synchron
**Build/Tests:** grün (97 Tests), keine Warnungen
**Letzter Release:** v1.5.1 (20.08.2026, GitHub Releases — Setup, Portable, Full + Delta)

---

## Aktueller Zustand

- App ist funktional komplett: Hotkey- und Floating-Button-Bedienung, beide Backends (OpenAI-Cloud / Claude mit lokalem Whisper), Settings-Dialog mit Backend-Umschaltung und Dirty-Tracking, DPAPI-Secrets, Schema-Migration, Velopack-Integration mit Update-UX.
- **Zweisprachigkeit (08/2026):** Oberflächensprache beim Erststart wählbar, Wechsel zur Laufzeit ohne Neustart. Diktat als **vier direkte Optionen** (Deutsch, Englisch, Deutsch → Englisch, Englisch → Deutsch) in Tray und Einstellungen, definiert in `Models/DictationChoice.cs`; Auswahl wird persistiert. Vier Prompt-Varianten. Fehlerzuordnung läuft über `SchnackError`-Codes statt über Exception-Texte — Voraussetzung dafür, dass Übersetzungen keine Fehlerpfade brechen. Settings-Schema 3 mit Migration.
- Komplett-Review 08/2026 abgeschlossen: Redundanzen zentralisiert (`ApiErrorLog`, Update-Check-Kern), Tray-Modus-Häkchen-Bug behoben, Pipeline in `DictationOrchestrator` extrahiert (mit State-Machine-Tests), tote Dateien und historische Arbeitsdokumente entfernt, Doku auf Ist-Zustand neu geschrieben.
- `releases/` enthält die gepackten Stände (zuletzt 1.4.0) — das jeweils letzte Full-Paket ist die Basis für künftige Delta-Updates und darf nicht gelöscht werden.
- **Vokabelliste (08/2026):** Eigennamen und Fachbegriffe unter „Einstellungen → Vokabular" hinterlegbar. Wirkt zweifach — als Vorab-Kontext beider Spracherkennungen (`VocabularyPrompt.ForSpeech`, gekappt aufs Kontextfenster) und als Schreibvorgabe im Nachbearbeitungs-Prompt (`{{VOCABULARY}}`). Kein Schema-Sprung nötig (rein additives Feld).

- **Logo überarbeitet (08/2026):** freigestellte Vektorfassung, zusätzlich weiße Silhouette für den Aufnahme-Knopf auf Rot/Gelb. SVG-Master liegen in `Resources/`. Tray-Icon neu erzeugt inkl. der bisher fehlenden 256-px-Größe.
- **Tray-Menü-Platzierung (08/2026):** `H.NotifyIcon` positioniert das Kontextmenü ohne Rücksicht auf den Arbeitsbereich — es rutschte sporadisch hinter die Taskleiste. `TrayService` übernimmt das Öffnen jetzt selbst, Rechenlogik in `Services/Internal/TrayMenuPlacement.cs`.

## Offene Punkte

Derzeit keine. Alle Issues im GitHub-Tracker sind geschlossen.

### Erledigt (08/2026)

- [x] Erster GitHub-Release (v1.3.2, 19.08.2026).
- [x] Repo public gestellt (19.08.2026) — In-App-Update-Check damit funktionsfähig.
- [x] **Update-Mechanismus end-to-end verifiziert** (20.08.2026): Sprung von der installierten v1.3.2 auf v1.5.1 lief über Benachrichtigung, Delta-Download (116 KB) und Neustart ohne Nacharbeit durch.

## Bekannte, bewusst offene Punkte aus dem Review 08/2026

- ~~Exception-Zuordnung über Message-Strings~~ — mit der Zweisprachigkeit auf `SchnackError`-Codes umgestellt. Dabei zwei Bugs mitbehoben: OpenAI-429 wurde nie als Rate Limit gemeldet, und erschöpfte Retries fielen wegen eines Label-Mismatches immer auf „Netzwerk".
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
