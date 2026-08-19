using System.Globalization;
using System.Reflection;
using Schnack.Localization;

namespace Schnack.Tests;

/// <summary>
/// Fängt vergessene Übersetzungen ab: jede Property von <see cref="Strings"/> muss in beiden
/// Sprachen einen Text liefern. Fehlt ein Schlüssel, gibt Get() den Schlüsselnamen zurück.
/// </summary>
public class LocalizationTests : IDisposable
{
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    public void Dispose() => CultureInfo.CurrentUICulture = _originalUiCulture;

    private static IEnumerable<PropertyInfo> StringProperties =>
        typeof(Strings)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string));

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void EveryKey_ResolvesInBothLanguages(string culture)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);

        var missing = StringProperties
            .Where(p => (string?)p.GetValue(null) == p.Name)
            .Select(p => p.Name)
            .ToList();

        Assert.True(missing.Count == 0,
            $"Fehlende Übersetzungen in '{culture}': {string.Join(", ", missing)}");
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void NoKey_ResolvesToEmpty(string culture)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);

        var empty = StringProperties
            .Where(p => string.IsNullOrWhiteSpace((string?)p.GetValue(null)))
            .Select(p => p.Name)
            .ToList();

        Assert.True(empty.Count == 0, $"Leere Texte in '{culture}': {string.Join(", ", empty)}");
    }

    /// <summary>Stellt sicher, dass die englische Satelliten-Assembly überhaupt geladen wird.</summary>
    [Fact]
    public void EnglishSatellite_IsActuallyUsed()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("de");
        var german = Strings.Tray_Exit;

        CultureInfo.CurrentUICulture = new CultureInfo("en");
        var english = Strings.Tray_Exit;

        Assert.Equal("Beenden", german);
        Assert.Equal("Exit", english);
    }

    [Fact]
    public void Format_SubstitutesPlaceholder()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("de");

        var result = Strings.Format(nameof(Strings.Tray_UpdateInstall), "1.4.0");

        Assert.Contains("1.4.0", result, StringComparison.Ordinal);
        Assert.DoesNotContain("{0}", result, StringComparison.Ordinal);
    }
}
