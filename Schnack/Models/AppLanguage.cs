using System.Text.Json.Serialization;

namespace Schnack.Models;

/// <summary>Sprache für Oberfläche bzw. Diktat. Beide sind unabhängig einstellbar.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppLanguage
{
    [JsonStringEnumMemberName("de")]
    De,
    [JsonStringEnumMemberName("en")]
    En
}

public static class AppLanguageExtensions
{
    /// <summary>ISO-639-1-Code für STT-APIs und CultureInfo.</summary>
    public static string ToIsoCode(this AppLanguage language) => language == AppLanguage.En ? "en" : "de";

    /// <summary>Die jeweils andere Sprache — Zielsprache beim Übersetzen.</summary>
    public static AppLanguage Other(this AppLanguage language) =>
        language == AppLanguage.De ? AppLanguage.En : AppLanguage.De;
}
