using Schnack.Localization;

namespace Schnack.Models;

/// <summary>
/// Eine der wählbaren Diktat-Optionen — die Kombination aus gesprochener Sprache und
/// Weiterverarbeitung. Übersetzt wird ausschließlich vom KI-Dienst; ohne aktive Glättung
/// bleiben deshalb nur die beiden reinen Diktiersprachen.
/// Tray-Menü und Einstellungen speisen sich aus <see cref="Available"/>, damit sie nicht
/// auseinanderlaufen.
/// </summary>
public readonly record struct DictationChoice(AppLanguage Language, DictationMode Mode)
{
    public static readonly DictationChoice[] All =
    [
        new(AppLanguage.De, DictationMode.Correct),
        new(AppLanguage.En, DictationMode.Correct),
        new(AppLanguage.De, DictationMode.Translate),
        new(AppLanguage.En, DictationMode.Translate)
    ];

    public string DisplayName => (Language, Mode) switch
    {
        (AppLanguage.De, DictationMode.Correct) => Strings.Mode_German,
        (AppLanguage.En, DictationMode.Correct) => Strings.Mode_English,
        (AppLanguage.De, DictationMode.Translate) => Strings.Mode_GermanToEnglish,
        _ => Strings.Mode_EnglishToGerman
    };

    /// <summary>Die unter der aktuellen Konfiguration tatsächlich möglichen Optionen.</summary>
    public static DictationChoice[] Available(bool smoothingActive) =>
        smoothingActive
            ? All
            : [.. All.Where(choice => choice.Mode == DictationMode.Correct)];

    /// <summary>
    /// Bildet eine nicht mehr unterstützte Auswahl auf die nächstbeste ab: die Sprache bleibt,
    /// nur die Übersetzung fällt weg — die für den Nutzer am wenigsten überraschende Abstufung.
    /// </summary>
    public static DictationChoice ClampTo(DictationChoice choice, bool smoothingActive) =>
        smoothingActive
            ? choice
            : new DictationChoice(choice.Language, DictationMode.Correct);

    /// <summary>Rekonstruiert die Auswahl aus den gespeicherten Settings-Feldern.</summary>
    public static DictationChoice FromSettings(AppSettings settings) =>
        new(settings.DictationLanguage,
            settings.DefaultMode == "translate" ? DictationMode.Translate : DictationMode.Correct);

    /// <summary>Wert für <see cref="AppSettings.DefaultMode"/>.</summary>
    public string ModeValue => Mode == DictationMode.Translate ? "translate" : "correct";
}
