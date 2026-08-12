using System.Collections.ObjectModel;
using System.Windows;

namespace FrontolFileAnalyzer;

public partial class RecordColumnsWindow : Window
{
    public RecordColumnsWindow(IEnumerable<RecordColumnOption> options)
    {
        Options = new ObservableCollection<RecordColumnOption>(options.Select(option =>
            new RecordColumnOption(option.Key, option.Header, option.IsVisible)));
        InitializeComponent();
        DataContext = this;
    }

    public ObservableCollection<RecordColumnOption> Options { get; }
    public bool ResetWidths { get; private set; }

    private void Reset_Click(object sender, RoutedEventArgs e) => ResetWidths = true;
    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}

public sealed class RecordColumnOption(string key, string header, bool isVisible)
{
    public string Key { get; } = key;
    public string Header { get; } = header;
    public bool IsVisible { get; set; } = isVisible;
}
