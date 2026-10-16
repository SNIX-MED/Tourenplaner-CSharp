using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tourenplaner.CSharp.App.ViewModels;

namespace Tourenplaner.CSharp.App;

public partial class MainWindow : Window
{
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || DataContext is not MainShellViewModel shell) return;
        var sections = shell.NavigationItems.Select(x => x.Section)
            .OfType<ViewModels.Sections.SectionViewModelBase>()
            .Where(x => x.HasUnsavedChanges).Select(x => x.Title).Distinct().ToList();
        if (Services.UnsavedChangesGuard.HasOpenUnsavedEditors) sections.Add("Offene Bearbeitungsfenster");
        if (sections.Count == 0) return;
        e.Cancel = !Services.UnsavedChangesGuard.ConfirmDiscard(this,
            $"Es gibt ungespeicherte Änderungen in: {string.Join(", ", sections)}.\n\nDas Programm trotzdem schliessen? Die ungespeicherten Änderungen gehen verloren.");
    }

    public MainWindow()
    {
        InitializeComponent();
        SetWindowIcon();
    }

    private void SetWindowIcon()
    {
        Icon = LoadIcon("pack://application:,,,/Assets/Applogo.png")
            ?? LoadIcon("pack://application:,,,/Assets/Banner.png");
    }

    private static ImageSource? LoadIcon(string uri)
    {
        try
        {
            var icon = new BitmapImage();
            icon.BeginInit();
            icon.UriSource = new Uri(uri, UriKind.Absolute);
            icon.CacheOption = BitmapCacheOption.OnLoad;
            icon.EndInit();
            icon.Freeze();
            return icon;
        }
        catch
        {
            return null;
        }
    }

    private CustomPopupPlacement[] ToastPopup_Placement(Size popupSize, Size targetSize, Point offset)
    {
        const double marginRight = 14d;
        const double marginBottom = 82d;

        var x = Math.Max(0d, targetSize.Width - popupSize.Width - marginRight);
        var y = Math.Max(0d, targetSize.Height - popupSize.Height - marginBottom);
        return
        [
            new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.None)
        ];
    }

    private void SidebarNavigationListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainShellViewModel vm)
        {
            return;
        }

        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not ListBoxItem listBoxItem ||
            listBoxItem.DataContext is not NavigationItemViewModel navigationItem)
        {
            return;
        }

        vm.ActivateSidebarNavigationItem(navigationItem);
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T target)
            {
                return target;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
