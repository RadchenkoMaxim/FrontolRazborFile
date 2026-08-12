using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FrontolFileAnalyzer.Core;

namespace FrontolFileAnalyzer;

public partial class StatisticsWindow : Window
{
    private static readonly Brush BlueAccent = CreateBrush("#2F6F9F");
    private static readonly Brush GreenAccent = CreateBrush("#3D7954");
    private static readonly Brush YellowAccent = CreateBrush("#A66A00");
    private static readonly Brush RedAccent = CreateBrush("#B54848");
    private static readonly Brush GrayAccent = CreateBrush("#7A838B");

    public StatisticsWindow(IReadOnlyList<ParsedRecord> records, int changeCount)
    {
        ArgumentNullException.ThrowIfNull(records);

        var productRows = records
            .Where(record => record.Kind == FrontolRecordKind.Data && record.IsProductCommand)
            .ToArray();

        var serviceRowCount = records.Count(record =>
            record.Kind == FrontolRecordKind.Data && !record.IsProductCommand);
        var commandCount = records.Count(record => record.Kind == FrontolRecordKind.Command);
        var errorCount = records.Count(record => record.Severity == IssueSeverity.Error);
        var warningCount = records.Count(record => record.Severity == IssueSeverity.Warning);
        var normalizedChangeCount = Math.Max(0, changeCount);

        Cards =
        [
            new StatisticCard("Всего строк", records.Count, BlueAccent),
            new StatisticCard("Товарных строк", productRows.Length, GreenAccent),
            new StatisticCard("Служебных", serviceRowCount, GrayAccent),
            new StatisticCard("Команд", commandCount, BlueAccent),
            new StatisticCard("Ошибок", errorCount, RedAccent),
            new StatisticCard("Предупреждений", warningCount, YellowAccent),
            new StatisticCard("Изменений", normalizedChangeCount, YellowAccent)
        ];

        MarkingRows = productRows
            .GroupBy(record => new
            {
                Code = string.IsNullOrWhiteSpace(record.ProductTypeCode) ? "—" : record.ProductTypeCode,
                Name = string.IsNullOrWhiteSpace(record.ProductTypeText)
                    ? "Тип не указан"
                    : record.ProductTypeText
            })
            .Select(group => new MarkingStatisticRow(
                group.Key.Code,
                group.Key.Name,
                group.Count(),
                productRows.Length == 0 ? 0d : group.Count() * 100d / productRows.Length))
            .OrderByDescending(row => row.Count)
            .ThenBy(row => row.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        CommandRows = records
            .Where(record => !string.IsNullOrWhiteSpace(record.CommandName))
            .GroupBy(record => record.CommandName!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CommandStatisticRow(
                group.Key,
                group.Count(record => record.Kind == FrontolRecordKind.Data),
                group.Count(record => record.Severity == IssueSeverity.Error),
                group.Count(record => record.Severity == IssueSeverity.Warning)))
            .OrderByDescending(row => row.DataRowCount)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        MarkingSummary = $"{MarkingRows.Count:N0} видов";
        CommandSummary = $"{CommandRows.Count:N0} уникальных";

        InitializeComponent();
        DataContext = this;
    }

    public IReadOnlyList<StatisticCard> Cards { get; }
    public IReadOnlyList<MarkingStatisticRow> MarkingRows { get; }
    public IReadOnlyList<CommandStatisticRow> CommandRows { get; }
    public string MarkingSummary { get; }
    public string CommandSummary { get; }

    private static Brush CreateBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }
}

public sealed record StatisticCard(string Caption, int Value, Brush AccentBrush)
{
    public string FormattedValue => Value.ToString("N0", CultureInfo.CurrentCulture);
}

public sealed record MarkingStatisticRow(string Code, string Name, int Count, double Share)
{
    public string ShareText => Share.ToString("0.0", CultureInfo.CurrentCulture) + " %";
}

public sealed record CommandStatisticRow(
    string Name,
    int DataRowCount,
    int ErrorCount,
    int WarningCount)
{
    public string DisplayName => "$$$" + Name;
}
