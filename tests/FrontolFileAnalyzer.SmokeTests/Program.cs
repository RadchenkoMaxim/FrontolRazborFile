using FrontolFileAnalyzer.Core;
using System.Text;
using System.Text.RegularExpressions;

if (args.Length is < 1 or > 3 || args.Any(path => !File.Exists(path)))
{
    Console.Error.WriteLine("Передайте путь к тестовому base.txt и, при необходимости, до двух полных файлов загрузки/выгрузки.");
    return 2;
}

var progressEvents = new List<FrontolParseProgress>();
var document = new FrontolFileParser().ParseFile(args[0], new InlineProgress<FrontolParseProgress>(progressEvents.Add));

Assert(document.EncodingName is "Windows-1251" or "UTF-8", $"Неожиданная кодировка: {document.EncodingName}");
Assert(document.Records.Count == 8, $"Ожидалось 8 строк, получено: {document.Records.Count}");
Assert(document.CommandCount == 3, $"Ожидалось 3 команды, получено: {document.CommandCount}");
Assert(document.DataRecordCount == 3, $"Ожидалось 3 строки данных, получено: {document.DataRecordCount}");
Assert(progressEvents.Count > 0 && progressEvents[^1].Percent == 100, "Индикатор разбора должен завершаться на 100%.");
Assert(FrontolReferenceCatalog.ProductTypes.Count == 38, "Справочник должен содержать 38 кодов типов номенклатуры.");
Assert(FrontolReferenceCatalog.ProductTypeValues["18"] == "Разливное пиво", "Код 18 должен означать разливное пиво.");

var product = document.Records.Single(record => record.LineNumber == 6);
Assert(product.CommandName == "REPLACEQUANTITY", "Строка товара должна относиться к REPLACEQUANTITY.");
Assert(product.Fields.Count == 68, $"Справочник товара должен раскрывать 68 полей, получено: {product.Fields.Count}");
Assert(product.Fields[54].RawValue == "18", "В поле 55 ожидалось значение 18.");
Assert(product.Fields[54].Interpretation.Contains("Разливное пиво", StringComparison.Ordinal), "Поле 55 должно расшифровываться как разливное пиво.");
Assert(product.Fields[54].HasExtendedInterpretation, "Содержательная расшифровка поля 55 должна раскрываться в панели подробностей.");
Assert(product.Fields[51].Interpretation.Contains("Разрешена", StringComparison.Ordinal), "Пустое поле 52 должно применять значение по умолчанию 1.");
Assert(product.Fields[65].Interpretation == "Литр", "Поле 66 должно расшифровываться как литр.");
Assert(product.IsProductRecord && product.ProductTypeCode == "18" && product.ProductTypeText == "Разливное пиво",
    "Товарная строка должна предоставлять код и название вида маркировки для фильтра.");
Assert(product.SectionGroup.EndsWith("Товары", StringComparison.Ordinal) &&
       product.CommandGroup.StartsWith("$$$REPLACEQUANTITY — Заменить товары и остаток", StringComparison.Ordinal),
    "Товар должен попадать в отдельный раздел и группу своей команды.");
Assert(!product.Fields[66].WasProvided && !product.Fields[67].WasProvided, "Поля 67 и 68 должны отмечаться как не переданные.");
Assert(product.Fields[66].IsValueEmpty && product.Fields[66].DisplayValue == "не передано",
    "Пустое поле должно сохранять пустое RawValue, но отображаться понятным текстом.");
var editedLines = document.Records.Select(record => record.RawText).ToArray();
var editedParts = editedLines[product.LineNumber - 1].Split(';', StringSplitOptions.None);
editedParts[2] = "Изменённое тестовое наименование";
editedLines[product.LineNumber - 1] = string.Join(';', editedParts);
var editedDocument = new FrontolFileParser().ParseLines(args[0], editedLines, document.EncodingName);
var editedProduct = editedDocument.Records.Single(record => record.LineNumber == product.LineNumber);
Assert(editedProduct.ContentText == "Изменённое тестовое наименование" && editedProduct.Fields[2].RawValue == editedProduct.ContentText,
    "Повторный разбор отредактированной физической строки должен обновлять значение и сводку товара.");

var unlistedProductTypeLines = document.Records.Select(record => record.RawText).ToArray();
var unlistedProductTypeParts = unlistedProductTypeLines[product.LineNumber - 1].Split(';', StringSplitOptions.None).ToList();
while (unlistedProductTypeParts.Count < 55)
{
    unlistedProductTypeParts.Add(string.Empty);
}
unlistedProductTypeParts[54] = "999";
unlistedProductTypeLines[product.LineNumber - 1] = string.Join(';', unlistedProductTypeParts);
var unlistedProductTypeDocument = new FrontolFileParser().ParseLines(args[0], unlistedProductTypeLines, document.EncodingName);
var unlistedProductTypeRecord = unlistedProductTypeDocument.Records.Single(record => record.LineNumber == product.LineNumber);
var unlistedProductTypeField = unlistedProductTypeRecord.Fields[54];
Assert(unlistedProductTypeField.RawValue == "999" && unlistedProductTypeRecord.ProductTypeCode == "999",
    "Целый код поля 55, которого ещё нет во встроенном справочнике, должен сохраняться без замены.");
Assert(unlistedProductTypeField.Severity == IssueSeverity.None && unlistedProductTypeRecord.Severity == IssueSeverity.None,
    "Неизвестный целый код поля 55 должен считаться допустимым без предупреждения.");
Assert(unlistedProductTypeRecord.ProductTypeText == "Код 999",
    "Неизвестный код поля 55 должен отображаться в UI без вымышленной расшифровки.");

unlistedProductTypeParts[54] = "новый-код";
unlistedProductTypeLines[product.LineNumber - 1] = string.Join(';', unlistedProductTypeParts);
var invalidProductTypeDocument = new FrontolFileParser().ParseLines(args[0], unlistedProductTypeLines, document.EncodingName);
var invalidProductTypeField = invalidProductTypeDocument.Records.Single(record => record.LineNumber == product.LineNumber).Fields[54];
Assert(invalidProductTypeField.Severity == IssueSeverity.Error &&
       invalidProductTypeField.Diagnostic.Contains("ожидалось целое число", StringComparison.Ordinal),
    "Нецелое значение поля 55 должно оставаться ошибкой формата.");
var unprovidedField = product.Fields.First(field => field.Interpretation == "Не передано");
Assert(!unprovidedField.HasExtendedInterpretation, "Значение «Не передано» не должно создавать большой блок расшифровки.");

var deleteBarcodes = document.Records.Single(record => record.LineNumber == 4);
Assert(deleteBarcodes.Fields[1].Interpretation.Contains("все штрихкоды", StringComparison.OrdinalIgnoreCase),
    "Пустое поле штрихкода должно объяснять удаление всех штрихкодов товара.");

var classifierLink = document.Records.Single(record => record.LineNumber == 8);
Assert(classifierLink.Fields.Count == 4, "Завершающая точка с запятой должна отображаться отдельным пустым сегментом.");
Assert(classifierLink.Fields[3].Severity == IssueSeverity.Info, "Завершающий пустой сегмент должен иметь информационный статус.");
Assert(classifierLink.Fields[3].WasProvided && classifierLink.Fields[3].DisplayValue == "пусто",
    "Переданный пустой сегмент нужно отличать от непереданного поля.");

var serviceProgress = new List<FrontolParseProgress>();
new FrontolFileParser().ParseLines(args[0], ["#", "$$$DELETEALLWARES", "$$$DELETEALLTAXRATES"], "UTF-8",
    new InlineProgress<FrontolParseProgress>(serviceProgress.Add));
Assert(serviceProgress.Any(item => item.ProcessedLines == 1) && serviceProgress[^1].Percent == 100,
    "Индикатор должен обновляться и для файла из служебных команд.");

var utf16BePath = Path.Combine(Path.GetTempPath(), $"frontol-utf16be-{Guid.NewGuid():N}.txt");
try
{
    File.WriteAllText(utf16BePath, "##@@&&\r\n#\r\n$$$DELETEALLWARES\r\n", new UnicodeEncoding(true, true, true));
    var utf16BeDocument = new FrontolFileParser().ParseFile(utf16BePath);
    Assert(utf16BeDocument.EncodingName == "UTF-16 BE" && utf16BeDocument.Records.Count == 3,
        "UTF-16 BE с BOM должен распознаваться без потери строк.");
}
finally
{
    File.Delete(utf16BePath);
}

var catalogFixturePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, "catalog-commands-sample.txt");
Assert(File.Exists(catalogFixturePath), $"Не найден тестовый файл команд: {catalogFixturePath}");
var catalogDocument = new FrontolFileParser().ParseFile(catalogFixturePath);
var unknownCatalogCommands = catalogDocument.Records
    .Where(record => record.Kind == FrontolRecordKind.Command && record.Definition is null)
    .Select(record => record.CommandName)
    .ToArray();
Assert(unknownCatalogCommands.Length == 0,
    $"Все команды тестового каталога должны быть описаны: {string.Join(", ", unknownCatalogCommands)}");
Assert(catalogDocument.ErrorCount == 0 && catalogDocument.WarningCount == 0,
    $"В тестовом каталоге не должно быть ошибок и предупреждений: {catalogDocument.ErrorCount}/{catalogDocument.WarningCount}");

var taxRate = catalogDocument.Records.Single(record => record.Kind == FrontolRecordKind.Data && record.CommandName == "ADDTAXRATES");
Assert(taxRate.Fields.Count == 6, "ADDTAXRATES должна содержать 6 полей.");
Assert(taxRate.Fields[3].Interpretation == "Процентный налог", "Тип налога 0 должен означать процентный налог.");
Assert(taxRate.Fields[5].Interpretation == "НДС 5%", "Код ККМ 7 должен означать НДС 5%.");

var taxGroup = catalogDocument.Records.Single(record => record.Kind == FrontolRecordKind.Data && record.CommandName == "ADDTAXGROUPS");
Assert(taxGroup.Fields.Count == 3 && taxGroup.Fields[1].Name == "Наименование", "ADDTAXGROUPS должна содержать 3 описанных поля.");

var taxGroupRate = catalogDocument.Records.Single(record => record.Kind == FrontolRecordKind.Data && record.CommandName == "ADDTAXGROUPRATES");
Assert(taxGroupRate.Fields.Count == 4 && taxGroupRate.Fields[3].Interpretation == "Да",
    "ADDTAXGROUPRATES должна расшифровывать поле «Смена базы».");

var classifier = catalogDocument.Records.Single(record => record.Kind == FrontolRecordKind.Data && record.CommandName == "ADDCLASSIFIERS");
Assert(classifier.Fields.Take(5).Select(field => field.Name).SequenceEqual([
    "Код классификатора", "Код группы классификаторов", "Классификатор или группа", "Наименование классификатора", "Текст"]),
    "ADDCLASSIFIERS должна раскрывать все 5 полей руководства.");
Assert(classifier.Fields[2].Interpretation == "Классификатор", "Тип элемента классификатора 0 должен быть расшифрован.");

var manualCommandsPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, "manual-command-names.txt");
Assert(File.Exists(manualCommandsPath), $"Не найден контрольный список команд руководства: {manualCommandsPath}");
var expectedManualCommands = File.ReadAllLines(manualCommandsPath)
    .Select(FrontolCommandCatalog.Normalize)
    .Where(name => name.Length > 0)
    .Distinct(StringComparer.Ordinal)
    .OrderBy(name => name, StringComparer.Ordinal)
    .ToArray();
var missingManualCommands = expectedManualCommands
    .Where(name => !FrontolCommandCatalog.TryGet(name, out _))
    .ToArray();
var unexpectedCommands = FrontolCommandCatalog.All
    .Select(definition => definition.Name)
    .Except(expectedManualCommands, StringComparer.Ordinal)
    .ToArray();
Assert(missingManualCommands.Length == 0,
    $"В каталоге нет команд из руководства: {string.Join(", ", missingManualCommands)}");
Assert(unexpectedCommands.Length == 0,
    $"В каталоге есть непроверенные команды: {string.Join(", ", unexpectedCommands)}");
Assert(FrontolCommandCatalog.All.Count == expectedManualCommands.Length,
    $"Ожидалось {expectedManualCommands.Length} команд, в каталоге: {FrontolCommandCatalog.All.Count}.");

var referencedSections = FrontolCommandCatalog.All
    .SelectMany(definition => Regex.Matches(definition.ManualReference, @"17\.2\.1\.(\d+)")
        .Select(match => int.Parse(match.Groups[1].Value)))
    .Distinct()
    .ToHashSet();
var missingManualSections = Enumerable.Range(1, 124).Where(section => !referencedSections.Contains(section)).ToArray();
Assert(missingManualSections.Length == 0,
    $"Не покрыты разделы 17.2.1: {string.Join(", ", missingManualSections)}");

foreach (var definition in FrontolCommandCatalog.All)
{
    var schemas = definition.HasVariants
        ? definition.Variants!.Select(variant => variant.Fields)
        : [definition.Fields];
    foreach (var fields in schemas)
    {
        Assert(fields.Select(field => field.Number).SequenceEqual(Enumerable.Range(1, fields.Count)),
            $"$$${definition.Name}: номера полей должны идти без пропусков.");
        Assert(fields.All(field => !string.IsNullOrWhiteSpace(field.Name) &&
                                   !string.IsNullOrWhiteSpace(field.DataType) &&
                                   !string.IsNullOrWhiteSpace(field.Purpose)),
            $"$$${definition.Name}: у каждого поля должны быть имя, тип и назначение.");
    }
}

Assert(FrontolCommandCatalog.TryGet("ADDMARKETINGEVENTS", out var marketingEvents), "Нет ADDMARKETINGEVENTS.");
Assert(marketingEvents.Variants?.Count == 19 && marketingEvents.VariantFieldNumber == 5,
    "ADDMARKETINGEVENTS должна содержать 19 схем по полю 5.");
var messageEventValues = new[] { "1", "1", "", "", "15", "Текст", "0", "0", "0" };
var messageEventFields = marketingEvents.ResolveFields(messageEventValues);
Assert(messageEventFields.Count == 9 && messageEventFields[5].Name == "Текст",
    "Действие 15 ADDMARKETINGEVENTS должно раскрывать схему вывода сообщения.");

Assert(FrontolCommandCatalog.TryGet("ADDMARKETINGCONDITIONS", out var marketingConditions), "Нет ADDMARKETINGCONDITIONS.");
Assert(marketingConditions.Variants?.Count == 24 && marketingConditions.VariantFieldNumber == 2,
    "ADDMARKETINGCONDITIONS должна содержать 24 схемы по полю 2.");
var dateConditionFields = marketingConditions.ResolveFields(new[] { "1", "24", "", "", "" });
Assert(dateConditionFields.Count == 5 && dateConditionFields[4].Name == "Контроль даты",
    "Условие 24 ADDMARKETINGCONDITIONS должно раскрывать схему дат.");

var reportFixturePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, "report-sample.txt");
Assert(File.Exists(reportFixturePath), $"Не найден тестовый отчёт о продажах: {reportFixturePath}");
var reportDocument = new FrontolFileParser().ParseFile(reportFixturePath);
Assert(reportDocument.FileKind == ExchangeFileKind.SalesReportFromFrontol,
    "Файл с шапкой @ / идентификатор БД / номер отчёта должен распознаваться как выгрузка продаж.");
Assert(reportDocument.Records.Count == 6 && reportDocument.DataRecordCount == 3,
    $"В тестовом отчёте ожидалось 6 строк и 3 транзакции, получено {reportDocument.Records.Count}/{reportDocument.DataRecordCount}.");
Assert(reportDocument.Records[0].Fields.Single().Interpretation.Contains("обработан", StringComparison.OrdinalIgnoreCase),
    "Символ @ должен объясняться как признак уже обработанного отчёта.");
var salePosition = reportDocument.Records.Single(record => record.CommandName == "11");
Assert(salePosition.IsProductRecord && salePosition.CodeText == "941" && salePosition.Fields.Count == 44,
    "Транзакция 11 должна раскрываться как товарная строка с 44 документированными полями.");
Assert(salePosition.HasTerminatingDelimiter && salePosition.Fields.All(field => field.Number <= 44),
    "Завершающая точка с запятой отчёта не должна создавать фиктивное поле №45.");
Assert(salePosition.Fields[31].Name == "Тип номенклатуры / маркировки" && salePosition.ProductTypeCode == "0",
    "Поле 32 транзакции товара должно определять вид номенклатуры/маркировки.");
var fiscalClose = reportDocument.Records.Single(record => record.CommandName == "45");
Assert(fiscalClose.Fields[43].Name == "Дата и время расчёта" && fiscalClose.Fields[43].RawValue == "06.07.2026 8:55:17",
    "Поле 44 транзакции 45 должно содержать дату и время фискального расчёта.");
Assert(FrontolSalesTransactionCatalog.All.Count == 56,
    $"В руководстве ожидается 56 типов транзакций, встроено {FrontolSalesTransactionCatalog.All.Count}.");
var expectedTransactionCodes = new[]
{
    "1", "2", "3", "4", "6", "9", "10", "11", "12", "14", "15", "16", "17",
    "21", "22", "23", "24", "25", "26", "27", "29", "30", "31", "32", "33", "34",
    "35", "36", "37", "38", "40", "42", "43", "45", "49", "50", "51", "55", "56",
    "57", "58", "60", "61", "62", "63", "64", "65", "82", "83", "84", "85", "86",
    "87", "88", "120", "121"
};
Assert(FrontolSalesTransactionCatalog.All.Select(definition => definition.Name)
        .OrderBy(code => int.Parse(code))
        .SequenceEqual(expectedTransactionCodes.OrderBy(code => int.Parse(code))),
    "Каталог должен в точности совпадать со списком транзакций на страницах 273-275 руководства.");
Assert(FrontolSalesTransactionCatalog.All.All(definition =>
        definition.Fields.Select(field => field.Number).SequenceEqual(Enumerable.Range(1, 44))),
    "Каждый тип транзакции выгрузки должен объяснять поля №1-44 без пропусков.");
Assert(FrontolSalesTransactionCatalog.All.All(definition =>
        definition.Description.Length > 100 && definition.Description.Contains("Frontol → учётная система", StringComparison.Ordinal)),
    "Каждая транзакция должна иметь развёрнутое объяснение бизнес-смысла и направления данных.");
Assert(FrontolSalesDocumentationCatalog.All.Count >= 30 &&
       FrontolSalesDocumentationCatalog.All.All(example =>
           example.Explanation.Length > 100 && example.ManualReference.Contains("17.2.2", StringComparison.Ordinal)),
    "Справка должна покрывать примеры и особые правила раздела 17.2.2 руководства.");
Assert(FrontolSalesDocumentationCatalog.All.Any(example =>
        example.Title.Contains("Постановка кега", StringComparison.Ordinal) &&
        example.OperationCodes.Contains("27", StringComparer.Ordinal)),
    "В справке должен быть сценарий постановки пивного кега на кран.");

var sampleAnalysis = SalesReportAnalysis.Build(reportDocument.Records);
Assert(sampleAnalysis.Documents.Count == 1 && sampleAnalysis.Documents[0].Items.Count == 1,
    "Тестовая выгрузка должна собираться в один понятный документ с одной товарной позицией.");
Assert(sampleAnalysis.Documents[0].Total == 53.17m && sampleAnalysis.Overview.GrossSales == 53.17m,
    "Если №55 отсутствует, итог закрытого документа должен браться из транзакции ККТ №45, а не из неполного набора позиций.");
Assert(sampleAnalysis.DocumentationExamples.Count == FrontolSalesDocumentationCatalog.All.Count,
    "Все примеры руководства должны отображаться даже при отсутствии в выбранном отчёте.");

var mixedOrganizationsLines = new[]
{
    "@", "1", "900",
    SalesTransaction("1", "42", "100", "0", "0"),
    SalesTransaction("2", "11", "100", "0", "1", "100", "1", "OOO-TOVAR"),
    SalesTransaction("3", "11", "100", "0", "2", "50", "1", "IP-TOVAR"),
    SalesTransaction("4", "40", "100", "0", "0", "130"),
    SalesTransaction("5", "43", "100", "0", "1", "88"),
    SalesTransaction("6", "43", "100", "0", "2", "42"),
    SalesTransaction("7", "36", "100", "0", "0", "20"),
    SalesTransaction("8", "86", "100", "0", "1", "12"),
    SalesTransaction("9", "86", "100", "0", "2", "8"),
    SalesTransaction("10", "49", "100", "0", "1", "100"),
    SalesTransaction("11", "49", "100", "0", "2", "50"),
    SalesTransaction("12", "55", "100", "0", "0", "150"),
    SalesTransaction("13", "42", "101", "1", "0"),
    SalesTransaction("14", "11", "101", "1", "1", "40", "1", "OOO-RETURN"),
    SalesTransaction("15", "33", "101", "1", "0", "40"),
    SalesTransaction("16", "83", "101", "1", "1", "40"),
    SalesTransaction("17", "49", "101", "1", "1", "40"),
    SalesTransaction("18", "55", "101", "1", "0", "40"),
    SalesTransaction("19", "42", "102", "0", "0"),
    SalesTransaction("20", "11", "102", "0", "1", "1000", "1", "CANCELLED-GROUP1"),
    SalesTransaction("21", "56", "102", "0", "0", "1000")
};
var mixedOrganizationsDocument = new FrontolFileParser().ParseLines(
    "mixed-organizations-report.txt", mixedOrganizationsLines, "UTF-8");
var mixedOrganizationsAnalysis = SalesReportAnalysis.Build(mixedOrganizationsDocument.Records);
Assert(mixedOrganizationsAnalysis.Documents.Count == 3 && mixedOrganizationsAnalysis.Overview.CancelledDocumentCount == 1,
    "Продажа, возврат и отменённый чек должны собираться в три отдельных документа.");
Assert(mixedOrganizationsAnalysis.PrintGroups.Count == 2,
    "Группы печати 1 и 2 должны стать двумя отдельными разрезами отчёта.");
var group1 = mixedOrganizationsAnalysis.PrintGroups.Single(group => group.PrintGroupCode == "1");
var group2 = mixedOrganizationsAnalysis.PrintGroups.Single(group => group.PrintGroupCode == "2");
Assert(group1.PrintGroupName == "Группа печати 1" && group1.GrossSales == 100m && group1.Returns == 40m &&
       group1.NetSales == 60m && group1.PaymentTotal == 60m && group1.DocumentCount == 2 && group1.BalanceText == "Сходится",
    "Группа 1 должна получить продажу 100, возврат 40 и оплату 60; отменённый чек на 1000 не должен попасть в итог.");
Assert(group2.PrintGroupName == "Группа печати 2" && group2.GrossSales == 50m && group2.Returns == 0m &&
       group2.NetSales == 50m && group2.PaymentTotal == 50m && group2.BalanceText == "Сходится",
    "Группа 2 должна получить только свою товарную часть и распределённую оплату.");
Assert(mixedOrganizationsAnalysis.Overview.GrossSales == 150m &&
       mixedOrganizationsAnalysis.Overview.Returns == 40m &&
       mixedOrganizationsAnalysis.Overview.NetSales == 110m &&
       mixedOrganizationsAnalysis.Overview.PaymentTotal == 110m &&
       mixedOrganizationsAnalysis.Overview.ProductLineCount == 3 &&
       mixedOrganizationsAnalysis.Products.All(product => product.ProductCode != "CANCELLED-GROUP1"),
    "Общий отчёт должен учитывать фискальную, нефискальную и бонусную оплаты ровно один раз.");

var returnStornoLines = new[]
{
    "@", "1", "901",
    SalesTransaction("1", "42", "200", "1", "1"),
    SalesTransaction("2", "11", "200", "1", "1", "-100", "-1", "RETURNED"),
    SalesTransaction("3", "12", "200", "1", "1", "40", "1", "STORNO-RETURNED"),
    SalesTransaction("4", "58", "200", "1", "1", "-60")
};
var returnStornoDocument = new FrontolFileParser().ParseLines("return-storno-report.txt", returnStornoLines, "UTF-8");
var returnStornoAnalysis = SalesReportAnalysis.Build(returnStornoDocument.Records);
var returnStorno = returnStornoAnalysis.Documents.Single();
Assert(returnStorno.Status == "Нефинансово закрыт" && returnStorno.ItemsAmount == -60m &&
       returnStorno.Quantity == 0m && returnStornoAnalysis.Overview.Returns == 60m,
    "Сторно в возврате должно иметь положительный знак, а №58 — завершать документ без требования оплаты.");

var openDocumentLines = new[]
{
    "@", "1", "902",
    SalesTransaction("1", "42", "210", "0", "1"),
    SalesTransaction("2", "11", "210", "0", "1", "500", "1", "OPEN-SALE")
};
var openDocumentAnalysis = SalesReportAnalysis.Build(new FrontolFileParser()
    .ParseLines("open-report.txt", openDocumentLines, "UTF-8").Records);
Assert(openDocumentAnalysis.Overview.OpenDocumentCount == 1 && openDocumentAnalysis.Overview.GrossSales == 0m &&
       openDocumentAnalysis.Products.Count == 0,
    "Открытый документ должен оставаться в аудите, но не попадать в продажи и товары финансового отчёта.");

var enterpriseLines = new[]
{
    "@", "DB", "903",
    SalesTransaction("1", "42", "300", "0", "1", enterprise: "1"),
    SalesTransaction("2", "11", "300", "0", "1", "100", "1", "A¦Товар предприятия 1", enterprise: "1"),
    SalesTransaction("3", "40", "300", "0", "0", "100", enterprise: "1"),
    SalesTransaction("4", "49", "300", "0", "1", "100", enterprise: "1"),
    SalesTransaction("5", "55", "300", "0", "0", "100", enterprise: "1"),
    SalesTransaction("6", "42", "300", "0", "1", enterprise: "2"),
    SalesTransaction("7", "11", "300", "0", "1", "50", "1", "B¦Товар предприятия 2", enterprise: "2"),
    SalesTransaction("8", "40", "300", "0", "0", "50", enterprise: "2"),
    SalesTransaction("9", "49", "300", "0", "1", "50", enterprise: "2"),
    SalesTransaction("10", "55", "300", "0", "0", "50", enterprise: "2")
};
var enterpriseAnalysis = SalesReportAnalysis.Build(new FrontolFileParser()
    .ParseLines("enterprise-report.txt", enterpriseLines, "UTF-8").Records);
Assert(enterpriseAnalysis.Documents.Count == 2 && enterpriseAnalysis.PrintGroups.Count == 2 &&
       enterpriseAnalysis.PrintGroups.Select(group => group.EnterpriseId).Order().SequenceEqual(new[] { "1", "2" }),
    "Одинаковый номер документа и код группы в разных предприятиях должны образовывать разные документы и разрезы 1С.");
Assert(enterpriseAnalysis.Products.Any(product => product.ProductCode == "A" && product.ProductDisplayName == "Товар предприятия 1"),
    "Идентификатор и наименование товара, переданные через разделитель, должны разбираться отдельно.");

var kegLines = new[]
{
    "@", "1", "904",
    SalesTransaction("1", "42", "400", "27", "2"),
    SalesTransaction("2", "11", "400", "27", "2", "1200", "20", "KEG¦Пивной кег", markCode: "MARK-KEG"),
    SalesTransaction("3", "49", "400", "27", "2", "1200"),
    SalesTransaction("4", "55", "400", "27", "2", "1200")
};
var kegAnalysis = SalesReportAnalysis.Build(new FrontolFileParser()
    .ParseLines("keg-report.txt", kegLines, "UTF-8").Records);
Assert(kegAnalysis.Kegs.Count == 1 && kegAnalysis.Kegs[0].KegCode == "MARK-KEG" &&
       kegAnalysis.Kegs[0].Volume == 20m && kegAnalysis.Products.Count == 0,
    "Постановка кега должна показываться в отдельной аналитике и не смешиваться с розничными продажами.");

var gapLines = new[]
{
    "@", "1", "905",
    SalesTransaction("1", "42", "500", "0", "1"),
    SalesTransaction("3", "55", "500", "0", "1", "0")
};
var gapAnalysis = SalesReportAnalysis.Build(new FrontolFileParser()
    .ParseLines("gap-report.txt", gapLines, "UTF-8").Records);
Assert(gapAnalysis.Diagnostics.Any(diagnostic => diagnostic.Category == "Диапазон транзакций" && diagnostic.Severity == "Ошибка"),
    "Пропуск номера транзакции должен отображаться во вкладке сверки.");

var shiftLines = new[]
{
    "@", "1", "906",
    SalesTransaction("1", "42", "600", "0", "1"),
    SalesTransaction("2", "11", "600", "0", "1", "100", "1", "SHIFT-SALE"),
    SalesTransaction("3", "40", "600", "0", "0", "100"),
    SalesTransaction("4", "49", "600", "0", "1", "100"),
    SalesTransaction("5", "55", "600", "0", "0", "100"),
    SalesTransaction("6", "61", "700", "10", "0", "100"),
    SalesTransaction("7", "63", "700", "10", "0", "100")
};
var shiftReport = new FrontolFileParser().ParseLines("shift-report.txt", shiftLines, "UTF-8");
var shiftAnalysis = SalesReportAnalysis.Build(shiftReport.Records,
    [new SalesReportHistoryEntry("1", "903", 1000, 1100, "old-report.txt", DateTime.Today)]);
var shift = shiftAnalysis.Shifts.Single();
Assert(shift.NetSales == 100m && shift.ProgramSalesTotal == 100m && shift.HardwareSalesTotal == 100m &&
       shift.ReconciliationText == "Сходится",
    "Смена должна сверять документы с программным итогом №61 и аппаратным итогом №63.");
Assert(shiftAnalysis.Diagnostics.Any(diagnostic => diagnostic.Category == "История отчётов" &&
                                                  diagnostic.Message.Contains("904–905", StringComparison.Ordinal)),
    "История должна предупреждать о пропущенных порядковых номерах отчётов одной базы.");

foreach (var fullPath in args.Skip(1))
{
    var fullDocument = new FrontolFileParser().ParseFile(fullPath);
    if (fullDocument.FileKind == ExchangeFileKind.SalesReportFromFrontol)
    {
        var unknownTransactionTypes = fullDocument.Records
            .Where(record => record.Kind == FrontolRecordKind.Data && record.Definition is null)
            .Select(record => record.CommandName)
            .Distinct()
            .ToArray();
        Assert(unknownTransactionTypes.Length == 0,
            $"В полном отчёте остались неизвестные транзакции: {string.Join(", ", unknownTransactionTypes)}");
        Assert(fullDocument.ErrorCount == 0,
            $"Полный отчёт о продажах должен разбираться без ошибок: {fullDocument.ErrorCount}.");
        var fullAnalysis = SalesReportAnalysis.Build(fullDocument.Records);
        Assert(fullAnalysis.Documents.Count > 0 && fullAnalysis.DocumentationExamples.Count == FrontolSalesDocumentationCatalog.All.Count,
            "Полный отчёт должен собираться в документы и сохранять всю справку руководства.");
        Console.WriteLine($"Полный отчёт: строк {fullDocument.Records.Count:N0}; транзакций {fullDocument.DataRecordCount:N0}; документов {fullAnalysis.Documents.Count:N0}; групп печати {fullAnalysis.PrintGroups.Count:N0}; итог {fullAnalysis.Overview.NetSales:N2}; незавершённых {fullAnalysis.Overview.OpenDocumentCount}; диагностик {fullAnalysis.Diagnostics.Count}; ошибок {fullDocument.ErrorCount}; предупреждений {fullDocument.WarningCount}.");
        continue;
    }

    var unknownFullCommands = fullDocument.Records
        .Where(record => record.Kind == FrontolRecordKind.Command && record.Definition is null)
        .Select(record => record.CommandName)
        .ToArray();
    Assert(unknownFullCommands.Length == 0,
        $"В полном файле остались неизвестные команды: {string.Join(", ", unknownFullCommands)}");
    Assert(fullDocument.ErrorCount == 0 && fullDocument.WarningCount == 0,
        $"Полный файл должен разбираться без ошибок и предупреждений: {fullDocument.ErrorCount}/{fullDocument.WarningCount}");
    Console.WriteLine($"Полный файл загрузки: строк {fullDocument.Records.Count:N0}; ошибок {fullDocument.ErrorCount}; предупреждений {fullDocument.WarningCount}.");
}

Console.WriteLine("Smoke-тесты пройдены.");
Console.WriteLine($"Кодировка: {document.EncodingName}");
Console.WriteLine($"Строк: {document.Records.Count}; команд: {document.CommandCount}; данных: {document.DataRecordCount}");
Console.WriteLine($"Товар: {product.Fields[0].RawValue}; тип: {product.Fields[54].Interpretation}; мера: {product.Fields[65].Interpretation}");
return 0;

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        Console.Error.WriteLine($"ОШИБК SMOKE-ТЕСТА: {message}");
        Environment.Exit(1);
    }
}

static string SalesTransaction(
    string transactionNumber,
    string transactionType,
    string documentNumber,
    string operationCode,
    string printGroupCode,
    string amount = "0",
    string quantity = "0",
    string productCode = "",
    string enterprise = "1",
    string markCode = "")
{
    var fields = new string[44];
    Array.Fill(fields, string.Empty);
    fields[0] = transactionNumber;
    fields[1] = "01.08.2026";
    fields[2] = "12:00:00";
    fields[3] = transactionType;
    fields[4] = "1";
    fields[5] = documentNumber;
    fields[6] = "7";
    fields[7] = productCode;
    fields[8] = transactionType is "32" or "33" or "34" or "36" or "40" or "43" or "82" or "83" or "84" or "86"
        ? "PAY"
        : string.Empty;
    fields[9] = transactionType switch
    {
        "36" or "86" => "6",
        "32" or "33" or "34" or "40" or "43" or "82" or "83" or "84" => "0",
        _ => amount
    };
    fields[10] = quantity;
    fields[11] = amount;
    fields[12] = operationCode;
    fields[13] = "10";
    fields[15] = amount;
    fields[16] = printGroupCode;
    fields[22] = operationCode == "1" ? "2" : "1";
    fields[25] = $"1/10/{documentNumber}";
    fields[26] = enterprise;
    fields[31] = "0";
    fields[32] = markCode;
    return string.Join(';', fields) + ";";
}

sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
