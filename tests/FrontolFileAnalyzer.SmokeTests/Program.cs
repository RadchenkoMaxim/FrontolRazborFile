using FrontolFileAnalyzer.Core;
using System.Text.RegularExpressions;

if (args.Length is < 1 or > 2 || !File.Exists(args[0]) || (args.Length == 2 && !File.Exists(args[1])))
{
    Console.Error.WriteLine("Передайте путь к тестовому base.txt и, при необходимости, к полному файлу.");
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
Assert(!product.Fields[66].WasProvided && !product.Fields[67].WasProvided, "Поля 67 и 68 должны отмечаться как не переданные.");
var unprovidedField = product.Fields.First(field => field.Interpretation == "Не передано");
Assert(!unprovidedField.HasExtendedInterpretation, "Значение «Не передано» не должно создавать большой блок расшифровки.");

var deleteBarcodes = document.Records.Single(record => record.LineNumber == 4);
Assert(deleteBarcodes.Fields[1].Interpretation.Contains("все штрихкоды", StringComparison.OrdinalIgnoreCase),
    "Пустое поле штрихкода должно объяснять удаление всех штрихкодов товара.");

var classifierLink = document.Records.Single(record => record.LineNumber == 8);
Assert(classifierLink.Fields.Count == 4, "Завершающая точка с запятой должна отображаться отдельным пустым сегментом.");
Assert(classifierLink.Fields[3].Severity == IssueSeverity.Info, "Завершающий пустой сегмент должен иметь информационный статус.");

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

if (args.Length == 2)
{
    var fullDocument = new FrontolFileParser().ParseFile(args[1]);
    var unknownFullCommands = fullDocument.Records
        .Where(record => record.Kind == FrontolRecordKind.Command && record.Definition is null)
        .Select(record => record.CommandName)
        .ToArray();
    Assert(unknownFullCommands.Length == 0,
        $"В полном файле остались неизвестные команды: {string.Join(", ", unknownFullCommands)}");
    Assert(fullDocument.ErrorCount == 0 && fullDocument.WarningCount == 0,
        $"Полный файл должен разбираться без ошибок и предупреждений: {fullDocument.ErrorCount}/{fullDocument.WarningCount}");
    Console.WriteLine($"Полный файл: строк {fullDocument.Records.Count:N0}; ошибок {fullDocument.ErrorCount}; предупреждений {fullDocument.WarningCount}.");
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

sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
