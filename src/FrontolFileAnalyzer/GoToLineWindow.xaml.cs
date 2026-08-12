using System.Windows;
using System.Windows.Input;

namespace FrontolFileAnalyzer;

public partial class GoToLineWindow : Window
{
    private readonly int _maximum;

    public GoToLineWindow(int maximum, int current)
    {
        _maximum = Math.Max(1, maximum);
        InitializeComponent();
        PromptText.Text = $"Номер физической строки (1–{_maximum:N0})";
        LineNumberBox.Text = Math.Clamp(current, 1, _maximum).ToString();
        Loaded += (_, _) => { LineNumberBox.Focus(); LineNumberBox.SelectAll(); };
    }

    public int LineNumber { get; private set; }

    private void Go_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(LineNumberBox.Text.Trim(), out var value) || value < 1 || value > _maximum)
        {
            PromptText.Text = $"Введите число от 1 до {_maximum:N0}.";
            PromptText.Foreground = FindResource("ErrorBrush") as System.Windows.Media.Brush;
            LineNumberBox.Focus();
            LineNumberBox.SelectAll();
            return;
        }

        LineNumber = value;
        DialogResult = true;
    }

    private void LineNumberBox_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = e.Text.Any(character => !char.IsDigit(character));
}
