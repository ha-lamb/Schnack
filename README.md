# Schnack – Voice-to-Text für Windows

Drück einen Hotkey, sprich, drück ihn nochmal — der Text landet in dem Feld, in dem der Cursor gerade steht. In Notepad, im Browser, in der E-Mail, überall.

Die Spracherkennung läuft **vollständig auf deinem Rechner**. Ohne Zugangsschlüssel, ohne Konto, ohne dass Audio das Gerät verlässt.

| Schicht | Wer | Privacy |
|---------|-----|---------|
| **Spracherkennung** | Whisper lokal (Whisper.net) — immer | nichts verlässt das Gerät |
| **Nachbearbeitung** (optional) | OpenAI **oder** Anthropic Claude | nur das Transkript geht an den gewählten Dienst |

Wer möchte, legt einen API-Schlüssel für OpenAI oder Claude ab und schaltet **„Text glätten"** ein. Dann übernimmt ein Sprachmodell Zeichensetzung, Füllwörter und auf Wunsch die Übersetzung zwischen Deutsch und Englisch. Ohne Schlüssel wird der Rohtext der Erkennung eingefügt — der ist bereits interpunktiert und in den meisten Fällen brauchbar.

---

## Herunterladen

**[⬇ Schnack-win-Setup.exe](https://github.com/ha-lamb/Schnack/releases/latest/download/Schnack-win-Setup.exe)** — Installer, richtet sich im Benutzerprofil ein, keine Administratorrechte nötig.

**[⬇ Schnack-win-Portable.zip](https://github.com/ha-lamb/Schnack/releases/latest/download/Schnack-win-Portable.zip)** — entpacken und starten, ohne Installation.

Alle Dateien und die Versionshinweise: [Releases](https://github.com/ha-lamb/Schnack/releases/latest)

### Was du sonst noch brauchst

- **Windows 11 (x64)** und ein Mikrofon.
- **[.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64)** — Schnack prüft das beim Start und verlinkt den Download, falls sie fehlt.
- **Ein Whisper-Modell.** Beim ersten Start unter *Einstellungen → Spracherkennung → Herunterladen* holen. Das Standardmodell `large-v3-turbo` ist rund **1,6 GB** groß. Ohne Modell kann Schnack nicht diktieren.
- Optional für Glätten und Übersetzen: ein API-Schlüssel von OpenAI oder Anthropic.
- Optional für Tempo: ein Grafiktreiber mit Vulkan-Unterstützung (bei aktuellen Karten vorhanden).

> **SmartScreen meldet sich beim ersten Start.** Die Anwendung ist nicht signiert — Code-Signing-Zertifikate kosten Geld, das ein privates Projekt nicht ausgibt. „Weitere Informationen" → „Trotzdem ausführen".

---

## Bedienung

### Hotkey (Standard: `Ctrl+Alt+S`)

1. Cursor in ein beliebiges Textfeld setzen
2. `Ctrl+Alt+S` drücken → Aufnahme startet
3. Sprechen
4. `Ctrl+Alt+S` erneut drücken → Erkennung, Verarbeitung, Text wird eingefügt

### Schwebender Aufnahme-Button

Über das Tray-Menü ein- und ausblendbar. Klick startet und stoppt die Aufnahme, die Farbe zeigt den Zustand (rot = Aufnahme, gelb = Verarbeitung). Frei verschiebbar; die Position bleibt über Neustarts erhalten.

### Tray-Menü

| Eintrag | Funktion |
|---------|---------|
| Deutsch / Englisch / Deutsch → Englisch / Englisch → Deutsch | Diktat-Modus wählen |
| Einstellungen… | Einstellungsdialog |
| Schwebender Aufnahme-Button | Button ein- und ausblenden |
| Über Schnack… | Version, Datum, Lizenz |
| Auf Updates prüfen | Manueller Update-Check |
| Beenden | Programm beenden |

### Diktat-Modi

Der Modus wird **im Tray-Menü** gewählt — dort, wo man ihn im Arbeitsfluss umschaltet.

| Option | Beschreibung |
|--------|-------------|
| Deutsch | Deutsch gesprochen → deutscher Text |
| Englisch | Englisch gesprochen → englischer Text |
| Deutsch → Englisch | Deutsch gesprochen → ins Englische übersetzt |
| Englisch → Deutsch | Englisch gesprochen → ins Deutsche übersetzt |

Die beiden Übersetzungsrichtungen erscheinen nur bei eingeschalteter Glättung — Whisper übersetzt nicht selbst, das kann nur der KI-Dienst. Die Auswahl bleibt über Neustarts erhalten.

---

## Einstellungen

Drei Reiter:

- **Spracherkennung** — Whisper-Modell, Download-Status, Modell beim Start vorladen, Grafikkarte nutzen, Vokabular.
- **Nachbearbeitung** — „Text glätten" ein- und ausschalten, KI-Dienst wählen (OpenAI oder Claude), dessen Modell und den Zugangsschlüssel.
- **Bedienung** — Oberflächensprache, Hotkey, Mikrofon, Zwischenablage-Verhalten, ausführliches Log.

Der Zugangsschlüssel wird **DPAPI-verschlüsselt** unter `%APPDATA%\Schnack\` abgelegt und ist nur mit deinem Windows-Konto lesbar. Alternativ per Umgebungsvariable:

```powershell
# für die Nachbearbeitung mit OpenAI:
setx OPENAI_API_KEY "sk-..."

# oder mit Claude:
setx ANTHROPIC_API_KEY "sk-ant-..."
```

### Geschwindigkeit

Die Erkennung kann die Grafikkarte nutzen (*Einstellungen → Spracherkennung → Grafikkarte nutzen*). Gemessen auf einer RTX 5070 Ti mit `large-v3-turbo` und 26,9 Sekunden Audio:

| | Dauer | Realtime-Faktor |
|---|---|---|
| Prozessor | 6757 ms | 0,25 |
| **Grafikkarte (Vulkan)** | **295 ms** | **0,011** |

Wortgleiches Transkript, Faktor 23. Ob es hilft, hängt vom Grafiktreiber ab — bei Problemen abschalten, dann rechnet Schnack auf dem Prozessor. Zusätzlich wird das Modell beim Start vorgeladen, was dem ersten Diktat rund 4,7 Sekunden Wartezeit abnimmt.

### Einstellungsdatei

`%APPDATA%\Schnack\settings.json` (Schema-Version 4, automatische Migration)

| Feld | Standard | Beschreibung |
|------|---------|-------------|
| `aiService` | `openai` | KI-Dienst für die Nachbearbeitung: `openai` oder `claude` |
| `textSmoothing` | `true` | Transkript glätten und ggf. übersetzen lassen. Aus: Rohtext einfügen |
| `uiLanguage` | Windows-Sprache | Sprache der Oberfläche: `de` oder `en` |
| `dictationLanguage` | Windows-Sprache | Gesprochene Sprache: `de` oder `en` |
| `defaultMode` | `correct` | `correct` oder `translate` — zusammen mit `dictationLanguage` ergibt das die vier Diktat-Modi |
| `whisperModel` | `large-v3-turbo` | Lokales Whisper-Modell |
| `whisperUseGpu` | `false` | Grafikkarte (Vulkan) für die Erkennung nutzen |
| `whisperPreload` | `true` | Modell beim Start vorladen |
| `openAiChatModel` | `gpt-4o-mini` | OpenAI-Modell für die Nachbearbeitung |
| `openAiChatMaxTokens` | `4096` | Maximale Ausgabelänge (OpenAI) |
| `claudeModel` | `claude-haiku-4-5` | Claude-Modell für die Nachbearbeitung |
| `claudeMaxTokens` | `4096` | Maximale Ausgabelänge (Claude) |
| `hotkey` | `Ctrl+Alt+S` | Globaler Aufnahme-Hotkey |
| `restoreClipboard` | `true` | Vorherigen Zwischenablage-Text wiederherstellen |
| `preferClipboardFreeInsertion` | `true` | Unicode-Tastatur statt Zwischenablage + Strg+V (empfohlen) |
| `vocabulary` | `[]` | Eigennamen und Fachbegriffe für bessere Erkennung |
| `debugLogging` | `false` | Ausführliches Log (ohne Transkripte). Alternativ `SCHNACK_DEBUG=1` |
| `microphoneDeviceId` | `null` | Mikrofon (`null` = System-Standard) |

Logs: `%APPDATA%\Schnack\logs\schnack-<datum>.log`, sieben Tage Aufbewahrung.

**Hotkey reagiert nicht?** Meist ist die Kombination schon belegt — anderes Programm beenden oder in den Einstellungen einen anderen Hotkey wählen.

---

## Vokabular

Eigennamen, Firmennamen und Fachbegriffe werden von der Spracherkennung gern verhört. Unter **Einstellungen → Spracherkennung → Vokabular** kannst du sie hinterlegen, ein Begriff pro Zeile:

```
Kubernetes
Krzysztof
Posteo
```

Die Liste geht als Vorab-Kontext an die Spracherkennung, damit die Begriffe überhaupt richtig *gehört* werden. Bei eingeschalteter Glättung wirkt sie zusätzlich als Schreibvorgabe für die Nachbearbeitung — damit korrigiert wird, was trotzdem danebenging.

**Grenze:** An die Spracherkennung passen nur rund 150 Wörter. Bei längeren Listen sehen die überzähligen Begriffe nur noch die Nachbearbeitung; ein Hinweis darauf landet im Log.

**Privacy:** Bei eingeschalteter Glättung werden die Begriffe bei jedem Diktat an den gewählten KI-Dienst übertragen, genau wie das Transkript. Ohne Glättung bleiben sie auf dem Gerät.

---

## Updates

Schnack prüft beim Start im Hintergrund auf neue Versionen. Bei einem Fund erscheint eine Tray-Benachrichtigung und ein Menüeintrag „Update vX.Y.Z installieren"; ein Klick lädt das Delta und startet die App neu. Manuell über **Tray-Menü → Auf Updates prüfen**.

Der Update-Check sendet einen anonymen HTTPS-Request an `github.com/ha-lamb/Schnack`. Keine Telemetrie, keine persönlichen Daten. Der App-Start wird dadurch nicht verzögert.

---

## Privacy

- **Audio verlässt das Gerät nie.** Die Spracherkennung läuft vollständig lokal über Whisper.net.
- **Mit eingeschalteter Glättung** geht das Transkript an den gewählten Dienst (OpenAI oder Anthropic) — die Aufnahme selbst nicht.
- **Ohne Glättung** arbeitet Schnack vollständig offline; es verlässt nichts das Gerät.
- Logs enthalten keine Transkripte, keine Zugangsschlüssel, keine Audiodaten.
- Temporäre WAV-Dateien (`%TEMP%\Schnack\`) werden nach der Verarbeitung gelöscht.
- Zugangsschlüssel liegen DPAPI-verschlüsselt in `%APPDATA%\Schnack\` und sind an dein Windows-Konto gebunden.

---

## Bekannte Einschränkungen

- **Das Whisper-Modell muss einmalig geladen werden** (ca. 1,6 GB). Ohne Modell kann Schnack nicht diktieren.
- **Übersetzen nur mit KI-Dienst.** Whisper übersetzt nicht selbst; ohne Schlüssel und eingeschaltete Glättung entfallen die Übersetzungsrichtungen.
- **Nur Deutsch und Englisch.** Weitere Sprachen und automatische Erkennung der Diktiersprache sind nicht vorgesehen.
- **Kein Auto-Stop.** Keine Voice Activity Detection — die Aufnahme wird manuell beendet.
- **Nur Text in der Zwischenablage.** Beim Einfügeweg über die Zwischenablage wird nur vorheriger *Text* wiederhergestellt; Bilder und Dateien gehen verloren. Der Standardweg (Unicode-Tastatur) berührt die Zwischenablage gar nicht.
- **Eine Instanz gleichzeitig.**
- **Fokuswechsel kann in seltenen Fällen scheitern.** Dann liegt der Text in der Zwischenablage und ein Tray-Hinweis bittet um manuelles `Strg+V`.
- **Die Sprache wird nicht im Installer abgefragt.** Die Setup-EXE läuft ohne Dialoge durch — der Preis für die Installation ohne Administratorrechte. Die Sprache wird beim ersten Start gewählt.

---

## Entwicklung

```powershell
git clone https://github.com/ha-lamb/Schnack.git
cd Schnack

dotnet restore
dotnet build
dotnet run --project Schnack
dotnet test
```

Vorausgesetzt wird das [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (x64). Architektur und Konventionen stehen in [CLAUDE.md](CLAUDE.md), der aktuelle Arbeitsstand in [PROJEKT_STATUS.md](PROJEKT_STATUS.md), der Release-Weg in [RELEASE.md](RELEASE.md).

### Abhängigkeiten

| Paket | Lizenz | Zweck |
|-------|--------|-------|
| H.NotifyIcon.Wpf | MIT | Tray-Icon für WPF |
| NHotkey.Wpf | MIT | Globaler Hotkey |
| NAudio | MIT | Audio-Aufnahme |
| Whisper.net (+ Runtime, Runtime.Vulkan) | MIT | Lokale Spracherkennung, wahlweise auf der Grafikkarte |
| Velopack | MIT | Installer und Auto-Update |
| Microsoft.Extensions.* | MIT | Dependency Injection, Logging, HTTP |
| Serilog.* | Apache 2.0 | Logging in Dateien |

---

## Lizenz

[MIT](LICENSE).

Schnack ist ein privates Projekt, das öffentlich verfügbar ist, weil es anderen nützen kann — kein Produkt und ohne Zusage von Support oder Weiterentwicklung. Fehlerberichte und Anregungen sind willkommen, eine Antwort ist nicht garantiert.
