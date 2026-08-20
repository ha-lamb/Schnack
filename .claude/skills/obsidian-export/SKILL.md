---
name: obsidian-export
description: >
  Exportiert das Wissen des aktuellen Projekts in den Obsidian-Vault
  "Claude-Projekte" (C:\Dropbox\Cowork\Obsidian\Claude-Projekte). Je Projekt ein Ordner mit
  fünf Bereichen: Zusammenfassung (für Hauke geschrieben), Doku, Offen, Sessions, Wissen.
  Einbahn — führend bleiben immer die Projektdateien, der Vault ist jederzeit neu erzeugbar.
  Idempotent über Quell-Hashes: unveränderte Quellen werden nicht neu geschrieben. Am Ende
  fordert der Skill zum Kompaktieren des Kontexts auf.
  Trigger: "export nach obsidian", "obsidian export", "projektwissen sichern",
  "wissen in den vault", "projekt exportieren", "projektdaten sichern".
---

# Obsidian-Export (Projektwissen → Vault "Claude-Projekte")

Dieser Skill ist **projektübergreifend**: er enthält nichts Projektspezifisches und
funktioniert in jedem Arbeitsverzeichnis. Er wird an einer Stelle gepflegt
(`C:\Dropbox\Cowork\_Skills\obsidian-export\`) und in die Projekte kopiert — siehe
[VERTEILEN.md](VERTEILEN.md).

## Grundregeln

1. **Einbahn.** Führend sind immer die Projektdateien. Der Vault ist eine abgeleitete
   Kopie und jederzeit neu erzeugbar. Es wird **nie** aus dem Vault zurückgeschrieben.
2. **Nur der eigene Projektordner.** Geschrieben wird ausschließlich unter
   `<Vault>\<Projekt>\` plus die Sammelnotiz `Offene Punkte (alle Projekte).md`. Alles
   andere im Vault gehört Hauke und wird nicht angefasst — auch nicht `Willkommen.md`
   oder `.obsidian\`.
3. **Nie löschen.** Verwaiste Notizen werden mit `veraltet: true` markiert, nicht entfernt.
4. **Unverändert = nicht anfassen.** Ohne Hash-Änderung wird eine Notiz nicht neu
   geschrieben. Das ist die wichtigste Regel dieses Skills — siehe Schritt 3.
5. **Deutsch**, Notizen im Stil der Projektdokumentation.

## Zielstruktur

```
C:\Dropbox\Cowork\Obsidian\Claude-Projekte\
├─ <Projekt>\
│   ├─ 0 Zusammenfassung\<Projekt>.md     für Hauke geschrieben, Einstiegspunkt
│   ├─ 1 Doku\                            Kurzfassungen der Projekt-Markdown-Dateien
│   ├─ 2 Offen\<Projekt> – Offene Punkte.md
│   ├─ 3 Sessions\JJJJ-MM-TT <Thema>.md
│   └─ 4 Wissen\<memory-slug>.md
├─ Offene Punkte (alle Projekte).md
└─ Willkommen.md                          (Haukes eigene Notiz)
```

Die Ordner sind **nummeriert**, weil Obsidians Dateiliste Ordner immer über Dateien
sortiert und alphabetisch ordnet — nur so steht die Zusammenfassung tatsächlich zuerst.

Die Zusammenfassungsnotiz heißt **wie das Projekt**, nicht „Zusammenfassung": der Ordner
sagt bereits, was drin ist, und drei gleichnamige Notizen würden den Graph unlesbar und
bare Wikilinks mehrdeutig machen.

| Was | Wert |
|---|---|
| Vault | `C:\Dropbox\Cowork\Obsidian\Claude-Projekte` |
| Memory-Basis | `C:\Users\hlamb\.claude\projects\<projekt-slug>\memory\` |

---

## Schritt 0 — Vorprüfung

```bash
ls -d "C:/Dropbox/Cowork/Obsidian/Claude-Projekte/.obsidian"
```

Fehlt der Vault oder das `.obsidian`-Verzeichnis: **abbrechen** mit der Meldung, dass der
Vault nicht gefunden wurde, und den erwarteten Pfad nennen. Nichts anlegen — ein falsch
angelegter Vault-Ordner ist schlimmer als ein fehlender.

## Schritt 1 — Projekt und Pfade bestimmen

- **Projektname** = Ordnername des Arbeitsverzeichnisses, z.B. `Finanzorganisation`.
- **Projekt-Slug** für Tags = Projektname klein, Leer- und Sonderzeichen zu `-`.
- **Memory-Verzeichnis**: Arbeitsverzeichnis-Pfad, in dem `:` und `\` durch `-` ersetzt
  werden. `C:\Dropbox\Cowork\Finanzorganisation` wird zu
  `C--Dropbox-Cowork-Finanzorganisation`.

**Memory-Fallback.** Ist dieses Verzeichnis leer oder fehlt es, in
`C:\Users\hlamb\.claude\projects\` nach einem anderen Verzeichnis suchen, dessen Name auf
den Projektordnernamen endet — Projekte, die einmal umgezogen sind, haben ihr Memory unter
dem alten Slug (so liegt Schnacks Memory unter `c--Projekte-Schnack`, während
`C--Dropbox-Cowork-Schnack` leer ist). Den Fund im Bericht nennen. Gibt es gar kein
Memory, ist das kein Fehler.

## Schritt 2 — Quellen sammeln

| Gruppe | Quelle | Ziel |
|---|---|---|
| Zusammenfassung | `CLAUDE.md` im Projektwurzelverzeichnis (ersatzweise `AGENTS.md`) | `0 Zusammenfassung\<Projekt>.md` |
| Wissen | `<memory>\*.md` **ohne** `MEMORY.md` (das ist nur ein Index) | `4 Wissen\<slug>.md` |
| Doku | Markdown im Projekt, siehe Filter | `1 Doku\<Basisname>.md` |

```bash
find . -name "*.md" -not -path "*/.git/*" -not -path "*/.claude/*" -not -path "*/.superpowers/*" -not -path "*/node_modules/*" -not -path "*/99_Archiv/*" -not -path "*/_ARCHIVIERT*" -not -path "*/venv/*" -not -path "*/__pycache__/*" -not -name "CLAUDE.md" -not -name "AGENTS.md" -size -400k -print
```

`.claude\` und `.superpowers\` sind Werkzeugverzeichnisse, kein Projektwissen — Skills,
Templates und SDD-Arbeitsbereiche gehören nicht in den Vault.

Kommen mehr als **30** Dateien heraus, nicht blind alle nehmen: die wichtigsten auswählen
(Wurzelnähe, sprechende Namen, Spezifikationen und Anforderungen zuerst) und die
übergangenen im Bericht **und** in der Zusammenfassung ausweisen. Stillschweigend
abschneiden ist verboten.

## Schritt 3 — Hashes und Änderungsabgleich

```bash
sha256sum "<quelldatei>" | cut -c1-16
```

Frontmatter der zugehörigen Vault-Notiz lesen (`head -12 "<notiz>"`), `quelle_hash:`
vergleichen:

- Notiz fehlt → **neu** · Hash unterschiedlich → **geändert** · Hash gleich → **unverändert**

Unveränderte Quellen dürfen gar nicht erst gelesen werden — das spart den größten Teil der
Arbeit. Wer eine Notiz ohne Hash-Änderung neu schreibt, verursacht Dropbox-Sync-Rauschen
und macht Obsidians Änderungsverlauf wertlos.

## Schritt 4 — Notizen schreiben (nur neu und geändert)

Templates liegen in [templates/](templates/). Platzhalter in geschweiften Klammern
ersetzen, Struktur beibehalten — sie ist bewusst starr, damit die Notizen nicht driften.

**Wissensnotiz** ([templates/wissen.md](templates/wissen.md)) — der Body der Memory-Datei
wird **wörtlich übernommen**, nicht umformuliert. Dateiname ist **exakt der Memory-Slug**,
damit die Wikilinks in den Memory-Dateien ohne Umschreibung treffen; der lesbare Titel
steht als `aliases` und als H1. Den Titel aus der Zeile in `MEMORY.md` ziehen, die auf die
Datei verweist. Das `type`-Feld gibt es in zwei Frontmatter-Formen — unter `metadata:`
eingerückt oder auf oberster Ebene; mit `grep -E '^ *type:'` beide erwischen.

**Doku-Notiz** ([templates/doku.md](templates/doku.md)) — **Kurzfassung von 5–15 Zeilen**
plus Link auf das Original. Kein Volltext-Spiegel: der erzeugt nur Redundanz, und führend
ist ohnehin das Original. Der `file:///`-Link braucht Vorwärtsschrägstriche und `%20` für
Leerzeichen.

## Schritt 5 — Aggregate immer neu bauen

**Zusammenfassung** ([templates/zusammenfassung.md](templates/zusammenfassung.md)) und
**Offene Punkte** ([templates/offen.md](templates/offen.md)) werden bei jedem Lauf neu
geschrieben, weil sie aus mehreren Quellen aggregieren.

Die Zusammenfassung ist **für Hauke geschrieben, nicht für die Maschine**: erzählend statt
tabellarisch, in ganzen Sätzen. Worum geht es, wo steht das Projekt, was steht an, wo
steigt man ein — dazu die Links in die vier Bereiche. Keine Führende-Systeme-Tabellen,
keine Verzeichnisbäume; die stehen in der Doku. Was wichtig ist, gehört hervorgehoben;
was nur vollständig ist, bleibt weg.

Offene Punkte kommen aus: Abschnitten mit der Überschrift „Offene Punkte", `TODO`- und
`FIXME`-Markern, Warnzeichen und Formulierungen wie „ausstehend", „offen", „noch nicht".
Erledigtes gehört **nicht** hinein. Je Eintrag ein Herkunftslink. Nach Themen gruppieren,
wenn es mehr als etwa fünf Einträge sind.

Danach `Offene Punkte (alle Projekte).md` fortschreiben: eine Zeile je Projekt mit Link und
Stichworten. Diese Sammelnotiz ist das einzige projektübergreifende Element und darf die
Einträge anderer Projekte **nicht** entfernen.

## Schritt 6 — Verwaiste Notizen markieren

Notizen in `1 Doku\` und `4 Wissen\`, deren `quelle:`-Datei nicht mehr existiert:
`veraltet: true` und `veraltet_seit: <heute>` ins Frontmatter, Body unverändert lassen.
**Nicht löschen.**

## Schritt 7 — Session-Notiz

Immer, bei jedem Export. [templates/session.md](templates/session.md), Dateiname
`3 Sessions\JJJJ-MM-TT <Thema>.md`. Thema = drei bis fünf Worte zum Kern des Gesprächs.
Existiert die Datei schon, ` 2`, ` 3` … anhängen — **nie überschreiben**.

Der wertvollste Abschnitt ist „Entscheidungen": jede Entscheidung **mit Begründung**. In
sechs Monaten interessiert nicht, was gemacht wurde, sondern warum so und nicht anders.

Ist das Gespräch bisher inhaltsleer (nur der Export-Aufruf), das offen schreiben statt
etwas zu erfinden.

## Schritt 8 — Bericht und Kompaktierung

Bericht als Tabelle: je Notiz `neu` / `aktualisiert` / `übersprungen` / `veraltet markiert`,
darunter die übergangenen Doku-Dateien, falls Schritt 2 begrenzt hat.

Dann Hauke zum Kompaktieren auffordern — **der Skill kann das nicht selbst**, `/compact`
wird nur erkannt, wenn Hauke es am Anfang einer eigenen Nachricht eingibt:

> Wissen ist im Vault gesichert. Du kannst den Kontext jetzt freimachen:
> `/compact Der Projektstand ist nach Obsidian exportiert. Behalte nur den offenen Arbeitsfaden.`

## Namensregeln und Wikilinks

| Notiz | Dateiname |
|---|---|
| Zusammenfassung | `<Projekt>\0 Zusammenfassung\<Projekt>.md` |
| Doku | `<Projekt>\1 Doku\<Original-Basisname>.md` |
| Offen | `<Projekt>\2 Offen\<Projekt> – Offene Punkte.md` (Halbgeviertstrich) |
| Session | `<Projekt>\3 Sessions\JJJJ-MM-TT <Thema>.md` |
| Wissen | `<Projekt>\4 Wissen\<memory-slug>.md` — Slug **unverändert** |

- Auf die Zusammenfassung: `[[<Projekt>]]` — der Name ist vaultweit eindeutig.
- Auf Wissensnotizen: **ohne Pfad**, damit die aus den Memory-Dateien übernommenen Links
  unverändert funktionieren.
- Auf Doku, Offen, Sessions: **mit vollem Pfad**, weil Namen wie `README` in mehreren
  Projekten vorkommen — `[[Schnack/1 Doku/README]]`. Mit Anzeigetext, wo der Pfad stört:
  `[[Schnack/2 Offen/Schnack – Offene Punkte|Offen]]`.
- Innerhalb eines Ordners dürfen Nachbarn ohne Pfad verlinkt werden.

## Fehlerfälle

| Fall | Verhalten |
|---|---|
| Vault fehlt | Abbrechen, Pfad nennen, nichts anlegen |
| Kein `CLAUDE.md` und kein `AGENTS.md` | Zusammenfassung aus der vorhandenen Doku bauen, im Bericht vermerken |
| Memory-Verzeichnis leer | Fallback über den alten Slug versuchen (Schritt 1), sonst Bereich „Wissen" weglassen |
| Notiz ohne `quelle_hash` | Als **geändert** behandeln und überschreiben — aber nur im eigenen Projektordner |
| Quelldatei größer als 400 KB | Überspringen, im Bericht nennen |
| Wikilink im Memory zeigt ins Leere | Stehen lassen. Er markiert eine Notiz, die es noch nicht gibt — das ist im Memory-System so vorgesehen |
