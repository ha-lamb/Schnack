using Schnack.Models;

namespace Schnack.Services;

public interface ISettingsService
{
    AppSettings Settings { get; }
    void UpdateSettings(AppSettings settings);
    Task LoadAsync(CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);

    /// <summary>True, wenn bei <see cref="LoadAsync"/> keine settings.json existierte und Standardwerte neu geschrieben wurden.</summary>
    bool CreatedDefaultSettingsOnLastLoad { get; }
}
