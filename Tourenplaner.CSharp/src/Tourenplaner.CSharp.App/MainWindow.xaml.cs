using System.Windows;
using System.Windows.Controls.Primitives;

namespace Tourenplaner.CSharp.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private CustomPopupPlacement[] ToastPopup_Placement(Size popupSize, Size targetSize, Point offset)
    {
        const double marginRight = 14d;
        const double marginBottom = 14d;

        var x = Math.Max(0d, targetSize.Width - popupSize.Width - marginRight);
        var y = Math.Max(0d, targetSize.Height - popupSize.Height - marginBottom);
        return
        [
            new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.None)
        ];
    }
}
