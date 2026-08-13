using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using FrontolFileAnalyzer.Core;

namespace FrontolFileAnalyzer;

public partial class CommandReferenceWindow : Window, INotifyPropertyChanged
{
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const double WorkingAreaMargin = 24d;

    private string _searchText = string.Empty;
    private CommandDefinition? _selectedCommand;
    private CommandVariant? _selectedVariant;
    private IReadOnlyList<CommandVariant> _availableVariants = [];
    private IReadOnlyList<FieldDefinition> _displayedFields = [];
    private readonly IReadOnlyList<CommandDefinition> _definitions;
    private readonly ExchangeFileKind _fileKind;

    public CommandReferenceWindow(ExchangeFileKind fileKind = ExchangeFileKind.UploadToFrontol)
    {
        _fileKind = fileKind;
        _definitions = fileKind == ExchangeFileKind.SalesReportFromFrontol
            ? FrontolSalesTransactionCatalog.All
            : FrontolCommandCatalog.All;
        InitializeComponent();
        Title = fileKind == ExchangeFileKind.SalesReportFromFrontol
            ? "Транзакции и поля отчёта о продажах Frontol 6"
            : "Команды и поля загрузки Frontol 6";
        CommandsView = CollectionViewSource.GetDefaultView(_definitions);
        CommandsView.Filter = FilterCommand;
        SelectedCommand = _definitions.FirstOrDefault();
        DataContext = this;
        SourceInitialized += (_, _) => ConstrainToWorkingArea();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICollectionView CommandsView { get; }
    public string CoverageText => _fileKind == ExchangeFileKind.SalesReportFromFrontol
        ? $"Встроено типов транзакций: {_definitions.Count} · для каждого объяснены поля №1-44"
        : $"Встроено команд: {_definitions.Count}";
    public string SearchLabel => _fileKind == ExchangeFileKind.SalesReportFromFrontol ? "Поиск транзакции:" : "Поиск команды:";
    public string CollectionLabel => _fileKind == ExchangeFileKind.SalesReportFromFrontol ? "Транзакции выгрузки" : "Команды загрузки";
    public string EmptyDefinitionText => _fileKind == ExchangeFileKind.SalesReportFromFrontol
        ? "Для этого типа транзакции нет отдельных полей."
        : "Эта команда не требует строк данных.";
    public IReadOnlyList<CommandVariant> AvailableVariants => _availableVariants;
    public IReadOnlyList<FieldDefinition> DisplayedFields => _displayedFields;
    public bool HasDisplayedFields => _displayedFields.Count > 0;

    public CommandDefinition? SelectedCommand
    {
        get => _selectedCommand;
        set
        {
            if (Equals(_selectedCommand, value))
            {
                return;
            }

            _selectedCommand = value;
            OnPropertyChanged();
            _availableVariants = value?.Variants ?? [];
            OnPropertyChanged(nameof(AvailableVariants));
            SelectedVariant = _availableVariants.FirstOrDefault();
            if (_availableVariants.Count == 0)
            {
                SetDisplayedFields(value?.Fields ?? []);
            }
        }
    }

    public CommandVariant? SelectedVariant
    {
        get => _selectedVariant;
        set
        {
            if (Equals(_selectedVariant, value))
            {
                return;
            }

            _selectedVariant = value;
            OnPropertyChanged();
            SetDisplayedFields(value?.Fields ?? SelectedCommand?.Fields ?? []);
        }
    }

    private bool FilterCommand(object item)
    {
        if (item is not CommandDefinition command || _searchText.Length == 0)
        {
            return true;
        }

        return command.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
               command.DisplayName.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase) ||
               command.Description.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized || DataContext is null)
        {
            return;
        }

        _searchText = SearchBox.Text.Trim();
        CommandsView.Refresh();
        SelectedCommand = CommandsView.Cast<CommandDefinition>().FirstOrDefault();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void SetDisplayedFields(IReadOnlyList<FieldDefinition> fields)
    {
        _displayedFields = fields;
        OnPropertyChanged(nameof(DisplayedFields));
        OnPropertyChanged(nameof(HasDisplayedFields));
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

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
}
