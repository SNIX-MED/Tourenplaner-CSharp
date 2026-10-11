using System.Windows.Controls;

using System.Windows;
using Tourenplaner.CSharp.App.Views.Dialogs;

namespace Tourenplaner.CSharp.App.Views.Sections;

public partial class SettingsSectionView : UserControl
{
    public static readonly DependencyProperty IsXmlImportOnlyModeProperty = DependencyProperty.Register(
        nameof(IsXmlImportOnlyMode),
        typeof(bool),
        typeof(SettingsSectionView),
        new PropertyMetadata(false, OnIsXmlImportOnlyModeChanged));

    public bool IsXmlImportOnlyMode
    {
        get => (bool)GetValue(IsXmlImportOnlyModeProperty);
        set => SetValue(IsXmlImportOnlyModeProperty, value);
    }

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
        ApplyDisplayMode();
        Loaded += (_, _) => SynchronizeWebfleetPassword();
        DataContextChanged += (_, _) => SynchronizeWebfleetPassword();
    }

    private static void OnIsXmlImportOnlyModeChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is SettingsSectionView view)
        {
            view.ApplyDisplayMode();
        }
    }

    private void ApplyDisplayMode()
    {
        if (SettingsSidebarColumn is null || SettingsSidebar is null || GeneralSaveButton is null)
        {
            return;
        }

        SettingsSidebarColumn.Width = IsXmlImportOnlyMode ? new GridLength(0) : new GridLength(252);
        SettingsSidebar.Visibility = IsXmlImportOnlyMode ? Visibility.Collapsed : Visibility.Visible;
        GeneralSaveButton.Visibility = IsXmlImportOnlyMode ? Visibility.Collapsed : Visibility.Visible;
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
