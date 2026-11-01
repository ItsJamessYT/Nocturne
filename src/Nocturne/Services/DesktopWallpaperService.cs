using Nocturne.Interop;

namespace Nocturne.Services;

/// <summary>
/// Remembers the wallpaper Windows had before Nocturne took over the desktop and
/// puts it back when the engine stops or the app is closed.
/// </summary>
public static class DesktopWallpaperService
{
    private static string _originalPath = string.Empty;
    private static bool _captured;

    public static void Capture()
    {
        if (_captured)
            return;

        _captured = true;

        try
        {
            _originalPath = NativeMethods.GetDesktopWallpaperPath();
            Log.Info(string.IsNullOrWhiteSpace(_originalPath)
                ? "No desktop wallpaper file is configured; the desktop color will be restored."
                : "Captured the current desktop wallpaper.");
        }
        catch (Exception ex)
        {
            Log.Warn("Could not read the current desktop wallpaper: " + ex.Message);
        }
    }

    public static void Restore()
    {
        if (!_captured)
            return;

        try
        {
            if (!string.IsNullOrWhiteSpace(_originalPath) && File.Exists(_originalPath))
            {
                NativeMethods.SetDesktopWallpaper(_originalPath);
                Log.Info("Restored the desktop wallpaper.");
            }
            else
            {
                // The original may be a slideshow or Spotlight image that has no stable
                // file path. Closing the wallpaper surfaces is enough; Explorer repaints.
                Log.Info("Live wallpaper surfaces closed; leaving the Windows wallpaper as configured.");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Could not restore the desktop wallpaper: " + ex.Message);
        }
    }
}
