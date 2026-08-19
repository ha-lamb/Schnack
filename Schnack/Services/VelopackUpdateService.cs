using Microsoft.Extensions.Logging;
using Schnack.Localization;
using Schnack.Services.Internal;
using Velopack;

namespace Schnack.Services;

public sealed class VelopackUpdateService : IUpdateService
{
    // Maintainer: vor erstem Release auf finalen Repo-Namen anpassen
    internal const string RepoUrl = "https://github.com/ha-lamb/Schnack";

    private readonly ILogger<VelopackUpdateService> _logger;
    private readonly ITrayService _trayService;
    private readonly IUpdateChecker _checker;
    private UpdateInfo? _pendingUpdate;

    public event EventHandler<UpdateAvailableEventArgs>? UpdateAvailable;
    public event EventHandler? BeforeApplyRestart;

    public bool HasPendingUpdate => _pendingUpdate != null;
    public string? PendingUpdateVersion => _pendingUpdate?.TargetFullRelease.Version.ToString();

    public VelopackUpdateService(
        ILogger<VelopackUpdateService> logger,
        ITrayService trayService,
        IUpdateChecker checker)
    {
        _logger = logger;
        _trayService = trayService;
        _checker = checker;
    }

    public Task CheckOnStartupAsync(CancellationToken ct = default) =>
        CheckCoreAsync(notifyOnNoUpdateOrError: false, ct);

    public Task CheckAndPromptAsync(CancellationToken ct = default) =>
        CheckCoreAsync(notifyOnNoUpdateOrError: true, ct);

    // Beim Start-Check keine "aktuell"/Fehler-Balloons, damit der App-Start ungestört bleibt.
    private async Task CheckCoreAsync(bool notifyOnNoUpdateOrError, CancellationToken ct)
    {
        try
        {
            var info = await _checker.CheckForUpdatesAsync(ct);
            if (info != null)
            {
                _pendingUpdate = info;
                var version = info.TargetFullRelease.Version.ToString();
                _logger.LogInformation("Update available: {Version}", version);
                UpdateAvailable?.Invoke(this, new UpdateAvailableEventArgs { NewVersion = version });
                _trayService.ShowBalloonTip(Strings.Update_AvailableTitle,
                    Strings.Format(nameof(Strings.Update_AvailableText), version));
            }
            else if (notifyOnNoUpdateOrError)
            {
                _trayService.ShowBalloonTip(Strings.Update_UpToDateTitle, Strings.Update_UpToDateText);
            }
            else
            {
                _logger.LogDebug("No update available");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning("Update check failed: {Type}", ex.GetType().Name);
            if (notifyOnNoUpdateOrError)
                _trayService.ShowBalloonTip(Strings.Update_CheckFailedTitle, Strings.Update_CheckFailedText);
        }
    }

    public async Task ApplyKnownUpdateAsync(CancellationToken ct = default)
    {
        if (_pendingUpdate == null)
        {
            _logger.LogWarning("ApplyKnownUpdateAsync called with no pending update");
            return;
        }

        var version = _pendingUpdate.TargetFullRelease.Version.ToString();
        _trayService.ShowBalloonTip(Strings.Update_DownloadingTitle,
            Strings.Format(nameof(Strings.Update_DownloadingText), version));
        _logger.LogInformation("Downloading update: {Version}", version);

        try
        {
            await _checker.DownloadUpdatesAsync(_pendingUpdate, ct);
            _logger.LogInformation("Update downloaded, applying: {Version}", version);
            BeforeApplyRestart?.Invoke(this, EventArgs.Empty);
            _checker.ApplyUpdatesAndRestart(_pendingUpdate);
            // Prozess wird durch ApplyUpdatesAndRestart beendet — dieser Code läuft nicht mehr
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError("Update apply failed: {Type}", ex.GetType().Name);
            _trayService.ShowBalloonTip(Strings.Update_FailedTitle, Strings.Update_FailedText);
        }
    }
}
