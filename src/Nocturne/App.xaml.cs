using System.Windows;
using System.Windows.Threading;
using Nocturne.Dialogs;
using Nocturne.Rendering;
using Nocturne.Services;
using Nocturne.ViewModels;
using Nocturne.Views;

namespace Nocturne;

public partial class App : Application
{
    private SingleInstance? _instance;

    public static new App Current => (App)Application.Current;

    public LibraryService Library { get; private set; } = null!;
    public SettingsService Settings { get; private set; } = null!;
    public WallpaperEngineService Engine { get; private set; } = null!;
    public TrayService Tray { get; private set; } = null!;
    public MainViewModel ViewModel { get; private set; } = null!;
    public MainWindow? AppWindow { get; private set; }

    public bool IsExiting { get; set; }
    public bool StartedInBackground { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled domain exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        AppPaths.EnsureCreated();
        Log.Info($"Nocturne starting. Version {typeof(App).Assembly.GetName().Version}.");

        _instance = new SingleInstance();
        if (!_instance.IsPrimary)
        {
            Log.Info("Another instance is running; asking it to show its window.");
            _instance.SignalExistingInstance();
            Shutdown();
            return;
        }

        StartedInBackground = e.Args.Any(a =>
            a.Equals("--background", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-background", StringComparison.OrdinalIgnoreCase));

        PlaybackEngine.EnsureInitialized();

        Library = new LibraryService();
        Library.Load();

        Settings = new SettingsService();

        // Remember the wallpaper Windows had before any Nocturne surface is shown.
        DesktopWallpaperService.Capture();

        Engine = new WallpaperEngineService(Library, Settings);
        Engine.Start();

        ViewModel = new MainViewModel(Library, Settings, Engine);

        Tray = new TrayService();
        Tray.OpenRequested += (_, _) => ShowMainWindow();
        Tray.SettingsRequested += (_, _) =>
        {
            ShowMainWindow();
            ViewModel.NavigateCommand.Execute(Models.AppPage.Settings);
        };
        Tray.TogglePauseRequested += (_, _) => Engine.ToggleUserPaused();
        Tray.NextRequested += (_, _) => ViewModel.ApplyRandomCommand.Execute(null);
        Tray.ExitRequested += (_, _) => ExitApp();

        _instance.ShowRequested += (_, _) => Dispatcher.BeginInvoke(ShowMainWindow);

        Engine.StateChanged += (_, _) =>
        {
            Tray.UpdateState(Engine.IsPaused, Engine.CurrentWallpaper?.Name);
        };
        Tray.UpdateState(Engine.IsPaused, Engine.CurrentWallpaper?.Name);

        AppWindow = new MainWindow(ViewModel);

        if (!StartedInBackground)
        {
            AppWindow.Show();
        }
        else if (Settings.Current.ShowNotifications)
        {
            Tray.ShowNotification("Nocturne is running",
                Engine.IsRunning ? "Your live wallpaper is playing in the background." : "Open Nocturne to choose a wallpaper.");
        }

        Log.Info("Nocturne ready.");
    }

    public void ShowMainWindow()
    {
        if (AppWindow is null)
            return;

        if (!AppWindow.IsVisible)
            AppWindow.Show();

        if (AppWindow.WindowState == WindowState.Minimized)
            AppWindow.WindowState = WindowState.Normal;

        AppWindow.ShowInTaskbar = true;
        AppWindow.Activate();

        AppWindow.Topmost = true;
        AppWindow.Topmost = false;
        AppWindow.Focus();
    }

    public void ExitApp()
    {
        if (IsExiting)
            return;

        IsExiting = true;
        Log.Info("Nocturne exiting.");

        try { Engine.Dispose(); } catch (Exception ex) { Log.Error("Engine shutdown", ex); }
        try { DesktopWallpaperService.Restore(); } catch { }
        try { Tray.Dispose(); } catch { }
        try { Settings.Dispose(); } catch { }
        try { Library.Save(); } catch { }
        try { PlaybackEngine.Shutdown(); } catch { }
        try { _instance?.Dispose(); } catch { }

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsExiting = true;
        try { Engine?.Dispose(); } catch { }
        try { DesktopWallpaperService.Restore(); } catch { }
        try { Tray?.Dispose(); } catch { }
        try { Settings?.Dispose(); } catch { }
        try { Library?.Save(); } catch { }
        try { PlaybackEngine.Shutdown(); } catch { }
        try { _instance?.Dispose(); } catch { }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;

        MessageBox.Show(
            "Nocturne hit an unexpected error, but the engine is still running.\n\n" +
            e.Exception.Message + "\n\nDetails were written to:\n" + AppPaths.LogFile,
            "Nocturne",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
