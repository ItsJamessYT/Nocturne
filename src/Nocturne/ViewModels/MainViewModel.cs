using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Nocturne.Dialogs;
using Nocturne.Models;
using Nocturne.Rendering;
using Nocturne.Services;

namespace Nocturne.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly LibraryService _library;
    private readonly SettingsService _settings;
    private readonly WallpaperEngineService _engine;
    private readonly DispatcherTimer _toastTimer;

    private AppPage _currentPage = AppPage.Home;
    private string _searchText = string.Empty;
    private WallpaperFilter _filter = WallpaperFilter.All;
    private CollectionItem? _activeCollection;
    private WallpaperItem? _currentWallpaper;
    private WallpaperItem? _featuredWallpaper;
    private bool _isBusy;
    private string _busyText = string.Empty;
    private string _toastMessage = string.Empty;
    private bool _isToastVisible;
    private string _monitorSignature = string.Empty;

    public MainViewModel(LibraryService library, SettingsService settings, WallpaperEngineService engine)
    {
        _library = library;
        _settings = settings;
        _engine = engine;

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.2) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        NavigateCommand = new RelayCommand(p => Navigate(p));
        ImportCommand = new AsyncRelayCommand(ImportAsync);
        ApplyCommand = new RelayCommand(p => Apply(p as WallpaperItem));
        PreviewCommand = new RelayCommand(p => Preview(p as WallpaperItem));
        ToggleFavoriteCommand = new RelayCommand(p => ToggleFavorite(p as WallpaperItem));
        RenameCommand = new RelayCommand(p => Rename(p as WallpaperItem));
        RemoveCommand = new RelayCommand(p => Remove(p as WallpaperItem));
        AddToCollectionCommand = new RelayCommand(p => AddToCollection(p as WallpaperItem));
        CreateCollectionCommand = new RelayCommand(CreateCollection);
        DeleteCollectionCommand = new RelayCommand(p => DeleteCollection(p as CollectionItem));
        SelectCollectionCommand = new RelayCommand(p => SelectCollection(p as CollectionItem));
        ClearCollectionCommand = new RelayCommand(() => ActiveCollection = null);
        SetFilterCommand = new RelayCommand(p =>
        {
            if (p is WallpaperFilter filter) Filter = filter;
            else if (p is string s && Enum.TryParse(s, true, out WallpaperFilter parsed)) Filter = parsed;
        });
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        ResetLibraryFiltersCommand = new RelayCommand(() =>
        {
            SearchText = string.Empty;
            Filter = WallpaperFilter.All;
            ActiveCollection = null;
        });
        ApplyRandomCommand = new RelayCommand(ApplyRandom);
        ShuffleCommand = new RelayCommand(ShuffleDiscover);
        TogglePauseCommand = new RelayCommand(() => _engine.ToggleUserPaused());
        StopWallpaperCommand = new RelayCommand(StopWallpaper);
        OpenLibraryFolderCommand = new RelayCommand(OpenLibraryFolder);
        ResetSettingsCommand = new RelayCommand(ResetSettings);
        ChangeMonitorWallpaperCommand = new RelayCommand(p => ChangeMonitorWallpaper(p as MonitorViewModel));
        ClearMonitorCommand = new RelayCommand(p => ClearMonitor(p as MonitorViewModel));
        RefreshMonitorsCommand = new RelayCommand(() =>
        {
            _engine.RefreshMonitors();
            UpdateMonitors(true);
            ShowToast("Display configuration refreshed.");
        });
        OpenLogCommand = new RelayCommand(OpenLogFolder);

        _library.Changed += (_, _) => RefreshAll();
        _engine.StateChanged += (_, _) => UpdateEngineState();
        _engine.WallpaperChanged += (_, item) =>
        {
            CurrentWallpaper = item;
            UpdateMonitors(true);
        };

        RefreshAll();
        UpdateEngineState();
        UpdateMonitors(true);
    }

    public AppSettings Settings => _settings.Current;

    public ObservableCollection<WallpaperItem> Wallpapers { get; } = new();
    public ObservableCollection<WallpaperItem> Favorites { get; } = new();
    public ObservableCollection<WallpaperItem> Recent { get; } = new();
    public ObservableCollection<WallpaperItem> DiscoverWallpapers { get; } = new();
    public ObservableCollection<CollectionItem> Collections => _library.Collections;
    public ObservableCollection<MonitorViewModel> Monitors { get; } = new();

    public RelayCommand NavigateCommand { get; }
    public AsyncRelayCommand ImportCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand PreviewCommand { get; }
    public RelayCommand ToggleFavoriteCommand { get; }
    public RelayCommand RenameCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand AddToCollectionCommand { get; }
    public RelayCommand CreateCollectionCommand { get; }
    public RelayCommand DeleteCollectionCommand { get; }
    public RelayCommand SelectCollectionCommand { get; }
    public RelayCommand ClearCollectionCommand { get; }
    public RelayCommand SetFilterCommand { get; }
    public RelayCommand ClearSearchCommand { get; }
    public RelayCommand ResetLibraryFiltersCommand { get; }
    public RelayCommand ApplyRandomCommand { get; }
    public RelayCommand ShuffleCommand { get; }
    public RelayCommand TogglePauseCommand { get; }
    public RelayCommand StopWallpaperCommand { get; }
    public RelayCommand OpenLibraryFolderCommand { get; }
    public RelayCommand ResetSettingsCommand { get; }
    public RelayCommand ChangeMonitorWallpaperCommand { get; }
    public RelayCommand ClearMonitorCommand { get; }
    public RelayCommand RefreshMonitorsCommand { get; }
    public RelayCommand OpenLogCommand { get; }

    public AppPage CurrentPage
    {
        get => _currentPage;
        set
        {
            if (_currentPage == value)
                return;
            _currentPage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(PageSubtitle));
            OnPropertyChanged(nameof(IsHomePage));
            OnPropertyChanged(nameof(IsDiscoverPage));
            OnPropertyChanged(nameof(IsLibraryPage));
            OnPropertyChanged(nameof(IsFavoritesPage));
            OnPropertyChanged(nameof(IsSettingsPage));
        }
    }

    public bool IsHomePage => CurrentPage == AppPage.Home;
    public bool IsDiscoverPage => CurrentPage == AppPage.Discover;
    public bool IsLibraryPage => CurrentPage == AppPage.Library;
    public bool IsFavoritesPage => CurrentPage == AppPage.Favorites;
    public bool IsSettingsPage => CurrentPage == AppPage.Settings;

    public string PageTitle => CurrentPage switch
    {
        AppPage.Home => "Home",
        AppPage.Discover => "Discover",
        AppPage.Library => "My Wallpapers",
        AppPage.Favorites => "Favorites",
        _ => "Settings"
    };

    public string PageSubtitle => CurrentPage switch
    {
        AppPage.Home => "Your desktop, alive.",
        AppPage.Discover => "Curated picks from your own collection.",
        AppPage.Library => ActiveCollection is null
            ? $"{Wallpapers.Count} shown · {_library.Wallpapers.Count} in library"
            : $"{Wallpapers.Count} in {ActiveCollection.Name}",
        AppPage.Favorites => Favorites.Count == 1 ? "1 favorite" : $"{Favorites.Count} favorites",
        _ => "Make Nocturne feel like yours."
    };

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value;
            OnPropertyChanged();
            RefreshFiltered();
            OnPropertyChanged(nameof(PageSubtitle));
        }
    }

    public WallpaperFilter Filter
    {
        get => _filter;
        set
        {
            if (_filter == value) return;
            _filter = value;
            OnPropertyChanged();
            RefreshFiltered();
        }
    }

    public SortMode Sort
    {
        get => Settings.LibrarySort;
        set
        {
            if (Settings.LibrarySort == value) return;
            Settings.LibrarySort = value;
            OnPropertyChanged();
            RefreshFiltered();
        }
    }

    public CollectionItem? ActiveCollection
    {
        get => _activeCollection;
        set
        {
            if (ReferenceEquals(_activeCollection, value)) return;
            _activeCollection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasActiveCollection));
            OnPropertyChanged(nameof(ActiveCollectionName));
            RefreshFiltered();
            if (value is not null)
                CurrentPage = AppPage.Library;
        }
    }

    public bool HasActiveCollection => ActiveCollection is not null;
    public string ActiveCollectionName => ActiveCollection?.Name ?? string.Empty;

    public WallpaperItem? CurrentWallpaper
    {
        get => _currentWallpaper;
        private set
        {
            if (ReferenceEquals(_currentWallpaper, value)) return;
            _currentWallpaper = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCurrentWallpaper));
            OnPropertyChanged(nameof(CurrentWallpaperName));
        }
    }

    public bool HasCurrentWallpaper => CurrentWallpaper is not null;
    public string CurrentWallpaperName => CurrentWallpaper?.Name ?? "No wallpaper applied";

    public WallpaperItem? FeaturedWallpaper
    {
        get => _featuredWallpaper;
        private set
        {
            if (ReferenceEquals(_featuredWallpaper, value)) return;
            _featuredWallpaper = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasFeaturedWallpaper));
        }
    }

    public bool HasFeaturedWallpaper => FeaturedWallpaper is not null;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
        }
    }

    public string BusyText
    {
        get => _busyText;
        private set
        {
            if (_busyText == value) return;
            _busyText = value;
            OnPropertyChanged();
        }
    }

    public string ToastMessage
    {
        get => _toastMessage;
        private set
        {
            if (_toastMessage == value) return;
            _toastMessage = value;
            OnPropertyChanged();
        }
    }

    public bool IsToastVisible
    {
        get => _isToastVisible;
        private set
        {
            if (_isToastVisible == value) return;
            _isToastVisible = value;
            OnPropertyChanged();
        }
    }

    public int LibraryCount => _library.Wallpapers.Count;
    public int FavoriteCount => Favorites.Count;
    public int MonitorCount => Monitors.Count;
    public bool IsLibraryEmpty => LibraryCount == 0;
    public bool IsFilteredEmpty => Wallpapers.Count == 0 && LibraryCount > 0;
    public bool HasFavorites => Favorites.Count > 0;
    public bool HasRecent => Recent.Count > 0;
    public bool HasDiscover => DiscoverWallpapers.Count > 0;

    public bool IsEnginePaused => _engine.IsPaused;
    public bool IsEngineRunning => _engine.IsRunning;
    public string EngineStatus => _engine.IsPaused ? "Paused" : _engine.IsRunning ? "Live" : "Idle";
    public string EngineDetail
    {
        get
        {
            if (_engine.IsPaused)
                return string.IsNullOrWhiteSpace(_engine.PauseReason) ? "Wallpaper paused" : _engine.PauseReason;
            if (_engine.IsRunning)
                return _engine.Monitors.Count == 1 ? "Rendering on 1 display" : $"Rendering on {_engine.Monitors.Count} displays";
            return "No wallpaper applied";
        }
    }

    public string CpuText => $"{_engine.CpuPercent:0.0}%";
    public string MemoryText => $"{_engine.MemoryMb:0} MB";
    public string VideoEngineText => PlaybackEngine.IsAvailable ? "LibVLC ready" : "LibVLC unavailable";

    public string LibraryPath => AppPaths.Root;

    public IReadOnlyList<SortMode> SortModes { get; } = Enum.GetValues<SortMode>();
    public IReadOnlyList<WallpaperScale> ScaleModes { get; } = Enum.GetValues<WallpaperScale>();

    public string AppVersion => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public string PauseButtonText => IsEnginePaused ? "Resume wallpaper" : "Pause wallpaper";

    public string PauseGlyph => IsEnginePaused ? "\uE768" : "\uE769";

    public string LibrarySizeText
    {
        get
        {
            double bytes = _library.Wallpapers.Sum(w => (double)w.FileSize);
            if (bytes >= 1024d * 1024d * 1024d)
                return $"{bytes / 1024d / 1024d / 1024d:0.0} GB";
            if (bytes >= 1024d * 1024d)
                return $"{bytes / 1024d / 1024d:0.0} MB";
            return $"{bytes / 1024d:0} KB";
        }
    }

    private void Navigate(object? parameter)
    {
        if (parameter is AppPage page)
            CurrentPage = page;
        else if (parameter is string text && Enum.TryParse(text, true, out AppPage parsed))
            CurrentPage = parsed;
    }

    private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add wallpapers to Nocturne",
            Filter = LibraryService.FileDialogFilter,
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
            return;

        await ImportPathsAsync(dialog.FileNames);
    }

    public async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        string[] files = paths.Where(File.Exists).ToArray();
        if (files.Length == 0)
            return;

        IsBusy = true;
        BusyText = files.Length == 1 ? "Importing wallpaper…" : $"Importing {files.Length} wallpapers…";

        try
        {
            IReadOnlyList<WallpaperItem> added = await _library.ImportAsync(files);
            RefreshAll();
            CurrentPage = AppPage.Library;

            if (added.Count > 0)
                ShowToast(added.Count == 1 ? $"Added “{added[0].Name}”" : $"Added {added.Count} wallpapers");
            else
                ShowToast("Those files are already in your library.");
        }
        catch (Exception ex)
        {
            Log.Error("Import failed", ex);
            ShowToast("Import failed. Check the log for details.");
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    private void Apply(WallpaperItem? item)
    {
        if (item is null)
            return;

        try
        {
            _engine.Apply(item);
            CurrentWallpaper = item;
            UpdateMonitors(true);
            ShowToast($"Applied “{item.Name}” to all displays.");
        }
        catch (Exception ex)
        {
            Log.Error("Apply failed", ex);
            ShowToast("Could not apply the wallpaper.");
        }
    }

    private void Preview(WallpaperItem? item)
    {
        if (item is null)
            return;

        try
        {
            var window = new PreviewWindow(item, Settings, _engine.Monitors)
            {
                Owner = Application.Current.MainWindow
            };

            bool? result = window.ShowDialog();
            if (result == true && window.Applied)
            {
                _engine.Apply(item, window.TargetDevice);
                CurrentWallpaper = item;
                UpdateMonitors(true);
                ShowToast(window.TargetDevice is null
                    ? $"Applied “{item.Name}” to all displays."
                    : $"Applied “{item.Name}” to one display.");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Preview failed", ex);
            ShowToast("Could not open the preview.");
        }
    }

    private void ToggleFavorite(WallpaperItem? item)
    {
        if (item is null)
            return;
        _library.ToggleFavorite(item);
        RefreshAll();
    }

    private void Rename(WallpaperItem? item)
    {
        if (item is null)
            return;

        string? name = InputDialog.Show(Application.Current.MainWindow, "Rename wallpaper",
            "Choose a name for this wallpaper.", item.Name, "Rename");

        if (name is null)
            return;

        _library.Rename(item, name);
        RefreshAll();
        ShowToast("Wallpaper renamed.");
    }

    private void Remove(WallpaperItem? item)
    {
        if (item is null)
            return;

        bool confirmed = ConfirmDialog.Show(Application.Current.MainWindow, "Remove wallpaper?",
            $"\"{item.Name}\" will be deleted from your library and from disk.", "Remove", danger: true);

        if (!confirmed)
            return;

        _engine.WallpaperRemoved(item.Id);
        _library.Remove(item);
        if (ActiveCollection is not null && !_library.Collections.Contains(ActiveCollection))
            ActiveCollection = null;
        RefreshAll();
        UpdateEngineState();
        ShowToast("Wallpaper removed.");
    }

    private void AddToCollection(WallpaperItem? item)
    {
        if (item is null)
            return;

        CollectionPickerDialog.Result? result = CollectionPickerDialog.Show(
            Application.Current.MainWindow, item.Name, _library.Collections, item.CollectionId);

        if (result is null)
            return;

        if (!string.IsNullOrWhiteSpace(result.NewCollectionName))
        {
            CollectionItem created = _library.CreateCollection(result.NewCollectionName);
            _library.SetCollection(item, created.Id);
        }
        else
        {
            _library.SetCollection(item, result.CollectionId);
        }

        RefreshAll();
        ShowToast("Collection updated.");
    }

    private void CreateCollection()
    {
        string? name = InputDialog.Show(Application.Current.MainWindow, "New collection",
            "Group your wallpapers into a collection.", "My collection", "Create");

        if (string.IsNullOrWhiteSpace(name))
            return;

        CollectionItem collection = _library.CreateCollection(name);
        ActiveCollection = collection;
        RefreshAll();
        ShowToast($"Created “{collection.Name}”.");
    }

    private void DeleteCollection(CollectionItem? collection)
    {
        if (collection is null)
            return;

        bool confirmed = ConfirmDialog.Show(Application.Current.MainWindow, "Delete collection?",
            $"“{collection.Name}” will be removed. The wallpapers stay in your library.",
            "Delete", danger: true);

        if (!confirmed)
            return;

        if (ReferenceEquals(ActiveCollection, collection))
            ActiveCollection = null;

        _library.DeleteCollection(collection);
        RefreshAll();
        ShowToast("Collection deleted.");
    }

    private void SelectCollection(CollectionItem? collection)
    {
        if (collection is null)
            return;
        ActiveCollection = collection;
    }

    private void ApplyRandom()
    {
        if (_library.Wallpapers.Count == 0)
        {
            ShowToast("Add a wallpaper first.");
            return;
        }

        WallpaperItem item = _library.Wallpapers[Random.Shared.Next(_library.Wallpapers.Count)];
        _engine.Apply(item);
        CurrentWallpaper = item;
        UpdateMonitors(true);
        ShowToast($"Now playing “{item.Name}”.");
    }

    private void ShuffleDiscover()
    {
        if (DiscoverWallpapers.Count <= 1)
        {
            ApplyRandom();
            return;
        }

        var items = DiscoverWallpapers.ToList();
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }

        DiscoverWallpapers.Clear();
        foreach (WallpaperItem item in items)
            DiscoverWallpapers.Add(item);

        FeaturedWallpaper = DiscoverWallpapers.FirstOrDefault();
        ShowToast("Shuffled your collection.");
    }

    private void StopWallpaper()
    {
        _engine.StopAll();
        CurrentWallpaper = null;
        UpdateMonitors(true);
        UpdateEngineState();
        ShowToast("Live wallpaper stopped.");
    }

    private void OpenLibraryFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Root}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("Could not open library folder: " + ex.Message);
        }
    }

    private void OpenLogFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{AppPaths.LogFile}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("Could not open log: " + ex.Message);
        }
    }

    private void ResetSettings()
    {
        bool confirmed = ConfirmDialog.Show(Application.Current.MainWindow, "Reset settings?",
            "All preferences return to their defaults. Your wallpapers and collections are kept.",
            "Reset", danger: false);

        if (!confirmed)
            return;

        _settings.Reset();
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(Sort));
        ShowToast("Settings reset.");
    }

    private void ChangeMonitorWallpaper(MonitorViewModel? monitor)
    {
        if (monitor is null)
            return;

        WallpaperItem? chosen = WallpaperPickerDialog.Show(
            Application.Current.MainWindow, monitor.DisplayLabel, _library.Wallpapers, monitor.Wallpaper?.Id);

        if (chosen is null)
            return;

        _engine.ApplyToMonitor(chosen, monitor.Monitor);
        UpdateMonitors(true);
        ShowToast($"“{chosen.Name}” applied to {monitor.FriendlyName}.");
    }

    private void ClearMonitor(MonitorViewModel? monitor)
    {
        if (monitor is null)
            return;

        _engine.ClearMonitor(monitor.Monitor);
        UpdateMonitors(true);
        ShowToast("Display cleared.");
    }

    private void RefreshAll()
    {
        RefreshFiltered();

        Favorites.Clear();
        foreach (WallpaperItem item in _library.Wallpapers
                     .Where(w => w.IsFavorite)
                     .OrderByDescending(w => w.AddedUtc))
            Favorites.Add(item);

        Recent.Clear();
        foreach (WallpaperItem item in _library.Wallpapers
                     .OrderByDescending(w => w.AddedUtc)
                     .Take(8))
            Recent.Add(item);

        DiscoverWallpapers.Clear();
        foreach (WallpaperItem item in _library.Wallpapers
                     .OrderByDescending(w => w.IsFavorite)
                     .ThenByDescending(w => w.FileSize)
                     .ThenByDescending(w => w.AddedUtc))
            DiscoverWallpapers.Add(item);

        FeaturedWallpaper = DiscoverWallpapers.FirstOrDefault();

        if (ActiveCollection is not null && !_library.Collections.Contains(ActiveCollection))
            ActiveCollection = null;

        OnPropertyChanged(nameof(LibraryCount));
        OnPropertyChanged(nameof(FavoriteCount));
        OnPropertyChanged(nameof(IsLibraryEmpty));
        OnPropertyChanged(nameof(IsFilteredEmpty));
        OnPropertyChanged(nameof(HasFavorites));
        OnPropertyChanged(nameof(HasRecent));
        OnPropertyChanged(nameof(HasDiscover));
        OnPropertyChanged(nameof(PageSubtitle));
        OnPropertyChanged(nameof(LibrarySizeText));

        UpdateMonitors(true);
    }

    private void RefreshFiltered()
    {
        IEnumerable<WallpaperItem> query = _library.Wallpapers;

        if (ActiveCollection is not null)
            query = query.Where(w => w.CollectionId == ActiveCollection.Id);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string term = SearchText.Trim();
            query = query.Where(w =>
                w.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                w.KindLabel.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        query = Filter switch
        {
            WallpaperFilter.Video => query.Where(w => w.Kind == WallpaperKind.Video),
            WallpaperFilter.Image => query.Where(w => w.Kind == WallpaperKind.Image),
            WallpaperFilter.Gif => query.Where(w => w.Kind == WallpaperKind.Gif),
            _ => query
        };

        query = Sort switch
        {
            SortMode.Oldest => query.OrderBy(w => w.AddedUtc),
            SortMode.Name => query.OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase),
            SortMode.Size => query.OrderByDescending(w => w.FileSize),
            _ => query.OrderByDescending(w => w.AddedUtc)
        };

        Wallpapers.Clear();
        foreach (WallpaperItem item in query)
            Wallpapers.Add(item);

        OnPropertyChanged(nameof(IsFilteredEmpty));
        OnPropertyChanged(nameof(PageSubtitle));
    }

    private void UpdateEngineState()
    {
        WallpaperItem? current = _engine.CurrentWallpaper;
        if (current is null && Settings.LastWallpaperId is Guid id)
            current = _library.Find(id);

        CurrentWallpaper = current;

        OnPropertyChanged(nameof(IsEnginePaused));
        OnPropertyChanged(nameof(IsEngineRunning));
        OnPropertyChanged(nameof(EngineStatus));
        OnPropertyChanged(nameof(EngineDetail));
        OnPropertyChanged(nameof(CpuText));
        OnPropertyChanged(nameof(MemoryText));
        OnPropertyChanged(nameof(VideoEngineText));
        OnPropertyChanged(nameof(PauseButtonText));
        OnPropertyChanged(nameof(PauseGlyph));

        foreach (MonitorViewModel monitor in Monitors)
            monitor.IsPaused = _engine.IsPaused;
    }

    private void UpdateMonitors(bool force)
    {
        string signature = string.Join("|", _engine.Monitors.Select(m => $"{m.DeviceName}:{m.Width}x{m.Height}@{m.Left},{m.Top}"));

        if (!force && signature == _monitorSignature)
        {
            foreach (MonitorViewModel vm in Monitors)
            {
                Guid? id = Settings.MonitorAssignments.TryGetValue(vm.DeviceName, out Guid assigned)
                    ? assigned
                    : Settings.LastWallpaperId;
                vm.Wallpaper = id is null ? null : _library.Find(id.Value);
                vm.IsPaused = _engine.IsPaused;
            }
            return;
        }

        _monitorSignature = signature;
        Monitors.Clear();

        foreach (MonitorInfo monitor in _engine.Monitors)
        {
            var vm = new MonitorViewModel(monitor);
            Guid? id = Settings.MonitorAssignments.TryGetValue(monitor.DeviceName, out Guid assigned)
                ? assigned
                : Settings.LastWallpaperId;
            vm.Wallpaper = id is null ? null : _library.Find(id.Value);
            vm.IsPaused = _engine.IsPaused;
            Monitors.Add(vm);
        }

        OnPropertyChanged(nameof(MonitorCount));
    }

    public void ShowToast(string message)
    {
        ToastMessage = message;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
