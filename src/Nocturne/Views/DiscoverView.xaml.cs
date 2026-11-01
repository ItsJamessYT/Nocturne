using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Nocturne.Models;
using Nocturne.ViewModels;

namespace Nocturne.Views;

public partial class DiscoverView : UserControl
{
    private WallpaperFilter _filter = WallpaperFilter.All;

    public DiscoverView()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
        => Dispatcher.BeginInvoke(ApplyFilter);

    private void OnFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked)
            return;

        _filter = Enum.TryParse(clicked.Tag?.ToString(), true, out WallpaperFilter filter)
            ? filter
            : WallpaperFilter.All;

        AllChip.IsChecked = ReferenceEquals(clicked, AllChip);
        VideoChip.IsChecked = ReferenceEquals(clicked, VideoChip);
        ImageChip.IsChecked = ReferenceEquals(clicked, ImageChip);
        GifChip.IsChecked = ReferenceEquals(clicked, GifChip);

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (DiscoverGrid.ItemsSource is null)
            return;

        WallpaperKind? kind = _filter switch
        {
            WallpaperFilter.Video => WallpaperKind.Video,
            WallpaperFilter.Image => WallpaperKind.Image,
            WallpaperFilter.Gif => WallpaperKind.Gif,
            _ => null
        };

        ICollectionView view = CollectionViewSource.GetDefaultView(DiscoverGrid.ItemsSource);
        view.Filter = item => kind is null || item is WallpaperItem wallpaper && wallpaper.Kind == kind;
        view.Refresh();

        bool hasAny = DataContext is MainViewModel vm && vm.DiscoverWallpapers.Count > 0;
        EmptyFilterState.Visibility = hasAny && view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnWallpaperDoubleClick(object sender, MouseButtonEventArgs e)
        => ViewInteraction.PreviewOnDoubleClick(sender, e);
}
