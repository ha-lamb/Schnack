using Schnack.Models;

namespace Schnack.Services;

/// <summary>Setzt die Oberflächensprache zur Laufzeit und meldet Wechsel an die UI-Teile,
/// die ihre Texte beim Erzeugen festschreiben (Tray-Menü, schwebender Button).</summary>
public interface ILocalizationService
{
    AppLanguage Current { get; }

    void Apply(AppLanguage language);

    event EventHandler? LanguageChanged;
}
