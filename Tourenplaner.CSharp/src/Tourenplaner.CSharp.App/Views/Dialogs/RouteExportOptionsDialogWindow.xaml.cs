using System.Windows;
using Tourenplaner.CSharp.App.Services;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class RouteExportOptionsDialogWindow : Window
{
    public RouteExportOptionsDialogWindow()
    {
        InitializeComponent();
    }

    public RouteExportOption? SelectedOption { get; private set; }

    private void OnGoogleMapsClicked(object sender, RoutedEventArgs e)
    {
        SelectedOption = RouteExportOption.GoogleMaps;
        DialogResult = true;
        Close();
    }

    private void OnPdfClicked(object sender, RoutedEventArgs e)
    {
        SelectedOption = RouteExportOption.Pdf;
        DialogResult = true;
        Close();
    }
}
