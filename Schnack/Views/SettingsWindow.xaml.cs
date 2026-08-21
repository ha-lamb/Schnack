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

    // Der Zugriff auf die PasswordBox erfolgt ausschliesslich aus ihrem eigenen Click-Handler.
    // Das ist wichtig: WPF erzeugt den Inhalt eines Reiters erst bei dessen erster Auswahl —
    // ein Zugriff von aussen (etwa beim Speichern) liefe auf null.
    private void OnSaveApiKeyClick(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password;
        if (!string.IsNullOrWhiteSpace(key))
            ViewModel.SaveApiKeyCommand.Execute(key);
        ApiKeyBox.Clear();
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
                Localization.Strings.Settings_DiscardChanges,
                Localization.Strings.Settings_Title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;
        }

        _closingHandled = true;
        Close();
    }
}
