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
        return ParseBytes(filePath, bytes, progress);
    }

    public AnalysisDocument ParseBytes(
        string filePath,
        byte[] bytes,
        IProgress<FrontolParseProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(bytes);

        var (text, encodingName) = Decode(bytes);
        var lines = SplitLines(text);
        return ParseLines(filePath, lines, encodingName, progress);
    }

    public AnalysisDocument ParseLines(
        string filePath,
        IReadOnlyList<string> lines,
        string encodingName,
        IProgress<FrontolParseProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(lines);

        if (LooksLikeSalesReport(lines))
        {
            return ParseSalesReportLines(filePath, lines, encodingName, progress);
        }

        var records = new List<ParsedRecord>(lines.Count);
        string? currentCommand = null;

        progress?.Report(new FrontolParseProgress(0, lines.Count, "Разбор строк"));

        for (var index = 0; index < lines.Count; index++)
        {
            var rawLine = lines[index];
            var lineNumber = index + 1;
            var trimmed = rawLine.Trim();

            if (trimmed.Length == 0)
            {
                records.Add(ServiceRecord(lineNumber, FrontolRecordKind.Empty, rawLine, "Пустая строка", "Разделитель или пустая строка"));
                ReportProgress(progress, index, lines.Count);
                continue;
            }

            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                records.Add(ServiceRecord(lineNumber, FrontolRecordKind.Comment, rawLine, "Комментарий", trimmed));
                ReportProgress(progress, index, lines.Count);
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
                ReportProgress(progress, index, lines.Count);
                continue;
            }

            if (trimmed.StartsWith("$$$", StringComparison.Ordinal))
            {
                currentCommand = FrontolCommandCatalog.Normalize(trimmed);
                records.Add(CommandRecord(lineNumber, rawLine, currentCommand));
                ReportProgress(progress, index, lines.Count);
                continue;
            }

            currentCommand ??= "ADDQUANTITY";
            records.Add(DataRecord(lineNumber, rawLine, currentCommand));

            ReportProgress(progress, index, lines.Count);
        }

        progress?.Report(new FrontolParseProgress(lines.Count, lines.Count, "Разбор завершен"));

        return new AnalysisDocument
        {
            FilePath = Path.GetFullPath(filePath),
            EncodingName = encodingName,
            Records = records,
            FileKind = ExchangeFileKind.UploadToFrontol
        };
    }

    private static bool LooksLikeSalesReport(IReadOnlyList<string> lines)
    {
        if (lines.Count < 3)
        {
            return false;
        }

        var marker = lines[0].Trim();
        if (marker == "@")
        {
            return true;
        }

        if (marker != "#" || !long.TryParse(lines[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        var firstTransaction = lines.Skip(3).FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
        if (firstTransaction is null)
        {
            return true;
        }

        var values = firstTransaction.Split(';', StringSplitOptions.None);
        return values.Length >= 4 && long.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _) &&
               long.TryParse(values[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }

    private static AnalysisDocument ParseSalesReportLines(
        string filePath,
        IReadOnlyList<string> lines,
        string encodingName,
        IProgress<FrontolParseProgress>? progress)
    {
        var records = new List<ParsedRecord>(lines.Count);
        progress?.Report(new FrontolParseProgress(0, lines.Count, "Разбор отчёта о продажах"));

        for (var index = 0; index < lines.Count; index++)
        {
            var rawLine = lines[index];
            var lineNumber = index + 1;
            if (index < 3)
            {
                records.Add(SalesHeaderRecord(lineNumber, rawLine));
            }
            else if (string.IsNullOrWhiteSpace(rawLine))
            {
                records.Add(new ParsedRecord
                {
                    LineNumber = lineNumber,
                    Kind = FrontolRecordKind.Empty,
                    RawText = rawLine,
                    Title = "Пустая строка",
                    Summary = "Разделитель или пустая строка отчёта",
                    FileKind = ExchangeFileKind.SalesReportFromFrontol
                });
            }
            else if (rawLine.TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                records.Add(new ParsedRecord
                {
                    LineNumber = lineNumber,
                    Kind = FrontolRecordKind.Comment,
                    RawText = rawLine,
                    Title = "Комментарий",
                    Summary = rawLine.Trim(),
                    FileKind = ExchangeFileKind.SalesReportFromFrontol
                });
            }
            else
            {
                records.Add(SalesTransactionRecord(lineNumber, rawLine));
            }

            ReportProgress(progress, index, lines.Count);
        }

        progress?.Report(new FrontolParseProgress(lines.Count, lines.Count, "Отчёт о продажах разобран"));
        return new AnalysisDocument
        {
            FilePath = Path.GetFullPath(filePath),
            EncodingName = encodingName,
            Records = records,
            FileKind = ExchangeFileKind.SalesReportFromFrontol
        };
    }

    private static ParsedRecord SalesHeaderRecord(int lineNumber, string rawLine)
    {
        var (title, name, dataType, purpose, values, interpreter) = lineNumber switch
        {
            1 => (
                "Признак обработки файла",
                "Признак обработки",
                "Строка 1",
                "«#» - файл ещё ожидает обработки учётной системой; после успешной обработки символ нужно заменить на «@».",
                (IReadOnlyDictionary<string, string>?)new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["#"] = "Ожидает обработки учётной системой",
                    ["@"] = "Уже обработан учётной системой"
                },
                (Func<string, string?>?)null),
            2 => (
                "Идентификатор базы Frontol",
                "Идентификатор БД",
                "Строка",
                "Идентификатор базы данных из настройки Frontol «База данных / Идентификатор БД».",
                null,
                null),
            _ => (
                "Порядковый номер отчёта",
                "Номер отчёта",
                "Целое",
                "Увеличивается при формировании нового файла в исходящем канале и помогает контролировать последовательность отчётов.",
                null,
                null)
        };

        var definition = new FieldDefinition(1, name, true, dataType, purpose, Values: values, CustomInterpreter: interpreter);
        var (severity, diagnostic) = Validate(definition, rawLine.Trim());
        var issues = severity == IssueSeverity.None
            ? Array.Empty<AnalysisIssue>()
            : [new AnalysisIssue(severity, $"{name}: {diagnostic}")];
        var headerDefinition = new CommandDefinition(
            $"REPORT_HEADER_{lineNumber}",
            title,
            purpose,
            "Руководство интегратора Frontol 6, раздел 17.2.2, стр. 272",
            [definition],
            SyntaxPrefix: string.Empty,
            Category: "0|Структура файла");

        return new ParsedRecord
        {
            LineNumber = lineNumber,
            Kind = FrontolRecordKind.Header,
            RawText = rawLine,
            Title = title,
            Summary = Interpret(definition, rawLine.Trim()),
            Definition = headerDefinition,
            Fields =
            [
                new AnalyzedField
                {
                    Number = 1,
                    Name = definition.Name,
                    RawValue = rawLine,
                    WasProvided = true,
                    Interpretation = Interpret(definition, rawLine.Trim()),
                    Required = true,
                    DataType = definition.DataType,
                    Purpose = definition.Purpose,
                    Severity = severity,
                    Diagnostic = diagnostic
                }
            ],
            FieldCount = 1,
            FieldSeverity = severity,
            Issues = issues,
            FileKind = ExchangeFileKind.SalesReportFromFrontol
        };
    }

    private static ParsedRecord SalesTransactionRecord(int lineNumber, string rawLine)
    {
        var hasTerminatingDelimiter = rawLine.EndsWith(';');
        var splitValues = rawLine.Split(';', StringSplitOptions.None);
        IReadOnlyList<string> values = hasTerminatingDelimiter && splitValues.Length > 0
            ? splitValues[..^1]
            : splitValues;

        if (values.Count < 4)
        {
            return new ParsedRecord
            {
                LineNumber = lineNumber,
                Kind = FrontolRecordKind.Data,
                RawText = rawLine,
                Title = "Нераспознанная строка выгрузки",
                Summary = $"Передано полей: {values.Count}; для определения транзакции требуется поле №4",
                RawValues = values,
                FieldCount = values.Count,
                FieldSeverity = IssueSeverity.Error,
                FieldFactory = () => BuildGenericFields(values),
                Issues = [new AnalysisIssue(IssueSeverity.Error, "В строке отсутствует обязательное поле №4 «Тип транзакции».")],
                FileKind = ExchangeFileKind.SalesReportFromFrontol,
                HasTerminatingDelimiter = hasTerminatingDelimiter
            };
        }

        var transactionCode = values[3].Trim();
        if (!FrontolSalesTransactionCatalog.TryGet(transactionCode, out var definition))
        {
            return new ParsedRecord
            {
                LineNumber = lineNumber,
                Kind = FrontolRecordKind.Data,
                RawText = rawLine,
                CommandName = transactionCode,
                Title = $"Транзакция №{transactionCode}",
                Summary = $"Неизвестный тип · передано полей: {values.Count}",
                RawValues = values,
                FieldCount = values.Count,
                FieldSeverity = IssueSeverity.Warning,
                FieldFactory = () => BuildGenericFields(values),
                Issues = [new AnalysisIssue(IssueSeverity.Warning, $"Тип транзакции №{transactionCode} отсутствует в разделе 17.2.2.1 текущего руководства Frontol 6.")],
                FileKind = ExchangeFileKind.SalesReportFromFrontol,
                HasTerminatingDelimiter = hasTerminatingDelimiter
            };
        }

        var fieldDefinitions = definition.Fields;
        var fieldCount = Math.Max(values.Count, fieldDefinitions.Count);
        var issues = new List<AnalysisIssue>();
        var fieldSeverity = IssueSeverity.None;
        for (var index = 0; index < fieldCount; index++)
        {
            var number = index + 1;
            var rawValue = index < values.Count ? values[index] : string.Empty;
            if (index >= fieldDefinitions.Count)
            {
                fieldSeverity = MaxSeverity(fieldSeverity, IssueSeverity.Error);
                issues.Add(new AnalysisIssue(IssueSeverity.Error, $"Поле {number}: формат выгрузки Frontol предусматривает не более {fieldDefinitions.Count} полей."));
                continue;
            }

            var (severity, diagnostic) = Validate(fieldDefinitions[index], rawValue);
            if (severity != IssueSeverity.None)
            {
                fieldSeverity = MaxSeverity(fieldSeverity, severity);
                issues.Add(new AnalysisIssue(severity, $"Поле {number} «{fieldDefinitions[index].Name}»: {diagnostic}"));
            }
        }

        return new ParsedRecord
        {
            LineNumber = lineNumber,
            Kind = FrontolRecordKind.Data,
            RawText = rawLine,
            CommandName = definition.Name,
            Definition = definition,
            Title = $"Транзакция №{definition.Name} · {definition.DisplayName}",
            Summary = BuildSalesSummary(definition, values, hasTerminatingDelimiter),
            RawValues = values,
            FieldCount = fieldCount,
            FieldSeverity = fieldSeverity,
            FieldFactory = () => BuildKnownFields(values, fieldDefinitions),
            Issues = issues,
            FileKind = ExchangeFileKind.SalesReportFromFrontol,
            HasTerminatingDelimiter = hasTerminatingDelimiter
        };
    }

    private static string BuildSalesSummary(
        CommandDefinition definition,
        IReadOnlyList<string> values,
        bool hasTerminatingDelimiter)
    {
        string Get(int number) => number <= values.Count ? values[number - 1] : string.Empty;
        var document = string.IsNullOrWhiteSpace(Get(6)) ? "без номера документа" : $"документ {Get(6)}";
        var when = string.Join(' ', new[] { Get(2), Get(3) }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var payload = FrontolSalesTransactionCatalog.IsProductTransaction(definition.Name)
            ? $"товар {EmptyAs(Get(8), "по свободной цене")} · количество {EmptyAs(Get(11), "не указано")}"
            : definition.DisplayName;
        var delimiter = hasTerminatingDelimiter ? "есть" : "нет";
        return $"{document} · {when} · {payload} · полей: {values.Count}/{definition.Fields.Count} · завершающий ;: {delimiter}";
    }

    private static void ReportProgress(IProgress<FrontolParseProgress>? progress, int index, int total)
    {
        if (index % 100 == 0 || index == total - 1)
        {
            progress?.Report(new FrontolParseProgress(index + 1, total, "Разбор строк"));
        }
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
            return new ParsedRecord
            {
                LineNumber = lineNumber,
                Kind = FrontolRecordKind.Data,
                RawText = rawLine,
                CommandName = commandName,
                Title = $"Данные · $$$${commandName}".Replace("$$$$", "$$$"),
                Summary = $"Полей: {values.Length}",
                RawValues = values,
                FieldCount = values.Length,
                FieldSeverity = IssueSeverity.Warning,
                FieldFactory = () => BuildGenericFields(values),
                Issues = [new AnalysisIssue(IssueSeverity.Warning, "Команда не распознана; поля только пронумерованы.")]
            };
        }

        var resolvedDefinitions = definition.ResolveFields(values);
        var fieldCount = Math.Max(values.Length, resolvedDefinitions.Count);
        var issues = new List<AnalysisIssue>();
        var fieldSeverity = IssueSeverity.None;

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
                fieldSeverity = MaxSeverity(fieldSeverity, extraSeverity);
                issues.Add(new AnalysisIssue(extraSeverity, $"Поле {number}: {extraDiagnostic}"));
                continue;
            }

            var fieldDefinition = resolvedDefinitions[index];
            var (severity, diagnostic) = Validate(fieldDefinition, rawValue);
            if (severity != IssueSeverity.None)
            {
                fieldSeverity = MaxSeverity(fieldSeverity, severity);
                issues.Add(new AnalysisIssue(severity, $"Поле {number} «{fieldDefinition.Name}»: {diagnostic}"));
            }
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
            RawValues = values,
            FieldCount = fieldCount,
            FieldSeverity = fieldSeverity,
            FieldFactory = () => BuildKnownFields(values, resolvedDefinitions),
            Issues = issues
        };
    }

    private static IReadOnlyList<AnalyzedField> BuildGenericFields(IReadOnlyList<string> values) =>
        values.Select((value, index) => new AnalyzedField
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

    private static IReadOnlyList<AnalyzedField> BuildKnownFields(
        IReadOnlyList<string> values,
        IReadOnlyList<FieldDefinition> definitions)
    {
        var fieldCount = Math.Max(values.Count, definitions.Count);
        var fields = new AnalyzedField[fieldCount];
        for (var index = 0; index < fieldCount; index++)
        {
            var number = index + 1;
            var wasProvided = index < values.Count;
            var rawValue = wasProvided ? values[index] : string.Empty;
            if (index >= definitions.Count)
            {
                var trailingEmpty = rawValue.Length == 0 && index == values.Count - 1;
                fields[index] = new AnalyzedField
                {
                    Number = number,
                    Name = trailingEmpty ? "Завершающий пустой сегмент" : "Лишнее поле",
                    RawValue = rawValue,
                    WasProvided = true,
                    Interpretation = trailingEmpty ? "Пустое значение после последней ;" : "Не предусмотрено форматом команды",
                    Required = false,
                    DataType = "—",
                    Purpose = "За пределами формата команды",
                    Severity = trailingEmpty ? IssueSeverity.Info : IssueSeverity.Error,
                    Diagnostic = trailingEmpty
                        ? "Дополнительный пустой сегмент появился из-за завершающей точки с запятой."
                        : "У команды нет поля с таким номером."
                };
                continue;
            }

            var definition = definitions[index];
            var (severity, diagnostic) = Validate(definition, rawValue);
            fields[index] = new AnalyzedField
            {
                Number = number,
                Name = definition.Name,
                RawValue = rawValue,
                WasProvided = wasProvided,
                Interpretation = Interpret(definition, rawValue),
                Required = definition.Required,
                DataType = definition.DataType,
                Purpose = definition.Purpose,
                Severity = severity,
                Diagnostic = diagnostic
            };
        }
        return fields;
    }

    private static IssueSeverity MaxSeverity(IssueSeverity left, IssueSeverity right) => left > right ? left : right;

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

        if (!dataType.Contains("Дата", StringComparison.OrdinalIgnoreCase) &&
            dataType.Contains("Время", StringComparison.OrdinalIgnoreCase) &&
            !DateTime.TryParse(rawValue, RussianCulture, DateTimeStyles.None, out _))
        {
            return (IssueSeverity.Error, "ожидалось время");
        }

        if (definition.Values is not null &&
            !definition.AllowUnknownValues &&
            !definition.Values.ContainsKey(rawValue))
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

        if (bytes.AsSpan().StartsWith(Encoding.BigEndianUnicode.GetPreamble()))
        {
            return (Encoding.BigEndianUnicode.GetString(bytes.AsSpan(Encoding.BigEndianUnicode.GetPreamble().Length)), "UTF-16 BE");
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
