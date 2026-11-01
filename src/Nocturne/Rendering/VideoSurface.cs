using System.Runtime.InteropServices;
using System.Windows.Interop;
using Nocturne.Interop;

namespace Nocturne.Rendering;

/// <summary>
/// A native child window used as LibVLC's video output. Keeping this separate from
/// WPF's compositor gives us the lowest-latency, highest-quality desktop playback.
/// </summary>
public sealed class VideoSurface : HwndHost
{
    private const uint WS_CHILD = 0x40000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_CLIPCHILDREN = 0x02000000;
    private const uint WS_CLIPSIBLINGS = 0x04000000;
    private const uint SS_BLACKRECT = 0x00000004;

    private IntPtr _handle;

    public IntPtr SurfaceHandle => _handle;
    public event EventHandler? HandleReady;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _handle = NativeMethods.CreateWindowEx(
            0,
            "static",
            string.Empty,
            WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS | SS_BLACKRECT,
            0, 0, 1, 1,
            hwndParent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        HandleReady?.Invoke(this, EventArgs.Empty);
        return new HandleRef(this, _handle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (hwnd.Handle != IntPtr.Zero)
            NativeMethods.DestroyWindow(hwnd.Handle);
        _handle = IntPtr.Zero;
    }
}
