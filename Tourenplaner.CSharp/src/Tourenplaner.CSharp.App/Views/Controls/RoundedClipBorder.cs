using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Tourenplaner.CSharp.App.Views.Controls;

public class RoundedClipBorder : Border
{
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateChildClip();
    }

    protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
    {
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);
        UpdateChildClip();
    }

    private void UpdateChildClip()
    {
        if (Child is null)
        {
            return;
        }

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            Child.Clip = null;
            return;
        }

        var radius = Math.Max(0d, CornerRadius.TopLeft);
        Child.Clip = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
    }
}
