using LibVLCSharp.Shared;
using Nocturne.Services;

namespace Nocturne.Rendering;

/// <summary>
/// Owns the single LibVLC instance shared by every display and the preview player.
/// </summary>
public static class PlaybackEngine
{
    private static readonly object Gate = new();
    private static LibVLC? _instance;
    private static bool _initialized;

    public static bool IsAvailable { get; private set; }
    public static string? Error { get; private set; }

    public static LibVLC? Instance
    {
        get
        {
            EnsureInitialized();
            return _instance;
        }
    }

    public static bool EnsureInitialized()
    {
        lock (Gate)
        {
            if (_initialized)
                return IsAvailable;

            _initialized = true;

            try
            {
                string baseDir = AppContext.BaseDirectory;
                string localVlc = Path.Combine(baseDir, "libvlc", "win-x64");
                if (Directory.Exists(localVlc))
                    Core.Initialize(localVlc);
                else
                    Core.Initialize();

                _instance = new LibVLC(
                    "--no-video-title-show",
                    "--no-osd",
                    "--no-snapshot-preview",
                    "--quiet",
                    "--avcodec-hw=any",
                    "--file-caching=1500",
                    "--network-caching=1500",
                    "--drop-late-frames",
                    "--no-sub-autodetect-file",
                    "--no-spu",
                    "--no-stats");

                IsAvailable = true;
                Error = null;
                Log.Info("LibVLC initialized (" + _instance.Version + ").");
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                Error = ex.Message;
                Log.Error("LibVLC could not be initialized", ex);
            }

            return IsAvailable;
        }
    }

    public static void Shutdown()
    {
        lock (Gate)
        {
            try { _instance?.Dispose(); } catch { }
            _instance = null;
            _initialized = false;
            IsAvailable = false;
        }
    }
}
