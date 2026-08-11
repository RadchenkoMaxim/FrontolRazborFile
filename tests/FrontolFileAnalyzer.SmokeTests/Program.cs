using FrontolFileAnalyzer.Core;

if (args.Length != 1 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Передайте путь к тестовому base.txt.");
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

Console.WriteLine("Smoke-тесты пройдены.");
Console.WriteLine($"Кодировка: {document.EncodingName}");
Console.WriteLine($"Строк: {document.Records.Count}; команд: {document.CommandCount}; данных: {document.DataRecordCount}");
Console.WriteLine($"Товар: {product.Fields[0].RawValue}; тип: {product.Fields[54].Interpretation}; мера: {product.Fields[65].Interpretation}");
return 0;

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
