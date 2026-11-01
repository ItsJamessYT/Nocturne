using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nocturne.Interop;
using Nocturne.Models;
using Nocturne.Services;

namespace Nocturne.Rendering;

/// <summary>
/// One fullscreen, borderless window per monitor. The window is reparented into
/// Explorer's WorkerW so it renders behind desktop icons, exactly like Windows'
/// own wallpaper layer.
/// </summary>
public sealed partial class WallpaperHostWindow : Window
{
    private readonly PlaybackController _playback;
    private MonitorInfo _monitor;
    private AppSettings _settings;
    private IntPtr _hwnd;
    private IntPtr _parent;
    private bool _attached;
    private bool _pendingVideoLoad;
    private bool _paused;
    private bool _disposed;

    public WallpaperItem? Item { get; private set; }
    public MonitorInfo Monitor => _monitor;
    public bool IsPaused => _paused;

    public bool IsAlive => _hwnd != IntPtr.Zero && NativeMethods.IsWindow(_hwnd);

    public bool IsHealthy
    {
        get
        {
            if (!_attached || _hwnd == IntPtr.Zero || !NativeMethods.IsWindow(_hwnd))
                return false;
            IntPtr parent = NativeMethods.GetParent(_hwnd);
            return parent != IntPtr.Zero && parent == _parent && NativeMethods.IsWindow(parent);
        }
    }

    public WallpaperHostWindow(MonitorInfo monitor, AppSettings settings)
    {
        InitializeComponent();

        _monitor = monitor;
        _settings = settings;

        _playback = new PlaybackController();
        _playback.VoutReady += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Render, ApplyScaling);
        _playback.Failed += (_, message) => Log.Warn($"Wallpaper playback on {monitor.DeviceName}: {message}");

        Surface.HandleReady += OnSurfaceReady;
        Closed += (_, _) => Cleanup();
    }

    private void OnSurfaceReady(object? sender, EventArgs e)
    {
        _playback.SetTarget(Surface.SurfaceHandle);
        if (_pendingVideoLoad && Item is not null)
        {
            _pendingVideoLoad = false;
            _playback.Load(Item, _settings);
        }
    }

    public void ShowOnDesktop(MonitorInfo monitor)
    {
        if (_disposed)
            return;

        _monitor = monitor;

        if (!IsVisible)
            Show();

        _hwnd = new WindowInteropHelper(this).Handle;
        AttachToDesktop();
    }

    private void AttachToDesktop()
    {
        if (_disposed)
            return;

        if (_hwnd == IntPtr.Zero)
            _hwnd = new WindowInteropHelper(this).Handle;

        IntPtr worker = NativeMethods.FindDesktopWorkerW();
        if (worker == IntPtr.Zero)
        {
            Log.Warn("Explorer's desktop layer (WorkerW) was not found.");
            return;
        }

        _parent = worker;
        _attached = NativeMethods.AttachToDesktop(_hwnd, worker, _monitor.Left, _monitor.Top, _monitor.Width, _monitor.Height);
        if (_attached)
        {
            _playback.SetTargetSize(_monitor.Width, _monitor.Height);
            ApplyScaling();
            Log.Info($"Wallpaper surface attached to {_monitor.DeviceName} ({_monitor.Width}x{_monitor.Height}).");

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (!_disposed && _hwnd != IntPtr.Zero)
                {
                    NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_BOTTOM,
                        _monitor.Left, _monitor.Top, _monitor.Width, _monitor.Height,
                        NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
                }
            });
        }
        else
        {
            Log.Warn("Could not attach the wallpaper surface to the desktop.");
        }
    }

    public void Play(WallpaperItem item, bool startPaused = false)
    {
        if (_disposed)
            return;

        Item = item;
        _paused = startPaused;

        if (item.IsAnimated)
        {
            StaticImage.Visibility = Visibility.Collapsed;
            StaticImage.Source = null;
            Surface.Visibility = Visibility.Visible;
            ApplyScaling();

            if (Surface.SurfaceHandle == IntPtr.Zero)
            {
                _pendingVideoLoad = true;
            }
            else
            {
                _pendingVideoLoad = false;
                _playback.Load(item, _settings);
                if (_paused)
                    _playback.SetPaused(true);
            }
        }
        else
        {
            _pendingVideoLoad = false;
            _playback.Stop();
            Surface.Visibility = Visibility.Collapsed;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(item.FilePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.EndInit();
                bitmap.Freeze();
                StaticImage.Source = bitmap;
                StaticImage.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not load image wallpaper '{item.Name}': {ex.Message}");
                // Fallback to LibVLC
                Surface.Visibility = Visibility.Visible;
                if (Surface.SurfaceHandle != IntPtr.Zero)
                    _playback.Load(item, _settings);
            }

            ApplyScaling();
        }
    }

    public void Clear()
    {
        Item = null;
        _pendingVideoLoad = false;
        _playback.Stop();
        Surface.Visibility = Visibility.Collapsed;
        StaticImage.Source = null;
        StaticImage.Visibility = Visibility.Visible;
        StaticImage.Stretch = Stretch.UniformToFill;
    }

    public void UpdateMonitor(MonitorInfo monitor)
    {
        _monitor = monitor;
        _playback.SetTargetSize(monitor.Width, monitor.Height);

        if (_attached && _hwnd != IntPtr.Zero && NativeMethods.IsWindow(_hwnd))
        {
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_BOTTOM,
                monitor.Left, monitor.Top, monitor.Width, monitor.Height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }
        else
        {
            AttachToDesktop();
        }

        ApplyScaling();
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _playback.ApplySettings(settings);
        ApplyScaling();
    }

    public void SetPaused(bool paused)
    {
        if (_paused == paused)
            return;

        _paused = paused;
        _playback.SetPaused(paused);
    }

    private void ApplyScaling()
    {
        Stretch stretch = _settings.Scaling switch
        {
            WallpaperScale.Fit => Stretch.Uniform,
            WallpaperScale.Stretch => Stretch.Fill,
            _ => Stretch.UniformToFill
        };

        if (StaticImage.Stretch != stretch)
            StaticImage.Stretch = stretch;
    }

    public void ReattachIfNeeded()
    {
        if (_disposed)
            return;

        if (!IsHealthy)
        {
            Log.Info("Desktop layer changed; re-attaching wallpaper surface.");
            AttachToDesktop();
        }
    }

    private void Cleanup()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            if (_hwnd != IntPtr.Zero && NativeMethods.IsWindow(_hwnd))
                NativeMethods.SetParent(_hwnd, IntPtr.Zero);
        }
        catch { }

        _playback.Dispose();
        _hwnd = IntPtr.Zero;
        _attached = false;
    }
}
