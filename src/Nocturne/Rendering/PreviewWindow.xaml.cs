using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nocturne.Interop;
using Nocturne.Models;
using Nocturne.Services;

namespace Nocturne.Rendering;

public partial class PreviewWindow : Window
{
    private sealed record DisplayOption(string Label, MonitorInfo? Monitor);

    private readonly PlaybackController _playback;
    private readonly DispatcherTimer _timer;
    private bool _ready;
    private bool _paused;
    private bool _pendingLoad;
    private bool _updatingProgress;

    public PreviewWindow(WallpaperItem item, AppSettings settings, IReadOnlyList<MonitorInfo> monitors)
    {
        InitializeComponent();

        Wallpaper = item;
        Settings = settings;
        DataContext = this;

        _playback = new PlaybackController();
        _playback.VoutReady += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Render, UpdateTargetSize);
        _playback.Failed += OnPlaybackFailed;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _timer.Tick += (_, _) => UpdateProgress();

        Surface.HandleReady += OnSurfaceReady;
        Surface.SizeChanged += (_, _) => UpdateTargetSize();
        Loaded += OnLoaded;
        Closed += OnClosed;
        settings.PropertyChanged += OnSettingsChanged;

        InitializeChoices(monitors);
        UpdatePlayButton();
    }

    public WallpaperItem Wallpaper { get; }
    public AppSettings Settings { get; }

    public bool Applied { get; private set; }
    public string? TargetDevice { get; private set; }

    private void InitializeChoices(IReadOnlyList<MonitorInfo> monitors)
    {
        _ready = false;

        var speeds = new List<double> { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };
        if (speeds.All(s => Math.Abs(s - Settings.PlaybackSpeed) > 0.001))
            speeds.Add(Settings.PlaybackSpeed);
        speeds.Sort();

        SpeedBox.ItemsSource = speeds;
        SpeedBox.SelectedItem = speeds.FirstOrDefault(s => Math.Abs(s - Settings.PlaybackSpeed) < 0.001);

        ScaleBox.ItemsSource = Enum.GetValues<WallpaperScale>();
        ScaleBox.SelectedItem = Settings.Scaling;

        var displays = new List<DisplayOption> { new("All displays", null) };
        displays.AddRange(monitors.Select(m => new DisplayOption(m.DisplayLabel, m)));
        DisplayBox.ItemsSource = displays;
        DisplayBox.SelectedIndex = 0;

        _ready = true;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateTargetSize();

        if (Wallpaper.IsAnimated)
        {
            PreviewImage.Visibility = Visibility.Collapsed;
            Surface.Visibility = Visibility.Visible;
            PlayButton.IsEnabled = true;
            _pendingLoad = true;

            if (Surface.SurfaceHandle != IntPtr.Zero)
            {
                _playback.SetTarget(Surface.SurfaceHandle);
                LoadVideo();
            }

            _timer.Start();
        }
        else
        {
            _pendingLoad = false;
            Surface.Visibility = Visibility.Collapsed;
            PlayButton.IsEnabled = false;
            ProgressSlider.IsEnabled = false;
            LoadStillImage();
        }

        UpdateImageStretch();
    }

    private void OnSurfaceReady(object? sender, EventArgs e)
    {
        _playback.SetTarget(Surface.SurfaceHandle);
        UpdateTargetSize();

        if (_pendingLoad && Wallpaper.IsAnimated)
            LoadVideo();
    }

    private void LoadVideo()
    {
        _pendingLoad = false;

        if (!PlaybackEngine.IsAvailable || !_playback.IsAvailable)
        {
            UnavailableText.Text = PlaybackEngine.Error ?? "LibVLC is not available.";
            UnavailableText.Visibility = Visibility.Visible;
            PlayButton.IsEnabled = false;
            return;
        }

        _playback.Load(Wallpaper, Settings);
        if (_paused)
            _playback.SetPaused(true);
    }

    private void LoadStillImage()
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(Wallpaper.FilePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            bitmap.Freeze();

            PreviewImage.Source = bitmap;
            PreviewImage.Visibility = Visibility.Visible;
            UpdateImageStretch();
        }
        catch (Exception ex)
        {
            Log.Warn("Preview image failed: " + ex.Message);
            UnavailableText.Visibility = Visibility.Visible;
        }
    }

    private void UpdateTargetSize()
    {
        int width = (int)Math.Max(1, Stage.ActualWidth);
        int height = (int)Math.Max(1, Stage.ActualHeight);
        _playback.SetTargetSize(width, height);
    }

    private void UpdateImageStretch()
    {
        if (PreviewImage is null)
            return;

        PreviewImage.Stretch = Settings.Scaling switch
        {
            WallpaperScale.Fit => Stretch.Uniform,
            WallpaperScale.Stretch => Stretch.Fill,
            _ => Stretch.UniformToFill
        };
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.MuteAudio) or nameof(AppSettings.Volume)
            or nameof(AppSettings.PlaybackSpeed) or nameof(AppSettings.Scaling))
        {
            _playback.ApplySettings(Settings);
            UpdateImageStretch();
        }
    }

    private void UpdateProgress()
    {
        if (!Wallpaper.IsAnimated)
            return;

        long length = _playback.Length;
        long time = _playback.Time;

        if (length <= 0)
            return;

        _updatingProgress = true;
        ProgressSlider.Value = Math.Clamp(time / (double)length, 0, 1);
        _updatingProgress = false;
        TimeText.Text = $"{FormatTime(time)} / {FormatTime(length)}";
    }

    private static string FormatTime(long milliseconds)
    {
        if (milliseconds <= 0)
            return "0:00";

        var time = TimeSpan.FromMilliseconds(milliseconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }

    private void OnProgressChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _updatingProgress || !Wallpaper.IsAnimated)
            return;

        _playback.Seek(e.NewValue);
    }

    private void OnTogglePlay(object sender, RoutedEventArgs e)
    {
        if (!Wallpaper.IsAnimated)
            return;

        _paused = !_paused;
        _playback.SetPaused(_paused);
        UpdatePlayButton();
    }

    private void UpdatePlayButton()
    {
        if (PlayGlyph is null || PlayText is null)
            return;

        PlayGlyph.Text = _paused ? "\uE768" : "\uE769";
        PlayText.Text = _paused ? "Play" : "Pause";
    }

    private void OnSpeedChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_ready || SpeedBox.SelectedItem is not double speed)
            return;

        Settings.PlaybackSpeed = speed;
    }

    private void OnScaleChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_ready || ScaleBox.SelectedItem is not WallpaperScale scale)
            return;

        Settings.Scaling = scale;
        UpdateImageStretch();
    }

    private void OnPlaybackFailed(object? sender, string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            UnavailableText.Text = message;
            UnavailableText.Visibility = Visibility.Visible;
        });
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        Applied = true;
        TargetDevice = (DisplayBox.SelectedItem as DisplayOption)?.Monitor?.DeviceName;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            OnCancel(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Space && e.OriginalSource is not System.Windows.Controls.TextBox)
        {
            OnTogglePlay(sender, e);
            e.Handled = true;
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int rounded = 2;
            NativeMethods.DwmSetWindowAttribute(hwnd, 33, ref rounded, sizeof(int));
            int dark = 1;
            NativeMethods.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        }
        catch { }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        Settings.PropertyChanged -= OnSettingsChanged;
        _playback.Dispose();
    }
}
