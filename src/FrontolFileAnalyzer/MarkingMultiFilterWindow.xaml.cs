using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;

namespace FrontolFileAnalyzer;

public partial class MarkingMultiFilterWindow : Window, INotifyPropertyChanged
{
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const double WorkingAreaMargin = 24;

    private readonly ObservableCollection<SelectableMarkingOption> _options;
    private string _searchText = string.Empty;
    private string _selectionSummary = string.Empty;

    public MarkingMultiFilterWindow(
        IEnumerable<MarkingMultiFilterOption> options,
        HashSet<string>? selectedCodes)
    {
        ArgumentNullException.ThrowIfNull(options);

        var selected = selectedCodes is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(selectedCodes, StringComparer.OrdinalIgnoreCase);

        _options = new ObservableCollection<SelectableMarkingOption>(
            options
                .Where(option => !string.IsNullOrWhiteSpace(option.Code))
                .GroupBy(option => option.Code, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(option => ParseNumericCode(option.Code!))
                .ThenBy(option => option.Code, StringComparer.OrdinalIgnoreCase)
                .Select(option => new SelectableMarkingOption(
                    option.Code!,
                    string.IsNullOrWhiteSpace(option.Name) ? "Без названия" : option.Name,
                    Math.Max(0, option.Count),
                    selected.Contains(option.Code!),
                    SelectionChanged)));

        SelectedCodes = selected;
        OptionsView = CollectionViewSource.GetDefaultView(_options);
        OptionsView.Filter = FilterOption;

        InitializeComponent();
        DataContext = this;
        UpdateSelectionSummary();
        SourceInitialized += (_, _) => ConstrainToWorkingArea();
        Loaded += (_, _) => SearchBox.Focus();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICollectionView OptionsView { get; }

    /// <summary>
    /// Выбранные коды после подтверждения окна. Результат следует читать при DialogResult == true.
    /// </summary>
    public HashSet<string> SelectedCodes { get; private set; }

    public string SelectionSummary
    {
        get => _selectionSummary;
        private set
        {
            if (_selectionSummary == value)
            {
                return;
            }

            _selectionSummary = value;
            OnPropertyChanged();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = (sender as TextBox)?.Text?.Trim() ?? string.Empty;
        OptionsView.Refresh();
        UpdateSelectionSummary();
    }

    private bool FilterOption(object item)
    {
        if (item is not SelectableMarkingOption option || _searchText.Length == 0)
        {
            return item is SelectableMarkingOption;
        }

        return option.Code.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
               option.Name.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase);
    }

    private void SelectVisible_Click(object sender, RoutedEventArgs e) => SetVisibleSelection(true);

    private void ClearVisible_Click(object sender, RoutedEventArgs e) => SetVisibleSelection(false);

    private void SetVisibleSelection(bool isSelected)
    {
        foreach (var option in OptionsView.Cast<SelectableMarkingOption>().ToArray())
        {
            option.IsSelected = isSelected;
        }

        UpdateSelectionSummary();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        SelectedCodes = _options
            .Where(option => option.IsSelected)
            .Select(option => option.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        DialogResult = true;
    }

    private void SelectionChanged() => UpdateSelectionSummary();

    private void UpdateSelectionSummary()
    {
        var selectedCount = _options.Count(option => option.IsSelected);
        var visibleCount = OptionsView.Cast<object>().Count();
        SelectionSummary = selectedCount == 0
            ? _searchText.Length == 0 ? $"Все виды ({_options.Count})" : $"Найдено: {visibleCount} · фильтр: все виды"
            : _searchText.Length == 0
                ? $"Выбрано: {selectedCount} из {_options.Count}"
                : $"Найдено: {visibleCount} · выбрано: {selectedCount}";
    }

    private static int ParseNumericCode(string code) =>
        int.TryParse(code, out var numericCode) ? numericCode : int.MaxValue;

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

    private sealed class SelectableMarkingOption : INotifyPropertyChanged
    {
        private readonly Action _selectionChanged;
        private bool _isSelected;

        public SelectableMarkingOption(
            string code,
            string name,
            int count,
            bool isSelected,
            Action selectionChanged)
        {
            Code = code;
            Name = name;
            Count = count;
            _isSelected = isSelected;
            _selectionChanged = selectionChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Code { get; }
        public string Name { get; }
        public int Count { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                _selectionChanged();
            }
        }
    }
}

public sealed record MarkingMultiFilterOption(string Code, string Name, int Count);
