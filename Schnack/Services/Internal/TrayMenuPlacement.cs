using System.Windows;

namespace Schnack.Services.Internal;

/// <summary>
/// Berechnet, wo das Tray-Kontextmenü aufgehen soll, damit es vollständig im
/// Arbeitsbereich liegt und nicht hinter der Taskleiste verschwindet.
///
/// Nötig, weil H.NotifyIcon das Menü stur auf die Cursorposition setzt und die
/// Korrektur WPFs Popup-Automatik überlässt — die greift aber nur, wenn die
/// Menühöhe beim Öffnen schon bekannt ist. Siehe TrayService.OnPreviewContextMenuOpen.
///
/// Reine Rechenlogik ohne WPF-Interaktion oder Win32, damit testbar.
/// </summary>
internal static class TrayMenuPlacement
{
    /// <summary>
    /// Liefert die linke obere Ecke des Menüs. Alle Angaben in denselben Einheiten (DIP).
    /// </summary>
    internal static Point Place(Point cursor, Size menu, Rect workArea)
    {
        return new Point(
            PlaceHorizontally(cursor.X, menu.Width, workArea),
            PlaceVertically(cursor.Y, menu.Height, workArea));
    }

    private static double PlaceVertically(double cursorY, double height, Rect workArea)
    {
        // Bevorzugt nach oben aufklappen — gewohntes Verhalten bei unten liegender Taskleiste.
        var y = cursorY - height;

        // Passt oberhalb nicht (z.B. Taskleiste am oberen Rand): nach unten aufklappen.
        if (y < workArea.Top)
            y = cursorY;

        return Clamp(y, height, workArea.Top, workArea.Bottom);
    }

    private static double PlaceHorizontally(double cursorX, double width, Rect workArea)
    {
        var x = cursorX;

        // Läuft nach rechts über: an der Cursorposition spiegeln.
        if (x + width > workArea.Right)
            x = cursorX - width;

        return Clamp(x, width, workArea.Left, workArea.Right);
    }

    /// <summary>
    /// Hält die Kante im Bereich. Ist das Menü größer als der Arbeitsbereich, gewinnt
    /// die obere bzw. linke Kante — abgeschnitten wird dann unten bzw. rechts, wo
    /// scrollbare Menüs ohnehin weiterblättern.
    /// </summary>
    private static double Clamp(double start, double length, double min, double max)
    {
        if (start + length > max)
            start = max - length;
        return start < min ? min : start;
    }
}
