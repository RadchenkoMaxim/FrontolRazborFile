using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FrontolFileAnalyzer.Core;

namespace FrontolFileAnalyzer;

public partial class CommandReferenceWindow : Window, INotifyPropertyChanged
{
    private string _searchText = string.Empty;
    private CommandDefinition? _selectedCommand;
    private CommandVariant? _selectedVariant;
    private IReadOnlyList<CommandVariant> _availableVariants = [];
    private IReadOnlyList<FieldDefinition> _displayedFields = [];

    public CommandReferenceWindow()
    {
        InitializeComponent();
        CommandsView = CollectionViewSource.GetDefaultView(FrontolCommandCatalog.All);
        CommandsView.Filter = FilterCommand;
        SelectedCommand = FrontolCommandCatalog.All.FirstOrDefault();
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICollectionView CommandsView { get; }
    public string CoverageText => $"Встроено команд: {FrontolCommandCatalog.All.Count}";
    public IReadOnlyList<CommandVariant> AvailableVariants => _availableVariants;
    public IReadOnlyList<FieldDefinition> DisplayedFields => _displayedFields;
    public bool HasDisplayedFields => _displayedFields.Count > 0;

    public CommandDefinition? SelectedCommand
    {
        get => _selectedCommand;
        set
        {
            if (Equals(_selectedCommand, value))
            {
                return;
            }

            _selectedCommand = value;
            OnPropertyChanged();
            _availableVariants = value?.Variants ?? [];
            OnPropertyChanged(nameof(AvailableVariants));
            SelectedVariant = _availableVariants.FirstOrDefault();
            if (_availableVariants.Count == 0)
            {
                SetDisplayedFields(value?.Fields ?? []);
            }
        }
    }

    public CommandVariant? SelectedVariant
    {
        get => _selectedVariant;
        set
        {
            if (Equals(_selectedVariant, value))
            {
                return;
            }

            _selectedVariant = value;
            OnPropertyChanged();
            SetDisplayedFields(value?.Fields ?? SelectedCommand?.Fields ?? []);
        }
    }

    private bool FilterCommand(object item)
    {
        if (item is not CommandDefinition command || _searchText.Length == 0)
        {
            return true;
        }

        return command.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
               command.DisplayName.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase) ||
               command.Description.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized || DataContext is null)
        {
            return;
        }

        _searchText = SearchBox.Text.Trim();
        CommandsView.Refresh();
        SelectedCommand = CommandsView.Cast<CommandDefinition>().FirstOrDefault();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void SetDisplayedFields(IReadOnlyList<FieldDefinition> fields)
    {
        _displayedFields = fields;
        OnPropertyChanged(nameof(DisplayedFields));
        OnPropertyChanged(nameof(HasDisplayedFields));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
