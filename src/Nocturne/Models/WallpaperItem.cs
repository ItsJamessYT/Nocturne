using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Nocturne.Models;

/// <summary>
/// A wallpaper in the local library. The original file is copied into the
/// Nocturne library folder on import, so moving or deleting the source file
/// never breaks the wallpaper.
/// </summary>
public sealed class WallpaperItem : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private bool _isFavorite;
    private Guid? _collectionId;
    private string? _thumbnailPath;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public string FilePath { get; set; } = string.Empty;

    public string? ThumbnailPath
    {
        get => _thumbnailPath;
        set
        {
            if (Set(ref _thumbnailPath, value))
                OnPropertyChanged(nameof(HasThumbnail));
        }
    }

    public WallpaperKind Kind { get; set; }

    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;

    public long FileSize { get; set; }

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (Set(ref _isFavorite, value))
                OnPropertyChanged(nameof(FavoriteGlyph));
        }
    }

    public Guid? CollectionId
    {
        get => _collectionId;
        set => Set(ref _collectionId, value);
    }

    public string? SourceFileName { get; set; }

    [JsonIgnore]
    public bool IsAnimated => Kind is WallpaperKind.Video or WallpaperKind.Gif;

    [JsonIgnore]
    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailPath) && File.Exists(ThumbnailPath);

    [JsonIgnore]
    public string KindLabel => Kind switch
    {
        WallpaperKind.Video => "VIDEO",
        WallpaperKind.Gif => "GIF",
        _ => "IMAGE"
    };

    [JsonIgnore]
    public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734";

    [JsonIgnore]
    public string SizeText
    {
        get
        {
            double mb = FileSize / 1024d / 1024d;
            if (mb >= 1024)
                return $"{mb / 1024:0.0} GB";
            if (mb >= 100)
                return $"{mb:0} MB";
            return $"{mb:0.0} MB";
        }
    }

    [JsonIgnore]
    public string AddedText
    {
        get
        {
            TimeSpan age = DateTime.UtcNow - AddedUtc;
            if (age.TotalMinutes < 2) return "Just now";
            if (age.TotalHours < 1) return $"{(int)age.TotalMinutes} min ago";
            if (age.TotalDays < 1) return $"{(int)age.TotalHours} hr ago";
            if (age.TotalDays < 7) return $"{(int)age.TotalDays} days ago";
            return AddedUtc.ToLocalTime().ToString("MMM d, yyyy");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(property);
        return true;
    }

    public void OnPropertyChanged([CallerMemberName] string? property = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
