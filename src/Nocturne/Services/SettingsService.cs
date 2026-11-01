using System.ComponentModel;
using System.Windows.Threading;
using Nocturne.Models;

namespace Nocturne.Services;

public sealed class SettingsService : IDisposable
{
    private readonly DispatcherTimer _saveTimer;
    private bool _disposed;

    public AppSettings Current { get; private set; }

    public event EventHandler? Changed;

    public SettingsService()
    {
        AppPaths.EnsureCreated();
        Current = JsonStore.Load<AppSettings>(AppPaths.SettingsFile) ?? new AppSettings();

        // Json deserialization loses the case-insensitive comparer.
        Current.MonitorAssignments = new Dictionary<string, Guid>(
            Current.MonitorAssignments ?? new Dictionary<string, Guid>(),
            StringComparer.OrdinalIgnoreCase);

        Current.StartWithWindows = StartupService.IsEnabled();
        Current.PropertyChanged += OnPropertyChanged;

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow();
        };

        Log.Info("Settings loaded.");
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.StartWithWindows))
            StartupService.SetEnabled(Current.StartWithWindows);

        _saveTimer.Stop();
        _saveTimer.Start();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SaveNow()
    {
        if (_disposed)
            return;
        JsonStore.Save(AppPaths.SettingsFile, Current);
    }

    public void NotifyMonitorAssignmentsChanged()
    {
        SaveNow();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        var defaults = new AppSettings();
        Current.MinimizeToTray = defaults.MinimizeToTray;
        Current.PauseOnFullscreen = defaults.PauseOnFullscreen;
        Current.PauseOnBattery = defaults.PauseOnBattery;
        Current.PauseOnRemoteSession = defaults.PauseOnRemoteSession;
        Current.MuteAudio = defaults.MuteAudio;
        Current.RestoreOnStartup = defaults.RestoreOnStartup;
        Current.ShowNotifications = defaults.ShowNotifications;
        Current.Volume = defaults.Volume;
        Current.PlaybackSpeed = defaults.PlaybackSpeed;
        Current.Scaling = defaults.Scaling;
        Current.LibrarySort = defaults.LibrarySort;
        Current.LibraryFilter = defaults.LibraryFilter;
        SaveNow();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        SaveNow();
        _disposed = true;
        _saveTimer.Stop();
        Current.PropertyChanged -= OnPropertyChanged;
    }
}
