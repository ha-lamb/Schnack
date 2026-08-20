using System.Windows;
using Schnack.Services.Internal;

namespace Schnack.Tests;

public class TrayMenuPlacementTests
{
    // Reale Maschine: 4096x1728, Taskleiste unten 48px -> nutzbar bis y=1680
    private static readonly Rect BottomTaskbar = new(0, 0, 4096, 1680);
    // Taskleiste oben: nutzbarer Bereich beginnt bei y=48
    private static readonly Rect TopTaskbar = new(0, 48, 4096, 1680);

    private static readonly Size Menu = new(240, 300);

    [Fact]
    public void BottomTaskbar_OpensUpwardFromCursor()
    {
        // Cursor im Tray, also unterhalb des Arbeitsbereichs-Endes
        var p = TrayMenuPlacement.Place(new Point(3900, 1700), Menu, BottomTaskbar);

        // Unterkante darf den Arbeitsbereich nicht überschreiten
        Assert.True(p.Y + Menu.Height <= BottomTaskbar.Bottom,
            $"Menü ragt bis {p.Y + Menu.Height}, erlaubt bis {BottomTaskbar.Bottom}");
    }

    [Fact]
    public void BottomTaskbar_CursorWellInside_StillOpensUpward()
    {
        var p = TrayMenuPlacement.Place(new Point(1000, 1200), Menu, BottomTaskbar);

        Assert.Equal(1200 - Menu.Height, p.Y);
    }

    [Fact]
    public void TopTaskbar_OpensDownward()
    {
        // Cursor direkt unter einer oben liegenden Taskleiste: nach oben ist kein Platz
        var p = TrayMenuPlacement.Place(new Point(3900, 60), Menu, TopTaskbar);

        Assert.Equal(60, p.Y);
        Assert.True(p.Y >= TopTaskbar.Top);
    }

    [Fact]
    public void OverflowRight_FlipsToLeftOfCursor()
    {
        var p = TrayMenuPlacement.Place(new Point(4090, 1700), Menu, BottomTaskbar);

        Assert.Equal(4090 - Menu.Width, p.X);
        Assert.True(p.X + Menu.Width <= BottomTaskbar.Right);
    }

    [Fact]
    public void NoOverflow_KeepsCursorX()
    {
        var p = TrayMenuPlacement.Place(new Point(500, 1700), Menu, BottomTaskbar);

        Assert.Equal(500, p.X);
    }

    [Fact]
    public void FlipWouldLeaveScreen_ClampsToLeftEdge()
    {
        // Schmaler Arbeitsbereich, Menü passt weder rechts noch gespiegelt
        var narrow = new Rect(0, 0, 200, 1000);
        var p = TrayMenuPlacement.Place(new Point(190, 990), new Size(240, 300), narrow);

        Assert.Equal(narrow.Left, p.X);
    }

    [Fact]
    public void MenuTallerThanWorkArea_AlignsToTop()
    {
        var small = new Rect(0, 0, 1920, 200);
        var p = TrayMenuPlacement.Place(new Point(100, 190), new Size(240, 500), small);

        Assert.Equal(small.Top, p.Y);
    }

    [Theory]
    [InlineData(0, 0)]           // Cursor exakt in der Ecke oben links
    [InlineData(4096, 1680)]     // exakt auf der unteren rechten Arbeitsbereichs-Kante
    [InlineData(4096, 1728)]     // unterhalb, also in der Taskleiste
    public void ResultAlwaysInsideWorkArea(double cx, double cy)
    {
        var p = TrayMenuPlacement.Place(new Point(cx, cy), Menu, BottomTaskbar);

        Assert.InRange(p.X, BottomTaskbar.Left, BottomTaskbar.Right - Menu.Width);
        Assert.InRange(p.Y, BottomTaskbar.Top, BottomTaskbar.Bottom - Menu.Height);
    }

    [Fact]
    public void WorkAreaWithOffset_RespectsLeftAndTop()
    {
        // Zweitmonitor rechts vom Hauptmonitor
        var secondary = new Rect(4096, 100, 1920, 1000);
        var p = TrayMenuPlacement.Place(new Point(4100, 1080), Menu, secondary);

        Assert.True(p.X >= secondary.Left);
        Assert.True(p.Y >= secondary.Top);
        Assert.True(p.Y + Menu.Height <= secondary.Bottom);
    }
}
