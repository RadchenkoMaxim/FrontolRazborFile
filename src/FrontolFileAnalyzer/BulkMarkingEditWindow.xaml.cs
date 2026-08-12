using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace FrontolFileAnalyzer;

public partial class BulkMarkingEditWindow : Window, INotifyPropertyChanged
{
    private readonly ObservableCollection<SelectableSourceOption> _sourceOptions;
    private readonly Dictionary<string, BulkMarkingTargetOption> _targetsByCode;
    private string _targetCodeInput = string.Empty;
    private string _targetHint = string.Empty;
    private string _selectionSummary = string.Empty;
    private string _validationMessage = string.Empty;
    private bool _isCustomTarget;
    private bool _isBulkSelectionChange;

    public BulkMarkingEditWindow(
        IEnumerable<BulkMarkingSourceOption> sourceOptions,
        IEnumerable<BulkMarkingTargetOption> targetOptions)
    {
        ArgumentNullException.ThrowIfNull(sourceOptions);
        ArgumentNullException.ThrowIfNull(targetOptions);

        _sourceOptions = new ObservableCollection<SelectableSourceOption>(
            NormalizeSourceOptions(sourceOptions)
                .Select(option => new SelectableSourceOption(
                    option.Code,
                    option.Name,
                    option.Count,
                    SelectionChanged)));

        TargetOptions = NormalizeTargetOptions(targetOptions);
        _targetsByCode = TargetOptions.ToDictionary(
            option => option.Code,
            StringComparer.OrdinalIgnoreCase);

        UpdateSelectionSummary();
        UpdateTargetHint();

        InitializeComponent();
        DataContext = this;
        SourceOptionsList.ItemsSource = _sourceOptions;
        SourceInitialized += (_, _) => WindowBoundsHelper.ConstrainToOwnerWorkingArea(this);

    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<BulkMarkingTargetOption> TargetOptions { get; }

    /// <summary>
    /// Коды выбранных исходных групп. Результат следует читать при DialogResult == true.
    /// </summary>
    public HashSet<string> SourceCodes { get; private set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Целевой код из справочника или введённое пользователем произвольное значение.
    /// Результат следует читать при DialogResult == true.
    /// </summary>
    public string TargetCode { get; private set; } = string.Empty;

    /// <summary>
    /// Суммарное количество товаров во всех выбранных исходных группах.
    /// Результат следует читать при DialogResult == true.
    /// </summary>
    public int AffectedCount { get; private set; }

    public string TargetCodeInput
    {
        get => _targetCodeInput;
        set
        {
            var newValue = value ?? string.Empty;
            if (_targetCodeInput == newValue)
            {
                return;
            }

            _targetCodeInput = newValue;
            OnPropertyChanged();
            ClearValidation();
            UpdateTargetHint();
        }
    }

    public string TargetHint
    {
        get => _targetHint;
        private set
        {
            if (_targetHint == value)
            {
                return;
            }

            _targetHint = value;
            OnPropertyChanged();
        }
    }

    public bool IsCustomTarget
    {
        get => _isCustomTarget;
        private set
        {
            if (_isCustomTarget == value)
            {
                return;
            }

            _isCustomTarget = value;
            OnPropertyChanged();
        }
    }

    public string SelectionSummary
    {
        get => _selectionSummary;
        private set
        {
            if (_selectionSummary == value)
            {
                return;
            }

            _selectionSummary = value;
            OnPropertyChanged();
        }
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (_validationMessage == value)
            {
                return;
            }

            _validationMessage = value;
            OnPropertyChanged();
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => SetAllSelected(true);

    private void ClearAll_Click(object sender, RoutedEventArgs e) => SetAllSelected(false);

    private void SetAllSelected(bool isSelected)
    {
        _isBulkSelectionChange = true;
        try
        {
            foreach (var option in _sourceOptions)
            {
                option.IsSelected = isSelected;
            }
        }
        finally
        {
            _isBulkSelectionChange = false;
        }

        UpdateSelectionSummary();
    }

    private void SelectionChanged()
    {
        if (!_isBulkSelectionChange)
        {
            UpdateSelectionSummary();
        }
    }

    private void UpdateSelectionSummary()
    {
        var selected = _sourceOptions.Where(option => option.IsSelected).ToArray();
        var affectedCount = selected.Sum(option => option.Count);
        SelectionSummary = $"Выбрано групп: {selected.Length:N0} · товаров: {affectedCount:N0}";
        ClearValidation();
    }

    private void UpdateTargetHint()
    {
        var code = TargetCodeInput.Trim();
        if (code.Length == 0)
        {
            IsCustomTarget = false;
            TargetHint = TargetOptions.Count == 0
                ? "Справочник пуст. Введите целевой код вручную."
                : "Выберите значение из справочника или введите код вручную.";
            return;
        }

        if (_targetsByCode.TryGetValue(code, out var target))
        {
            IsCustomTarget = false;
            TargetHint = $"Справочник: {target.Name}";
            return;
        }

        IsCustomTarget = true;
        TargetHint = "Такого кода нет в справочнике. Он будет применён как произвольное значение.";
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var selected = _sourceOptions.Where(option => option.IsSelected).ToArray();
        var targetCode = TargetCodeInput.Trim();

        if (selected.Length == 0 && targetCode.Length == 0)
        {
            ValidationMessage = "Выберите хотя бы одну исходную группу и укажите целевой код.";
            return;
        }

        if (selected.Length == 0)
        {
            ValidationMessage = "Выберите хотя бы одну исходную группу.";
            SourceOptionsList.Focus();
            return;
        }

        if (targetCode.Length == 0)
        {
            ValidationMessage = "Укажите целевой код маркировки.";
            TargetCodeBox.Focus();
            return;
        }

        if (targetCode.Contains(';') || targetCode.Contains('\r') || targetCode.Contains('\n'))
        {
            ValidationMessage = "Код не должен содержать точку с запятой или перевод строки.";
            TargetCodeBox.Focus();
            return;
        }

        SourceCodes = selected
            .Select(option => option.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        TargetCode = targetCode;
        AffectedCount = selected.Sum(option => option.Count);
        DialogResult = true;
    }

    private void ClearValidation()
    {
        if (ValidationMessage.Length > 0)
        {
            ValidationMessage = string.Empty;
        }
    }

    private static IReadOnlyList<BulkMarkingSourceOption> NormalizeSourceOptions(
        IEnumerable<BulkMarkingSourceOption> options) =>
        options
            .Where(option => option is not null && !string.IsNullOrWhiteSpace(option.Code))
            .GroupBy(option => option.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var count = group.Sum(option => (long)Math.Max(0, option.Count));
                return new BulkMarkingSourceOption(
                    first.Code.Trim(),
                    NormalizeName(first.Name),
                    (int)Math.Min(int.MaxValue, count));
            })
            .OrderBy(option => ParseNumericCode(option.Code))
            .ThenBy(option => option.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<BulkMarkingTargetOption> NormalizeTargetOptions(
        IEnumerable<BulkMarkingTargetOption> options) =>
        options
            .Where(option => option is not null && !string.IsNullOrWhiteSpace(option.Code))
            .GroupBy(option => option.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return new BulkMarkingTargetOption(first.Code.Trim(), NormalizeName(first.Name));
            })
            .OrderBy(option => ParseNumericCode(option.Code))
            .ThenBy(option => option.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string NormalizeName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "Без названия" : name.Trim();

    private static long ParseNumericCode(string code) =>
        long.TryParse(code, out var numericCode) ? numericCode : long.MaxValue;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class SelectableSourceOption : INotifyPropertyChanged
    {
        private readonly Action _selectionChanged;
        private bool _isSelected;

        public SelectableSourceOption(string code, string name, int count, Action selectionChanged)
        {
            Code = code;
            Name = name;
            Count = count;
            _selectionChanged = selectionChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Code { get; }
        public string Name { get; }
        public int Count { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                _selectionChanged();
            }
        }
    }
}

public sealed record BulkMarkingSourceOption(string Code, string Name, int Count);

public sealed record BulkMarkingTargetOption(string Code, string Name);
