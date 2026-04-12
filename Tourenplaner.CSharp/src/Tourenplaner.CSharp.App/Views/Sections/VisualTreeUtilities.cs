using System.Windows;
using System.Windows.Media;

namespace Tourenplaner.CSharp.App.Views.Sections;

internal static class VisualTreeUtilities
{
    public static T? FindAncestor<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
            {
                return match;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    public static T? FindDescendant<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null)
        {
            return null;
        }

        if (parent is T match)
        {
            return match;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < childCount; i++)
        {
            var nested = FindDescendant<T>(VisualTreeHelper.GetChild(parent, i));
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
