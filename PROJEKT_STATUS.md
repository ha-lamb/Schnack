# PROJEKT_STATUS.md – Schnack

> Lebendige Status-Übersicht: aktueller Arbeitsstand und offene Punkte. Architektur und Konventionen stehen in `CLAUDE.md`.

**Stand:** 21. August 2026
**Version:** siehe `<Version>` in `Schnack/Schnack.csproj`
**Repo:** `ha-lamb/Schnack` (**public** seit 19.08.2026, MIT), `main` synchron
**Build/Tests:** grün (142 Tests), keine Warnungen
**Letzter Release:** v1.6.0 (21.08.2026, GitHub Releases — Setup, Portable, Full + Delta)
**Vorheriger Release:** v1.5.1 (20.08.2026) — dessen Full-Paket bleibt in `releases/` als Delta-Basis erhalten

---

## Aktueller Zustand

- App ist funktional komplett: Hotkey- und Floating-Button-Bedienung, beide Backends (OpenAI-Cloud / Claude mit lokalem Whisper), Settings-Dialog mit Backend-Umschaltung und Dirty-Tracking, DPAPI-Secrets, Schema-Migration, Velopack-Integration mit Update-UX.
- **Zweisprachigkeit (08/2026):** Oberflächensprache beim Erststart wählbar, Wechsel zur Laufzeit ohne Neustart. Diktat als **vier direkte Optionen** (Deutsch, Englisch, Deutsch → Englisch, Englisch → Deutsch) in Tray und Einstellungen, definiert in `Models/DictationChoice.cs`; Auswahl wird persistiert. Vier Prompt-Varianten. Fehlerzuordnung läuft über `SchnackError`-Codes statt über Exception-Texte — Voraussetzung dafür, dass Übersetzungen keine Fehlerpfade brechen. Settings-Schema 3 mit Migration.
- Komplett-Review 08/2026 abgeschlossen: Redundanzen zentralisiert (`ApiErrorLog`, Update-Check-Kern), Tray-Modus-Häkchen-Bug behoben, Pipeline in `DictationOrchestrator` extrahiert (mit State-Machine-Tests), tote Dateien und historische Arbeitsdokumente entfernt, Doku auf Ist-Zustand neu geschrieben.
- `releases/` enthält die gepackten Stände (zuletzt 1.4.0) — das jeweils letzte Full-Paket ist die Basis für künftige Delta-Updates und darf nicht gelöscht werden.
- **Vokabelliste (08/2026):** Eigennamen und Fachbegriffe unter „Einstellungen → Vokabular" hinterlegbar. Wirkt zweifach — als Vorab-Kontext beider Spracherkennungen (`VocabularyPrompt.ForSpeech`, gekappt aufs Kontextfenster) und als Schreibvorgabe im Nachbearbeitungs-Prompt (`{{VOCABULARY}}`). Kein Schema-Sprung nötig (rein additives Feld).

- **Logo überarbeitet (08/2026):** freigestellte Vektorfassung, zusätzlich weiße Silhouette für den Aufnahme-Knopf auf Rot/Gelb. SVG-Master liegen in `Resources/`. Tray-Icon neu erzeugt inkl. der bisher fehlenden 256-px-Größe.
- **Tray-Menü-Platzierung (08/2026):** `H.NotifyIcon` positioniert das Kontextmenü ohne Rücksicht auf den Arbeitsbereich — es rutschte sporadisch hinter die Taskleiste. `TrayService` übernimmt das Öffnen jetzt selbst, Rechenlogik in `Services/Internal/TrayMenuPlacement.cs`.

- **Lokaler Betrieb (08/2026):** Drittes Backend `Lokal` — Whisper auf dem Gerät, keine Nachbearbeitung, kein Zugangsschlüssel. Quer dazu der Schalter „Text glätten", der den Nachbearbeitungsschritt auch bei den Cloud-Stacks abschaltet. Beim Erststart wählt `BackendAutoSelect` anhand vorhandener Schlüssel; fehlt der Schlüssel des eingestellten Stacks, fällt Schnack für die Sitzung auf Lokal zurück, ohne die Einstellung zu überschreiben. Rein additive Settings-Felder, deshalb **kein** Schema-Sprung.
- **Spracherkennung beschleunigt (08/2026):** Vulkan-Runtime (`Whisper.net.Runtime.Vulkan`) neben der CPU-Runtime, explizite Thread-Zahl, Greedy-Sampling, Vorladen beim Start. Gemessen auf RTX 5070 Ti mit `large-v3-turbo` und 26,9 s Audio: **CPU 6757 ms gegen Vulkan 295 ms** bei wortgleichem Transkript. Das Vorladen nimmt dem ersten Diktat 4749 ms ab. Zeitmessung und Realtime-Faktor stehen jetzt im Log — vorher gab es im Projekt keine einzige Messung.
- **Befund: Turbo-Modelle übersetzen nicht.** `large-v3-turbo` ignoriert das Translate-Flag und liefert still Deutsch; dieselbe Aufnahme übersetzt `base` korrekt. Belegt, nicht vermutet. `WhisperModelCapabilities` kennt die Grenze, die Oberfläche blendet nicht funktionierende Optionen aus.

- **Schichtenmodell statt Stack-Wahl (08/2026):** Der erste Entwurf von v1.6.0 modellierte drei wählbare Stacks — das war falsch. Tatsächlich gibt es zwei Schichten: die Spracherkennung läuft **immer** lokal, die Nachbearbeitung liegt optional darüber. Die OpenAI-Cloud-Spracherkennung wurde ersatzlos entfernt, `BackendProvider` durch `AiService` (nur noch: wer bearbeitet nach) abgelöst. Übersetzt wird ausschließlich vom KI-Dienst; `WhisperTranslationPolicy` und `WhisperModelCapabilities` sind damit gegenstandslos und gelöscht. Die Regel „wird geglättet?" steht jetzt an einer Stelle: `SmoothingPolicy.IsActive` = Schalter **und** hinterlegter Schlüssel.
- **Einstellungsdialog neu (08/2026):** Drei Reiter — Spracherkennung, Nachbearbeitung, Bedienung — mit festem Fußbereich und gemeinsamen Stilen in `Window.Resources`. Der Zugangsschlüssel steht jetzt neben dem Dienst, der ihn braucht, statt ganz unten. Ein einziges Schlüsselfeld für beide Dienste, weil zwei Felder einen getippten Schlüssel beim falschen Anbieter hätten landen lassen.
- **Der Diktat-Modus gehört allein dem Tray-Menü.** Im Dialog war er redundant und am falschen Ort — er wechselt je Diktat, Einstellungen bearbeitet man einmal. Wichtig für künftige Änderungen: `SettingsViewModel.SaveSettings` darf `DictationLanguage` und `DefaultMode` **nicht** schreiben, sonst überschriebe jedes Speichern die Tray-Wahl mit dem Stand vom Öffnen des Dialogs. Am laufenden Programm gegengeprüft.
- **Settings-Schema 4:** `backendProvider` → `aiService`. Der alte Wert wird aus dem Roh-JSON gelesen; `TextSmoothing` wird aus der Datei übernommen und nicht auf `true` gezwungen. Am echten Profil verifiziert: Vokabular, Hotkey, Mikrofon und Diktat-Modus überstehen die Migration unverändert.

## Offene Punkte

- **Downgrade-Falle:** Eine ältere Schnack-Version kann `"backendProvider": "local"` nicht lesen; der `catch` in `LoadAsync` setzt dann still alle Einstellungen zurück. Velopack bewegt sich praktisch nur vorwärts, deshalb bewusst nicht abgesichert.
- Der Hotkey-Fehlertext in `App.OnSettingsRequested` ist noch hart auf Deutsch verdrahtet, während der gleichlautende Text beim Start lokalisiert ist.
- Ob sich Beam Search gegenüber Greedy lohnt, ist noch nicht gegengemessen. Ohne Glättung wiegt Erkennungsgenauigkeit schwerer als sonst.
- **Kein Rückschritt auf ältere Versionen mehr möglich:** Nach der Schema-4-Migration kennt eine ältere Schnack-Version `aiService` nicht und fiele auf die dort noch vorhandene Cloud-Spracherkennung zurück.
- Das Whisper-Modell (1,6 GB) liegt weiterhin im **Roaming**-Profil (`%APPDATA%`). Fachlich gehörte es nach `%LocalAppData%`; das Verschieben bestehender Dateien ist eine eigene Änderung mit eigenem Risiko.
- Der Modell-Download nutzt den namenlosen `HttpClient` mit 100 s Standard-Timeout. Für 1,6 GB auf langsamer Leitung zu knapp — ein eigener benannter Client mit großzügigem Timeout steht aus.
- Sprachwechsel im Einstellungsdialog wirkt erst beim nächsten Öffnen (Texte kommen über `{x:Static}`). Mit Reitern fällt das stärker auf als vorher.
- Unter `.claude/worktrees/frosty-jemison-035652/` liegt eine veraltete, vollständige Projektkopie aus einer früheren Sitzung — aufzuräumen.

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
