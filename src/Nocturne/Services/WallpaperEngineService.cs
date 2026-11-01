using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Win32;
using Nocturne.Interop;
using Nocturne.Models;
using Nocturne.Rendering;

namespace Nocturne.Services;

/// <summary>
/// Keeps one live wallpaper surface per monitor, restores assignments, detects
/// fullscreen games, display changes and Explorer restarts. The engine lives for
/// the whole application lifetime and is independent of the main window.
/// </summary>
public sealed class WallpaperEngineService : IDisposable
{
    private readonly LibraryService _library;
    private readonly SettingsService _settings;
    private readonly Dictionary<string, WallpaperHostWindow> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _timer;

    private DateTime _lastCpuSample = DateTime.UtcNow;
    private TimeSpan _lastCpuTime;
    private bool _running;
    private bool _disposed;
    private bool _userPaused;
    private bool _sessionLocked;
    private bool _policyPaused;
    private string _pauseReason = string.Empty;
    private WallpaperItem? _current;

    public IReadOnlyList<MonitorInfo> Monitors { get; private set; } = Array.Empty<MonitorInfo>();
    public WallpaperItem? CurrentWallpaper => _current;
    public bool IsPaused => _userPaused || _policyPaused;
    public bool IsUserPaused => _userPaused;
    public bool IsRunning => _hosts.Count > 0;
    public string PauseReason => _userPaused ? "Paused by you" : _policyPaused ? _pauseReason : string.Empty;
    public double CpuPercent { get; private set; }
    public double MemoryMb { get; private set; }

    public event EventHandler? StateChanged;
    public event EventHandler<WallpaperItem>? WallpaperChanged;

    public WallpaperEngineService(LibraryService library, SettingsService settings)
    {
        _library = library;
        _settings = settings;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1.5)
        };
        _timer.Tick += (_, _) => Tick();
    }

    public void Start()
    {
        if (_running || _disposed)
            return;

        _running = true;
        PlaybackEngine.EnsureInitialized();

        if (!PlaybackEngine.IsAvailable)
        {
            Log.Warn("The wallpaper engine started without video support: " + PlaybackEngine.Error);
        }

        RefreshMonitors();

        if (_settings.Current.RestoreOnStartup)
            RestoreSavedAssignments();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        _settings.Current.PropertyChanged += OnSettingsChanged;

        _timer.Start();
        Log.Info("Wallpaper engine started.");
        RaiseStateChanged();
    }

    public void RefreshMonitors()
    {
        if (_disposed)
            return;

        List<MonitorInfo> monitors = DisplayManager.GetMonitors();
        Monitors = monitors;

        var connected = monitors.Select(m => m.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string device in _hosts.Keys.Where(d => !connected.Contains(d)).ToList())
        {
            CloseHost(device);
        }

        foreach (MonitorInfo monitor in monitors)
        {
            if (_hosts.TryGetValue(monitor.DeviceName, out WallpaperHostWindow? host))
                host.UpdateMonitor(monitor);
        }

        RaiseStateChanged();
    }

    private void RestoreSavedAssignments()
    {
        AppSettings settings = _settings.Current;

        if (settings.LastWallpaperId is Guid lastId && _library.Find(lastId) is WallpaperItem last)
            _current = last;

        bool any = false;
        foreach (MonitorInfo monitor in Monitors)
        {
            Guid? id = settings.MonitorAssignments.TryGetValue(monitor.DeviceName, out Guid assigned)
                ? assigned
                : settings.LastWallpaperId;

            if (id is null)
                continue;

            WallpaperItem? item = _library.Find(id.Value);
            if (item is null)
                continue;

            ApplyToHost(monitor, item);
            any = true;
        }

        if (any)
            Log.Info("Restored saved wallpaper assignments.");
    }

    public void Apply(WallpaperItem item, string? monitorDevice = null)
    {
        if (_disposed)
            return;

        AppSettings settings = _settings.Current;
        _current = item;

        if (string.IsNullOrWhiteSpace(monitorDevice))
        {
            settings.LastWallpaperId = item.Id;
            foreach (MonitorInfo monitor in Monitors)
                settings.MonitorAssignments[monitor.DeviceName] = item.Id;
        }
        else
        {
            settings.MonitorAssignments[monitorDevice] = item.Id;
            settings.LastWallpaperId = item.Id;
        }

        _settings.SaveNow();

        foreach (MonitorInfo monitor in Monitors)
        {
            if (!string.IsNullOrWhiteSpace(monitorDevice) &&
                !string.Equals(monitor.DeviceName, monitorDevice, StringComparison.OrdinalIgnoreCase))
                continue;

            ApplyToHost(monitor, item);
        }

        WallpaperChanged?.Invoke(this, item);
        RaiseStateChanged();
    }

    private void ApplyToHost(MonitorInfo monitor, WallpaperItem item)
    {
        WallpaperHostWindow host = EnsureHost(monitor);

        if (host.Item?.Id == item.Id && host.IsHealthy)
        {
            host.ApplySettings(_settings.Current);
            host.SetPaused(IsPaused);
            return;
        }

        host.ShowOnDesktop(monitor);
        host.Play(item, IsPaused);
    }

    private WallpaperHostWindow EnsureHost(MonitorInfo monitor)
    {
        if (_hosts.TryGetValue(monitor.DeviceName, out WallpaperHostWindow? existing) && existing.IsAlive)
            return existing;

        if (existing is not null)
        {
            try { existing.Close(); } catch { }
            _hosts.Remove(monitor.DeviceName);
        }

        var host = new WallpaperHostWindow(monitor, _settings.Current);
        _hosts[monitor.DeviceName] = host;
        return host;
    }

    public void ApplyToMonitor(WallpaperItem item, MonitorInfo monitor) => Apply(item, monitor.DeviceName);

    public void WallpaperRemoved(Guid id)
    {
        AppSettings settings = _settings.Current;
        bool wasCurrent = _current?.Id == id;

        foreach (string device in settings.MonitorAssignments
                     .Where(kv => kv.Value == id)
                     .Select(kv => kv.Key)
                     .ToList())
        {
            settings.MonitorAssignments.Remove(device);
        }

        foreach (WallpaperHostWindow host in _hosts.Values.Where(h => h.Item?.Id == id).ToList())
            CloseHost(host.Monitor.DeviceName);

        if (settings.LastWallpaperId == id)
            settings.LastWallpaperId = null;

        if (wasCurrent)
            _current = null;

        _settings.SaveNow();
        RestoreDesktopIfIdle();
        RaiseStateChanged();
    }

    public void ClearMonitor(MonitorInfo monitor)
    {
        _settings.Current.MonitorAssignments.Remove(monitor.DeviceName);
        _settings.SaveNow();
        CloseHost(monitor.DeviceName);
        RestoreDesktopIfIdle();
        RaiseStateChanged();
    }

    private void RestoreDesktopIfIdle()
    {
        if (_hosts.Count == 0)
            DesktopWallpaperService.Restore();
    }

    public void StopAll()
    {
        foreach (string device in _hosts.Keys.ToList())
            CloseHost(device);

        _current = null;
        _settings.Current.LastWallpaperId = null;
        _settings.Current.MonitorAssignments.Clear();
        _settings.SaveNow();
        DesktopWallpaperService.Restore();
        RaiseStateChanged();
    }

    private void CloseHost(string device)
    {
        if (!_hosts.TryGetValue(device, out WallpaperHostWindow? host))
            return;

        _hosts.Remove(device);
        try { host.Close(); } catch (Exception ex) { Log.Warn("Could not close wallpaper surface: " + ex.Message); }
    }

    public void SetUserPaused(bool paused)
    {
        if (_userPaused == paused)
            return;

        _userPaused = paused;
        ApplyPauseState();
        RaiseStateChanged();
    }

    public void ToggleUserPaused() => SetUserPaused(!_userPaused);

    private void Tick()
    {
        if (_disposed)
            return;

        UpdateResourceUsage();

        foreach (WallpaperHostWindow host in _hosts.Values.ToList())
        {
            try
            {
                if (!host.IsAlive)
                {
                    WallpaperItem? item = host.Item ?? _current;
                    CloseHost(host.Monitor.DeviceName);
                    if (item is not null)
                        ApplyToHost(host.Monitor, item);
                    continue;
                }

                host.ReattachIfNeeded();
            }
            catch (Exception ex)
            {
                Log.Warn("Engine health check: " + ex.Message);
            }
        }

        EvaluatePausePolicy();
        RaiseStateChanged();
    }

    private void UpdateResourceUsage()
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            DateTime now = DateTime.UtcNow;
            TimeSpan cpu = process.TotalProcessorTime;
            double elapsed = (now - _lastCpuSample).TotalMilliseconds;

            if (elapsed > 250 && _lastCpuTime != TimeSpan.Zero)
            {
                double used = (cpu - _lastCpuTime).TotalMilliseconds;
                CpuPercent = Math.Clamp(used / (elapsed * Environment.ProcessorCount) * 100, 0, 100);
            }

            _lastCpuSample = now;
            _lastCpuTime = cpu;
            MemoryMb = process.WorkingSet64 / 1024d / 1024d;
        }
        catch { }
    }

    private void EvaluatePausePolicy()
    {
        AppSettings settings = _settings.Current;
        bool pause = false;
        string reason = string.Empty;

        if (settings.PauseOnFullscreen && NativeMethods.IsFullscreenAppActive())
        {
            pause = true;
            reason = "Fullscreen app detected";
        }
        else if (settings.PauseOnBattery && System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus ==
                 System.Windows.Forms.PowerLineStatus.Offline)
        {
            pause = true;
            reason = "Running on battery";
        }
        else if (settings.PauseOnRemoteSession && System.Windows.Forms.SystemInformation.TerminalServerSession)
        {
            pause = true;
            reason = "Remote desktop session";
        }
        else if (_sessionLocked)
        {
            pause = true;
            reason = "Session locked";
        }

        bool changed = pause != _policyPaused || reason != _pauseReason;
        _policyPaused = pause;
        _pauseReason = reason;

        if (changed)
        {
            ApplyPauseState();
            RaiseStateChanged();
        }
    }

    private void ApplyPauseState()
    {
        bool paused = IsPaused;
        foreach (WallpaperHostWindow host in _hosts.Values)
        {
            try { host.SetPaused(paused); } catch { }
        }
    }

    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        AppSettings settings = _settings.Current;

        if (e.PropertyName is nameof(AppSettings.MuteAudio) or nameof(AppSettings.Volume)
            or nameof(AppSettings.PlaybackSpeed) or nameof(AppSettings.Scaling))
        {
            foreach (WallpaperHostWindow host in _hosts.Values)
            {
                try { host.ApplySettings(settings); } catch { }
            }
        }

        if (e.PropertyName is nameof(AppSettings.PauseOnFullscreen) or nameof(AppSettings.PauseOnBattery)
            or nameof(AppSettings.PauseOnRemoteSession))
        {
            EvaluatePausePolicy();
        }

        RaiseStateChanged();
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        _sessionLocked = e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            ApplyPauseState();
            RaiseStateChanged();
        });
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Log.Info("Display configuration changed.");
            RefreshMonitors();

            foreach (MonitorInfo monitor in Monitors)
            {
                Guid? id = _settings.Current.MonitorAssignments.TryGetValue(monitor.DeviceName, out Guid assigned)
                    ? assigned
                    : _current?.Id;

                if (id is not null && _library.Find(id.Value) is WallpaperItem item)
                    ApplyToHost(monitor, item);
            }
        });
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _timer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _settings.Current.PropertyChanged -= OnSettingsChanged;

        foreach (string device in _hosts.Keys.ToList())
            CloseHost(device);
    }
}
