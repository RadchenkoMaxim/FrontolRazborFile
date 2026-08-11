using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FrontolFileAnalyzer.Core;

namespace FrontolFileAnalyzer;

public sealed class FieldVisibilityOption : INotifyPropertyChanged
{
    private bool _isVisible;

    public required int Number { get; init; }
    public required string Name { get; init; }
    public required bool IsFilled { get; init; }
    public string StateText => IsFilled ? "заполнено" : "пусто";

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value)
            {
                return;
            }

            _isVisible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class FieldVisibilityWindow : Window
{
    private readonly ObservableCollection<FieldVisibilityOption> _fields;
    private string _searchText = string.Empty;

    public FieldVisibilityWindow(string commandName, IReadOnlyList<AnalyzedField> fields, IReadOnlySet<int> hiddenNumbers)
    {
        CommandTitle = $"Поля команды $$${commandName}";
        _fields = new ObservableCollection<FieldVisibilityOption>(fields.Select(field => new FieldVisibilityOption
        {
            Number = field.Number,
            Name = field.Name,
            IsFilled = !string.IsNullOrEmpty(field.RawValue),
            IsVisible = !hiddenNumbers.Contains(field.Number)
        }));
        FieldsView = CollectionViewSource.GetDefaultView(_fields);
        FieldsView.Filter = FilterField;
        InitializeComponent();
        DataContext = this;
    }

    public string CommandTitle { get; }
    public ICollectionView FieldsView { get; }
    public IReadOnlySet<int> HiddenNumbers => _fields.Where(item => !item.IsVisible).Select(item => item.Number).ToHashSet();

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = (sender as TextBox)?.Text?.Trim() ?? string.Empty;
        FieldsView.Refresh();
    }

    private bool FilterField(object item) => item is FieldVisibilityOption field &&
        (_searchText.Length == 0 || field.Number.ToString().Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
         field.Name.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase));

    private void SelectAll_Click(object sender, RoutedEventArgs e) => SetVisible(_ => true);
    private void SelectNone_Click(object sender, RoutedEventArgs e) => SetVisible(_ => false);
    private void OnlyFilled_Click(object sender, RoutedEventArgs e) => SetVisible(field => field.IsFilled);

    private void SetVisible(Func<FieldVisibilityOption, bool> selector)
    {
        foreach (var field in _fields)
        {
            field.IsVisible = selector(field);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
