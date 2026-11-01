using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Nocturne.Models;

public sealed class CollectionItem : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private int _count;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    [JsonIgnore]
    public int Count
    {
        get => _count;
        set => Set(ref _count, value);
    }

    [JsonIgnore]
    public string CountText => Count == 1 ? "1 wallpaper" : $"{Count} wallpapers";

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(property);
        if (property == nameof(Count))
            OnPropertyChanged(nameof(CountText));
        return true;
    }

    public void OnPropertyChanged([CallerMemberName] string? property = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
