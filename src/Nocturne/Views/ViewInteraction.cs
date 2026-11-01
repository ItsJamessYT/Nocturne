using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nocturne.Models;
using Nocturne.ViewModels;

namespace Nocturne.Views;

/// <summary>Small shared interactions used by the wallpaper grids.</summary>
internal static class ViewInteraction
{
    public static void PreviewOnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not FrameworkElement element)
            return;

        // Buttons inside the card handle their own clicks.
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
            return;

        if (element.DataContext is not MainViewModel viewModel)
            return;

        if ((e.OriginalSource as FrameworkElement)?.DataContext is WallpaperItem item)
            viewModel.PreviewCommand.Execute(item);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
