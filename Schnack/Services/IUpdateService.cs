namespace Schnack.Services;

public interface IUpdateService
{
    /// <summary>Update-Check beim App-Start im Hintergrund. Wirft keine Exception bei Netzfehler.</summary>
    Task CheckOnStartupAsync(CancellationToken ct = default);

    /// <summary>Manueller Trigger aus dem Tray-Menü. Zeigt Status-Notifications.</summary>
    Task CheckAndPromptAsync(CancellationToken ct = default);

    /// <summary>Lädt und installiert das zuletzt erkannte Update. App startet danach neu.</summary>
    Task ApplyKnownUpdateAsync(CancellationToken ct = default);

    event EventHandler<UpdateAvailableEventArgs>? UpdateAvailable;

    /// <summary>Gefeuert kurz vor ApplyUpdatesAndRestart, damit App.xaml.cs den Mutex freigeben kann.</summary>
    event EventHandler? BeforeApplyRestart;

    bool HasPendingUpdate { get; }
    string? PendingUpdateVersion { get; }
}

public sealed class UpdateAvailableEventArgs : EventArgs
{
    public required string NewVersion { get; init; }
}
