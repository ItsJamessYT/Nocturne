using System.Windows.Controls;
using System.Windows.Input;

namespace Nocturne.Views;

public partial class FavoritesView : UserControl
{
    public FavoritesView()
    {
        InitializeComponent();
    }

    private void OnWallpaperDoubleClick(object sender, MouseButtonEventArgs e)
        => ViewInteraction.PreviewOnDoubleClick(sender, e);
}
