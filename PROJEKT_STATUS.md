# PROJEKT_STATUS.md – Schnack

> Lebendige Status-Übersicht: aktueller Arbeitsstand und offene Punkte. Architektur und Konventionen stehen in `CLAUDE.md`.

**Stand:** 22. August 2026
**Version:** siehe `<Version>` in `Schnack/Schnack.csproj`
**Repo:** `ha-lamb/Schnack` (**public** seit 19.08.2026, MIT), `main` synchron
**Build/Tests:** grün (167 Tests), keine Warnungen
**Release:** **v1.6.2 (22.08.2026)** — zwei Fehlerbehebungen: halluzinierte Floskeln auf Stille, Zombie-Prozess beim Beenden; keine offenen Feature-Wünsche
**Vorgänger:** v1.6.1 (22.08.2026). Das Full-Paket der **jeweils letzten** Version bleibt in `releases/` als Delta-Basis erhalten — derzeit `Schnack-1.6.2-full.nupkg`.

---

## Aktueller Zustand

Die App ist funktional komplett und im Alltagsgebrauch. Was sie ausmacht:

- **Zwei Schichten statt Backend-Wahl.** Die Spracherkennung läuft **immer** lokal über Whisper.net; die Nachbearbeitung (Glätten, Übersetzen) liegt optional darüber und braucht OpenAI oder Anthropic Claude. Ohne Zugangsschlüssel arbeitet Schnack vollständig offline und fügt den Rohtext ein. Die Regel dafür steht an genau einer Stelle: `SmoothingPolicy.IsActive` = Schalter **und** hinterlegter Schlüssel.
- **Übersetzt wird ausschließlich vom KI-Dienst.** Ohne aktive Glättung bietet die Oberfläche nur die beiden reinen Diktiersprachen an. Der Diktat-Modus wird **allein über das Tray-Menü** gesetzt — im Einstellungsdialog wäre er redundant und am falschen Ort.
- **Bedienpfade:** globaler Hotkey und schwebender Aufnahme-Knopf. Das Tray-Menü steuert Diktat-Modus, Einstellungen, Update und Beenden.
- **Zweisprachig (DE/EN):** Oberflächensprache beim Erststart wählbar, Wechsel zur Laufzeit ohne Neustart. Fehlermeldungen laufen über `SchnackError`-Codes statt über Exception-Texte — Voraussetzung dafür, dass Übersetzungen keine Fehlerpfade brechen.
- **Vokabelliste** für Eigennamen und Fachbegriffe. Wirkt als Vorab-Kontext der Erkennung und — bei aktiver Glättung — zusätzlich als Schreibvorgabe im Nachbearbeitungs-Prompt.
- **Einstellungsdialog:** drei Reiter (Spracherkennung, Nachbearbeitung, Bedienung), fester Fußbereich, gemeinsame Stile in `Window.Resources`. Der Zugangsschlüssel steht neben dem Dienst, der ihn braucht.
- **Settings-Schema 4**, DPAPI-Secrets, Velopack-Installer mit Auto-Update über GitHub Releases.

### Leistung der Spracherkennung

Gemessen auf RTX 5070 Ti, `large-v3-turbo`, 26,9 s Audio:

| Konfiguration | Dauer | Realtime-Faktor |
|---|---|---|
| CPU | 6757 ms | 0,25 |
| **Vulkan** | **295 ms** | **0,011** |

Wortgleiches Transkript, Faktor 23. Das Vorladen beim Start nimmt dem ersten Diktat weitere 4749 ms ab. Zeitmessung und Realtime-Faktor stehen im Log — vorher gab es im Projekt keine einzige Messung.

### Glättung hielt sich nicht zurück (08/2026, v1.6.1)

Im Alltagsgebrauch kamen beim Glätten zunehmend inhaltliche Änderungen und Ergänzungen dazu. Ursache war **nicht** der Wortlaut der Prompts, sondern ein fehlender Parameter: `ClaudeService` setzte gar keine Temperatur, Anthropic legt ohne Angabe **1,0** an — das Maximum. Behoben durch Temperatur 0, Regeln im `system`-Feld, eingefasstes Transkript und entschärfte Prompts. Am echten Modell gegengeprüft: 168 Zeichen rein, 168 raus; eine diktierte Frage kommt korrigiert zurück statt beantwortet.

### Halluzinierte Floskeln auf Stille (08/2026, v1.6.2)

Nach dem Glättungs-Fix hängte Schnack „vielen Dank" an ein langes Diktat. Der erste Verdacht — wieder die Glättung — war **falsch**: Ein Gegentest mit 590 Zeichen nachrichtenartigem Text ergab dreimal identisch nichts Hinzugefügtes. Die Ursache liegt in der Spracherkennung, die auf sprachfreien Abschnitten Untertitel-Floskeln erfindet. Am Nachbau mit Raumklang reproduziert und über `SegmentFilter` behoben. Messwerte und die drei verworfenen Alternativen stehen in `CLAUDE.md`.

Lehre fürs nächste Mal: Erst reproduzieren, dann reparieren. Digitale Stille und weißes Rauschen lösen die Halluzination **nicht** aus — nötig war tiefpassgefiltertes Rauschen mit Netzbrummen, also echter Raumklang.

### Unsichtbarer Prozess nach dem Beenden (08/2026, v1.6.2)

„Schnack läuft bereits" beim Start, obwohl nichts zu sehen war — und es lief tatsächlich. Beim Beenden über das Tray-Menü warf `WhisperLocalTranscriptionService.DisposeAsync` eine `ObjectDisposedException`, weil der Container die Instanz wegen der beiden Weiterleitungs-Registrierungen mehrfach entsorgt. Die Ausnahme übersprang Mutex-Freigabe und `Shutdown(0)`; das Tray-Symbol war da schon weg, der Prozess blieb. Behoben an beiden Enden: `DisposeAsync` ist jetzt mehrfach aufrufbar, und `CleanupAndShutdown` gibt den Mutex im `finally` frei. Regressionstest in `ServiceRegistrationTests` — er schlug gegen den alten Stand fehl.

### Zwei Befunde, die man teuer neu entdecken würde

- **Turbo-Modelle übersetzen nicht.** `large-v3-turbo` ignoriert das Translate-Flag und liefert still die Quellsprache, während dieselbe Aufnahme mit `base` sauber übersetzt. Zusammen mit der Einschränkung, dass Whisper ohnehin nur ins Englische übersetzen kann, war das der Grund, die Übersetzung ganz dem KI-Dienst zu überlassen.
- **`SettingsViewModel.SaveSettings` darf `DictationLanguage` und `DefaultMode` nicht schreiben.** Sonst überschriebe jedes Speichern im Dialog die Tray-Wahl mit dem Stand vom Öffnen. Am laufenden Programm gegengeprüft.

## Offene Punkte

Keine Feature-Wünsche offen. Was an technischen Punkten bewusst liegen bleibt:

- **Kein Rückschritt auf ältere Versionen.** Nach der Schema-4-Migration kennt eine ältere Schnack-Version das Feld `aiService` nicht und fiele auf die dort noch vorhandene Cloud-Spracherkennung zurück. Velopack bewegt sich praktisch nur vorwärts, deshalb nicht abgesichert.
- Das Whisper-Modell (1,6 GB) liegt im **Roaming**-Profil (`%APPDATA%`). Fachlich gehörte es nach `%LocalAppData%`; das Verschieben bestehender Dateien wäre eine eigene Änderung mit eigenem Risiko.
- Der Modell-Download nutzt den namenlosen `HttpClient` mit 100 s Standard-Timeout — für 1,6 GB auf langsamer Leitung zu knapp. Kein Resume, kein Abbrechen.
- Ob Beam Search gegenüber Greedy lohnt, ist nicht gegengemessen. Ohne Glättung wiegt Erkennungsgenauigkeit schwerer als sonst.
- Sprachwechsel im Einstellungsdialog wirkt erst beim nächsten Öffnen (Texte kommen über `{x:Static}`).
- `SaveAsync`-Fehler der Settings sind nur im Log sichtbar (fire-and-forget).
- Theoretisches Schreibrennen zwischen Floating-Button-Positionsspeicherung und Settings-Dialog (last-write-wins, praktisch irrelevant).
- `ClaudeProcessResult` heißt anbieterlastig, wird aber von allen Nachbearbeitungs-Diensten geliefert (Umbenennung wäre Geschmacksfrage).

### Erledigt

- [x] Erster GitHub-Release (v1.3.2, 19.08.2026).
- [x] Repo public gestellt (19.08.2026) — In-App-Update-Check damit funktionsfähig.
- [x] Update-Mechanismus verifiziert (20.08.2026): v1.3.2 → v1.5.1 über Benachrichtigung, Delta-Download (116 KB) und Neustart.
- [x] **Update auf v1.6.0 verifiziert (21.08.2026):** Sprung von der installierten v1.5.1 durchgelaufen — samt Settings-Migration auf Schema 4 im echten Update-Pfad. Das Delta war mit 28 MB ausnahmsweise fast so groß wie ein Vollpaket, weil die Vulkan-Runtime komplett neu dazukam.
- [x] **v1.6.1 veröffentlicht und Update verifiziert (22.08.2026):** Delta wieder klein — **63 KB**, weil Velopack nur 6 von 62 Dateien patchen musste. Damit ist belegt, dass der 28-MB-Ausreißer einmalig war.
- [x] **v1.6.2 veröffentlicht (22.08.2026):** Delta 62 KB, 6 von 62 Dateien gepatcht — dieselbe Größenordnung wie bei v1.6.1.
- [x] **Veraltete Worktree-Kopie entfernt (22.08.2026):** 25 MB unter `.claude/worktrees/`. Der Branch enthielt noch einen ungenutzten Fix — der lokalisierte Hotkey-Fehlertext in `App.OnSettingsRequested` — der vorher nach `main` übernommen wurde; der Rest des Branches (Versionsstand 1.5.2) war überholt.

---

## Tooling-Setup

- **Code:** VS Code + Claude Code (Plan-Mode + Accept-Edits), Ordner `C:\Dropbox\Cowork\Schnack`
- **Issues:** GitHub Issues per GitHub-MCP (Classic-PAT mit `repo`-Scope) — derzeit keine offenen
- **Update-Verteilung:** Velopack über GitHub Releases

## Chat-Konvention

- **Hauptchat** beginnt mit `HUB - <Thema>`: Strategie, Übersicht, „was als Nächstes".
- **Nebenchats** mit sprechenden Titeln ohne Emoji (z.B. `Bugfix Settings-Crash`).
- Vor inhaltlicher Antwort in jedem Chat: diese Datei lesen; wichtige Erkenntnisse hierher zurückschreiben.
- Konkrete Specs als Markdown-Datei ins Repo, nicht im Chat lassen.
