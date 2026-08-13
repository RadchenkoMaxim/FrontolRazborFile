using System.Globalization;

namespace FrontolFileAnalyzer.Core;

public sealed class SalesReportAnalysis
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly HashSet<string> GroupPaymentTransactionTypes =
        ["43", "82", "83", "84", "86"];

    private static readonly HashSet<string> FinancialOperationCodes = ["0", "1"];
    private static readonly HashSet<string> KegOperationCodes = ["18", "27", "28", "29", "30"];

    public static SalesReportAnalysis Empty { get; } =
        new([], [], [], [], [], [], [], [], [], [], [], new SalesReportOverview());

    public SalesReportAnalysis(
        IReadOnlyList<SalesDocumentSummary> documents,
        IReadOnlyList<SalesProductSummary> products,
        IReadOnlyList<SalesPaymentSummary> payments,
        IReadOnlyList<SalesShiftSummary> shifts,
        IReadOnlyList<SalesOperationSummary> operations,
        IReadOnlyList<SalesPrintGroupSummary> printGroups,
        IReadOnlyList<SalesDocumentationExampleSummary> documentationExamples,
        IReadOnlyList<SalesKegSummary> kegs,
        IReadOnlyList<SalesAdjustmentSummary> adjustments,
        IReadOnlyList<SalesTaxSummary> taxes,
        IReadOnlyList<SalesDiagnosticSummary> diagnostics,
        SalesReportOverview overview)
    {
        Documents = documents;
        Products = products;
        Payments = payments;
        Shifts = shifts;
        Operations = operations;
        PrintGroups = printGroups;
        DocumentationExamples = documentationExamples;
        Kegs = kegs;
        Adjustments = adjustments;
        Taxes = taxes;
        Diagnostics = diagnostics;
        Overview = overview;
    }

    public IReadOnlyList<SalesDocumentSummary> Documents { get; }
    public IReadOnlyList<SalesProductSummary> Products { get; }
    public IReadOnlyList<SalesPaymentSummary> Payments { get; }
    public IReadOnlyList<SalesShiftSummary> Shifts { get; }
    public IReadOnlyList<SalesOperationSummary> Operations { get; }
    public IReadOnlyList<SalesPrintGroupSummary> PrintGroups { get; }
    public IReadOnlyList<SalesDocumentationExampleSummary> DocumentationExamples { get; }
    public IReadOnlyList<SalesKegSummary> Kegs { get; }
    public IReadOnlyList<SalesAdjustmentSummary> Adjustments { get; }
    public IReadOnlyList<SalesTaxSummary> Taxes { get; }
    public IReadOnlyList<SalesDiagnosticSummary> Diagnostics { get; }
    public SalesReportOverview Overview { get; }

    public static SalesReportAnalysis Build(
        IReadOnlyList<ParsedRecord> records,
        IReadOnlyList<SalesReportHistoryEntry>? history = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        var transactions = records
            .Where(record => record.FileKind == ExchangeFileKind.SalesReportFromFrontol &&
                             record.Kind == FrontolRecordKind.Data)
            .ToArray();
        var databaseId = records.FirstOrDefault(record => record.Kind == FrontolRecordKind.Header && record.LineNumber == 2)
            ?.RawText.Trim() ?? string.Empty;
        var reportNumber = records.FirstOrDefault(record => record.Kind == FrontolRecordKind.Header && record.LineNumber == 3)
            ?.RawText.Trim() ?? string.Empty;
        var processingMarker = records.FirstOrDefault(record => record.Kind == FrontolRecordKind.Header && record.LineNumber == 1)
            ?.RawText.Trim() ?? string.Empty;

        var documentAccumulators = new Dictionary<string, DocumentAccumulator>(StringComparer.Ordinal);
        foreach (var transaction in transactions)
        {
            var documentNumber = transaction.GetRawValue(6).Trim();
            if (documentNumber.Length == 0)
            {
                continue;
            }

            var coordinates = DocumentCoordinates(transaction);
            var enterpriseId = transaction.GetRawValue(27).Trim();
            var key = DocumentKey(databaseId, enterpriseId, coordinates.Workstation, coordinates.Shift, coordinates.Document);
            if (!documentAccumulators.TryGetValue(key, out var accumulator))
            {
                accumulator = new DocumentAccumulator(
                    key,
                    coordinates.Document,
                    coordinates.Workstation,
                    coordinates.Shift,
                    enterpriseId);
                documentAccumulators.Add(key, accumulator);
            }
            accumulator.Add(transaction);
        }

        var documents = documentAccumulators.Values
            .Select(accumulator => accumulator.Build())
            .OrderByDescending(document => document.SortDate)
            .ThenByDescending(document => ParseLong(document.DocumentNumber))
            .ToArray();
        var completedDocuments = documents.Where(document => document.IsCompleted && !document.IsCancelled).ToArray();
        var financialDocuments = completedDocuments.Where(document => FinancialOperationCodes.Contains(document.OperationCode)).ToArray();

        var productLines = financialDocuments.SelectMany(document => document.Items).ToArray();
        var products = productLines
            .GroupBy(line => $"{line.EnterpriseId}\u001f{line.ProductCode}\u001f{line.ProductTypeCode}\u001f{line.Barcode}\u001f{line.PrintGroupCode}", StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var quantity = group.Sum(line => line.Quantity);
                var amount = group.Sum(line => line.Amount);
                var averagePrice = quantity == 0 ? group.Last().Price : Math.Abs(amount / quantity);
                var first = group.First();
                return new SalesProductSummary(
                    first.EnterpriseId,
                    first.ProductCode,
                    first.ProductDisplayName,
                    first.ProductTypeCode,
                    first.ProductTypeName,
                    first.Barcode,
                    first.PrintGroupCode,
                    first.PrintGroupName,
                    group.Select(line => line.DocumentKey).Distinct(StringComparer.Ordinal).Count(),
                    group.Count(),
                    quantity,
                    averagePrice,
                    amount);
            })
            .OrderByDescending(product => Math.Abs(product.Amount))
            .ThenBy(product => product.ProductCode, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var paymentLines = financialDocuments.SelectMany(document => document.Payments).ToArray();
        var payments = paymentLines
            .GroupBy(line => new { line.EnterpriseId, line.TransactionType, line.PaymentCode, line.PaymentName })
            .Select(group => new SalesPaymentSummary(
                group.Key.EnterpriseId,
                group.Key.TransactionType,
                group.Key.PaymentCode,
                group.Key.PaymentName,
                group.Select(line => line.DocumentKey).Distinct(StringComparer.Ordinal).Count(),
                group.Count(),
                group.Sum(line => line.Amount)))
            .OrderByDescending(payment => Math.Abs(payment.Amount))
            .ThenBy(payment => payment.PaymentName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var shifts = BuildShifts(transactions, financialDocuments);
        var operations = FrontolSalesTransactionCatalog.OperationTypes
            .Select(pair =>
            {
                var operationDocuments = completedDocuments.Where(document => document.OperationCode == pair.Key).ToArray();
                var transactionCount = transactions.Count(transaction => transaction.GetRawValue(13).Trim() == pair.Key);
                var explanation = FrontolSalesTransactionCatalog.OperationScenarios.TryGetValue(pair.Key, out var scenario)
                    ? scenario
                    : "Операция поддерживается форматом Frontol; точная цепочка зависит от настроенного вида документа.";
                return new SalesOperationSummary(
                    pair.Key,
                    pair.Value,
                    explanation,
                    operationDocuments.Length,
                    transactionCount,
                    operationDocuments.Sum(document => document.Total));
            })
            .OrderByDescending(operation => operation.DocumentCount)
            .ThenBy(operation => int.Parse(operation.OperationCode))
            .ToArray();
        var printGroups = financialDocuments
            .SelectMany(document => document.PrintGroups)
            .GroupBy(group => new { group.EnterpriseId, group.PrintGroupCode })
            .Select(group => new SalesPrintGroupSummary(
                group.Key.EnterpriseId,
                group.Key.PrintGroupCode,
                PrintGroupDisplayName(group.Key.PrintGroupCode),
                group.Select(item => item.DocumentKey).Distinct(StringComparer.Ordinal).Count(),
                group.Sum(item => item.ProductLineCount),
                group.Sum(item => item.Quantity),
                group.Sum(item => item.GrossSales),
                group.Sum(item => item.Returns),
                group.Sum(item => item.NetSales),
                group.Sum(item => item.PaymentTotal),
                group.Sum(item => item.TransactionCount)))
            .OrderBy(group => group.EnterpriseId, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(group => ParseLong(group.PrintGroupCode))
            .ThenBy(group => group.PrintGroupCode, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var documentationExamples = FrontolSalesDocumentationCatalog.All
            .Select(example =>
            {
                var matchingCount = example.TransactionCodes.Count == 0
                    ? (transactions.Length > 0 ? 1 : 0)
                    : transactions.Count(transaction =>
                        example.TransactionCodes.Contains(transaction.CommandName ?? string.Empty, StringComparer.Ordinal) &&
                        (example.OperationCodes.Count == 0 ||
                         example.OperationCodes.Contains(transaction.GetRawValue(13).Trim(), StringComparer.Ordinal)));
                return new SalesDocumentationExampleSummary(
                    example.Section,
                    example.Title,
                    example.TransactionText,
                    example.OperationText,
                    example.Explanation,
                    example.ManualReference,
                    matchingCount);
            })
            .OrderBy(example => example.Section, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(example => example.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var salesDocuments = financialDocuments.Where(document => document.OperationCode == "0").ToArray();
        var returnDocuments = financialDocuments.Where(document => document.OperationCode == "1").ToArray();
        var grossSales = salesDocuments.Sum(document => Math.Max(0, document.Total));
        var returns = returnDocuments.Sum(document => Math.Abs(document.Total));
        var netSales = grossSales - returns;
        var salesAndReturnPayments = financialDocuments
            .SelectMany(document => document.Payments)
            .Sum(payment => payment.Amount);
        var kegs = BuildKegs(completedDocuments);
        var adjustments = BuildAdjustments(financialDocuments);
        var taxes = BuildTaxes(financialDocuments);
        var diagnostics = BuildDiagnostics(
            transactions,
            documents,
            financialDocuments,
            printGroups,
            databaseId,
            reportNumber,
            processingMarker,
            history ?? []);
        var firstTransaction = transactions.Select(TransactionDate).Where(date => date is not null).Min();
        var lastTransaction = transactions.Select(TransactionDate).Where(date => date is not null).Max();
        var overview = new SalesReportOverview
        {
            TransactionCount = transactions.Length,
            DocumentCount = documents.Length,
            CompletedDocumentCount = completedDocuments.Length,
            CancelledDocumentCount = documents.Count(document => document.IsCancelled),
            OpenDocumentCount = documents.Count(document => !document.IsCompleted && !document.IsCancelled),
            NonFinancialDocumentCount = completedDocuments.Count(document => !FinancialOperationCodes.Contains(document.OperationCode)),
            SalesDocumentCount = salesDocuments.Length,
            ReturnDocumentCount = returnDocuments.Length,
            ProductLineCount = productLines.Length,
            UniqueProductCount = products.Length,
            Quantity = productLines.Sum(line => line.Quantity),
            GrossSales = grossSales,
            Returns = returns,
            NetSales = netSales,
            PaymentTotal = salesAndReturnPayments,
            AverageDocument = salesDocuments.Length == 0 ? 0 : grossSales / salesDocuments.Length,
            PrintGroupCount = printGroups.Length,
            EnterpriseCount = transactions.Select(transaction => transaction.GetRawValue(27).Trim())
                .Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).Count(),
            DatabaseId = databaseId,
            ReportNumber = reportNumber,
            ProcessingMarker = processingMarker,
            FirstTransactionNumber = transactions.Select(transaction => ParseLong(transaction.GetRawValue(1).Trim()))
                .Where(number => number != long.MinValue).DefaultIfEmpty().Min(),
            LastTransactionNumber = transactions.Select(transaction => ParseLong(transaction.GetRawValue(1).Trim()))
                .Where(number => number != long.MinValue).DefaultIfEmpty().Max(),
            FirstTransaction = firstTransaction,
            LastTransaction = lastTransaction
        };

        return new SalesReportAnalysis(
            documents, products, payments, shifts, operations, printGroups, documentationExamples,
            kegs, adjustments, taxes, diagnostics, overview);
    }

    private static IReadOnlyList<SalesShiftSummary> BuildShifts(
        IReadOnlyList<ParsedRecord> transactions,
        IReadOnlyList<SalesDocumentSummary> documents)
    {
        var keys = transactions
            .Select(transaction => new
            {
                Enterprise = transaction.GetRawValue(27).Trim(),
                Workstation = transaction.GetRawValue(5).Trim(),
                Shift = transaction.GetRawValue(14).Trim()
            })
            .Where(key => key.Workstation.Length > 0 || key.Shift.Length > 0)
            .Distinct()
            .ToArray();

        return keys.Select(key =>
            {
                var shiftTransactions = transactions.Where(transaction =>
                    string.Equals(transaction.GetRawValue(27).Trim(), key.Enterprise, StringComparison.Ordinal) &&
                    string.Equals(transaction.GetRawValue(5).Trim(), key.Workstation, StringComparison.Ordinal) &&
                    string.Equals(transaction.GetRawValue(14).Trim(), key.Shift, StringComparison.Ordinal)).ToArray();
                var shiftDocuments = documents.Where(document =>
                    string.Equals(document.EnterpriseId, key.Enterprise, StringComparison.Ordinal) &&
                    string.Equals(document.Workstation, key.Workstation, StringComparison.Ordinal) &&
                    string.Equals(document.ShiftNumber, key.Shift, StringComparison.Ordinal)).ToArray();
                var opened = shiftTransactions
                    .Where(transaction => transaction.CommandName is "62" or "64")
                    .Select(TransactionDate).Where(date => date is not null).Min();
                var closed = shiftTransactions
                    .Where(transaction => transaction.CommandName is "61" or "63")
                    .Select(TransactionDate).Where(date => date is not null).Max();
                var first = shiftTransactions.Select(TransactionDate).Where(date => date is not null).Min();
                var last = shiftTransactions.Select(TransactionDate).Where(date => date is not null).Max();
                var programClose = shiftTransactions.LastOrDefault(transaction => transaction.CommandName == "61");
                var hardwareClose = shiftTransactions.LastOrDefault(transaction => transaction.CommandName == "63");
                var calculated = shiftDocuments.Sum(document => document.Total);
                var programRevenue = programClose is null ? (decimal?)null : DecimalAt(programClose, 10);
                var programSales = programClose is null ? (decimal?)null : DecimalAt(programClose, 12);
                var hardwareRevenue = hardwareClose is null ? (decimal?)null : DecimalAt(hardwareClose, 10);
                var hardwareSales = hardwareClose is null ? (decimal?)null : DecimalAt(hardwareClose, 12);
                return new SalesShiftSummary(
                    key.Enterprise,
                    key.Workstation,
                    key.Shift,
                    opened ?? first,
                    closed,
                    closed is null ? "Открыта / закрытие не выгружено" : "Закрыта",
                    shiftDocuments.Length,
                    shiftDocuments.Count(document => document.OperationCode == "1"),
                    calculated,
                    programRevenue,
                    programSales,
                    hardwareRevenue,
                    hardwareSales,
                    shiftTransactions.Length,
                    last);
            })
            .OrderByDescending(shift => shift.LastActivity)
            .ThenByDescending(shift => ParseLong(shift.ShiftNumber))
            .ToArray();
    }

    private static (string Workstation, string Shift, string Document) DocumentCoordinates(ParsedRecord record)
    {
        var workstation = record.GetRawValue(5).Trim();
        var shift = record.GetRawValue(14).Trim();
        var document = record.GetRawValue(6).Trim();
        var information = record.GetRawValue(26).Trim();
        var parts = information.Split('/');
        if (parts.Length == 3 && information != "0/0/0" && parts.All(part => part.Trim().Length > 0))
        {
            return (parts[0].Trim(), parts[1].Trim(), parts[2].Trim());
        }
        return (workstation, shift, document);
    }

    private static string DocumentKey(
        string databaseId,
        string enterpriseId,
        string workstation,
        string shift,
        string document) =>
        $"{databaseId}|{enterpriseId}|{workstation}|{shift}|{document}";

    private static DateTime? TransactionDate(ParsedRecord record)
    {
        var text = $"{record.GetRawValue(2).Trim()} {record.GetRawValue(3).Trim()}".Trim();
        return DateTime.TryParse(text, RussianCulture, DateTimeStyles.None, out var value) ? value : null;
    }

    private static decimal DecimalAt(ParsedRecord record, int fieldNumber)
    {
        var value = record.GetRawValue(fieldNumber).Trim();
        if (decimal.TryParse(value, NumberStyles.Number, RussianCulture, out var russian))
        {
            return russian;
        }
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariant) ? invariant : 0;
    }

    private static decimal Signed(decimal value, string operationCode, bool forceNegative = false)
    {
        if (forceNegative || operationCode == "1")
        {
            return value > 0 ? -value : value;
        }
        return value;
    }

    private static decimal SignedItem(decimal value, string operationCode, bool isStorno)
    {
        if (isStorno)
        {
            return operationCode == "1" ? Math.Abs(value) : -Math.Abs(value);
        }
        return operationCode == "1" ? -Math.Abs(value) : value;
    }

    private static long ParseLong(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : long.MinValue;

    private static string OperationName(string code) =>
        FrontolSalesTransactionCatalog.OperationTypes.TryGetValue(code, out var name)
            ? name
            : string.IsNullOrWhiteSpace(code) ? "Не указана" : $"Операция {code}";

    public static string PrintGroupDisplayName(string code) =>
        string.IsNullOrWhiteSpace(code) ? "Группа печати не указана" : $"Группа печати {code}";

    private static (string Code, string Name) ProductIdentity(string value)
    {
        value = value.Trim();
        var separator = value.IndexOfAny(['\u00a6', '|']);
        if (separator < 0)
        {
            return (value, value.Length == 0 ? "Товар по свободной цене" : $"Товар {value}");
        }
        var code = value[..separator].Trim();
        var name = value[(separator + 1)..].Trim();
        return (code, name.Length == 0 ? (code.Length == 0 ? "Товар по свободной цене" : $"Товар {code}") : name);
    }

    private static IReadOnlyList<SalesKegSummary> BuildKegs(IReadOnlyList<SalesDocumentSummary> documents) =>
        documents.Where(document => KegOperationCodes.Contains(document.OperationCode))
            .SelectMany(document => document.Items.Select(item => new SalesKegSummary(
                document.Key,
                document.DocumentNumber,
                document.DateText,
                document.EnterpriseId,
                document.OperationCode,
                document.OperationName,
                item.ProductCode,
                item.ProductDisplayName,
                item.MarkCode,
                item.PrintGroupCode,
                item.PrintGroupName,
                item.Quantity,
                item.Price,
                item.Amount,
                document.Status,
                document.OperationCode switch
                {
                    "27" => "Объём кега при постановке на кран",
                    "28" => "Доступный остаток при снятии с крана",
                    "30" => "Списанный объём вскрытой тары",
                    "29" => "Объём вскрытой тары для коктейлей",
                    _ => "Объём вскрытой тары"
                })))
            .OrderByDescending(item => item.DateText, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    private static IReadOnlyList<SalesAdjustmentSummary> BuildAdjustments(IReadOnlyList<SalesDocumentSummary> documents)
    {
        var codes = new HashSet<string>(["15", "17", "35", "37", "38", "85", "87"], StringComparer.Ordinal);
        return documents.SelectMany(document => document.Transactions.Select(transaction => new { document, transaction }))
            .Where(item => codes.Contains(item.transaction.CommandName ?? string.Empty))
            .Select(item => new
            {
                item.document.EnterpriseId,
                Code = item.transaction.CommandName ?? string.Empty,
                Name = item.transaction.Definition?.DisplayName ?? $"Транзакция №{item.transaction.CommandName}",
                Group = item.transaction.GetRawValue(17).Trim(),
                Document = item.document.Key,
                Amount = Signed(DecimalAt(item.transaction, 12), item.document.OperationCode)
            })
            .GroupBy(item => new { item.EnterpriseId, item.Code, item.Name, item.Group })
            .Select(group => new SalesAdjustmentSummary(
                group.Key.EnterpriseId,
                group.Key.Code,
                group.Key.Name,
                group.Key.Group,
                PrintGroupDisplayName(group.Key.Group),
                group.Select(item => item.Document).Distinct(StringComparer.Ordinal).Count(),
                group.Count(),
                group.Sum(item => item.Amount)))
            .OrderBy(item => ParseLong(item.TransactionType))
            .ThenBy(item => item.EnterpriseId, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<SalesTaxSummary> BuildTaxes(IReadOnlyList<SalesDocumentSummary> documents)
    {
        var lines = new List<(string Enterprise, string Group, string Source, string Rate, string Document, decimal Amount)>();
        foreach (var document in documents)
        {
            foreach (var transaction in document.Transactions)
            {
                var group = transaction.GetRawValue(17).Trim();
                if (transaction.CommandName is "4" or "14")
                {
                    lines.Add((document.EnterpriseId, group, $"№{transaction.CommandName} · налог позиции",
                        transaction.GetRawValue(11).Trim(), document.Key,
                        Signed(DecimalAt(transaction, 12), document.OperationCode)));
                    continue;
                }
                if (transaction.CommandName != "88")
                {
                    continue;
                }
                foreach (var (field, rate) in new[]
                         {
                             (28, "0%"), (29, "10%"), (30, "20%"), (31, "Без НДС"),
                             (32, "10/110"), (33, "20/120")
                         })
                {
                    var amount = DecimalAt(transaction, field);
                    if (amount != -1)
                    {
                        lines.Add((document.EnterpriseId, group, "№88 · НДС чека ККТ", rate, document.Key,
                            Signed(amount, document.OperationCode)));
                    }
                }
            }
        }
        return lines.GroupBy(line => new { line.Enterprise, line.Group, line.Source, line.Rate })
            .Select(group => new SalesTaxSummary(
                group.Key.Enterprise,
                group.Key.Group,
                PrintGroupDisplayName(group.Key.Group),
                group.Key.Source,
                group.Key.Rate,
                group.Select(item => item.Document).Distinct(StringComparer.Ordinal).Count(),
                group.Sum(item => item.Amount)))
            .OrderBy(item => item.EnterpriseId, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.PrintGroupCode, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Rate, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<SalesDiagnosticSummary> BuildDiagnostics(
        IReadOnlyList<ParsedRecord> transactions,
        IReadOnlyList<SalesDocumentSummary> documents,
        IReadOnlyList<SalesDocumentSummary> financialDocuments,
        IReadOnlyList<SalesPrintGroupSummary> printGroups,
        string databaseId,
        string reportNumber,
        string processingMarker,
        IReadOnlyList<SalesReportHistoryEntry> history)
    {
        var diagnostics = new List<SalesDiagnosticSummary>();
        var transactionNumbers = transactions
            .Select(transaction => transaction.GetRawValue(1).Trim())
            .Select(value => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? (long?)number : null)
            .Where(value => value is not null).Select(value => value!.Value).ToArray();
        var duplicates = transactionNumbers.GroupBy(number => number).Where(group => group.Count() > 1).ToArray();
        if (duplicates.Length > 0)
        {
            diagnostics.Add(new("Ошибка", "Диапазон транзакций", "Файл", $"Повторяются номера транзакций: {string.Join(", ", duplicates.Take(10).Select(group => group.Key))}.", duplicates.Sum(group => group.Count() - 1)));
        }
        var orderedNumbers = transactionNumbers.Distinct().OrderBy(number => number).ToArray();
        var gaps = orderedNumbers.Zip(orderedNumbers.Skip(1), (left, right) => (left, right))
            .Where(pair => pair.right - pair.left > 1).ToArray();
        if (gaps.Length > 0)
        {
            diagnostics.Add(new("Ошибка", "Диапазон транзакций", "Файл", $"Обнаружены пропуски диапазона: {string.Join(", ", gaps.Take(10).Select(gap => $"{gap.left + 1}–{gap.right - 1}"))}.", gaps.Length));
        }
        if (transactionNumbers.Length > 1 && transactionNumbers.Zip(transactionNumbers.Skip(1), (left, right) => right < left).Any(value => value))
        {
            diagnostics.Add(new("Предупреждение", "Диапазон транзакций", "Файл", "Номера транзакций идут не по возрастанию.", 1));
        }
        if (processingMarker == "@")
        {
            diagnostics.Add(new("Информация", "Шапка отчёта", $"БД {databaseId} / отчёт {reportNumber}", "Файл уже отмечен Frontol как обработанный учётной системой.", 1));
        }
        var currentReportNumber = ParseLong(reportNumber);
        if (currentReportNumber != long.MinValue && orderedNumbers.Length > 0)
        {
            var databaseHistory = history.Where(entry => string.Equals(entry.DatabaseId, databaseId, StringComparison.Ordinal)).ToArray();
            var overlap = databaseHistory.FirstOrDefault(entry =>
                orderedNumbers[0] <= entry.LastTransactionNumber && orderedNumbers[^1] >= entry.FirstTransactionNumber);
            if (overlap is not null)
            {
                diagnostics.Add(new("Предупреждение", "История отчётов", $"БД {databaseId} / отчёт {reportNumber}", $"Диапазон транзакций пересекается с ранее открытым отчётом №{overlap.ReportNumber}: {overlap.FirstTransactionNumber}–{overlap.LastTransactionNumber}.", 1));
            }
            var latest = databaseHistory.OrderByDescending(entry => ParseLong(entry.ReportNumber)).FirstOrDefault();
            var latestNumber = latest is null ? long.MinValue : ParseLong(latest.ReportNumber);
            if (latestNumber != long.MinValue && currentReportNumber > latestNumber + 1)
            {
                diagnostics.Add(new("Предупреждение", "История отчётов", $"БД {databaseId}", $"После отчёта №{latestNumber} открыт №{currentReportNumber}; отсутствуют номера {latestNumber + 1}–{currentReportNumber - 1}.", (int)Math.Min(int.MaxValue, currentReportNumber - latestNumber - 1)));
            }
        }
        var openDocuments = documents.Where(document => !document.IsCompleted && !document.IsCancelled).ToArray();
        if (openDocuments.Length > 0)
        {
            diagnostics.Add(new("Предупреждение", "Документы", "Открытые документы", "Незавершённые документы исключены из продаж, оплат и товарных итогов.", openDocuments.Length));
        }
        var missingGroups = financialDocuments.SelectMany(document => document.Items)
            .Count(item => string.IsNullOrWhiteSpace(item.PrintGroupCode));
        if (missingGroups > 0)
        {
            diagnostics.Add(new("Ошибка", "Группы печати", "Товарные позиции", "У товарных позиций не указан код группы печати в поле №17.", missingGroups));
        }
        var missingEnterprises = financialDocuments.Count(document => string.IsNullOrWhiteSpace(document.EnterpriseId));
        if (missingEnterprises > 0)
        {
            diagnostics.Add(new("Ошибка", "Предприятия", "Финансовые документы", "Не указан идентификатор предприятия в поле №27; такие документы нельзя надёжно разделить для 1С.", missingEnterprises));
        }
        foreach (var document in financialDocuments)
        {
            if (!document.IsNonFinancialClose && document.PaymentTotal != document.Total)
            {
                diagnostics.Add(new("Предупреждение", "Сверка документа", $"Документ {document.DocumentNumber}", $"Итог {document.Total:N2}, оплаты {document.PaymentTotal:N2}, разница {document.PaymentTotal - document.Total:N2}.", 1));
            }
            if (document.PrintGroups.Count > 1 && !document.Transactions.Any(transaction => transaction.CommandName == "49"))
            {
                diagnostics.Add(new("Предупреждение", "Группы печати", $"Документ {document.DocumentNumber}", "Несколько групп печати, но отсутствуют итоги №49; суммы рассчитаны по товарным позициям.", 1));
            }
        }
        foreach (var group in printGroups.Where(group => group.PaymentTotal != group.NetSales))
        {
            diagnostics.Add(new("Предупреждение", "Сверка группы печати", $"Предприятие {group.EnterpriseId}, ГП {group.PrintGroupCode}", $"Итог {group.NetSales:N2}, оплаты {group.PaymentTotal:N2}, разница {group.PaymentTotal - group.NetSales:N2}.", 1));
        }
        if (diagnostics.Count == 0)
        {
            diagnostics.Add(new("ОК", "Общая сверка", "Файл", "Ошибок диапазона, незавершённых документов и расхождений оплат не обнаружено.", 0));
        }
        return diagnostics;
    }

    private static string PaymentName(ParsedRecord record)
    {
        var operation = record.GetRawValue(10).Trim();
        return record.CommandName switch
        {
            "40" => operation switch
            {
                "0" => "Наличные",
                "1" => "Банковская карта или QR-код",
                "3" => "Внутренняя предоплата",
                "6" => "Внутренняя подарочная карта",
                "7" => "Пользовательская оплата",
                "8" => "Внешняя подарочная карта",
                "9" => "Банковская карта с выдачей наличных",
                _ => "Фискальная оплата"
            },
            "36" => operation == "8" ? "Внешняя подарочная карта" : "Нефискальная оплата",
            "32" => operation == "1" ? "Внешние бонусы" : "Внутренние бонусы",
            "33" => operation == "1" ? "Возврат внешних бонусов" : "Возврат внутренних бонусов",
            "34" => "Документ предоплаты",
            _ => record.Definition?.DisplayName ?? $"Транзакция №{record.CommandName}"
        };
    }

    private sealed class DocumentAccumulator(
        string key,
        string documentNumber,
        string workstation,
        string shiftNumber,
        string enterpriseId)
    {
        private readonly List<ParsedRecord> _transactions = [];

        public void Add(ParsedRecord record) => _transactions.Add(record);

        public SalesDocumentSummary Build()
        {
            var ordered = _transactions.OrderBy(record => record.LineNumber).ToArray();
            var operationCode = ordered.Select(record => record.GetRawValue(13).Trim())
                .FirstOrDefault(value => value.Length > 0) ?? string.Empty;
            var openRecord = ordered.FirstOrDefault(record => record.CommandName == "42");
            var closeRecord = ordered.LastOrDefault(record => record.CommandName == "55");
            var cancelRecord = ordered.LastOrDefault(record => record.CommandName == "56");
            var nonFinancialCloseRecord = ordered.LastOrDefault(record => record.CommandName == "58");
            var fiscalRecord = ordered.LastOrDefault(record => record.CommandName == "45");
            var closeTotal = closeRecord is null ? 0 : Signed(DecimalAt(closeRecord, 12), operationCode);
            var start = openRecord is null ? ordered.Select(TransactionDate).Where(date => date is not null).Min() : TransactionDate(openRecord);
            var endSource = cancelRecord ?? closeRecord ?? nonFinancialCloseRecord ?? fiscalRecord;
            var end = endSource is null ? ordered.Select(TransactionDate).Where(date => date is not null).Max() : TransactionDate(endSource);

            var itemRecords = ordered.Where(record => record.CommandName is "1" or "2" or "11" or "12").ToArray();
            var items = itemRecords.Select(record =>
            {
                var isStorno = record.CommandName is "2" or "12";
                var itemOperation = record.GetRawValue(13).Trim();
                var rawQuantity = DecimalAt(record, 11);
                var quantity = SignedItem(rawQuantity, itemOperation, isStorno);
                var price = DecimalAt(record, 10);
                var rawAmount = DecimalAt(record, 16);
                if (rawAmount == 0)
                {
                    rawAmount = DecimalAt(record, 12);
                }
                if (rawAmount == 0 && price != 0 && rawQuantity != 0)
                {
                    rawAmount = Math.Abs(price * rawQuantity);
                }
                var amount = SignedItem(rawAmount, itemOperation, isStorno);
                var identity = ProductIdentity(record.GetRawValue(8));
                var productTypeCode = record.GetRawValue(32).Trim();
                if (productTypeCode.Length == 0)
                {
                    productTypeCode = "0";
                }
                var productTypeName = FrontolReferenceCatalog.ProductTypeValues.TryGetValue(productTypeCode, out var typeName)
                    ? typeName
                    : $"Код {productTypeCode}";
                return new SalesProductLine(
                    key,
                    documentNumber,
                    record.GetRawValue(1).Trim(),
                    enterpriseId,
                    identity.Code,
                    identity.Name,
                    record.GetRawValue(19).Trim(),
                    productTypeCode,
                    productTypeName,
                    record.GetRawValue(33).Trim(),
                    NormalizePrintGroup(record.GetRawValue(17)),
                    PrintGroupDisplayName(NormalizePrintGroup(record.GetRawValue(17))),
                    quantity,
                    price,
                    amount,
                    isStorno ? "Сторно" : OperationName(itemOperation));
            }).ToArray();

            var paymentRecords = ordered.Where(record => record.CommandName is "32" or "33" or "34" or "36" or "40").ToArray();
            var payments = paymentRecords.Select(record =>
            {
                var paymentOperation = record.GetRawValue(13).Trim();
                var amount = Signed(DecimalAt(record, 12), paymentOperation, record.CommandName == "33");
                return new SalesPaymentLine(
                    key,
                    documentNumber,
                    enterpriseId,
                    record.GetRawValue(1).Trim(),
                    record.CommandName ?? string.Empty,
                    record.GetRawValue(9).Trim(),
                    PaymentName(record),
                    amount);
            }).ToArray();
            var total = closeRecord is not null
                ? closeTotal
                : fiscalRecord is not null
                    ? Signed(DecimalAt(fiscalRecord, 12), operationCode)
                    : nonFinancialCloseRecord is not null
                        ? Signed(DecimalAt(nonFinancialCloseRecord, 12), operationCode)
                    : items.Sum(item => item.Amount);

            var printGroupCodes = items.Select(item => item.PrintGroupCode)
                .Concat(ordered.Where(record => record.CommandName == "49" ||
                                                 GroupPaymentTransactionTypes.Contains(record.CommandName ?? string.Empty))
                    .Select(record => NormalizePrintGroup(record.GetRawValue(17))))
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var printGroups = printGroupCodes.Select(printGroupCode =>
            {
                var groupItems = items.Where(item => item.PrintGroupCode == printGroupCode).ToArray();
                var groupClosures = ordered.Where(record => record.CommandName == "49" &&
                    NormalizePrintGroup(record.GetRawValue(17)) == printGroupCode).ToArray();
                var groupPayments = ordered.Where(record => GroupPaymentTransactionTypes.Contains(record.CommandName ?? string.Empty) &&
                    NormalizePrintGroup(record.GetRawValue(17)) == printGroupCode).ToArray();
                var groupTransactions = ordered.Count(record =>
                    NormalizePrintGroup(record.GetRawValue(17)) == printGroupCode);
                var groupTotal = groupClosures.Length > 0
                    ? groupClosures.Sum(record => Signed(DecimalAt(record, 12), operationCode))
                    : printGroupCodes.Length == 1 ? total : groupItems.Sum(item => item.Amount);
                var gross = operationCode == "0" ? Math.Max(0, groupTotal) : 0;
                var returns = operationCode == "1" ? Math.Abs(groupTotal) : 0;
                var groupPaymentTotal = operationCode is "0" or "1"
                    ? groupPayments.Length > 0
                        ? groupPayments.Sum(record => Signed(
                            DecimalAt(record, 12), operationCode, record.CommandName == "83"))
                        : printGroupCodes.Length == 1 ? payments.Sum(payment => payment.Amount) : 0
                    : 0;
                return new SalesDocumentPrintGroupSummary(
                    key,
                    enterpriseId,
                    printGroupCode,
                    PrintGroupDisplayName(printGroupCode),
                    groupItems.Length,
                    groupItems.Sum(item => item.Quantity),
                    gross,
                    returns,
                    gross - returns,
                    groupPaymentTotal,
                    groupTransactions);
            }).ToArray();
            var status = cancelRecord is not null
                ? "Отменён"
                : closeRecord is not null ? "Закрыт"
                : nonFinancialCloseRecord is not null ? "Нефинансово закрыт"
                : fiscalRecord is not null ? "Закрыт в ККТ"
                : !FinancialOperationCodes.Contains(operationCode) && !KegOperationCodes.Contains(operationCode)
                    ? "Завершённая операция"
                    : "Открыт / не завершён";
            var oneCView = operationCode == "1"
                ? "Возврат розничной продажи"
                : operationCode == "0" ? "Отчёт о розничных продажах" : OperationName(operationCode);
            var cashier = (closeRecord ?? openRecord ?? ordered[0]).GetRawValue(7).Trim();

            return new SalesDocumentSummary(
                key,
                documentNumber,
                enterpriseId,
                workstation,
                shiftNumber,
                cashier,
                operationCode,
                OperationName(operationCode),
                oneCView,
                status,
                start,
                end,
                items.Sum(item => item.Quantity),
                items.Length,
                items.Sum(item => item.Amount),
                total,
                payments.Sum(payment => payment.Amount),
                ordered.Length,
                items,
                payments,
                printGroups,
                ordered);
        }
    }

    private static string NormalizePrintGroup(string value)
    {
        return value.Trim();
    }

}

public sealed class SalesReportOverview
{
    public int TransactionCount { get; init; }
    public int DocumentCount { get; init; }
    public int CompletedDocumentCount { get; init; }
    public int CancelledDocumentCount { get; init; }
    public int OpenDocumentCount { get; init; }
    public int NonFinancialDocumentCount { get; init; }
    public int SalesDocumentCount { get; init; }
    public int ReturnDocumentCount { get; init; }
    public int ProductLineCount { get; init; }
    public int UniqueProductCount { get; init; }
    public int PrintGroupCount { get; init; }
    public int EnterpriseCount { get; init; }
    public decimal Quantity { get; init; }
    public decimal GrossSales { get; init; }
    public decimal Returns { get; init; }
    public decimal NetSales { get; init; }
    public decimal PaymentTotal { get; init; }
    public decimal AverageDocument { get; init; }
    public string DatabaseId { get; init; } = string.Empty;
    public string ReportNumber { get; init; } = string.Empty;
    public string ProcessingMarker { get; init; } = string.Empty;
    public long FirstTransactionNumber { get; init; }
    public long LastTransactionNumber { get; init; }
    public DateTime? FirstTransaction { get; init; }
    public DateTime? LastTransaction { get; init; }
    public string PeriodText => FirstTransaction is null
        ? "Период не определён"
        : $"{FirstTransaction:dd.MM.yyyy HH:mm} - {LastTransaction:dd.MM.yyyy HH:mm}";
}

public sealed record SalesDocumentSummary(
    string Key,
    string DocumentNumber,
    string EnterpriseId,
    string Workstation,
    string ShiftNumber,
    string Cashier,
    string OperationCode,
    string OperationName,
    string OneCView,
    string Status,
    DateTime? OpenedAt,
    DateTime? ClosedAt,
    decimal Quantity,
    int ItemCount,
    decimal ItemsAmount,
    decimal Total,
    decimal PaymentTotal,
    int TransactionCount,
    IReadOnlyList<SalesProductLine> Items,
    IReadOnlyList<SalesPaymentLine> Payments,
    IReadOnlyList<SalesDocumentPrintGroupSummary> PrintGroups,
    IReadOnlyList<ParsedRecord> Transactions)
{
    public DateTime? SortDate => ClosedAt ?? OpenedAt;
    public string DateText => (ClosedAt ?? OpenedAt)?.ToString("dd.MM.yyyy HH:mm") ?? "-";
    public string DisplayNumber => string.IsNullOrWhiteSpace(DocumentNumber) ? "без номера" : DocumentNumber;
    public bool IsCancelled => Status == "Отменён";
    public bool IsCompleted => Status is "Закрыт" or "Закрыт в ККТ" or "Нефинансово закрыт" or "Завершённая операция";
    public bool IsNonFinancialClose => Status == "Нефинансово закрыт";
    public string BalanceText => IsNonFinancialClose
        ? "Нефинансовое закрытие"
        : PaymentTotal == Total ? "Сходится" : $"Разница {PaymentTotal - Total:N2}";
    public string PrintGroupsText => PrintGroups.Count == 0
        ? "не определены"
        : string.Join(", ", PrintGroups.Select(group => group.PrintGroupName));
}

public sealed record SalesProductLine(
    string DocumentKey,
    string DocumentNumber,
    string TransactionNumber,
    string EnterpriseId,
    string ProductCode,
    string ProductDisplayName,
    string Barcode,
    string ProductTypeCode,
    string ProductTypeName,
    string MarkCode,
    string PrintGroupCode,
    string PrintGroupName,
    decimal Quantity,
    decimal Price,
    decimal Amount,
    string Operation);

public sealed record SalesProductSummary(
    string EnterpriseId,
    string ProductCode,
    string ProductDisplayName,
    string ProductTypeCode,
    string ProductTypeName,
    string Barcode,
    string PrintGroupCode,
    string PrintGroupName,
    int DocumentCount,
    int LineCount,
    decimal Quantity,
    decimal AveragePrice,
    decimal Amount);

public sealed record SalesPaymentLine(
    string DocumentKey,
    string DocumentNumber,
    string EnterpriseId,
    string TransactionNumber,
    string TransactionType,
    string PaymentCode,
    string PaymentName,
    decimal Amount);

public sealed record SalesPaymentSummary(
    string EnterpriseId,
    string TransactionType,
    string PaymentCode,
    string PaymentName,
    int DocumentCount,
    int TransactionCount,
    decimal Amount);

public sealed record SalesShiftSummary(
    string EnterpriseId,
    string Workstation,
    string ShiftNumber,
    DateTime? OpenedAt,
    DateTime? ClosedAt,
    string Status,
    int DocumentCount,
    int ReturnDocumentCount,
    decimal NetSales,
    decimal? ProgramRevenue,
    decimal? ProgramSalesTotal,
    decimal? HardwareRevenue,
    decimal? HardwareSalesTotal,
    int TransactionCount,
    DateTime? LastActivity)
{
    public string OpenedText => OpenedAt?.ToString("dd.MM.yyyy HH:mm") ?? "-";
    public string ClosedText => ClosedAt?.ToString("dd.MM.yyyy HH:mm") ?? "-";
    public string ProgramSalesText => ProgramSalesTotal?.ToString("N2") ?? "не передано";
    public string HardwareSalesText => HardwareSalesTotal?.ToString("N2") ?? "не передано";
    public string ReconciliationText
    {
        get
        {
            var differences = new List<string>();
            if (ProgramSalesTotal is { } program && program != NetSales)
            {
                differences.Add($"№61: {program - NetSales:N2}");
            }
            if (HardwareSalesTotal is { } hardware && hardware != NetSales)
            {
                differences.Add($"№63: {hardware - NetSales:N2}");
            }
            return differences.Count == 0 ? "Сходится" : string.Join("; ", differences);
        }
    }
}

public sealed record SalesOperationSummary(
    string OperationCode,
    string OperationName,
    string Explanation,
    int DocumentCount,
    int TransactionCount,
    decimal Total)
{
    public bool IsPresent => TransactionCount > 0 || DocumentCount > 0;
    public string PresenceText => IsPresent ? "Есть в файле" : "Нет в текущем файле";
}

public sealed record SalesDocumentPrintGroupSummary(
    string DocumentKey,
    string EnterpriseId,
    string PrintGroupCode,
    string PrintGroupName,
    int ProductLineCount,
    decimal Quantity,
    decimal GrossSales,
    decimal Returns,
    decimal NetSales,
    decimal PaymentTotal,
    int TransactionCount)
{
    public string OneCReport => $"Отчёт о розничных продажах — предприятие {EnterpriseId}, ГП {PrintGroupCode}";
    public string BalanceText => PaymentTotal == NetSales ? "Сходится" : $"Разница {PaymentTotal - NetSales:N2}";
}

public sealed record SalesPrintGroupSummary(
    string EnterpriseId,
    string PrintGroupCode,
    string PrintGroupName,
    int DocumentCount,
    int ProductLineCount,
    decimal Quantity,
    decimal GrossSales,
    decimal Returns,
    decimal NetSales,
    decimal PaymentTotal,
    int TransactionCount)
{
    public string OneCReport => $"Отчёт о розничных продажах — предприятие {EnterpriseId}, ГП {PrintGroupCode}";
    public string BalanceText => PaymentTotal == NetSales ? "Сходится" : $"Разница {PaymentTotal - NetSales:N2}";
    public string CalculationSource => "Итог: №49 / позиции · оплаты: №43, №82–86";
    public string Explanation => "Разрез построен по полю №27 «Идентификатор предприятия» и полю №17 «Код группы печати». Итог берётся из транзакции №49, " +
                                  "фискальная оплата — из №43, распределения бонусов/предоплаты/нефискальной оплаты — из №82/83/84/86; " +
                                  "при отсутствии №49 используется сумма товарных позиций группы.";
}

public sealed record SalesKegSummary(
    string DocumentKey,
    string DocumentNumber,
    string DateText,
    string EnterpriseId,
    string OperationCode,
    string OperationName,
    string ProductCode,
    string ProductName,
    string KegCode,
    string PrintGroupCode,
    string PrintGroupName,
    decimal Volume,
    decimal Price,
    decimal Amount,
    string Status,
    string VolumeMeaning);

public sealed record SalesAdjustmentSummary(
    string EnterpriseId,
    string TransactionType,
    string Name,
    string PrintGroupCode,
    string PrintGroupName,
    int DocumentCount,
    int TransactionCount,
    decimal Amount);

public sealed record SalesTaxSummary(
    string EnterpriseId,
    string PrintGroupCode,
    string PrintGroupName,
    string Source,
    string Rate,
    int DocumentCount,
    decimal Amount);

public sealed record SalesDiagnosticSummary(
    string Severity,
    string Category,
    string Scope,
    string Message,
    int Count);

public sealed record SalesReportHistoryEntry(
    string DatabaseId,
    string ReportNumber,
    long FirstTransactionNumber,
    long LastTransactionNumber,
    string FilePath,
    DateTime AnalyzedAt);
