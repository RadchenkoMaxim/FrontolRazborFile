using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace FrontolFileAnalyzer;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        DataContext = new { VersionLabel = ApplicationInfo.VersionLabel };
    }

    private void DeveloperLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
