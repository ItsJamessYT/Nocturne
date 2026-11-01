using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Nocturne.Models;

namespace Nocturne.Dialogs;

public partial class WallpaperPickerDialog : Window
{
    private readonly ListCollectionView _view;

    private WallpaperPickerDialog(IEnumerable<WallpaperItem> items)
    {
        InitializeComponent();

        var source = new ObservableCollection<WallpaperItem>(items);
        _view = new ListCollectionView(source);
        WallpaperList.ItemsSource = _view;

        CountText.Text = source.Count == 1 ? "1 wallpaper" : $"{source.Count} wallpapers";

        SearchBox.TextChanged += (_, _) =>
        {
            SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilter();
        };
    }

    public static WallpaperItem? Show(Window? owner, string monitorName, IEnumerable<WallpaperItem> items, Guid? currentId)
    {
        var dialog = new WallpaperPickerDialog(items) { Owner = owner };
        dialog.SubtitleText.Text = $"Pick something for {monitorName}.";
        dialog.WallpaperList.SelectedItem = dialog.WallpaperList.Items
            .OfType<WallpaperItem>()
            .FirstOrDefault(w => w.Id == currentId);

        return dialog.ShowDialog() == true ? dialog.WallpaperList.SelectedItem as WallpaperItem : null;
    }

    private void ApplyFilter()
    {
        string term = SearchBox.Text.Trim();

        _view.Filter = item =>
        {
            if (item is not WallpaperItem wallpaper)
                return false;
            if (string.IsNullOrWhiteSpace(term))
                return true;
            return wallpaper.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                   wallpaper.KindLabel.Contains(term, StringComparison.OrdinalIgnoreCase);
        };

        EmptyState.Visibility = _view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = _view.Count == 1 ? "1 wallpaper" : $"{_view.Count} wallpapers";
    }

    private void OnSearchChanged(object sender, RoutedEventArgs e) => ApplyFilter();

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (WallpaperList.SelectedItem is WallpaperItem)
            OnApply(sender, e);
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        if (WallpaperList.SelectedItem is not WallpaperItem)
            return;

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            OnCancel(sender, e);
        else if (e.Key == Key.Enter)
            OnApply(sender, e);
    }
}
