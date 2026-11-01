using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using Nocturne.Models;

namespace Nocturne.Services;

public sealed class LibraryService
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mkv", ".mov", ".m4v", ".avi", ".wmv", ".mpg", ".mpeg", ".ts", ".m2ts", ".mts", ".vob", ".flv", ".ogv", ".3gp", ".3g2", ".asf", ".rm", ".rmvb"
    };

    private static readonly HashSet<string> GifExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".gif", ".apng"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tif", ".tiff", ".ico", ".jfif", ".jpe", ".dib"
    };

    public static readonly string FileDialogFilter =
        "Wallpapers|*.mp4;*.webm;*.mkv;*.mov;*.m4v;*.avi;*.wmv;*.mpg;*.mpeg;*.ts;*.m2ts;*.mts;*.vob;*.flv;*.ogv;*.3gp;*.3g2;*.asf;*.gif;*.apng;*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.tif;*.tiff;*.ico;*.jfif|" +
        "Videos|*.mp4;*.webm;*.mkv;*.mov;*.m4v;*.avi;*.wmv;*.mpg;*.mpeg;*.ts;*.m2ts;*.mts;*.vob;*.flv;*.ogv;*.3gp;*.3g2;*.asf|" +
        "Animated|*.gif;*.apng;*.webp|" +
        "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.tif;*.tiff;*.ico;*.jfif|" +
        "All files|*.*";

    private sealed class LibraryData
    {
        public List<WallpaperItem> Wallpapers { get; set; } = new();
        public List<CollectionItem> Collections { get; set; } = new();
    }

    private readonly SemaphoreSlim _thumbnailGate = new(2, 2);
    private bool _loaded;

    public ObservableCollection<WallpaperItem> Wallpapers { get; } = new();
    public ObservableCollection<CollectionItem> Collections { get; } = new();

    public event EventHandler? Changed;

    public void Load()
    {
        AppPaths.EnsureCreated();
        LibraryData? data = JsonStore.Load<LibraryData>(AppPaths.LibraryFile);

        Wallpapers.Clear();
        Collections.Clear();

        if (data is not null)
        {
            foreach (WallpaperItem item in data.Wallpapers)
            {
                if (string.IsNullOrWhiteSpace(item.FilePath) || !File.Exists(item.FilePath))
                {
                    Log.Warn($"Skipping missing wallpaper file: {item.FilePath}");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.Name))
                    item.Name = Path.GetFileNameWithoutExtension(item.FilePath);

                Wallpapers.Add(item);
            }

            foreach (CollectionItem collection in data.Collections)
                Collections.Add(collection);
        }

        _loaded = true;
        RecomputeCollectionCounts();
        Log.Info($"Library loaded: {Wallpapers.Count} wallpapers, {Collections.Count} collections.");
    }

    public WallpaperItem? Find(Guid id) => Wallpapers.FirstOrDefault(w => w.Id == id);

    public async Task<IReadOnlyList<WallpaperItem>> ImportAsync(IEnumerable<string> paths)
    {
        var imported = new List<WallpaperItem>();
        var failures = new List<string>();

        foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(path))
                {
                    failures.Add($"{Path.GetFileName(path)} (not found)");
                    continue;
                }

                WallpaperKind kind = KindFromExtension(Path.GetExtension(path)) ?? DetectKind(path);

                var info = new FileInfo(path);
                WallpaperItem? duplicate = Wallpapers.FirstOrDefault(w =>
                    w.FileSize == info.Length &&
                    string.Equals(w.SourceFileName, info.Name, StringComparison.OrdinalIgnoreCase));

                if (duplicate is not null)
                {
                    Log.Info($"Skipped duplicate import: {info.Name}");
                    continue;
                }

                var item = new WallpaperItem
                {
                    Id = Guid.NewGuid(),
                    Name = Path.GetFileNameWithoutExtension(path),
                    Kind = kind,
                    AddedUtc = DateTime.UtcNow,
                    FileSize = info.Length,
                    SourceFileName = info.Name
                };

                string folder = Path.Combine(AppPaths.Wallpapers, item.Id.ToString("N"));
                string fileName = SanitizeFileName(Path.GetFileNameWithoutExtension(path)) + Path.GetExtension(path).ToLowerInvariant();
                string destination = Path.Combine(folder, fileName);

                await Task.Run(() =>
                {
                    Directory.CreateDirectory(folder);
                    File.Copy(path, destination, true);
                });

                item.FilePath = destination;
                Wallpapers.Insert(0, item);
                imported.Add(item);
                Save();

                _ = GenerateThumbnailAsync(item);
            }
            catch (Exception ex)
            {
                Log.Error($"Import failed for {path}", ex);
                failures.Add($"{Path.GetFileName(path)} ({ex.Message})");
            }
        }

        if (failures.Count > 0)
            Log.Warn("Some files were not imported: " + string.Join("; ", failures));

        RecomputeCollectionCounts();
        RaiseChanged();
        return imported;
    }

    private async Task GenerateThumbnailAsync(WallpaperItem item)
    {
        await _thumbnailGate.WaitAsync();
        try
        {
            string output = Path.Combine(AppPaths.Thumbnails, item.Id.ToString("N") + ".png");
            string? thumb = await ThumbnailService.CreateAsync(item.FilePath, item.Kind, output, item.Name);
            if (thumb is null)
                return;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                item.ThumbnailPath = thumb;
                Save();
                RaiseChanged();
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"Thumbnail pipeline failed: {ex.Message}");
        }
        finally
        {
            _thumbnailGate.Release();
        }
    }

    public void Remove(WallpaperItem item)
    {
        Wallpapers.Remove(item);
        try
        {
            string? folder = Path.GetDirectoryName(item.FilePath);
            if (!string.IsNullOrWhiteSpace(folder) && folder.StartsWith(AppPaths.Wallpapers, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Could not delete wallpaper files: " + ex.Message);
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(item.ThumbnailPath) && File.Exists(item.ThumbnailPath))
                File.Delete(item.ThumbnailPath);
        }
        catch { }

        Save();
        RecomputeCollectionCounts();
        RaiseChanged();
    }

    public void Rename(WallpaperItem item, string newName)
    {
        newName = newName.Trim();
        if (string.IsNullOrWhiteSpace(newName))
            return;

        item.Name = newName;
        Save();
        RaiseChanged();
    }

    public void ToggleFavorite(WallpaperItem item)
    {
        item.IsFavorite = !item.IsFavorite;
        Save();
        RaiseChanged();
    }

    public void SetCollection(WallpaperItem item, Guid? collectionId)
    {
        item.CollectionId = collectionId;
        Save();
        RecomputeCollectionCounts();
        RaiseChanged();
    }

    public CollectionItem CreateCollection(string name)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = "New collection";

        string baseName = name;
        int n = 2;
        while (Collections.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName} {n++}";

        var collection = new CollectionItem { Name = name };
        Collections.Add(collection);
        Save();
        RaiseChanged();
        return collection;
    }

    public void RenameCollection(CollectionItem collection, string name)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;
        collection.Name = name;
        Save();
        RaiseChanged();
    }

    public void DeleteCollection(CollectionItem collection)
    {
        foreach (WallpaperItem item in Wallpapers.Where(w => w.CollectionId == collection.Id).ToList())
            item.CollectionId = null;

        Collections.Remove(collection);
        Save();
        RecomputeCollectionCounts();
        RaiseChanged();
    }

    public void RecomputeCollectionCounts()
    {
        foreach (CollectionItem collection in Collections)
            collection.Count = Wallpapers.Count(w => w.CollectionId == collection.Id);
    }

    public void Save()
    {
        if (!_loaded)
            return;

        var data = new LibraryData
        {
            Wallpapers = Wallpapers.ToList(),
            Collections = Collections.ToList()
        };
        JsonStore.Save(AppPaths.LibraryFile, data);
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public static WallpaperKind? KindFromExtension(string extension)
    {
        if (VideoExtensions.Contains(extension)) return WallpaperKind.Video;
        if (GifExtensions.Contains(extension)) return WallpaperKind.Gif;
        if (ImageExtensions.Contains(extension)) return WallpaperKind.Image;
        return null;
    }

    /// <summary>
    /// Best-effort detection for files whose extension is not in the known
    /// lists. Tries to decode the file as an image; anything else is handed
    /// to LibVLC, which can play a very wide range of containers and codecs.
    /// </summary>
    public static WallpaperKind DetectKind(string path)
    {
        try
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            if (bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
                return WallpaperKind.Image;
        }
        catch
        {
            // Not a decodable image; fall through to video.
        }

        return WallpaperKind.Video;
    }

    private static string SanitizeFileName(string name)
    {
        string cleaned = Regex.Replace(name, @"[<>:""/\\|?*\x00-\x1F]", "_").Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = "wallpaper";
        return cleaned.Length > 80 ? cleaned[..80] : cleaned;
    }
}
