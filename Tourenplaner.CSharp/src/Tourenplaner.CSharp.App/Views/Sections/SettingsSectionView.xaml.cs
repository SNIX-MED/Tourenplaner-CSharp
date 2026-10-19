using System.Windows.Controls;

using System.Windows;
using System.ComponentModel;
using System.Windows.Input;
using Tourenplaner.CSharp.App.Views.Dialogs;

namespace Tourenplaner.CSharp.App.Views.Sections;

public partial class SettingsSectionView : UserControl
{
    private bool _synchronizingPasswords;
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
        if (!_synchronizingPasswords && DataContext is ViewModels.Sections.SettingsSectionViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.WebfleetPassword = passwordBox.Password;
        }
    }
    private void OnPostgreSqlPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_synchronizingPasswords && DataContext is ViewModels.Sections.SettingsSectionViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.PostgreSqlPassword = passwordBox.Password;
        }
    }

    public SettingsSectionView()
    {
        InitializeComponent();
        ApplyDisplayMode();
        Loaded += (_, _) => SynchronizePasswords();
        DataContextChanged += OnSettingsDataContextChanged;
    }

    private void OnSettingsDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged previous)
            PropertyChangedEventManager.RemoveHandler(previous, OnSettingsPropertyChanged, string.Empty);
        if (e.NewValue is INotifyPropertyChanged current)
            PropertyChangedEventManager.AddHandler(current, OnSettingsPropertyChanged, string.Empty);
        SynchronizePasswords();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) ||
            e.PropertyName is nameof(ViewModels.Sections.SettingsSectionViewModel.PostgreSqlPassword) or
                nameof(ViewModels.Sections.SettingsSectionViewModel.WebfleetPassword))
            SynchronizePasswords();
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

    private void SynchronizePasswords()
    {
        _synchronizingPasswords = true;
        try
        {
            var viewModel = DataContext as ViewModels.Sections.SettingsSectionViewModel;
            var webfleetPassword = viewModel?.WebfleetPassword ?? string.Empty;
            var postgreSqlPassword = viewModel?.PostgreSqlPassword ?? string.Empty;
            if (WebfleetPasswordBox.Password != webfleetPassword)
                WebfleetPasswordBox.Password = webfleetPassword;
            if (PostgreSqlPasswordBox.Password != postgreSqlPassword)
                PostgreSqlPasswordBox.Password = postgreSqlPassword;
        }
        finally
        {
            _synchronizingPasswords = false;
        }
    }

    private void OnNestedScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer nestedViewer || SettingsContentScrollViewer is null)
        {
            return;
        }

        var nestedCanScroll = e.Delta > 0
            ? nestedViewer.VerticalOffset > 0
            : nestedViewer.VerticalOffset < nestedViewer.ScrollableHeight;
        if (nestedCanScroll)
        {
            return;
        }

        SettingsContentScrollViewer.ScrollToVerticalOffset(
            SettingsContentScrollViewer.VerticalOffset - (e.Delta / 3d));
        e.Handled = true;
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
