using System.Windows;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class StartupTourArchiveConfirmDialogWindow : Window
{
    public StartupTourArchiveConfirmDialogWindow(int pastTourCount)
    {
        InitializeComponent();
        HeadlineTextBlock.Text = $"Es wurden {pastTourCount} vergangene Tour(en) gefunden.";
    }

    private void OnYesClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnNoClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
