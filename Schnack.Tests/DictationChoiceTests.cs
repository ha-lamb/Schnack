using System.Globalization;
using Schnack.Models;

namespace Schnack.Tests;

public class DictationChoiceTests : IDisposable
{
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    public void Dispose() => CultureInfo.CurrentUICulture = _originalUiCulture;

    [Fact]
    public void All_ContainsTheFourCombinationsInOrder()
    {
        Assert.Equal(
        [
            new DictationChoice(AppLanguage.De, DictationMode.Correct),
            new DictationChoice(AppLanguage.En, DictationMode.Correct),
            new DictationChoice(AppLanguage.De, DictationMode.Translate),
            new DictationChoice(AppLanguage.En, DictationMode.Translate)
        ], DictationChoice.All);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void DisplayNames_AreSetAndDistinct(string culture)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);

        var names = DictationChoice.All.Select(c => c.DisplayName).ToList();

        Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
        Assert.Equal(4, names.Distinct().Count());
    }

    [Fact]
    public void DisplayNames_ShowTranslationDirection()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("de");

        Assert.Equal("Deutsch", new DictationChoice(AppLanguage.De, DictationMode.Correct).DisplayName);
        Assert.Equal("Englisch", new DictationChoice(AppLanguage.En, DictationMode.Correct).DisplayName);
        Assert.Equal("Deutsch → Englisch", new DictationChoice(AppLanguage.De, DictationMode.Translate).DisplayName);
        Assert.Equal("Englisch → Deutsch", new DictationChoice(AppLanguage.En, DictationMode.Translate).DisplayName);
    }

    // Runde durch die Settings: Auswahl → speichern → wieder auslesen
    [Theory]
    [MemberData(nameof(AllChoices))]
    public void FromSettings_RoundTripsEveryChoice(DictationChoice choice)
    {
        var settings = new AppSettings
        {
            DictationLanguage = choice.Language,
            DefaultMode = choice.ModeValue
        };

        Assert.Equal(choice, DictationChoice.FromSettings(settings));
    }

    [Fact]
    public void FromSettings_UnknownMode_FallsBackToCorrect()
    {
        var settings = new AppSettings { DictationLanguage = AppLanguage.En, DefaultMode = "de_correct" };

        Assert.Equal(new DictationChoice(AppLanguage.En, DictationMode.Correct),
            DictationChoice.FromSettings(settings));
    }

    public static TheoryData<DictationChoice> AllChoices()
    {
        var data = new TheoryData<DictationChoice>();
        foreach (var choice in DictationChoice.All)
            data.Add(choice);
        return data;
    }
}
