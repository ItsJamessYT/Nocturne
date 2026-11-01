using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nocturne.Models;

namespace Nocturne.Services;

/// <summary>
/// Produces consistent 16:9 preview images for the library grid. Uses the Windows
/// shell thumbnail provider for videos and direct decoding for still images, with
/// an original generated card as a graceful fallback.
/// </summary>
public static class ThumbnailService
{
    public const int Width = 640;
    public const int Height = 360;

    public static Task<string?> CreateAsync(string sourcePath, WallpaperKind kind, string outputPath, string displayName)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                string? result = CreateCore(sourcePath, kind, outputPath, displayName);
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                Log.Warn($"Thumbnail generation failed for {Path.GetFileName(sourcePath)}: {ex.Message}");
                tcs.TrySetResult(null);
            }
        })
        {
            IsBackground = true,
            Name = "Nocturne thumbnail"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private static string? CreateCore(string sourcePath, WallpaperKind kind, string outputPath, string displayName)
    {
        BitmapSource? preview = null;

        if (kind == WallpaperKind.Image)
        {
            preview = LoadImage(sourcePath, 1024);
        }

        preview ??= TryShellThumbnail(sourcePath);
        preview ??= LoadImage(sourcePath, 1024);

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(17, 17, 23)), null,
                new Rect(0, 0, Width, Height));

            if (preview is not null)
            {
                double scale = Math.Max(Width / (double)preview.PixelWidth, Height / (double)preview.PixelHeight);
                double w = preview.PixelWidth * scale;
                double h = preview.PixelHeight * scale;
                var dest = new Rect((Width - w) / 2, (Height - h) / 2, w, h);

                RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
                dc.DrawImage(preview, dest);
            }
            else
            {
                DrawPlaceholder(dc, displayName);
            }

            var shade = new LinearGradientBrush(
                Color.FromArgb(0, 0, 0, 0),
                Color.FromArgb(120, 0, 0, 0),
                new Point(0, 0.35), new Point(0, 1));
            dc.DrawRectangle(shade, null, new Rect(0, 0, Width, Height));
        }

        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream fs = File.Create(outputPath))
            encoder.Save(fs);

        return outputPath;
    }

    private static void DrawPlaceholder(DrawingContext dc, string displayName)
    {
        int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(displayName ?? string.Empty);
        double hue = Math.Abs(hash % 360);
        Color a = FromHsl(hue, 0.62, 0.35);
        Color b = FromHsl((hue + 55) % 360, 0.55, 0.08);

        var brush = new LinearGradientBrush(a, b, new Point(0, 0), new Point(1, 1));
        dc.DrawRectangle(brush, null, new Rect(0, 0, Width, Height));

        var glow = new RadialGradientBrush(
            Color.FromArgb(90, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255))
        {
            Center = new Point(0.75, 0.2),
            GradientOrigin = new Point(0.75, 0.2),
            RadiusX = 0.7,
            RadiusY = 1.1
        };
        dc.DrawRectangle(glow, null, new Rect(0, 0, Width, Height));

        string initial = string.IsNullOrWhiteSpace(displayName)
            ? "N"
            : displayName.Trim()[..1].ToUpper(CultureInfo.InvariantCulture);

        var text = new FormattedText(initial, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            150, new SolidColorBrush(Color.FromArgb(225, 255, 255, 255)), 1.0);

        dc.DrawText(text, new Point(44, Height - text.Height - 28));
    }

    private static BitmapSource? LoadImage(string path, int decodeWidth)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.DecodePixelWidth = decodeWidth;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? TryShellThumbnail(string path)
    {
        IntPtr hBitmap = IntPtr.Zero;
        IShellItemImageFactory? factory = null;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
            if (factory is null)
                return null;

            var size = new SIZE(1024, 576);
            int hr = factory.GetImage(size, SIIGBF.ThumbnailOnly | SIIGBF.BiggerSizeOk, out hBitmap);
            if (hr != 0 || hBitmap == IntPtr.Zero)
            {
                hr = factory.GetImage(size, SIIGBF.BiggerSizeOk, out hBitmap);
                if (hr != 0 || hBitmap == IntPtr.Zero)
                    return null;
            }

            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
                DeleteObject(hBitmap);
            if (factory is not null)
                Marshal.ReleaseComObject(factory);
        }
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

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
        public SIZE(int w, int h) { cx = w; cy = h; }
    }

    [Flags]
    private enum SIIGBF
    {
        ResizeToFit = 0x00,
        BiggerSizeOk = 0x01,
        MemoryOnly = 0x02,
        IconOnly = 0x04,
        ThumbnailOnly = 0x08,
        InCacheOnly = 0x10,
        CropToSquare = 0x20,
        WideThumbnails = 0x40,
        IconBackground = 0x80,
        ScaleUp = 0x100
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
