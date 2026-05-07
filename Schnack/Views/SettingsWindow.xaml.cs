using System.Windows;
using Schnack.ViewModels;

namespace Schnack.Views;

public partial class SettingsWindow : Window
{
    public SettingsViewModel ViewModel { get; }

    private bool _closingHandled;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    private void OnSaveAnthropicApiKeyClick(object sender, RoutedEventArgs e)
    {
        var key = AnthropicApiKeyBox.Password;
        if (!string.IsNullOrWhiteSpace(key))
            ViewModel.SaveApiKeyCommand.Execute(key);
        AnthropicApiKeyBox.Clear();
    }

    private void OnSaveOpenAiApiKeyClick(object sender, RoutedEventArgs e)
    {
        var key = OpenAiApiKeyBox.Password;
        if (!string.IsNullOrWhiteSpace(key))
            ViewModel.SaveOpenAiApiKeyCommand.Execute(key);
        OpenAiApiKeyBox.Clear();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => CommitAndClose();

    private void OnCancelClick(object sender, RoutedEventArgs e) => TryCancel();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_closingHandled)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        TryCancel();
    }

    private void CommitAndClose()
    {
        ViewModel.SaveCommand.Execute(null);
        if (Application.Current is Schnack.App app)
            app.ApplyDebugLogLevelFromSettings();
        _closingHandled = true;
        Close();
    }

    private void TryCancel()
    {
        if (ViewModel.IsDirty)
        {
            var result = MessageBox.Show(
                "Ungespeicherte Änderungen verwerfen?",
                "Schnack – Einstellungen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;
        }

        _closingHandled = true;
        Close();
    }
}
