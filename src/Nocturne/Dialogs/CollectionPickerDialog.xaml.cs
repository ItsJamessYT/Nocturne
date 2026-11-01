using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Nocturne.Models;

namespace Nocturne.Dialogs;

public partial class CollectionPickerDialog : Window
{
    public sealed record Result(Guid? CollectionId, string? NewCollectionName);

    private Result? _result;

    private CollectionPickerDialog()
    {
        InitializeComponent();
        NewNameBox.TextChanged += (_, _) =>
            NewNameHint.Visibility = string.IsNullOrEmpty(NewNameBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Result? Show(Window? owner, string wallpaperName, ObservableCollection<CollectionItem> collections, Guid? current)
    {
        var dialog = new CollectionPickerDialog { Owner = owner };
        dialog.SubtitleText.Text = wallpaperName;
        dialog.CollectionList.ItemsSource = collections;
        dialog.CollectionList.SelectedItem = collections.FirstOrDefault(c => c.Id == current);

        return dialog.ShowDialog() == true ? dialog._result : null;
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        string newName = NewNameBox.Text.Trim();

        if (!string.IsNullOrWhiteSpace(newName))
        {
            _result = new Result(null, newName);
        }
        else if (CollectionList.SelectedItem is CollectionItem selected)
        {
            _result = new Result(selected.Id, null);
        }
        else
        {
            return;
        }

        DialogResult = true;
        Close();
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        _result = new Result(null, null);
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _result = null;
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
