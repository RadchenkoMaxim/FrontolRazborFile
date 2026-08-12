using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using FrontolFileAnalyzer.Core;

namespace FrontolFileAnalyzer;

public sealed class FieldVisibilityOption : INotifyPropertyChanged
{
    private bool _isVisible;

    public required int Number { get; init; }
    public required string Name { get; init; }
    public required bool IsFilled { get; init; }
    public string StateText => IsFilled ? "заполнено" : "пусто";

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value)
            {
                return;
            }

            _isVisible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class FieldVisibilityWindow : Window
{
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const double WorkingAreaMargin = 24d;

    private readonly ObservableCollection<FieldVisibilityOption> _fields;
    private string _searchText = string.Empty;

    public FieldVisibilityWindow(string commandName, IReadOnlyList<AnalyzedField> fields, IReadOnlySet<int> hiddenNumbers)
    {
        CommandTitle = $"Поля команды $$${commandName}";
        _fields = new ObservableCollection<FieldVisibilityOption>(fields.Select(field => new FieldVisibilityOption
        {
            Number = field.Number,
            Name = field.Name,
            IsFilled = !string.IsNullOrEmpty(field.RawValue),
            IsVisible = !hiddenNumbers.Contains(field.Number)
        }));
        FieldsView = CollectionViewSource.GetDefaultView(_fields);
        FieldsView.Filter = FilterField;
        InitializeComponent();
        DataContext = this;
        SourceInitialized += (_, _) => ConstrainToWorkingArea();
        Loaded += (_, _) => SearchBox.Focus();
    }

    public string CommandTitle { get; }
    public ICollectionView FieldsView { get; }
    public IReadOnlySet<int> HiddenNumbers => _fields.Where(item => !item.IsVisible).Select(item => item.Number).ToHashSet();

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = (sender as TextBox)?.Text?.Trim() ?? string.Empty;
        FieldsView.Refresh();
    }

    private bool FilterField(object item) => item is FieldVisibilityOption field &&
        (_searchText.Length == 0 || field.Number.ToString().Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
         field.Name.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase));

    private void SelectAll_Click(object sender, RoutedEventArgs e) => SetVisible(_ => true);
    private void SelectNone_Click(object sender, RoutedEventArgs e) => SetVisible(_ => false);
    private void OnlyFilled_Click(object sender, RoutedEventArgs e) => SetVisible(field => field.IsFilled);

    private void SetVisible(Func<FieldVisibilityOption, bool> selector)
    {
        foreach (var field in FieldsView.Cast<FieldVisibilityOption>().ToArray())
        {
            field.IsVisible = selector(field);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

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
}
