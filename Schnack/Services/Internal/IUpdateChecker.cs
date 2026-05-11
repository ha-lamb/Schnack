using Velopack;

namespace Schnack.Services.Internal;

public interface IUpdateChecker
{
    Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken ct = default);
    Task DownloadUpdatesAsync(UpdateInfo updateInfo, CancellationToken ct = default);
    // UpdateInfo implicitly converts to VelopackAsset for ApplyUpdatesAndRestart
    void ApplyUpdatesAndRestart(UpdateInfo updateInfo);
}
