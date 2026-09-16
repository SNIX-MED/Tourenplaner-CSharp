using System.Windows.Controls;

using System.Windows;

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
}
