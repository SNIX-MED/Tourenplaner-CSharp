using System.ComponentModel;
using System.Windows;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class XmlImportDialogWindow : Window
{
    public XmlImportDialogWindow(SettingsSectionViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is not SettingsSectionViewModel { IsXmlImportBusy: true })
        {
            return;
        }

        e.Cancel = true;
        AppMessageBox.Show(
            this,
            "Der XML-Import beziehungsweise die Prüfung läuft noch. Bitte warte, bis der Vorgang abgeschlossen ist.",
            "XML-Import läuft",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
