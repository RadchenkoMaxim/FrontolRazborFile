using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using FrontolFileAnalyzer.Core;

namespace FrontolFileAnalyzer;

public partial class MarkingCodesWindow : Window
{
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const double WorkingAreaMargin = 24d;

    private readonly ObservableCollection<FrontolCodeReference> _codes = new(FrontolReferenceCatalog.ProductTypes);
    private string _searchText = string.Empty;

    public MarkingCodesWindow()
    {
        CodesView = CollectionViewSource.GetDefaultView(_codes);
        CodesView.Filter = FilterCode;
        RelatedCodes = FrontolReferenceCatalog.RelatedMarkingCodes;
        InitializeComponent();
        DataContext = this;
        SourceInitialized += (_, _) => ConstrainToWorkingArea();
    }

    public ICollectionView CodesView { get; }
    public IReadOnlyList<FrontolCodeReference> RelatedCodes { get; }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = (sender as TextBox)?.Text?.Trim() ?? string.Empty;
        CodesView.Refresh();
    }

    private bool FilterCode(object item) => item is FrontolCodeReference code &&
        (_searchText.Length == 0 || code.Code.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
         code.Name.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase));

    private void CopySelected_Click(object sender, RoutedEventArgs e)
    {
        if (CodesGrid.SelectedItem is FrontolCodeReference code)
        {
            Clipboard.SetText($"{code.Code} - {code.Name}");
        }
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

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

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
