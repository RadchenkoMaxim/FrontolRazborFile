using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FrontolFileAnalyzer;

internal static class WindowBoundsHelper
{
    private const uint MonitorDefaultToNearest = 2;

    public static void ConstrainToOwnerWorkingArea(Window window, double margin = 24)
    {
        var ownHandle = new WindowInteropHelper(window).Handle;
        var ownerHandle = window.Owner is null ? IntPtr.Zero : new WindowInteropHelper(window.Owner).Handle;
        var monitor = MonitorFromWindow(ownerHandle != IntPtr.Zero ? ownerHandle : ownHandle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
        {
            var dpi = VisualTreeHelper.GetDpi(window.Owner ?? window);
            Apply(window, info.WorkArea.Width / dpi.DpiScaleX, info.WorkArea.Height / dpi.DpiScaleY, margin);
            return;
        }

        Apply(window, SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height, margin);
    }

    private static void Apply(Window window, double workingWidth, double workingHeight, double margin)
    {
        var maxWidth = Math.Max(1, Math.Floor(workingWidth - margin));
        var maxHeight = Math.Max(1, Math.Floor(workingHeight - margin));
        window.MinWidth = Math.Min(window.MinWidth, maxWidth);
        window.MinHeight = Math.Min(window.MinHeight, maxHeight);
        window.MaxWidth = Math.Min(window.MaxWidth, maxWidth);
        window.MaxHeight = Math.Min(window.MaxHeight, maxHeight);
        window.Width = Math.Min(window.Width, maxWidth);
        window.Height = Math.Min(window.Height, maxHeight);
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
