using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Nocturne.Dialogs;

public partial class ConfirmDialog : Window
{
    private bool _result;

    private ConfirmDialog()
    {
        InitializeComponent();
    }

    public static bool Show(Window? owner, string title, string message, string confirmText = "Confirm", bool danger = false)
    {
        var dialog = new ConfirmDialog { Title = title, Owner = owner };
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.ConfirmButton.Content = confirmText;

        if (danger)
        {
            dialog.ConfirmButton.Style = (Style)dialog.FindResource("DangerButton");
            dialog.ConfirmButton.Foreground = Brushes.White;
        }

        return dialog.ShowDialog() == true && dialog._result;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        _result = true;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _result = false;
        DialogResult = false;
        Close();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            OnCancel(sender, e);
    }
}
