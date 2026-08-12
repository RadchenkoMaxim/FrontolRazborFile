using System.Windows;

namespace FrontolFileAnalyzer;

public partial class TextEditWindow : Window
{
    public TextEditWindow(string title, string prompt, string value)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        ValueText.Text = value;
        SourceInitialized += (_, _) => WindowBoundsHelper.ConstrainToOwnerWorkingArea(this);
        Loaded += (_, _) =>
        {
            ValueText.Focus();
            ValueText.SelectAll();
        };
    }

    public string Value => ValueText.Text;

    private void Apply_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
