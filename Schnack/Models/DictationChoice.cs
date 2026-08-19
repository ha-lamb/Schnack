using Schnack.Localization;

namespace Schnack.Models;

/// <summary>
/// Eine der vier wählbaren Diktat-Optionen — die Kombination aus gesprochener Sprache und
/// Weiterverarbeitung. Geglättet wird immer; <see cref="DictationMode.Translate"/> übersetzt
/// zusätzlich in die jeweils andere Sprache.
/// Tray-Menü und Einstellungen speisen sich aus <see cref="All"/>, damit sie nicht auseinanderlaufen.
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

    /// <summary>Rekonstruiert die Auswahl aus den gespeicherten Settings-Feldern.</summary>
    public static DictationChoice FromSettings(AppSettings settings) =>
        new(settings.DictationLanguage,
            settings.DefaultMode == "translate" ? DictationMode.Translate : DictationMode.Correct);

    /// <summary>Wert für <see cref="AppSettings.DefaultMode"/>.</summary>
    public string ModeValue => Mode == DictationMode.Translate ? "translate" : "correct";
}
