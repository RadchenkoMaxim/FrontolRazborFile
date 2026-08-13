using System.ComponentModel;

namespace FrontolFileAnalyzer.Core;

public enum IssueSeverity
{
    None,
    Info,
    Warning,
    Error
}

public enum FrontolRecordKind
{
    Header,
    Command,
    Data,
    Comment,
    Empty
}

public enum ExchangeFileKind
{
    UploadToFrontol,
    SalesReportFromFrontol
}

public static class ExchangeFileKindExtensions
{
    public static string DisplayName(this ExchangeFileKind kind) => kind switch
    {
        ExchangeFileKind.SalesReportFromFrontol => "Отчёт о продажах: Frontol → учётная система",
        _ => "Файл загрузки: учётная система → Frontol"
    };
}

public sealed record AnalysisIssue(IssueSeverity Severity, string Message);

public sealed record FrontolParseProgress(int ProcessedLines, int TotalLines, string Stage)
{
    public int Percent => TotalLines <= 0 ? 0 : (int)Math.Round(ProcessedLines * 100d / TotalLines);
}

public sealed record FieldDefinition(
    int Number,
    string Name,
    bool Required,
    string DataType,
    string Purpose,
    string? DefaultValue = null,
    IReadOnlyDictionary<string, string>? Values = null,
    Func<string, string?>? CustomInterpreter = null,
    bool AllowUnknownValues = false)
{
    public string RequiredText => Required ? "Да" : "Нет";
}

public sealed record CommandVariant(
    string Key,
    string DisplayName,
    IReadOnlyList<FieldDefinition> Fields)
{
    public string DisplayText => $"{Key} — {DisplayName}";
}

public sealed record CommandDefinition(
    string Name,
    string DisplayName,
    string Description,
    string ManualReference,
    IReadOnlyList<FieldDefinition> Fields,
    IReadOnlyList<CommandVariant>? Variants = null,
    int? VariantFieldNumber = null,
    string SyntaxPrefix = "$$$",
    string? Category = null)
{
    public bool HasVariants => Variants is { Count: > 0 };
    public bool HasFields => Fields.Count > 0 || HasVariants;
    public int MaximumFieldCount => HasVariants ? Variants!.Max(variant => variant.Fields.Count) : Fields.Count;
    public string CommandText => $"{SyntaxPrefix}{Name}";
    public string FieldCountText => HasVariants
        ? $"Вариантов: {Variants!.Count} · до {MaximumFieldCount} полей"
        : HasFields ? $"Полей: {Fields.Count}" : "Данных нет";

    public IReadOnlyList<FieldDefinition> ResolveFields(IReadOnlyList<string> values)
    {
        if (!HasVariants || VariantFieldNumber is null || VariantFieldNumber <= 0 || values.Count < VariantFieldNumber)
        {
            return Fields;
        }

        var key = values[VariantFieldNumber.Value - 1];
        return Variants!.FirstOrDefault(variant => string.Equals(variant.Key, key, StringComparison.OrdinalIgnoreCase))?.Fields
               ?? Fields;
    }
}

public sealed class AnalyzedField
{
    public required int Number { get; init; }
    public required string Name { get; init; }
    public required string RawValue { get; init; }
    public required bool WasProvided { get; init; }
    public required string Interpretation { get; init; }
    public required bool Required { get; init; }
    public required string DataType { get; init; }
    public required string Purpose { get; init; }
    public required IssueSeverity Severity { get; init; }
    public required string Diagnostic { get; init; }

    public string RequiredText => Required ? "Да" : "Нет";

    public string SourceText => WasProvided
        ? string.IsNullOrEmpty(RawValue) ? "Передано пустым" : "Передано"
        : "Не передано";

    public string StatusText => Severity switch
    {
        IssueSeverity.Error => "Ошибка",
        IssueSeverity.Warning => "Внимание",
        IssueSeverity.Info => "Инфо",
        _ => "ОК"
    };

    public bool HasExtendedInterpretation =>
        !string.IsNullOrWhiteSpace(Interpretation) &&
        !string.Equals(Interpretation, "Не передано", StringComparison.Ordinal) &&
        !string.Equals(Interpretation, "Значение передано", StringComparison.Ordinal) &&
        !string.Equals(Interpretation, "Пустое значение", StringComparison.Ordinal);

    public string DetailInterpretation => HasExtendedInterpretation
        ? Interpretation.Replace(" · ", Environment.NewLine, StringComparison.Ordinal)
        : string.Empty;

    public bool IsValueEmpty => string.IsNullOrEmpty(RawValue);
    public string DisplayValue => !WasProvided ? "не передано" : IsValueEmpty ? "пусто" : RawValue;
}

public sealed class ParsedRecord : INotifyPropertyChanged
{
    private bool _isModified;
    private IReadOnlyList<AnalyzedField>? _fields;
    private string? _searchText;

    public event PropertyChangedEventHandler? PropertyChanged;
    public required int LineNumber { get; init; }
    public required FrontolRecordKind Kind { get; init; }
    public required string RawText { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public ExchangeFileKind FileKind { get; init; } = ExchangeFileKind.UploadToFrontol;
    public bool HasTerminatingDelimiter { get; init; }
    public string? CommandName { get; init; }
    public CommandDefinition? Definition { get; init; }
    public IReadOnlyList<AnalyzedField> Fields
    {
        get => _fields ??= FieldFactory?.Invoke() ?? [];
        init => _fields = value;
    }
    internal Func<IReadOnlyList<AnalyzedField>>? FieldFactory { get; init; }
    internal IReadOnlyList<string>? RawValues { get; init; }
    internal int FieldCount { get; init; }
    internal IssueSeverity FieldSeverity { get; init; }
    public IReadOnlyList<AnalysisIssue> Issues { get; init; } = [];
    public bool IsModified
    {
        get => _isModified;
        set
        {
            if (_isModified == value)
            {
                return;
            }
            _isModified = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsModified)));
        }
    }

    public IssueSeverity Severity
    {
        get
        {
            var issueSeverity = Issues.Count == 0 ? IssueSeverity.None : Issues.Max(issue => issue.Severity);
            var fieldSeverity = FieldSeverity;
            if (fieldSeverity == IssueSeverity.None && _fields is { Count: > 0 })
            {
                fieldSeverity = _fields.Max(item => item.Severity);
            }
            return issueSeverity > fieldSeverity ? issueSeverity : fieldSeverity;
        }
    }

    public string KindText => Kind switch
    {
        FrontolRecordKind.Header => "Заголовок",
        FrontolRecordKind.Command => "Команда",
        FrontolRecordKind.Data when IsSalesReport => "Транзакция",
        FrontolRecordKind.Data => "Данные",
        FrontolRecordKind.Comment => "Комментарий",
        _ => "Пустая строка"
    };

    public string StatusText => Severity switch
    {
        IssueSeverity.Error => "Ошибка",
        IssueSeverity.Warning => "Внимание",
        IssueSeverity.Info => "Инфо",
        _ => "ОК"
    };

    public bool IsSalesReport => FileKind == ExchangeFileKind.SalesReportFromFrontol;

    public string CommandText => string.IsNullOrEmpty(CommandName)
        ? string.Empty
        : Definition?.CommandText ?? (IsSalesReport ? $"№{CommandName}" : $"$$${CommandName}");

    public string CodeText => RawValueAt(IsSalesReport ? 8 : 1);

    public string BarcodeText => RawValueAt(IsSalesReport ? 19 : 2);

    public string PriceText => RawValueAt(IsSalesReport ? 10 : 5);
    public string ProductNameText => IsSalesReport ? Definition?.DisplayName ?? string.Empty : RawValueAt(3);

    public string CodeLabel => IsSalesReport ? "Идентификатор: " : "Код: ";
    public string ProductNameLabel => IsSalesReport ? "Транзакция: " : "Наименование: ";

    public string CodeDisplayText => IsProductRecord ? EmptyAsNotProvided(CodeText) : CodeText;
    public string BarcodeDisplayText => IsProductRecord ? EmptyAsNotProvided(BarcodeText) : BarcodeText;
    public string PriceDisplayText => IsProductRecord ? EmptyAsNotProvided(PriceText) : PriceText;
    public string ProductNameDisplayText => IsProductRecord ? EmptyAsNotProvided(ProductNameText) : ProductNameText;

    public bool IsProductCommand => IsSalesReport
        ? FrontolSalesTransactionCatalog.IsProductTransaction(CommandName)
        : CommandName?.Contains("QUANTITY", StringComparison.OrdinalIgnoreCase) == true;

    public bool IsProductRecord =>
        Kind == FrontolRecordKind.Data &&
        IsProductCommand &&
        (IsSalesReport || FieldCount >= 55);

    public string SectionGroup => Kind switch
    {
        FrontolRecordKind.Header or FrontolRecordKind.Comment or FrontolRecordKind.Empty => "0|Структура файла",
        _ when IsSalesReport && Definition?.Category is { Length: > 0 } category => category,
        _ when IsSalesReport => "4|Прочие транзакции",
        _ when IsProductCommand => "1|Товары",
        _ => "2|Служебные команды"
    };

    public string CommandGroup => Kind switch
    {
        FrontolRecordKind.Header or FrontolRecordKind.Comment or FrontolRecordKind.Empty => $"{KindText}",
        _ when !string.IsNullOrWhiteSpace(CommandName) && Definition is not null => $"{Definition.CommandText} — {Definition.DisplayName}",
        _ when !string.IsNullOrWhiteSpace(CommandName) && IsSalesReport => $"№{CommandName} — назначение не описано",
        _ when !string.IsNullOrWhiteSpace(CommandName) => $"$$${CommandName} — назначение не описано",
        _ => "Без команды"
    };

    public string? ProductTypeCode
    {
        get
        {
            if (!IsProductRecord)
            {
                return null;
            }

            var value = RawValueAt(IsSalesReport ? 32 : 55).Trim();
            return value.Length == 0 ? "0" : value;
        }
    }

    public string ProductTypeText => ProductTypeCode is { } code
        ? FrontolReferenceCatalog.ProductTypeValues.TryGetValue(code, out var name) ? name : $"Код {code}"
        : string.Empty;

    public string ProductTypeDisplayText => IsProductRecord ? EmptyAsNotProvided(ProductTypeText) : ProductTypeText;

    public string ContentText
    {
        get
        {
            if (Kind == FrontolRecordKind.Data && IsSalesReport)
            {
                return Summary;
            }

            if (Kind == FrontolRecordKind.Data &&
                IsProductCommand &&
                FieldCount >= 3)
            {
                return EmptyAsNotProvided(ProductNameText);
            }

            return Kind == FrontolRecordKind.Data ? Summary : Title;
        }
    }

    public string DetailText => Issues.Count == 0
        ? BuildDetailText()
        : string.Join(Environment.NewLine, Issues.Select(issue => $"• {issue.Message}"));

    public string SearchText => _searchText ??= string.Join(' ', new[]
        { LineNumber.ToString(), KindText, CommandName, Title, Summary, RawText, CodeText, ContentText, ProductTypeText, BarcodeText, PriceText }
        .Where(value => !string.IsNullOrWhiteSpace(value)));

    public string GetRawValue(int number) => RawValueAt(number);

    private static string EmptyAsNotProvided(string value) => string.IsNullOrEmpty(value) ? "не передано" : value;

    private string BuildDetailText()
    {
        var description = Definition?.Description ?? "Служебная строка файла обмена.";
        if (!IsSalesReport || Kind != FrontolRecordKind.Data)
        {
            return description;
        }

        return HasTerminatingDelimiter
            ? $"{description}{Environment.NewLine}Строка заканчивается техническим разделителем «;»; он не является полем №45."
            : $"{description}{Environment.NewLine}Завершающий разделитель «;» в исходной строке отсутствует; поля при этом разобраны по позициям.";
    }

    private string RawValueAt(int number)
    {
        if (Kind != FrontolRecordKind.Data || number <= 0)
        {
            return string.Empty;
        }

        if (RawValues is { } values)
        {
            return number <= values.Count ? values[number - 1] : string.Empty;
        }

        return number <= Fields.Count ? Fields[number - 1].RawValue : string.Empty;
    }
}

public sealed class AnalysisDocument
{
    public required string FilePath { get; init; }
    public required string EncodingName { get; init; }
    public required IReadOnlyList<ParsedRecord> Records { get; init; }
    public ExchangeFileKind FileKind { get; init; } = ExchangeFileKind.UploadToFrontol;

    public string FileKindText => FileKind.DisplayName();

    public int CommandCount => Records.Count(record => record.Kind == FrontolRecordKind.Command);
    public int DataRecordCount => Records.Count(record => record.Kind == FrontolRecordKind.Data);
    public int ErrorCount => Records.Count(record => record.Severity == IssueSeverity.Error);
    public int WarningCount => Records.Count(record => record.Severity == IssueSeverity.Warning);
}
