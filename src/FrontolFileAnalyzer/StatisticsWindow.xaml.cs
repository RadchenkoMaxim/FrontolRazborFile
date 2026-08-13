using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using FrontolFileAnalyzer.Core;

namespace FrontolFileAnalyzer;

public partial class StatisticsWindow : Window
{
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const double WorkingAreaMargin = 24d;
    private const double StackedTablesWidth = 1000d;
    private const double WideMinimumContentHeight = 520d;
    private const double StackedMinimumContentHeight = 760d;

    private static readonly Brush BlueAccent = CreateBrush("#2F6F9F");
    private static readonly Brush GreenAccent = CreateBrush("#3D7954");
    private static readonly Brush YellowAccent = CreateBrush("#A66A00");
    private static readonly Brush RedAccent = CreateBrush("#B54848");
    private static readonly Brush GrayAccent = CreateBrush("#7A838B");

    public StatisticsWindow(IReadOnlyList<ParsedRecord> records, int changeCount)
    {
        ArgumentNullException.ThrowIfNull(records);
        var salesReport = records.Any(record => record.FileKind == ExchangeFileKind.SalesReportFromFrontol);

        var productRows = records
            .Where(record => record.Kind == FrontolRecordKind.Data && record.IsProductCommand)
            .ToArray();

        var serviceRowCount = records.Count(record =>
            record.Kind == FrontolRecordKind.Data && !record.IsProductCommand);
        var commandCount = salesReport
            ? records.Where(record => record.Kind == FrontolRecordKind.Data && !string.IsNullOrWhiteSpace(record.CommandName))
                .Select(record => record.CommandName).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            : records.Count(record => record.Kind == FrontolRecordKind.Command);
        var errorRowCount = records.Count(record => record.Severity == IssueSeverity.Error);
        var warningRowCount = records.Count(record => record.Severity == IssueSeverity.Warning);
        var normalizedChangeCount = Math.Max(0, changeCount);

        Cards =
        [
            new StatisticCard("Всего строк", records.Count, BlueAccent),
            new StatisticCard(salesReport ? "Товарных операций" : "Товарных строк", productRows.Length, GreenAccent),
            new StatisticCard(salesReport ? "Других транзакций" : "Служебных", serviceRowCount, GrayAccent),
            new StatisticCard(salesReport ? "Типов транзакций" : "Команд", commandCount, BlueAccent),
            new StatisticCard("Ошибок (строк)", errorRowCount, RedAccent),
            new StatisticCard("Предупр. (строк)", warningRowCount, YellowAccent),
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
                group.First().CommandText,
                group.Count(record => record.Kind == FrontolRecordKind.Data),
                group.Count(record => record.Severity == IssueSeverity.Error),
                group.Count(record => record.Severity == IssueSeverity.Warning)))
            .OrderByDescending(row => row.DataRowCount)
            .ThenBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        MarkingSummary = $"{MarkingRows.Count:N0} видов";
        CommandSummary = salesReport ? $"{CommandRows.Count:N0} типов" : $"{CommandRows.Count:N0} уникальных";
        SummarySubtitle = salesReport
            ? "Состав отчёта, распределение маркировки и диагностика транзакций"
            : "Состав файла, распределение маркировки и диагностика команд";
        EntitySectionTitle = salesReport ? "Типы транзакций" : "Команды файла";

        InitializeComponent();
        Title = salesReport ? "Сводная статистика отчёта о продажах Frontol" : "Сводная статистика файла загрузки Frontol";
        DataContext = this;
        SourceInitialized += (_, _) => ConstrainToWorkingArea();
    }

    public IReadOnlyList<StatisticCard> Cards { get; }
    public IReadOnlyList<MarkingStatisticRow> MarkingRows { get; }
    public IReadOnlyList<CommandStatisticRow> CommandRows { get; }
    public string MarkingSummary { get; }
    public string CommandSummary { get; }
    public string SummarySubtitle { get; }
    public string EntitySectionTitle { get; }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateResponsiveLayout();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout();
    }

    private void UpdateResponsiveLayout()
    {
        if (!IsInitialized)
        {
            return;
        }

        var useStackedTables = PageScroller.ActualWidth < StackedTablesWidth;

        if (useStackedTables)
        {
            TablesLeftColumn.Width = new GridLength(1, GridUnitType.Star);
            TablesColumnGap.Width = new GridLength(0);
            TablesRightColumn.Width = new GridLength(0);
            TablesTopRow.Height = new GridLength(1, GridUnitType.Star);
            TablesRowGap.Height = new GridLength(16);
            TablesBottomRow.Height = new GridLength(1, GridUnitType.Star);

            Grid.SetRow(MarkingSection, 0);
            Grid.SetColumn(MarkingSection, 0);
            Grid.SetRow(CommandSection, 2);
            Grid.SetColumn(CommandSection, 0);
        }
        else
        {
            TablesLeftColumn.Width = new GridLength(1.08, GridUnitType.Star);
            TablesColumnGap.Width = new GridLength(16);
            TablesRightColumn.Width = new GridLength(0.92, GridUnitType.Star);
            TablesTopRow.Height = new GridLength(1, GridUnitType.Star);
            TablesRowGap.Height = new GridLength(0);
            TablesBottomRow.Height = new GridLength(0);

            Grid.SetRow(MarkingSection, 0);
            Grid.SetColumn(MarkingSection, 0);
            Grid.SetRow(CommandSection, 0);
            Grid.SetColumn(CommandSection, 2);
        }

        var viewportHeight = PageScroller.ViewportHeight;
        if (!double.IsFinite(viewportHeight) || viewportHeight <= 0)
        {
            viewportHeight = PageScroller.ActualHeight;
        }

        var verticalMargins = ContentRoot.Margin.Top + ContentRoot.Margin.Bottom;
        var availableContentHeight = Math.Max(0, viewportHeight - verticalMargins);
        var minimumContentHeight = useStackedTables
            ? StackedMinimumContentHeight
            : WideMinimumContentHeight;

        ContentRoot.Height = Math.Max(availableContentHeight, minimumContentHeight);
    }

    private void ConstrainToWorkingArea()
    {
        var windowHandle = new WindowInteropHelper(this).Handle;
        var ownerHandle = Owner is null ? IntPtr.Zero : new WindowInteropHelper(Owner).Handle;
        var referenceHandle = ownerHandle != IntPtr.Zero ? ownerHandle : windowHandle;
        var monitorHandle = MonitorFromWindow(referenceHandle, MonitorDefaultToNearest);

        var monitorInfo = new MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>()
        };

        if (monitorHandle != IntPtr.Zero && GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            var dpi = VisualTreeHelper.GetDpi(Owner ?? this);
            ApplyWorkingAreaLimits(
                monitorInfo.WorkArea.Width / dpi.DpiScaleX,
                monitorInfo.WorkArea.Height / dpi.DpiScaleY);
            return;
        }

        ApplyWorkingAreaLimits(SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
    }

    private void ApplyWorkingAreaLimits(double workingWidth, double workingHeight)
    {
        var availableWidth = Math.Max(1, Math.Floor(workingWidth - WorkingAreaMargin));
        var availableHeight = Math.Max(1, Math.Floor(workingHeight - WorkingAreaMargin));
        var constrainedMaxWidth = Math.Min(MaxWidth, availableWidth);
        var constrainedMaxHeight = Math.Min(MaxHeight, availableHeight);

        MinWidth = Math.Min(MinWidth, constrainedMaxWidth);
        MinHeight = Math.Min(MinHeight, constrainedMaxHeight);
        MaxWidth = constrainedMaxWidth;
        MaxHeight = constrainedMaxHeight;
        Width = Math.Min(Width, constrainedMaxWidth);
        Height = Math.Min(Height, constrainedMaxHeight);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

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
    string DisplayName,
    int DataRowCount,
    int ErrorCount,
    int WarningCount);
