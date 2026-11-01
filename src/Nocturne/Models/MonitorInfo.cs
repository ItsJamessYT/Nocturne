namespace Nocturne.Models;

public sealed class MonitorInfo
{
    public required string DeviceName { get; init; }
    public required string FriendlyName { get; init; }
    public int Left { get; init; }
    public int Top { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsPrimary { get; init; }
    public int Dpi { get; init; } = 96;

    public string ResolutionText => $"{Width} × {Height}";
    public string DisplayLabel => IsPrimary ? $"{FriendlyName} (Primary)" : FriendlyName;

    public override string ToString() => DisplayLabel;
}
