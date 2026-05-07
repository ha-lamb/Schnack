# Manuelle Testergebnisse – Schnack

> Beobachtungen aus einem End-to-End-Test aus Nutzersicht. Diese Datei ist die **aktuelle Aufgabenquelle** für Claude Code. Bei Konflikten gilt: `TEST_RESULTS.md` > `CLAUDE.md` > `PROMPT.md`.
>
> Vorgehen: Plan-Mode, dann Plan abwarten, dann Implementierung.

## Zusammenfassung

| Bedienpfad | Status | Befund |
|------------|--------|--------|
| Schwebender Button – Klick → Aufnahme | ✅ funktioniert | nichts zu tun |
| Schwebender Button – Status-Farben (gelb/rot) | ✅ funktioniert | nichts zu tun |
| Schwebender Button – Texteinfügung | ✅ funktioniert | nichts zu tun |
| Hotkey `Ctrl+Alt+S` | ✅ funktioniert zuverlässig | nichts zu tun |
| Tray-Menü → Aufnahme starten/stoppen | ❌ Texteinfügung scheitert | **Befund 1** |
| Schwebender Button – per Drag verschieben | ❌ funktioniert nicht | **Befund 2** |
| Schwebenden Button über Tray ein-/ausblenden | ❌ funktioniert nicht (Toggle fehlt) | **Befund 3** |
| Settings-Dialog → „Speichern"-Button | ❌ App stürzt ab | **Befund 4** (kritisch) |

## Detailbefunde

### Befund 1 – Tray-Menü-Pfad fügt keinen Text ein

**Reproduktion:**
1. Cursor in beliebiges Textfeld (z.B. Notepad) setzen.
2. Rechtsklick auf Schnack-Tray-Icon → „Aufnahme starten" wählen.
3. Sprechen.
4. Rechtsklick auf Tray-Icon → „Aufnahme stoppen" wählen.
5. Verarbeitung läuft sichtbar durch.

**Erwartetes Verhalten:** Korrigierter Text erscheint im Notepad-Textfeld, identisch zum Hotkey-Pfad.

**Tatsächliches Verhalten:** Kein Text wird eingefügt.

**Eindruck des Nutzers:** „Der Fokus kann nicht gleichzeitig im Dokument sein, während ich im Tray-Menü navigiere." Während das Tray-Menü geöffnet ist, geht der Fokus auf Tray/Explorer verloren — der Cache des Ziel-HWND zeigt vermutlich auf das Schnack-eigene Fenster oder `explorer.exe`.

**Vergleich:** Hotkey-Pfad und Floating-Button-Pfad funktionieren beide einwandfrei mit identischer nachgelagerter Pipeline. Das Problem liegt **ausschließlich im Foreground-Caching beim Tray-Pfad**, nicht in der Texteinfügung selbst.

**Lösungsvorgaben in Reihenfolge der Präferenz:**

1. **Bevorzugt — Bug fixen.** Vorgehen:
   - Diagnose-Logging einbauen (LogDebug): HWND beim `MouseDown` vor Menü-Öffnen, HWND nach Menü-Schließen, finales HWND an `TextInsertionService`, `SetForegroundWindow`-Ergebnis, Process-Name des Ziel-HWND.
   - HWND aus dem `MouseDown`-Handler vor Menü-Öffnen sichern.
   - Nach Menü-Schließen verifizieren, dass das HWND nicht auf einen Schnack-eigenen Prozess oder `explorer.exe` zeigt — wenn doch, das frühere `MouseDown`-HWND verwenden.
   - Falls trotz aller Fallbacks kein gültiges Ziel-Fenster vorhanden: automatisch auf **Clipboard-Fallback** umschalten und Tray-Notification anzeigen: „Kein Zielfenster erkannt – Text liegt in der Zwischenablage, bitte mit Strg+V einfügen."

2. **Zweitwahl, falls Fix nach ernsthaftem Versuch nicht zuverlässig wird — Funktion entfernen:**
   - Tray-Menüeinträge „Aufnahme starten" und „Aufnahme stoppen" komplett entfernen.
   - Stattdessen ein deaktivierter Hinweis-Eintrag oben im Tray-Menü: „Aufnahme über Hotkey (Ctrl+Alt+S) oder schwebenden Button starten".
   - Modus-Auswahl, Settings, Über und Beenden bleiben im Tray-Menü.
   - README aktualisieren: Tray-Menü dokumentiert keinen Aufnahme-Steuerpfad mehr.
   - Begründung im Code-Kommentar (`TrayService`): „Removed start/stop entries because Win32 foreground tracking through tray menu interaction is not reliable; use hotkey or floating button."

**Eskalations-Regel für Claude Code:** Versuche zuerst Lösung 1 mit max. 2 Diagnose-Iterationen (Logging → Fix-Versuch → Test). Wenn der Fix nach dem zweiten Versuch nicht zuverlässig funktioniert: melde das, schlage Lösung 2 vor und **warte auf Bestätigung**. Nicht eigenmächtig entfernen.

**Akzeptanztest des Nutzers:**
1. Cursor in Notepad → Tray → „Aufnahme starten" → sprechen → Tray → „Aufnahme stoppen" → Text erscheint in Notepad.
2. Im Fall des Clipboard-Fallbacks: Tray-Notification ist sichtbar, Text liegt zur Verifikation per Strg+V in der Zwischenablage.

---

### Befund 2 – Schwebender Button lässt sich nicht verschieben

**Reproduktion:**
1. Schwebenden Button über Tray-Menü einblenden.
2. Linke Maustaste auf den Button drücken und gedrückt halten.
3. Maus bewegen.

**Erwartetes Verhalten:** Button folgt der Maus, beim Loslassen bleibt er an neuer Position. Position überlebt App-Neustart.

**Tatsächliches Verhalten:** Button bleibt an seiner Stelle. Möglicherweise wird stattdessen die Aufnahme getoggelt oder der Drag wird unterdrückt.

**Vorgehen:**
- Diagnose im `FloatingRecordWindow`: Reagiert `MouseLeftButtonDown` auf das richtige Element, oder fängt ein Child-Control den Event ab? Wird `DragMove()` korrekt aufgerufen? Wird die Aufnahme-Toggle-Logik bei einem Drag fälschlicherweise mitausgelöst (Schwellwert prüfen)?
- Beachte: `WS_EX_NOACTIVATE` ist gesetzt — das beeinflusst Aktivierung, nicht Mouse-Events, sollte also nicht stören.
- Falls `DragMove()` mit `WS_EX_NOACTIVATE` Edge-Cases hat: eigene Drag-Logik bauen (`Mouse.GetPosition`-Differenz seit MouseDown direkt auf `Left` / `Top` schreiben).
- Drag-Schwelle 3–4 Pixel, damit Mikrobewegungen die Aufnahme nicht ungewollt unterdrücken.
- Position nach Loslassen in `AppSettings` (`FloatingButtonLeft`, `FloatingButtonTop`) speichern. Diese Werte beim nächsten Einblenden lesen.
- Diagnose-Logging als `LogDebug` drinlassen (für künftige Probleme), nicht als `LogInformation`.

**Akzeptanztest des Nutzers:**
1. Button per Drag von rechts oben nach links unten ziehen → Button folgt.
2. Maus loslassen → Button bleibt links unten.
3. App schließen, neu starten, Button einblenden → erscheint links unten.
4. Kurzer Klick auf Button (ohne Drag-Bewegung) → toggelt Aufnahme wie bisher.

---

### Befund 3 – Schwebender Button kann nicht ausgeblendet werden

**Reproduktion:**
1. Schwebenden Button über Tray-Menü einblenden.
2. Rechtsklick auf Tray → „Schwebender Aufnahme-Button" erneut anklicken.

**Erwartetes Verhalten:** Button wird wieder ausgeblendet (Toggle-Verhalten). Beim erneuten Klick wieder eingeblendet, an zuletzt gespeicherter Position.

**Tatsächliches Verhalten:** Button bleibt sichtbar, der Menü-Klick hat keinen Effekt. Kein Weg, den Button auszublenden außer App-Beenden.

**Vorgehen:**

- **`IFloatingRecordUi`-Interface erweitern:**
  - Neue Methode `Hide()`.
  - Neues Property `bool IsVisible`.
  - Neues Event `VisibilityChanged`.
- **`FloatingRecordUiService`-Implementation:**
  - `Hide()` ruft `_window?.Hide()`, wirft `VisibilityChanged`. Window wird **nicht** zerstört (Position und State bleiben).
  - `IsVisible` liefert `_window?.IsVisible ?? false`.
  - `ShowOrActivate()` ruft `VisibilityChanged` ebenfalls.
- **`TrayService`:** Menüeintrag „Schwebender Aufnahme-Button" als `MenuItem { IsCheckable = true }` umsetzen. Click wirft ein neues Event `ToggleFloatingRecorderRequested` (das bisherige `ShowFloatingRecorderRequested` wird umbenannt oder ersetzt). Neue Methode `UpdateFloatingButtonVisibility(bool visible)` setzt `IsChecked` über Dispatcher.
- **`App.xaml.cs`:** Handler für `ToggleFloatingRecorderRequested` ruft je nach `IsVisible` entweder `Hide()` oder `ShowOrActivate()`. Subscribe auf `VisibilityChanged`, damit das Tray-Häkchen synchron bleibt. Initial `UpdateFloatingButtonVisibility(false)` setzen.
- Default beim App-Start: Button **nicht** sichtbar (so wie aktuell).
- Sichtbarkeit über App-Sessions persistieren: **nicht** im MVP. Optional als Kommentar `FloatingButtonVisibleOnStartup` in `AppSettings` für später vorbereiten.

**Akzeptanztest des Nutzers:**
1. App starten → Button nicht sichtbar, Tray-Eintrag „Schwebender Aufnahme-Button" hat **kein Häkchen**.
2. Eintrag anklicken → Häkchen erscheint, Button wird sichtbar.
3. Eintrag erneut anklicken → Häkchen verschwindet, Button wird ausgeblendet.
4. Eintrag wieder anklicken → Häkchen erscheint, Button erscheint an **derselben Position** wie zuletzt.
5. Während Aufnahme: Toggle bleibt erlaubt; State (rote Farbe etc.) bleibt nach Wieder-Einblenden korrekt.

---

### Befund 4 – Settings-Dialog: „Speichern" lässt App abstürzen 🔥

**Schweregrad:** Kritisch (App-Crash, kein Workaround außer Settings-Dialog gar nicht öffnen).

**Reproduktion:**
1. Schnack starten.
2. Tray-Menü → „Einstellungen öffnen".
3. Beliebige Änderung vornehmen (oder auch ohne Änderung).
4. Auf „Speichern" klicken.

**Erwartetes Verhalten:** Settings werden persistiert, Settings-Fenster schließt sich, Schnack läuft weiter im Tray.

**Tatsächliches Verhalten:** App wirkt wie abgestürzt. Settings-Dialog schließt nicht ordentlich, Schnack reagiert nicht mehr / verschwindet.

**Vermutete Ursachen** (alle bitte prüfen):

1. **Threading-Problem in `OnSaveClick` / `OnOk`:** `await`-Aufruf gegen `ISettingsService.SaveAsync` läuft synchron weiter und löst eine `InvalidOperationException` auf dem Dispatcher aus. Prüfen, ob der Save-Pfad ein `await` korrekt verwendet und die UI-Operationen (`Close()`) auf dem UI-Thread laufen.
2. **Re-Initialisierung von Hotkey/Tray nach Save:** Wenn Save auch `HotkeyService.Reregister()` oder ähnliches triggert und dabei eine Exception fliegt (z.B. weil ein Hotkey schon belegt ist), könnte der Crash dort liegen. `try/catch` um Reregister legen.
3. **JSON-Serialisierungsfehler:** Eine Property in `AppSettings` ist nicht serialisierbar oder verursacht eine Exception beim Schreiben (z.B. `null`-Field, das `[JsonPropertyName]` ohne Default hat). Prüfen mit Try-Catch im `JsonSettingsService.SaveAsync`, klare Fehlermeldung statt Crash.
4. **Window.Closing-Loop:** `Window.Close()` löst `OnClosing` aus, dort wird nochmal gespeichert und nochmal `Close()` gerufen → Stack Overflow oder Reentrancy. Save-Logik muss aus `OnClosing` raus, sonst Doppel-Save mit Race.
5. **DI-Lifecycle:** `SettingsViewModel` als Singleton vs. Transient → Service-Provider wird disposed, bevor Save fertig ist. Lifecycle prüfen.

**Vorgehen für Claude Code:**

1. **Diagnose zuerst:** Logfile (`%APPDATA%\Schnack\logs\schnack-*.log`) auf den Crash-Eintrag prüfen — dort sollte eine Exception mit Stacktrace stehen. Falls im Log nichts steht: globalen Exception-Handler (`AppDomain.UnhandledException` und `Application.DispatcherUnhandledException`) bauen, der mindestens den Exception-Typ + Stacktrace ohne PII loggt. Beachte das Logging-Verbot aus CLAUDE.md (keine Inhalte, keine API-Keys).
2. **Wenn Ursache identifiziert:** gezielt fixen.
3. **Wenn Ursache nicht aus Logs/Code-Lesen ableitbar:** Reproduktionsschritte mit zusätzlichem Logging in den Save-Pfad einbauen, dann den Nutzer um manuellen Re-Test bitten und das frische Log analysieren.

**Wichtig:**
- Settings-Dialog hat aktuell vermutlich Buttons `[Speichern]` und `[Schließen]` (oder ähnlich). Im Zuge dieser Behebung **die UX harmonisieren**:
  - `[Abbrechen]` links — verwirft Änderungen, schließt Fenster (bei ungespeicherten Änderungen Rückfrage „Änderungen verwerfen?").
  - `[Speichern]` rechts — persistiert + schließt Fenster automatisch.
  - Window-X-Button verhält sich wie `[Abbrechen]`.
  - Enter = Speichern (`IsDefault="True"`), Escape = Abbrechen (`IsCancel="True"`).
- Dirty-Tracking im `SettingsViewModel`: Baseline beim Konstruktor, `IsDirty`-Property aus Vergleich.

**Akzeptanztest des Nutzers:**
1. Settings öffnen, irgendwas ändern, Speichern → Dialog schließt, App läuft normal weiter, neue Einstellung wirkt.
2. Settings öffnen, nichts ändern, Speichern → Dialog schließt, App läuft normal weiter.
3. Settings öffnen, ändern, Abbrechen → Rückfrage „Änderungen verwerfen?" → bei „Ja" Dialog schließt, alte Werte bleiben.
4. Settings öffnen, nichts ändern, Abbrechen oder X → Dialog schließt direkt ohne Rückfrage.
5. Mehrfaches Öffnen/Speichern direkt hintereinander → keine Crashs, keine Doubletten in der Settings-Datei.

---

## Was funktioniert (zur Sicherheit nicht ändern)

- Floating-Button-Klick → startet/stoppt Aufnahme zuverlässig.
- Floating-Button Statusfarben: Default → gelb (Verarbeitung) → rot (Aufnahme) → zurück.
- Floating-Button Texteinfügung in Zielfenster.
- Hotkey `Ctrl+Alt+S` → identische Funktion wie Floating-Button-Klick.
- Modus-Umschaltung (de_correct / de_to_en) im Tray-Menü.
- Settings-Dialog **öffnen** und **anzeigen** funktioniert (nur Save crashed).
- App-Beenden über Tray-Menü.

Diese Pfade sind im aktuellen Stand stabil — Änderungen daran nur, wenn ein Bugfix sie zwingend mitnehmen muss, dann mit explizitem Hinweis im Plan.

---

## Priorität / Reihenfolge

Empfehlung an Claude Code, falls nicht alles in einer Session schaffbar ist:

1. **Befund 4 (Settings-Crash)** — kritisch, muss zuerst weg. Crashes sind Showstopper.
2. **Befund 3 (Floating-Button-Toggle)** — kleinster restlicher Aufwand, klar spezifiziert, sofort sichtbarer Mehrwert.
3. **Befund 2 (Floating-Button-Drag)** — mittlerer Aufwand, gut diagnostizierbar.
4. **Befund 1 (Tray-Pfad)** — höchster Aufwand und höchstes Restrisiko. Falls Fix scheitert, Eskalation auf „entfernen" mit Bestätigung.

## Allgemeine Konventionen für die Umsetzung

Geltend für alle vier Befunde:

- Lies vorher `CLAUDE.md` (Codestil, Logging-Verbote, Threading-Regeln, Win32-Konventionen).
- Build und Tests müssen am Ende grün sein.
- Logging-Verbote beachten — keine Transkripte, keine API-Keys, kein Inhalts-Echo in Logs.
- Bei UI-Änderungen sicherstellen, dass alle UI-Operationen auf dem Dispatcher laufen.
- Bei Win32-Interop: Pattern aus `TextInsertionService` (`AttachThreadInput`-Trick) bleibt verbindlich.
- Keine neuen NuGet-Pakete ohne Rückfrage.
- Am Ende: kurze Zusammenfassung pro Befund, Hinweis welche Tests/Verifikationen ich (Nutzer) manuell durchführen muss. **Nicht committen** — ich committe selbst nach Review.
