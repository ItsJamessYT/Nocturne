using System.ComponentModel;
using System.Runtime.CompilerServices;
using Nocturne.Models;

namespace Nocturne.ViewModels;

public sealed class MonitorViewModel : INotifyPropertyChanged
{
    private WallpaperItem? _wallpaper;
    private bool _isPaused;

    public MonitorInfo Monitor { get; }

    public string DeviceName => Monitor.DeviceName;
    public string DisplayLabel => Monitor.DisplayLabel;
    public string ResolutionText => Monitor.ResolutionText;
    public string FriendlyName => Monitor.FriendlyName;
    public bool IsPrimary => Monitor.IsPrimary;

    public WallpaperItem? Wallpaper
    {
        get => _wallpaper;
        set
        {
            if (ReferenceEquals(_wallpaper, value))
                return;
            _wallpaper = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasWallpaper));
            OnPropertyChanged(nameof(WallpaperName));
            OnPropertyChanged(nameof(ThumbnailPath));
        }
    }

    public bool HasWallpaper => Wallpaper is not null;
    public string WallpaperName => Wallpaper?.Name ?? "Desktop default";
    public string? ThumbnailPath => Wallpaper?.ThumbnailPath;

    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (_isPaused == value) return;
            _isPaused = value;
            OnPropertyChanged();
        }
    }

    public MonitorViewModel(MonitorInfo monitor)
    {
        Monitor = monitor;
    }

    public void UpdateMonitor(MonitorInfo monitor)
    {
        // MonitorInfo is immutable; replace through the parent list instead.
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
