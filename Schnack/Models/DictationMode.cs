namespace Schnack.Models;

/// <summary>
/// Was mit dem Diktat passiert. Die Sprache steckt nicht im Modus, sondern in
/// <see cref="AppSettings.DictationLanguage"/> — <see cref="Translate"/> übersetzt
/// immer in die jeweils andere Sprache.
/// </summary>
public enum DictationMode
{
    Correct,
    Translate
}
