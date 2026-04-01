using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Tourenplaner.CSharp.App.Views.Components;

public partial class DashboardTopBar : UserControl
{
    public DashboardTopBar()
    {
        InitializeComponent();
    }

    private CustomPopupPlacement[] CenterPopupUnderButton(Size popupSize, Size targetSize, Point offset)
    {
        var x = (targetSize.Width - popupSize.Width) / 2d;
        var y = targetSize.Height + 6d;
        return [new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.None)];
    }

    private void TogglePopupButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ToggleButton button || button.IsChecked != true)
        {
            return;
        }

        button.IsChecked = false;
        e.Handled = true;
    }
}
