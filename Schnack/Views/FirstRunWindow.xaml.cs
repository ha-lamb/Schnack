using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Schnack.Views;

public partial class FirstRunWindow : Window
{
    private readonly IServiceProvider _serviceProvider;

    public FirstRunWindow(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;
    }

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e)
    {
        var window = _serviceProvider.GetRequiredService<SettingsWindow>();
        window.Show();
        window.Activate();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
