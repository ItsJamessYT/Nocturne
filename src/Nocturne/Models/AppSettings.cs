using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Nocturne.Models;

public sealed class AppSettings : INotifyPropertyChanged
{
    private bool _startWithWindows;
    private bool _minimizeToTray = true;
    private bool _pauseOnFullscreen = true;
    private bool _pauseOnBattery;
    private bool _pauseOnRemoteSession = true;
    private bool _muteAudio = true;
    private bool _restoreOnStartup = true;
    private bool _showNotifications = true;
    private double _volume;
    private double _playbackSpeed = 1.0;
    private WallpaperScale _scaling = WallpaperScale.Fill;
    private SortMode _librarySort = SortMode.Newest;
    private WallpaperFilter _libraryFilter = WallpaperFilter.All;
    private Guid? _lastWallpaperId;

    /// <summary>Launch Nocturne when the user signs in to Windows.</summary>
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => Set(ref _startWithWindows, value);
    }

    /// <summary>Minimizing the window hides it to the notification area.</summary>
    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => Set(ref _minimizeToTray, value);
    }

    public bool PauseOnFullscreen
    {
        get => _pauseOnFullscreen;
        set => Set(ref _pauseOnFullscreen, value);
    }

    public bool PauseOnBattery
    {
        get => _pauseOnBattery;
        set => Set(ref _pauseOnBattery, value);
    }

    public bool PauseOnRemoteSession
    {
        get => _pauseOnRemoteSession;
        set => Set(ref _pauseOnRemoteSession, value);
    }

    public bool MuteAudio
    {
        get => _muteAudio;
        set => Set(ref _muteAudio, value);
    }

    /// <summary>Re-apply the last wallpaper when the engine starts.</summary>
    public bool RestoreOnStartup
    {
        get => _restoreOnStartup;
        set => Set(ref _restoreOnStartup, value);
    }

    public bool ShowNotifications
    {
        get => _showNotifications;
        set => Set(ref _showNotifications, value);
    }

    /// <summary>0 - 100.</summary>
    public double Volume
    {
        get => _volume;
        set => Set(ref _volume, Math.Clamp(value, 0, 100));
    }

    /// <summary>0.25 - 2.0.</summary>
    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set => Set(ref _playbackSpeed, Math.Clamp(Math.Round(value, 2), 0.25, 2.0));
    }

    public WallpaperScale Scaling
    {
        get => _scaling;
        set => Set(ref _scaling, value);
    }

    public SortMode LibrarySort
    {
        get => _librarySort;
        set => Set(ref _librarySort, value);
    }

    public WallpaperFilter LibraryFilter
    {
        get => _libraryFilter;
        set => Set(ref _libraryFilter, value);
    }

    public Guid? LastWallpaperId
    {
        get => _lastWallpaperId;
        set => Set(ref _lastWallpaperId, value);
    }

    /// <summary>Per-display wallpaper overrides, keyed by the GDI device name (e.g. \\.\DISPLAY1).</summary>
    public Dictionary<string, Guid> MonitorAssignments { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public string SpeedText => $"{PlaybackSpeed:0.00}×";

    [JsonIgnore]
    public string VolumeText => $"{(int)Math.Round(Volume)}%";

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(property);
        if (property == nameof(PlaybackSpeed))
            OnPropertyChanged(nameof(SpeedText));
        if (property == nameof(Volume))
            OnPropertyChanged(nameof(VolumeText));
        return true;
    }

    public void OnPropertyChanged([CallerMemberName] string? property = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
