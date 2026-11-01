using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Nocturne.Interop;
using Nocturne.Models;
using Nocturne.ViewModels;

namespace Nocturne;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Dictionary<AppPage, FrameworkElement> _pages = new();

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        _pages[AppPage.Home] = HomePage;
        _pages[AppPage.Library] = LibraryPage;
        _pages[AppPage.Favorites] = FavoritesPage;
        _pages[AppPage.Settings] = SettingsPage;

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ShowPage(viewModel.CurrentPage, animate: false);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentPage))
            ShowPage(_viewModel.CurrentPage, animate: true);
    }

    private void ShowPage(AppPage page, bool animate)
    {
        foreach ((AppPage key, FrameworkElement element) in _pages)
        {
            if (key == page)
                continue;

            element.Visibility = Visibility.Collapsed;
            element.Opacity = 1;
            if (element.RenderTransform is TranslateTransform old)
                old.Y = 0;
        }

        if (!_pages.TryGetValue(page, out FrameworkElement? target))
            return;

        target.Visibility = Visibility.Visible;
        target.Opacity = 1;

        if (!animate)
            return;

        var transform = target.RenderTransform as TranslateTransform;
        if (transform is null)
        {
            transform = new TranslateTransform();
            target.RenderTransform = transform;
        }

        target.Opacity = 0;
        transform.Y = 12;

        target.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });

        transform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed && WindowState == WindowState.Normal)
        {
            try { DragMove(); } catch { }
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnWindowStateChanged(object sender, EventArgs e)
    {
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";

        if (WindowState == WindowState.Minimized && _viewModel.Settings.MinimizeToTray)
        {
            Hide();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (App.Current.IsExiting)
        {
            base.OnClosing(e);
            return;
        }

        // Closing the window quits Nocturne and returns the desktop to the
        // wallpaper Windows had before. The tray icon and Minimize keep the
        // engine alive when background playback is wanted.
        e.Cancel = true;
        App.Current.ExitApp();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int rounded = 2; // DWMWCP_ROUND
            NativeMethods.DwmSetWindowAttribute(hwnd, 33, ref rounded, sizeof(int));
            int dark = 1;
            NativeMethods.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        }
        catch { }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            await _viewModel.ImportPathsAsync(files);
    }
}
