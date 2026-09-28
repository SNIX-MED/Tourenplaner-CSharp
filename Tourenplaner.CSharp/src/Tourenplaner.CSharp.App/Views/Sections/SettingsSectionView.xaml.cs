using System.Windows.Controls;

using System.Windows;
using Tourenplaner.CSharp.App.Views.Dialogs;

namespace Tourenplaner.CSharp.App.Views.Sections;

public partial class SettingsSectionView : UserControl
{
    private void OnWebfleetPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.Sections.SettingsSectionViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.WebfleetPassword = passwordBox.Password;
        }
    }
    public SettingsSectionView()
    {
        InitializeComponent();
        Loaded += (_, _) => SynchronizeWebfleetPassword();
        DataContextChanged += (_, _) => SynchronizeWebfleetPassword();
    }

    private void SynchronizeWebfleetPassword()
    {
        if (DataContext is ViewModels.Sections.SettingsSectionViewModel viewModel &&
            WebfleetPasswordBox.Password != viewModel.WebfleetPassword)
        {
            WebfleetPasswordBox.Password = viewModel.WebfleetPassword;
        }
    }

    private async void OnEditAdditionalMaterialGroups(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.Sections.SettingsSectionViewModel viewModel)
        {
            return;
        }

        var dialog = new AdditionalMaterialGroupsDialogWindow(viewModel.GetAdditionalMaterialGroups())
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() == true)
        {
            await viewModel.SetAdditionalMaterialGroupsAsync(dialog.Result);
        }
    }
}
