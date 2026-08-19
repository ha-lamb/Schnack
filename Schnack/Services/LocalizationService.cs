using System.Globalization;
using Microsoft.Extensions.Logging;
using Schnack.Models;

namespace Schnack.Services;

public sealed class LocalizationService : ILocalizationService
{
    private readonly ILogger<LocalizationService> _logger;

    public AppLanguage Current { get; private set; } = AppLanguage.De;

    public event EventHandler? LanguageChanged;

    public LocalizationService(ILogger<LocalizationService> logger) => _logger = logger;

    public void Apply(AppLanguage language)
    {
        var culture = new CultureInfo(language.ToIsoCode());

        // Default*: gilt für Threads, die noch keine eigene Kultur gesetzt haben (Worker der Pipeline).
        // Zusätzlich der aktuelle Thread, damit der Wechsel sofort im UI greift.
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;

        Current = language;
        _logger.LogInformation("UI language applied: {Language}", language);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
