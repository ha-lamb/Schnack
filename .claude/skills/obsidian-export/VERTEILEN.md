# Verteilen des Skills

Cowork- und Desktop-Sessions lesen `~/.claude/skills/` **nicht** — sie laden projektlokale
Skills aus `<Projekt>\.claude\skills\` und die für den claude.ai-Account aktivierten
([Doku](https://code.claude.com/docs/en/skills)). Deshalb wird dieser Skill hier gepflegt
und in jedes Projekt kopiert, in dem er greifen soll.

## In ein Projekt kopieren

```bash
cp -r "C:/Dropbox/Cowork/_Skills/obsidian-export" "<PROJEKTPFAD>/.claude/skills/"
```

Wichtig: der Zielpfad endet auf `.claude/skills/` **ohne** angehängten Skill-Namen — sonst
landen `SKILL.md` und `templates\` direkt in `skills\` statt in einem eigenen Ordner, und
der Skill wird nicht erkannt.

Nach dem Kopieren die Session neu starten, damit der Skill geladen wird.

## Nach einer Änderung neu verteilen

Die Quelle hier ändern, dann in alle Projekte der Liste unten erneut kopieren. Der
Kopierbefehl überschreibt vorhandene Dateien; entfernte Dateien bleiben allerdings am Ziel
liegen und müssen von Hand weg (etwa `templates\projekt.md`, ersetzt durch
`templates\zusammenfassung.md`).

## Verteilt an

| Projekt | Pfad | Stand |
|---|---|---|
| Finanzorganisation | `C:\Dropbox\Cowork\Finanzorganisation\.claude\skills\obsidian-export\` | 2026-08-20 |
| Heimserver | `C:\Dropbox\Cowork\Heimserver\.claude\skills\obsidian-export\` | 2026-08-20 |
| Schnack | `C:\Dropbox\Cowork\Schnack\.claude\skills\obsidian-export\` | 2026-08-20 |

Weiterer Kandidat, noch nicht verteilt: `github\Photo-Pipeline`.
