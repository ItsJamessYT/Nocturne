using System.Runtime.InteropServices;
using System.Text;
using Nocturne.Models;

namespace Nocturne.Interop;

internal static class DisplayManager
{
    public static List<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();

        try
        {
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr _, ref NativeMethods.RECT _, IntPtr _) =>
            {
                var info = new NativeMethods.MONITORINFOEX
                {
                    cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
                    szDevice = string.Empty
                };

                if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
                {
                    string device = info.szDevice;
                    int dpi = 96;
                    try
                    {
                        if (NativeMethods.GetDpiForMonitor(hMonitor, 0, out uint dpiX, out _) == 0 && dpiX > 0)
                            dpi = (int)dpiX;
                    }
                    catch { /* shcore unavailable */ }

                    monitors.Add(new MonitorInfo
                    {
                        DeviceName = device,
                        FriendlyName = GetFriendlyName(device),
                        Left = info.rcMonitor.Left,
                        Top = info.rcMonitor.Top,
                        Width = info.rcMonitor.Width,
                        Height = info.rcMonitor.Height,
                        IsPrimary = (info.dwFlags & 1) != 0,
                        Dpi = dpi
                    });
                }

                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            // fall through to a synthetic primary display
        }

        if (monitors.Count == 0)
        {
            var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
            monitors.Add(new MonitorInfo
            {
                DeviceName = @"\\.\DISPLAY1",
                FriendlyName = "Primary display",
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
                IsPrimary = true
            });
        }

        return monitors
            .OrderByDescending(m => m.IsPrimary)
            .ThenBy(m => m.Left)
            .ThenBy(m => m.Top)
            .ToList();
    }

    private static string GetFriendlyName(string deviceName)
    {
        try
        {
            var device = new NativeMethods.DISPLAY_DEVICE
            {
                cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>(),
                DeviceName = string.Empty,
                DeviceString = string.Empty,
                DeviceID = string.Empty,
                DeviceKey = string.Empty
            };

            if (NativeMethods.EnumDisplayDevices(deviceName, 0, ref device, 0) && !string.IsNullOrWhiteSpace(device.DeviceString))
                return CleanName(device.DeviceString);
        }
        catch { }

        return deviceName.Replace(@"\\.\", "Display ");
    }

    private static string CleanName(string name)
    {
        string cleaned = name.Trim();
        if (cleaned.Contains("Generic PnP Monitor", StringComparison.OrdinalIgnoreCase))
            return "Display";
        return cleaned;
    }
}
