using System.Collections.ObjectModel;

namespace FrontolFileAnalyzer.Core;

public sealed record FrontolCodeReference(string Code, string Name, string Description);

public static class FrontolReferenceCatalog
{
    public static IReadOnlyList<FrontolCodeReference> ProductTypes { get; } =
    [
        Item("0", "Обычный товар"),
        Item("1", "Алкогольная продукция"),
        Item("2", "Изделия из меха"),
        Item("3", "Лекарственные препараты"),
        Item("4", "Табачная продукция"),
        Item("5", "Обувь"),
        Item("6", "Лотерея"),
        Item("7", "Иная маркированная продукция"),
        Item("8", "Фототовары"),
        Item("9", "Парфюмерная продукция"),
        Item("10", "Шины"),
        Item("11", "Товары легкой промышленности"),
        Item("12", "Альтернативная табачная продукция"),
        Item("13", "Молочная продукция"),
        Item("14", "Ювелирные изделия"),
        Item("15", "Вода"),
        Item("16", "Никотиносодержащая продукция"),
        Item("17", "Фасованное пиво"),
        Item("18", "Разливное пиво"),
        Item("19", "БАДы"),
        Item("20", "Антисептики"),
        Item("21", "Медицинские изделия"),
        Item("22", "Кресла-коляски"),
        Item("23", "Безалкогольные напитки"),
        Item("24", "Средства реабилитации"),
        Item("25", "Безалкогольное пиво"),
        Item("26", "Икра осетровых и лососевых рыб"),
        Item("27", "Велосипеды"),
        Item("28", "Ветеринарные препараты"),
        Item("29", "Корма для животных"),
        Item("30", "Растительные масла"),
        Item("31", "Слабоалкогольные напитки"),
        Item("32", "Консервированные продукты"),
        Item("33", "Бакалея"),
        Item("34", "Моторные масла"),
        Item("35", "Спортивное питание"),
        Item("36", "Детские товары"),
        Item("101", "Табачная продукция (Казахстан)")
    ];

    public static IReadOnlyDictionary<string, string> ProductTypeValues { get; } =
        new ReadOnlyDictionary<string, string>(ProductTypes.ToDictionary(
            item => item.Code,
            item => item.Name,
            StringComparer.OrdinalIgnoreCase));

    public static IReadOnlyList<FrontolCodeReference> RelatedMarkingCodes { get; } =
    [
        new("52 = 0", "Регистрация без маркировки запрещена", "Поле 52 товарной команды."),
        new("52 = 1", "Регистрация без маркировки разрешена", "Поле 52. Значение по умолчанию - 1."),
        new("56 = 0", "Алкоголь с акцизной маркой", "Поле 56 товарной команды."),
        new("56 = 1", "Алкоголь без акцизной марки", "Поле 56 товарной команды."),
        new("65 = 0", "Проверка по штрихкодам товара", "Поле 65. Значение по умолчанию - 0."),
        new("65 = 1", "Проверка по штрихкоду регистрации", "Поле 65 товарной команды.")
    ];

    private static FrontolCodeReference Item(string code, string name) =>
        new(code, name, "Поле 55 - тип номенклатуры / маркировки");
}
