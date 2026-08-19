using System.Reflection;
using System.Windows;

namespace Schnack.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var asm = Assembly.GetExecutingAssembly();
        var version = asm.GetName().Version?.ToString(3) ?? "1.0.0";
        var author = asm.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "–";
        var date = asm.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "ReleaseDate")?.Value ?? "–";

        VersionText.Text = Localization.Strings.Format(nameof(Localization.Strings.About_Version), version);
        DateText.Text = date;
        AuthorText.Text = author;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
