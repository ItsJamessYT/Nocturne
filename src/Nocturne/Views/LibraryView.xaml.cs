using System.Windows.Controls;
using System.Windows.Input;

namespace Nocturne.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
    }

    private void OnWallpaperDoubleClick(object sender, MouseButtonEventArgs e)
        => ViewInteraction.PreviewOnDoubleClick(sender, e);
}
