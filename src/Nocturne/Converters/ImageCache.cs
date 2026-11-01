using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nocturne.Converters;

internal static class ImageCache
{
    private const int MaxEntries = 150;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Cache = new(StringComparer.OrdinalIgnoreCase);

    private sealed record Entry(DateTime Stamp, BitmapImage Image);

    public static BitmapImage? Load(string? path, int decodeWidth)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        string key = path + "|" + decodeWidth;

        try
        {
            DateTime stamp = File.GetLastWriteTimeUtc(path);

            lock (Gate)
            {
                if (Cache.TryGetValue(key, out Entry? cached) && cached.Stamp == stamp)
                    return cached.Image;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.DecodePixelWidth = decodeWidth;
            bitmap.EndInit();
            bitmap.Freeze();

            lock (Gate)
            {
                if (Cache.Count >= MaxEntries)
                    Cache.Clear();
                Cache[key] = new Entry(stamp, bitmap);
            }

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public static Brush FallbackBrush(string? seed)
    {
        int hash = 0;
        if (!string.IsNullOrEmpty(seed))
        {
            foreach (char c in seed)
                hash = unchecked(hash * 31 + c);
        }

        double hue = (hash & 0x7fffffff) % 360;
        Color a = FromHsl(hue, 0.48, 0.24);
        Color b = FromHsl((hue + 45) % 360, 0.42, 0.07);

        var brush = new LinearGradientBrush(a, b, new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
        brush.Freeze();
        return brush;
    }

    private static Color FromHsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = l - c / 2;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x)
        };
        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
