using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FrontolFileAnalyzer.Core;

public sealed class FrontolFileParser
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly Regex StringLengthPattern = new(@"Строка\s+(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static FrontolFileParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public AnalysisDocument ParseFile(string filePath, IProgress<FrontolParseProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        progress?.Report(new FrontolParseProgress(0, 0, "Чтение файла"));
        var bytes = File.ReadAllBytes(filePath);
        var (text, encodingName) = Decode(bytes);
        var lines = SplitLines(text);
        var records = new List<ParsedRecord>(lines.Length);
        string? currentCommand = null;

        progress?.Report(new FrontolParseProgress(0, lines.Length, "Разбор строк"));

        for (var index = 0; index < lines.Length; index++)
        {
            var rawLine = lines[index];
            var lineNumber = index + 1;
            var trimmed = rawLine.Trim();

            if (trimmed.Length == 0)
            {
                records.Add(ServiceRecord(lineNumber, FrontolRecordKind.Empty, rawLine, "Пустая строка", "Разделитель или пустая строка"));
                continue;
            }

            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                records.Add(ServiceRecord(lineNumber, FrontolRecordKind.Comment, rawLine, "Комментарий", trimmed));
                continue;
            }

            if (trimmed is "##@@&&" or "#")
            {
                records.Add(ServiceRecord(
                    lineNumber,
                    FrontolRecordKind.Header,
                    rawLine,
                    trimmed == "##@@&&" ? "Сигнатура Frontol" : "Строка заголовка",
                    trimmed == "##@@&&" ? "Начало файла загрузки" : "Служебная строка заголовка"));
                continue;
            }

            if (trimmed.StartsWith("$$$", StringComparison.Ordinal))
            {
                currentCommand = FrontolCommandCatalog.Normalize(trimmed);
                records.Add(CommandRecord(lineNumber, rawLine, currentCommand));
                continue;
            }

            currentCommand ??= "ADDQUANTITY";
            records.Add(DataRecord(lineNumber, rawLine, currentCommand));

            if (index % 100 == 0 || index == lines.Length - 1)
            {
                progress?.Report(new FrontolParseProgress(index + 1, lines.Length, "Разбор строк"));
            }
        }

        progress?.Report(new FrontolParseProgress(lines.Length, lines.Length, "Разбор завершен"));

        return new AnalysisDocument
        {
            FilePath = Path.GetFullPath(filePath),
            EncodingName = encodingName,
            Records = records
        };
    }

    private static ParsedRecord ServiceRecord(
        int lineNumber,
        FrontolRecordKind kind,
        string rawLine,
        string title,
        string summary) => new()
        {
            LineNumber = lineNumber,
            Kind = kind,
            RawText = rawLine,
            Title = title,
            Summary = summary
        };

    private static ParsedRecord CommandRecord(int lineNumber, string rawLine, string commandName)
    {
        if (FrontolCommandCatalog.TryGet(commandName, out var definition))
        {
            return new ParsedRecord
            {
                LineNumber = lineNumber,
                Kind = FrontolRecordKind.Command,
                RawText = rawLine,
                CommandName = definition.Name,
                Definition = definition,
                Title = $"$$${definition.Name}",
                Summary = definition.DisplayName
            };
        }

        return new ParsedRecord
        {
            LineNumber = lineNumber,
            Kind = FrontolRecordKind.Command,
            RawText = rawLine,
            CommandName = commandName,
            Title = $"$$${commandName}",
            Summary = "Команда без встроенного справочника полей",
            Issues = [new AnalysisIssue(IssueSeverity.Warning, "Для этой команды пока нет встроенного описания полей. Данные все равно будут разделены и пронумерованы.")]
        };
    }

    private static ParsedRecord DataRecord(int lineNumber, string rawLine, string commandName)
    {
        var values = rawLine.Split(';', StringSplitOptions.None);
        if (!FrontolCommandCatalog.TryGet(commandName, out var definition))
        {
            var genericFields = values.Select((value, index) => new AnalyzedField
            {
                Number = index + 1,
                Name = $"Поле №{index + 1}",
                RawValue = value,
                WasProvided = true,
                Interpretation = string.IsNullOrEmpty(value) ? "Пустое значение" : "Значение передано",
                Required = false,
                DataType = "Неизвестно",
                Purpose = "Описание отсутствует во встроенном справочнике",
                Severity = IssueSeverity.Warning,
                Diagnostic = "Сверьте поле с разделом команды в руководстве интегратора."
            }).ToArray();

            return new ParsedRecord
            {
                LineNumber = lineNumber,
                Kind = FrontolRecordKind.Data,
                RawText = rawLine,
                CommandName = commandName,
                Title = $"Данные · $$$${commandName}".Replace("$$$$", "$$$"),
                Summary = $"Полей: {values.Length}",
                Fields = genericFields,
                Issues = [new AnalysisIssue(IssueSeverity.Warning, "Команда не распознана; поля только пронумерованы.")]
            };
        }

        var resolvedDefinitions = definition.ResolveFields(values);
        var fieldCount = Math.Max(values.Length, resolvedDefinitions.Count);
        var fields = new List<AnalyzedField>(fieldCount);
        var issues = new List<AnalysisIssue>();

        for (var index = 0; index < fieldCount; index++)
        {
            var number = index + 1;
            var wasProvided = index < values.Length;
            var rawValue = wasProvided ? values[index] : string.Empty;

            if (index >= resolvedDefinitions.Count)
            {
                var trailingEmpty = rawValue.Length == 0 && index == values.Length - 1;
                var extraSeverity = trailingEmpty ? IssueSeverity.Info : IssueSeverity.Error;
                var extraDiagnostic = trailingEmpty
                    ? "Дополнительный пустой сегмент появился из-за завершающей точки с запятой."
                    : "У команды нет поля с таким номером.";
                fields.Add(new AnalyzedField
                {
                    Number = number,
                    Name = trailingEmpty ? "Завершающий пустой сегмент" : "Лишнее поле",
                    RawValue = rawValue,
                    WasProvided = true,
                    Interpretation = trailingEmpty ? "Пустое значение после последней ;" : "Не предусмотрено форматом команды",
                    Required = false,
                    DataType = "—",
                    Purpose = "За пределами формата команды",
                    Severity = extraSeverity,
                    Diagnostic = extraDiagnostic
                });
                issues.Add(new AnalysisIssue(extraSeverity, $"Поле {number}: {extraDiagnostic}"));
                continue;
            }

            var fieldDefinition = resolvedDefinitions[index];
            var (severity, diagnostic) = Validate(fieldDefinition, rawValue);
            if (severity != IssueSeverity.None)
            {
                issues.Add(new AnalysisIssue(severity, $"Поле {number} «{fieldDefinition.Name}»: {diagnostic}"));
            }

            fields.Add(new AnalyzedField
            {
                Number = number,
                Name = fieldDefinition.Name,
                RawValue = rawValue,
                WasProvided = wasProvided,
                Interpretation = Interpret(fieldDefinition, rawValue),
                Required = fieldDefinition.Required,
                DataType = fieldDefinition.DataType,
                Purpose = fieldDefinition.Purpose,
                Severity = severity,
                Diagnostic = diagnostic
            });
        }

        return new ParsedRecord
        {
            LineNumber = lineNumber,
            Kind = FrontolRecordKind.Data,
            RawText = rawLine,
            CommandName = definition.Name,
            Definition = definition,
            Title = $"Данные · $$${definition.Name}",
            Summary = BuildSummary(definition, values, resolvedDefinitions.Count),
            Fields = fields,
            Issues = issues
        };
    }

    private static string BuildSummary(CommandDefinition definition, IReadOnlyList<string> values, int resolvedFieldCount)
    {
        string Get(int number) => number <= values.Count ? values[number - 1] : string.Empty;

        return definition.Name switch
        {
            "ADDQUANTITY" or "REPLACEQUANTITY" or "REPLACEQUANTITYWITHOUTSALE" =>
                $"Товар {Get(1)} · {EmptyAs(Get(3), "без наименования")} · передано полей: {values.Count}/{resolvedFieldCount}",
            "DELETEBARCODESBYWARECODE" =>
                string.IsNullOrEmpty(Get(2)) ? $"Товар {Get(1)} · удалить все штрихкоды" : $"Товар {Get(1)} · удалить {Get(2)}",
            "ADDCLASSIFIERLINKS" =>
                $"Классификатор {Get(1)} → элемент {Get(3)} · передано полей: {values.Count}/{resolvedFieldCount}",
            _ => $"Полей: {values.Count}/{resolvedFieldCount}"
        };
    }

    private static string EmptyAs(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static (IssueSeverity Severity, string Diagnostic) Validate(FieldDefinition definition, string rawValue)
    {
        if (string.IsNullOrEmpty(rawValue))
        {
            return definition.Required
                ? (IssueSeverity.Error, "обязательное значение отсутствует")
                : (IssueSeverity.None, string.Empty);
        }

        var dataType = definition.DataType;
        var maxLengthMatch = StringLengthPattern.Match(dataType);
        if (maxLengthMatch.Success && int.TryParse(maxLengthMatch.Groups[1].Value, out var maxLength) && rawValue.Length > maxLength)
        {
            return (IssueSeverity.Error, $"длина {rawValue.Length} превышает допустимые {maxLength} символов");
        }

        if (!dataType.Contains("Строка", StringComparison.OrdinalIgnoreCase) &&
            dataType.Contains("Целое", StringComparison.OrdinalIgnoreCase) &&
            !long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return (IssueSeverity.Error, "ожидалось целое число");
        }

        if (dataType.Contains("Дробное", StringComparison.OrdinalIgnoreCase) && !IsDecimal(rawValue))
        {
            return (IssueSeverity.Error, "ожидалось дробное число с запятой или точкой");
        }

        if (dataType.Contains("Дата", StringComparison.OrdinalIgnoreCase) &&
            !DateTime.TryParse(rawValue, RussianCulture, DateTimeStyles.None, out _))
        {
            return (IssueSeverity.Error, "ожидалась дата");
        }

        if (definition.Values is not null && !definition.Values.ContainsKey(rawValue))
        {
            return (IssueSeverity.Warning, $"значение «{rawValue}» отсутствует в перечне руководства");
        }

        return (IssueSeverity.None, string.Empty);
    }

    private static bool IsDecimal(string value) =>
        decimal.TryParse(value, NumberStyles.Number, RussianCulture, out _) ||
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _);

    private static string Interpret(FieldDefinition definition, string rawValue)
    {
        if (string.IsNullOrEmpty(rawValue))
        {
            if (definition.DefaultValue is not null)
            {
                var defaultMeaning = InterpretNonEmpty(definition, definition.DefaultValue);
                return string.IsNullOrEmpty(defaultMeaning)
                    ? $"Не передано → по умолчанию: {definition.DefaultValue}"
                    : $"Не передано → по умолчанию: {definition.DefaultValue} · {defaultMeaning}";
            }

            var emptyMeaning = definition.CustomInterpreter?.Invoke(string.Empty);
            return string.IsNullOrEmpty(emptyMeaning) ? "Не передано" : emptyMeaning;
        }

        return InterpretNonEmpty(definition, rawValue) ?? "Значение передано";
    }

    private static string? InterpretNonEmpty(FieldDefinition definition, string value)
    {
        if (definition.Values is not null && definition.Values.TryGetValue(value, out var mappedValue))
        {
            return mappedValue;
        }

        return definition.CustomInterpreter?.Invoke(value);
    }

    private static (string Text, string EncodingName) Decode(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()))
        {
            return (Encoding.UTF8.GetString(bytes.AsSpan(Encoding.UTF8.GetPreamble().Length)), "UTF-8 с BOM");
        }

        if (bytes.AsSpan().StartsWith(Encoding.Unicode.GetPreamble()))
        {
            return (Encoding.Unicode.GetString(bytes.AsSpan(Encoding.Unicode.GetPreamble().Length)), "UTF-16 LE");
        }

        try
        {
            var strictUtf8 = new UTF8Encoding(false, true);
            return (strictUtf8.GetString(bytes), "UTF-8");
        }
        catch (DecoderFallbackException)
        {
            var windows1251 = Encoding.GetEncoding(1251);
            return (windows1251.GetString(bytes), "Windows-1251");
        }
    }

    private static string[] SplitLines(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            return lines[..^1];
        }

        return lines;
    }
}
