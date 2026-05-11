using Velopack;
using Velopack.Sources;

namespace Schnack.Services.Internal;

public sealed class VelopackUpdateChecker : IUpdateChecker
{
    private readonly UpdateManager _mgr;

    public VelopackUpdateChecker(string repoUrl)
        => _mgr = new UpdateManager(new GithubSource(repoUrl, accessToken: null, prerelease: false));

    public Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken ct = default)
        => _mgr.CheckForUpdatesAsync();

    public Task DownloadUpdatesAsync(UpdateInfo updateInfo, CancellationToken ct = default)
        => _mgr.DownloadUpdatesAsync(updateInfo, progress: null, ct);

    public void ApplyUpdatesAndRestart(UpdateInfo updateInfo)
        => _mgr.ApplyUpdatesAndRestart(updateInfo);  // implicit UpdateInfo → VelopackAsset
}
