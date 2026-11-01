using System.Windows;
using System.Windows.Input;

namespace Nocturne.Dialogs;

public partial class InputDialog : Window
{
    private string? _result;

    private InputDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public static string? Show(Window? owner, string title, string message, string initial, string okText = "Save")
    {
        var dialog = new InputDialog
        {
            Title = title,
            Owner = owner
        };
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.ValueBox.Text = initial;
        dialog.OkButton.Content = okText;

        return dialog.ShowDialog() == true ? dialog._result : null;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        string value = ValueBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return;

        _result = value;
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
            OnOk(sender, e);
    }
}
