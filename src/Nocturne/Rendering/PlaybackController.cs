using LibVLCSharp.Shared;
using Nocturne.Models;
using Nocturne.Services;

namespace Nocturne.Rendering;

/// <summary>
/// Wraps a LibVLC MediaPlayer bound to a native child window. Handles seamless
/// looping, scaling modes, audio, speed and pause/resume.
/// </summary>
public sealed class PlaybackController : IDisposable
{
    private readonly MediaPlayer? _player;
    private Media? _media;
    private IntPtr _hwnd;
    private int _targetWidth;
    private int _targetHeight;
    private WallpaperScale _scaling = WallpaperScale.Fill;
    private bool _paused;
    private bool _disposed;
    private WallpaperItem? _item;

    public bool IsAvailable => _player is not null;
    public bool IsPaused => _paused;
    public WallpaperItem? Item => _item;
    public long Time => _player?.Time ?? 0;
    public long Length => _player?.Length ?? 0;
    public bool IsPlaying => _player?.IsPlaying ?? false;

    public void Seek(double fraction)
    {
        if (_player is null)
            return;

        try
        {
            long length = _player.Length;
            if (length <= 0)
                return;
            _player.Time = (long)(Math.Clamp(fraction, 0, 1) * length);
        }
        catch (Exception ex)
        {
            Log.Warn("Seek failed: " + ex.Message);
        }
    }

    public event EventHandler? VoutReady;
    public event EventHandler<string>? Failed;

    public PlaybackController()
    {
        if (!PlaybackEngine.EnsureInitialized() || PlaybackEngine.Instance is null)
            return;

        try
        {
            _player = new MediaPlayer(PlaybackEngine.Instance)
            {
                EnableHardwareDecoding = true,
                EnableKeyInput = false,
                EnableMouseInput = false
            };

            _player.Playing += (_, _) => ApplyScaling();
            _player.Vout += (_, _) =>
            {
                ApplyScaling();
                VoutReady?.Invoke(this, EventArgs.Empty);
            };
            _player.EncounteredError += (_, _) => Failed?.Invoke(this, "This file could not be decoded by LibVLC.");
            _player.EndReached += OnEndReached;
        }
        catch (Exception ex)
        {
            Log.Error("Could not create a LibVLC media player", ex);
            _player = null;
        }
    }

    public void SetTarget(IntPtr hwnd)
    {
        _hwnd = hwnd;
        if (_player is not null && hwnd != IntPtr.Zero)
            _player.Hwnd = hwnd;
    }

    public void SetTargetSize(int width, int height)
    {
        _targetWidth = Math.Max(1, width);
        _targetHeight = Math.Max(1, height);
        ApplyScaling();
    }

    public void Load(WallpaperItem item, AppSettings settings)
    {
        if (_player is null || PlaybackEngine.Instance is null)
        {
            Failed?.Invoke(this, "Video engine unavailable.");
            return;
        }

        try
        {
            StopInternal();

            _item = item;
            _paused = false;

            if (_hwnd != IntPtr.Zero)
                _player.Hwnd = _hwnd;

            _media = new Media(PlaybackEngine.Instance, item.FilePath, FromType.FromPath);
            _media.AddOption(":input-repeat=65535");
            _media.AddOption(":no-sub-autodetect-file");
            _media.AddOption(":file-caching=300");
            _media.AddOption(":image-duration=3600");

            ApplySettings(settings);
            _player.Play(_media);
            Log.Info($"Playing '{item.Name}' ({item.Kind}).");
        }
        catch (Exception ex)
        {
            Log.Error($"Could not play {item.Name}", ex);
            Failed?.Invoke(this, ex.Message);
        }
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        // input-repeat normally keeps the media looping. This is a safety net for
        // containers that ignore it; never call libvlc from its own callback thread.
        Media? media = _media;
        MediaPlayer? player = _player;
        if (media is null || player is null || _disposed)
            return;

        Task.Run(() =>
        {
            try
            {
                if (_disposed || _media != media) return;
                player.Stop();
                player.Play(media);
                if (_paused) player.SetPause(true);
            }
            catch (Exception ex)
            {
                Log.Warn("Loop restart failed: " + ex.Message);
            }
        });
    }

    public void ApplySettings(AppSettings settings)
    {
        if (_player is null)
            return;

        try
        {
            _scaling = settings.Scaling;
            _player.Mute = settings.MuteAudio;
            _player.Volume = (int)Math.Round(Math.Clamp(settings.Volume, 0, 100));
            _player.SetRate((float)settings.PlaybackSpeed);
            ApplyScaling();
        }
        catch (Exception ex)
        {
            Log.Warn("Could not apply playback settings: " + ex.Message);
        }
    }

    public void ApplyScaling()
    {
        if (_player is null)
            return;

        try
        {
            _player.Scale = 0;

            if (_targetWidth <= 1 || _targetHeight <= 1)
                return;

            switch (_scaling)
            {
                case WallpaperScale.Fit:
                    _player.CropGeometry = null;
                    _player.AspectRatio = null;
                    break;

                case WallpaperScale.Stretch:
                    _player.CropGeometry = null;
                    _player.AspectRatio = AspectString(_targetWidth, _targetHeight);
                    break;

                default:
                    _player.AspectRatio = null;
                    _player.CropGeometry = BuildCropGeometry();
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Scaling failed: " + ex.Message);
        }
    }

    private string? BuildCropGeometry()
    {
        if (_player is null)
            return null;

        try
        {
            uint width = 0;
            uint height = 0;
            if (!_player.Size(0, ref width, ref height) || width == 0 || height == 0)
            {
                Media? media = _player.Media ?? _media;
                MediaTrack? track = media?.Tracks.FirstOrDefault(t => t.TrackType == TrackType.Video);
                if (track is null)
                    return null;
                width = track.Value.Data.Video.Width;
                height = track.Value.Data.Video.Height;
            }

            if (width == 0 || height == 0)
                return null;

            double targetAspect = _targetWidth / (double)_targetHeight;
            double sourceAspect = width / (double)height;

            int cropWidth = (int)width;
            int cropHeight = (int)height;
            int x = 0, y = 0;

            if (sourceAspect > targetAspect)
            {
                cropWidth = (int)Math.Round(height * targetAspect);
                x = (int)((width - cropWidth) / 2d);
            }
            else if (sourceAspect < targetAspect)
            {
                cropHeight = (int)Math.Round(width / targetAspect);
                y = (int)((height - cropHeight) / 2d);
            }
            else
            {
                return null;
            }

            if (cropWidth <= 0 || cropHeight <= 0)
                return null;

            return $"{cropWidth}x{cropHeight}+{x}+{y}";
        }
        catch
        {
            return null;
        }
    }

    private static string AspectString(int width, int height)
    {
        int a = width, b = height;
        while (b != 0)
        {
            int t = b;
            b = a % b;
            a = t;
        }
        return $"{width / a}:{height / a}";
    }

    public void SetPaused(bool paused)
    {
        if (_player is null || _paused == paused)
            return;

        try
        {
            _player.SetPause(paused);
            _paused = paused;
        }
        catch (Exception ex)
        {
            Log.Warn("Pause failed: " + ex.Message);
        }
    }

    public void Stop()
    {
        _paused = false;
        StopInternal();
    }

    private void StopInternal()
    {
        try
        {
            if (_player is not null)
            {
                _player.Stop();
                _player.Hwnd = IntPtr.Zero;
            }
        }
        catch { }

        try { _media?.Dispose(); } catch { }
        _media = null;
        _item = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            if (_player is not null)
            {
                _player.EndReached -= OnEndReached;
                _player.Stop();
                _player.Hwnd = IntPtr.Zero;
                _player.Dispose();
            }
        }
        catch { }

        try { _media?.Dispose(); } catch { }
        _media = null;
    }
}
