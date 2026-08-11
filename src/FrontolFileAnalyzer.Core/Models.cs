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
    Func<string, string?>? CustomInterpreter = null)
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
    int? VariantFieldNumber = null)
{
    public bool HasVariants => Variants is { Count: > 0 };
    public bool HasFields => Fields.Count > 0 || HasVariants;
    public int MaximumFieldCount => HasVariants ? Variants!.Max(variant => variant.Fields.Count) : Fields.Count;
    public string CommandText => $"$$${Name}";
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
        ? string.IsNullOrEmpty(RawValue) ? "Пусто" : "Передано"
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
}

public sealed class ParsedRecord
{
    public required int LineNumber { get; init; }
    public required FrontolRecordKind Kind { get; init; }
    public required string RawText { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public string? CommandName { get; init; }
    public CommandDefinition? Definition { get; init; }
    public IReadOnlyList<AnalyzedField> Fields { get; init; } = [];
    public IReadOnlyList<AnalysisIssue> Issues { get; init; } = [];

    public IssueSeverity Severity
    {
        get
        {
            var issueSeverity = Issues.Count == 0 ? IssueSeverity.None : Issues.Max(issue => issue.Severity);
            var fieldSeverity = Fields.Count == 0 ? IssueSeverity.None : Fields.Max(item => item.Severity);
            return issueSeverity > fieldSeverity ? issueSeverity : fieldSeverity;
        }
    }

    public string KindText => Kind switch
    {
        FrontolRecordKind.Header => "Заголовок",
        FrontolRecordKind.Command => "Команда",
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

    public string CommandText => string.IsNullOrEmpty(CommandName) ? string.Empty : $"$$${CommandName}";

    public string CodeText => Kind == FrontolRecordKind.Data && Fields.Count > 0 ? Fields[0].RawValue : string.Empty;

    public string ContentText
    {
        get
        {
            if (Kind == FrontolRecordKind.Data &&
                CommandName?.Contains("QUANTITY", StringComparison.OrdinalIgnoreCase) == true &&
                Fields.Count >= 3 &&
                !string.IsNullOrEmpty(Fields[2].RawValue))
            {
                return Fields[2].RawValue;
            }

            return Kind == FrontolRecordKind.Data ? Summary : Title;
        }
    }

    public string DetailText => Issues.Count == 0
        ? Definition?.Description ?? "Служебная строка файла обмена."
        : string.Join(Environment.NewLine, Issues.Select(issue => $"• {issue.Message}"));

    public string SearchText => string.Join(' ', new[] { LineNumber.ToString(), KindText, CommandName, Title, Summary, RawText }
        .Where(value => !string.IsNullOrWhiteSpace(value)));
}

public sealed class AnalysisDocument
{
    public required string FilePath { get; init; }
    public required string EncodingName { get; init; }
    public required IReadOnlyList<ParsedRecord> Records { get; init; }

    public int CommandCount => Records.Count(record => record.Kind == FrontolRecordKind.Command);
    public int DataRecordCount => Records.Count(record => record.Kind == FrontolRecordKind.Data);
    public int ErrorCount => Records.Count(record => record.Severity == IssueSeverity.Error);
    public int WarningCount => Records.Count(record => record.Severity == IssueSeverity.Warning);
}
