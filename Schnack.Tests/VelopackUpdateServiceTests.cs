using Microsoft.Extensions.Logging;
using Moq;
using NuGet.Versioning;
using Schnack.Services;
using Schnack.Services.Internal;
using Velopack;

namespace Schnack.Tests;

public class VelopackUpdateServiceTests
{
    private readonly Mock<ILogger<VelopackUpdateService>> _logger = new();
    private readonly Mock<ITrayService> _tray = new();
    private readonly Mock<IUpdateChecker> _checker = new();

    private VelopackUpdateService CreateSut() =>
        new(_logger.Object, _tray.Object, _checker.Object);

    // ── Szenario 1: Kein Update verfügbar ────────────────────────────────────

    [Fact]
    public async Task CheckOnStartupAsync_NoUpdate_HasPendingUpdateFalse()
    {
        _checker.Setup(c => c.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((UpdateInfo?)null);

        var sut = CreateSut();
        bool eventFired = false;
        sut.UpdateAvailable += (_, _) => eventFired = true;

        await sut.CheckOnStartupAsync();

        Assert.False(sut.HasPendingUpdate);
        Assert.Null(sut.PendingUpdateVersion);
        Assert.False(eventFired);
    }

    [Fact]
    public async Task CheckAndPromptAsync_NoUpdate_ShowsUpToDateBalloon()
    {
        _checker.Setup(c => c.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((UpdateInfo?)null);

        var sut = CreateSut();
        await sut.CheckAndPromptAsync();

        _tray.Verify(t => t.ShowBalloonTip(
            It.Is<string>(s => s.Contains("aktuell")),
            It.IsAny<string>()), Times.Once);
    }

    // ── Szenario 2: Update verfügbar ─────────────────────────────────────────

    [Fact]
    public async Task CheckOnStartupAsync_UpdateAvailable_EventFiredAndVersionSet()
    {
        var asset = new VelopackAsset { Version = new SemanticVersion(1, 4, 0) };
        var updateInfo = new UpdateInfo(asset, false, null!, []);

        _checker.Setup(c => c.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateInfo);

        var sut = CreateSut();
        string? capturedVersion = null;
        sut.UpdateAvailable += (_, e) => capturedVersion = e.NewVersion;

        await sut.CheckOnStartupAsync();

        Assert.True(sut.HasPendingUpdate);
        Assert.Equal("1.4.0", sut.PendingUpdateVersion);
        Assert.Equal("1.4.0", capturedVersion);
    }

    [Fact]
    public async Task CheckOnStartupAsync_UpdateAvailable_ShowsBalloon()
    {
        var asset = new VelopackAsset { Version = new SemanticVersion(1, 4, 0) };
        var updateInfo = new UpdateInfo(asset, false, null!, []);

        _checker.Setup(c => c.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateInfo);

        var sut = CreateSut();
        await sut.CheckOnStartupAsync();

        _tray.Verify(t => t.ShowBalloonTip(
            It.IsAny<string>(),
            It.Is<string>(s => s.Contains("1.4.0"))), Times.Once);
    }

    // ── Szenario 3: Netzwerkfehler beim Startup-Check ────────────────────────

    [Fact]
    public async Task CheckOnStartupAsync_NetworkException_NoExceptionPropagated()
    {
        _checker.Setup(c => c.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Keine Verbindung"));

        var sut = CreateSut();

        // Darf keine Exception werfen
        await sut.CheckOnStartupAsync();

        Assert.False(sut.HasPendingUpdate);
    }

    [Fact]
    public async Task CheckOnStartupAsync_NetworkException_LogsWarning()
    {
        _checker.Setup(c => c.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Keine Verbindung"));

        var sut = CreateSut();
        await sut.CheckOnStartupAsync();

        _logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task CheckOnStartupAsync_NetworkException_NoTrayBalloon()
    {
        _checker.Setup(c => c.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Keine Verbindung"));

        var sut = CreateSut();
        await sut.CheckOnStartupAsync();

        // Beim Start-Check: kein Balloon bei Fehler (App-Start unbeeinträchtigt)
        _tray.Verify(t => t.ShowBalloonTip(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ── BeforeApplyRestart-Event ──────────────────────────────────────────────

    [Fact]
    public async Task ApplyKnownUpdateAsync_NoPendingUpdate_NoOp()
    {
        var sut = CreateSut();
        await sut.ApplyKnownUpdateAsync();

        _checker.Verify(c => c.DownloadUpdatesAsync(
            It.IsAny<UpdateInfo>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
