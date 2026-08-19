using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Schnack.Localization;
using Schnack.Models;
using Schnack.Services;
using Schnack.ViewModels;

namespace Schnack.Views;

public partial class FirstRunWindow : Window
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISettingsService _settings;
    private readonly ILocalizationService _localization;
    private bool _initialized;

    public FirstRunWindow(
        IServiceProvider serviceProvider,
        ISettingsService settings,
        ILocalizationService localization)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;
        _settings = settings;
        _localization = localization;

        LanguageBox.ItemsSource = new[]
        {
            new LanguageOption { Value = AppLanguage.De, Display = "Deutsch" },
            new LanguageOption { Value = AppLanguage.En, Display = "English" }
        };
        LanguageBox.SelectedIndex = _settings.Settings.UiLanguage == AppLanguage.En ? 1 : 0;

        ApplyLocalization();
        _initialized = true;
    }

    // Die Texte werden im Code gesetzt (statt x:Static), damit der Sprachwechsel
    // im Dialog selbst sofort sichtbar wird.
    private void ApplyLocalization()
    {
        Title = Strings.FirstRun_Title;
        LanguageLabel.Text = Strings.FirstRun_Language;
        IntroText.Text = Strings.FirstRun_Intro;
        Step1Text.Text = Strings.FirstRun_Step1;
        Step2Text.Text = Strings.FirstRun_Step2;
        OpenSettingsButton.Content = Strings.FirstRun_OpenSettings;
        CloseButton.Content = Strings.Common_Close;
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || LanguageBox.SelectedItem is not LanguageOption option)
            return;

        // Diktiersprache folgt hier der Oberfläche; beide sind später einzeln änderbar.
        _settings.UpdateSettings(_settings.Settings with
        {
            UiLanguage = option.Value,
            DictationLanguage = option.Value
        });
        _ = _settings.SaveAsync();

        _localization.Apply(option.Value);
        ApplyLocalization();
    }

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e)
    {
        var window = _serviceProvider.GetRequiredService<SettingsWindow>();
        window.Show();
        window.Activate();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
