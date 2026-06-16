using System.Collections.ObjectModel;
using System.Windows;
using Tourenplaner.CSharp.App.ViewModels;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class UserSelectionDialogWindow : Window
{
    public UserSelectionDialogWindow(IReadOnlyList<string> userNames, string? selectedUserName = null)
    {
        InitializeComponent();
        ViewModel = new UserSelectionDialogViewModel(userNames, selectedUserName);
        DataContext = ViewModel;
    }

    public UserSelectionDialogViewModel ViewModel { get; }

    public string SelectedUserName => ViewModel.SelectedUserName;

    private void OnConfirmClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.SelectedUserName))
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(this, "Bitte einen Benutzer auswählen.", "Benutzer auswählen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }
}

public sealed class UserSelectionDialogViewModel : ObservableObject
{
    private string _selectedUserName = string.Empty;

    public UserSelectionDialogViewModel(IReadOnlyList<string> userNames, string? selectedUserName)
    {
        UserNames = new ObservableCollection<string>((userNames ?? []).Where(x => !string.IsNullOrWhiteSpace(x)));
        _selectedUserName = ResolveSelection(selectedUserName, UserNames);
    }

    public ObservableCollection<string> UserNames { get; }

    public string SelectedUserName
    {
        get => _selectedUserName;
        set => SetProperty(ref _selectedUserName, (value ?? string.Empty).Trim());
    }

    private static string ResolveSelection(string? preferred, IReadOnlyList<string> names)
    {
        var normalizedPreferred = (preferred ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(normalizedPreferred))
        {
            var exact = names.FirstOrDefault(x => string.Equals(x, normalizedPreferred, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(exact))
            {
                return exact;
            }
        }

        return names.FirstOrDefault() ?? string.Empty;
    }
}
