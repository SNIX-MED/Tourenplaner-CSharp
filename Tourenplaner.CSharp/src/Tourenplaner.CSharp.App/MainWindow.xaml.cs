using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json;
using System.IO;
using Tourenplaner.CSharp.App.ViewModels;

namespace Tourenplaner.CSharp.App;

public partial class MainWindow : Window
{
    private static readonly string WindowPlacementPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tourenplaner.CSharp", "data", "main-window.json");

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;
        if (DataContext is MainShellViewModel shell)
        {
            var sections = shell.NavigationItems.Select(x => x.Section)
                .OfType<ViewModels.Sections.SectionViewModelBase>()
                .Where(x => x.HasUnsavedChanges).Select(x => x.Title).Distinct().ToList();
            if (Services.UnsavedChangesGuard.HasOpenUnsavedEditors) sections.Add("Offene Bearbeitungsfenster");
            if (sections.Count > 0)
            {
                e.Cancel = !Services.UnsavedChangesGuard.ConfirmDiscard(this,
                    $"Es gibt ungespeicherte Änderungen in: {string.Join(", ", sections)}.\n\nDas Programm trotzdem schliessen? Die ungespeicherten Änderungen gehen verloren.");
            }
        }

        if (!e.Cancel) SaveWindowPlacement();
    }

    public MainWindow()
    {
        InitializeComponent();
        SetWindowIcon();
        RestoreWindowPlacement();
    }

    private void RestoreWindowPlacement()
    {
        try
        {
            if (!File.Exists(WindowPlacementPath)) return;
            var placement = JsonSerializer.Deserialize<MainWindowPlacement>(File.ReadAllText(WindowPlacementPath));
            if (placement is null || !double.IsFinite(placement.Width) || !double.IsFinite(placement.Height) ||
                !double.IsFinite(placement.Left) || !double.IsFinite(placement.Top)) return;

            var width = Math.Max(MinWidth, placement.Width);
            var height = Math.Max(MinHeight, placement.Height);
            var virtualScreen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            var savedBounds = new Rect(placement.Left, placement.Top, width, height);
            var visibleBounds = Rect.Intersect(savedBounds, virtualScreen);
            if (visibleBounds.Width < 100 || visibleBounds.Height < 100)
            {
                var workArea = SystemParameters.WorkArea;
                savedBounds = new Rect(
                    workArea.Left + Math.Max(0, (workArea.Width - width) / 2d),
                    workArea.Top + Math.Max(0, (workArea.Height - height) / 2d), width, height);
            }

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = savedBounds.Left;
            Top = savedBounds.Top;
            Width = savedBounds.Width;
            Height = savedBounds.Height;
            WindowState = placement.IsMaximized ? WindowState.Maximized : WindowState.Normal;
        }
        catch
        {
            // Ungültige oder nicht lesbare UI-Einstellungen dürfen den Start nicht verhindern.
        }
    }

    private void SaveWindowPlacement()
    {
        try
        {
            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;
            if (!double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) ||
                bounds.Width <= 0 || bounds.Height <= 0) return;

            Directory.CreateDirectory(Path.GetDirectoryName(WindowPlacementPath)!);
            var placement = new MainWindowPlacement(bounds.Left, bounds.Top,
                Math.Max(MinWidth, bounds.Width), Math.Max(MinHeight, bounds.Height),
                WindowState == WindowState.Maximized);
            File.WriteAllText(WindowPlacementPath, JsonSerializer.Serialize(placement));
        }
        catch
        {
            // Das Schließen der Anwendung darf nicht an UI-Einstellungen scheitern.
        }
    }

    private sealed record MainWindowPlacement(
        double Left, double Top, double Width, double Height, bool IsMaximized);

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
