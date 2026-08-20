---
typ: zusammenfassung
projekt: {PROJEKT}
quelle: "{QUELLE_PFAD}"
quelle_hash: {HASH}
export: {DATUM}
generiert: true
tags: [projekt/{projekt-slug}, typ/zusammenfassung]
---

> [!warning] Generiert vom Skill `obsidian-export`. Änderungen hier werden beim nächsten Export überschrieben.

# {PROJEKT}

## Worum es geht

{Zwei bis vier Absätze in ganzen Sätzen, für Hauke geschrieben. Was ist das für ein
Projekt, wozu dient es, was ist der interessanteste Entwurfsentscheid darin. Keine
Tabellen, keine Verzeichnisbäume — die stehen in der Doku.}

## Wo das Projekt steht

{Was fertig ist und was zuletzt passierte, erzählend. Konkrete Zahlen und Daten nennen,
wo sie etwas aussagen.}

## Was gerade ansteht

{Die zwei oder drei Dinge, die wirklich anliegen — hervorgehoben, nicht als Liste
abgearbeitet. Der Rest steht in den Offenen Punkten.}

## Wo du einsteigst

{Der konkrete erste Handgriff: Batch-Datei, Befehl, Ordner. Dazu Konventionen, die man
kennen muss, bevor man loslegt.}

## Die vier Bereiche

- **[[{PROJEKT}/1 Doku/{eine Doku-Notiz}|Doku]]** — {ein Halbsatz, was dort liegt}
- **[[{PROJEKT}/2 Offen/{PROJEKT} – Offene Punkte|Offen]]** — {ein Halbsatz}
- **[[{PROJEKT}/3 Sessions/{eine Session}|Sessions]]** — {ein Halbsatz; falls leer: "noch
  leer, füllt sich beim nächsten Export in einer {PROJEKT}-Session"}
- **Wissen** — {Wikilinks auf alle Wissensnotizen, ohne Pfad}

{Falls Doku-Dateien übergangen wurden: ein [!note]-Kasten "Nicht exportiert" mit Angabe,
welche und warum.}
