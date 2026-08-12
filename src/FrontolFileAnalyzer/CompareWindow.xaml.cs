using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FrontolFileAnalyzer;

public partial class CompareWindow : Window
{
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const double WorkingAreaMargin = 24d;

    public CompareWindow(IReadOnlyList<CompareRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        Rows = rows;
        HasRows = rows.Count > 0;
        Summary = $"Различий: {rows.Count:N0} · добавлено: {rows.Count(row => row.Status == "Добавлен"):N0} · удалено: {rows.Count(row => row.Status == "Удалён"):N0} · изменено: {rows.Count(row => row.Status == "Изменён"):N0}";
        InitializeComponent();
        DataContext = this;
        SourceInitialized += (_, _) => ConstrainToWorkingArea();
    }

    public IReadOnlyList<CompareRow> Rows { get; }
    public bool HasRows { get; }
    public string Summary { get; }

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

public sealed record CompareRow(string Status, string Code, string OldName, string NewName, string Changes);
