# Strukturierte Anforderungen (aus Nutzerbeobachtungen)

> Stand: abgeleitet aus unstrukturierten Notizen. Umsetzung erfolgt schrittweise; erledigte Punkte in CLAUDE.md/README festhalten.

## 1. Spracherkennung (STT) – Cloud OpenAI

- **Kein lokales Whisper** mehr (keine Whisper.net-Modelle auf der Platte).
- Transkription über **OpenAI Speech-to-Text** (HTTP-API, kein lokales Modell).
- Eigener **OpenAI-API-Key** (Env `OPENAI_API_KEY` und/oder DPAPI-Datei, analog Anthropic).
- In den Einstellungen: **STT-Modell per Dropdown** (kein Freitext), sinnvolle vordefinierte Modell-IDs.

## 2. Bedienung: schwebender Button

- Kleiner **Floating-Button** (positionierbar), Icon z. B. **Sprechblasen-/Whisper-Grafik**.
- Klick **Start** Aufnahme, erneuter Klick **Stopp**.
- Während Aufnahme: **visuelles Recording** (z. B. roter Balken/Indikator am Symbol).

## 3. Marke & Version

- Name/Marke **„Schnack“** mit **Symbol + weißem Hintergrund** (Tray/UI konsistent).
- **Version 1.10** (Assembly-Version z. B. `1.10.0`).

## 4. Einstellungen allgemein

- **Standard-Hotkey** auf **Strg+Alt+S** (statt bisheriger Vorgabe).
- **Claude-Modell** (oder später OpenAI-Chat): nicht Freitext, sondern **Dropdown** mit erlaubten Modell-IDs.
- **Debug-Logging-Text** kürzen: nur „Ausführliches Log“ o. Ä.; Klammerzusatz „Phasen, Threads …“ entfernen.
- **OpenAI-API-Key**: nach **Speichern** kurz **Validität testen** → Erfolgsmeldung oder Fehler (kein stilles Speichern falscher Keys).
- **Buttons unten**: klar trennen – **Speichern** (nur persistieren), **Schließen** separat; kein „Speichern und Schließen“ als einzige Aktion / kein verwirrendes kombiniertes Verhalten.

## 5. Logging / Privatsphäre

- Weiterhin **keine** Transkripte / sensiblen API-Bodies in Logs (Projektregeln).

## 6. Sonstiges (Backlog)

- Hotkey-/Tray-Verhalten bei Mehrfenster-Fokus ggf. weiter härten (bereits teils adressiert).

---

## Umsetzungsreihenfolge (Vorschlag)

1. **OpenAI Cloud STT** statt lokales Whisper (dieser Schritt).
2. Floating-Button + Recording-Indikator.
3. Branding (Icon, weißer Hintergrund) + Version 1.10.0.
4. Einstellungen: Hotkey-Default, Modell-Dropdowns, Debug-Text, OpenAI-Key-Validierung, Button-Layout.
