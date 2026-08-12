using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using FrontolFileAnalyzer.Core;

namespace FrontolFileAnalyzer;

public partial class FieldEditWindow : Window, INotifyPropertyChanged
{
    private static readonly string[] ProductFlagNames =
    [
        "Дробное количество", "Продажа", "Возврат", "Отрицательные остатки", "Без ввода количества",
        "Списание остатков", "Редактирование цены", "Ручной ввод количества", "Печать в документе",
        "Наливаемый товар", "Скидки", "Запрос цены", "Запрос штрихкода", "Округление", "Деление упаковки"
    ];

    private readonly EditorMode _mode;
    private string _simpleValue;
    private ChoiceItem? _selectedChoice;

    public FieldEditWindow(int lineNumber, AnalyzedField field, FieldDefinition? definition)
    {
        ArgumentNullException.ThrowIfNull(field);
        _simpleValue = field.RawValue;
        FieldTitle = $"{field.Number}. {field.Name}";
        FieldMetadata = $"Физическая строка {lineNumber:N0} · тип: {field.DataType} · обязательное: {field.RequiredText}";
        Purpose = field.Purpose;
        CurrentDisplayValue = !field.WasProvided
            ? "не передано"
            : string.IsNullOrEmpty(field.RawValue) ? "передано пустым" : field.RawValue;

        if (string.Equals(field.Name, "Флаги товара", StringComparison.OrdinalIgnoreCase))
        {
            _mode = EditorMode.Flags;
            var values = field.RawValue.Split(',', StringSplitOptions.TrimEntries);
            Flags = ProductFlagNames.Select((name, index) =>
                new FlagItem(index + 1, name, index < values.Length && values[index] == "1")).ToArray();
            Choices = [];
            EditorHint = "Каждый переключатель соответствует позиции в поле «Флаги товара».";
        }
        else if (definition?.Values is { Count: > 0 } values)
        {
            _mode = EditorMode.Choice;
            var choices = values
                .Select(pair => new ChoiceItem(pair.Key, pair.Value))
                .OrderBy(item => int.TryParse(item.Code, out var number) ? number : int.MaxValue)
                .ThenBy(item => item.Code, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            choices.Insert(0, new ChoiceItem(string.Empty,
                definition.DefaultValue is null ? "Не передавать значение" : $"Не передавать — по умолчанию: {definition.DefaultValue}"));
            if (field.RawValue.Length > 0 && choices.All(item => item.Code != field.RawValue))
            {
                choices.Add(new ChoiceItem(field.RawValue, "Текущее нестандартное значение"));
            }
            Choices = choices;
            SelectedChoice = choices.First(item => item.Code == field.RawValue);
            Flags = [];
            EditorHint = "Выберите допустимое значение из справочника Frontol.";
        }
        else
        {
            _mode = EditorMode.Text;
            Choices = [];
            Flags = [];
            EditorHint = "Оставьте поле пустым, если значение не должно передаваться.";
        }

        InitializeComponent();
        DataContext = this;
        SourceInitialized += (_, _) => WindowBoundsHelper.ConstrainToOwnerWorkingArea(this);
        SimpleValueText.Visibility = _mode == EditorMode.Text ? Visibility.Visible : Visibility.Collapsed;
        ChoiceBox.Visibility = _mode == EditorMode.Choice ? Visibility.Visible : Visibility.Collapsed;
        FlagsScroller.Visibility = _mode == EditorMode.Flags ? Visibility.Visible : Visibility.Collapsed;
        if (_mode == EditorMode.Flags)
        {
            Height = 560;
            MinHeight = 360;
        }
        Loaded += (_, _) =>
        {
            if (_mode == EditorMode.Text)
            {
                SimpleValueText.Focus();
                SimpleValueText.SelectAll();
            }
            else if (_mode == EditorMode.Choice)
            {
                ChoiceBox.Focus();
                ChoiceBox.IsDropDownOpen = true;
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string FieldTitle { get; }
    public string FieldMetadata { get; }
    public string Purpose { get; }
    public string CurrentDisplayValue { get; }
    public string EditorHint { get; }
    public IReadOnlyList<ChoiceItem> Choices { get; }
    public IReadOnlyList<FlagItem> Flags { get; }

    public string SimpleValue
    {
        get => _simpleValue;
        set { _simpleValue = value; OnPropertyChanged(); }
    }

    public ChoiceItem? SelectedChoice
    {
        get => _selectedChoice;
        set { _selectedChoice = value; OnPropertyChanged(); }
    }

    public string Value => _mode switch
    {
        EditorMode.Choice => SelectedChoice?.Code ?? string.Empty,
        EditorMode.Flags => string.Join(',', Flags.Select(item => item.IsEnabled ? "1" : "0")),
        _ => SimpleValue
    };

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;
        if (Value.Contains(';') || Value.Contains('\r') || Value.Contains('\n'))
        {
            ValidationText.Text = "Значение поля не может содержать точку с запятой или перевод строки.";
            return;
        }
        DialogResult = true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private enum EditorMode { Text, Choice, Flags }
}

public sealed record ChoiceItem(string Code, string Name)
{
    public string DisplayText => Code.Length == 0 ? Name : $"{Code} — {Name}";
}

public sealed class FlagItem(int number, string name, bool isEnabled)
{
    public int Number { get; } = number;
    public string Name { get; } = name;
    public bool IsEnabled { get; set; } = isEnabled;
    public string DisplayText => $"{Number}. {Name}";
}
