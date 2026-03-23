using System.Windows;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels;
using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class EmployeeEditorDialogWindow : Window
{
    public EmployeeEditorDialogWindow(EmployeeEditorSeed seed)
    {
        InitializeComponent();
        ViewModel = new EmployeeEditorDialogViewModel(seed);
        DataContext = ViewModel;
    }

    public EmployeeEditorDialogViewModel ViewModel { get; }

    public EmployeeEditorResult? Result { get; private set; }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryBuildResult(out var result, out var error))
        {
            MessageBox.Show(this, error, "Eingabe prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = result;
        DialogResult = true;
        Close();
    }

    private void PhoneTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !IsDigitsOnly(e.Text);
    }

    private void PhoneTextBox_OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(typeof(string)))
        {
            e.CancelCommand();
            return;
        }

        var pastedText = e.DataObject.GetData(typeof(string)) as string ?? string.Empty;
        if (!IsDigitsOnly(pastedText))
        {
            e.CancelCommand();
        }
    }

    private static bool IsDigitsOnly(string value)
    {
        return value.All(char.IsDigit);
    }
}

public sealed class EmployeeEditorDialogViewModel : ObservableObject
{
    private readonly string? _id;
    private string _name;
    private string _shortCode;
    private string _phone;
    private bool _active;

    public EmployeeEditorDialogViewModel(EmployeeEditorSeed seed)
    {
        _id = seed.Id;
        _name = seed.Name ?? string.Empty;
        _shortCode = seed.ShortCode ?? string.Empty;
        _phone = seed.Phone ?? string.Empty;
        _active = seed.Active;
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string ShortCode
    {
        get => _shortCode;
        set => SetProperty(ref _shortCode, value);
    }

    public string Phone
    {
        get => _phone;
        set => SetProperty(ref _phone, value);
    }

    public bool Active
    {
        get => _active;
        set => SetProperty(ref _active, value);
    }

    public bool TryBuildResult(out EmployeeEditorResult result, out string error)
    {
        result = default!;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "Bitte einen Namen eingeben.";
            return false;
        }

        result = new EmployeeEditorResult(
            Id: _id,
            Name: Name.Trim(),
            ShortCode: (ShortCode ?? string.Empty).Trim(),
            Phone: (Phone ?? string.Empty).Trim(),
            Active: Active);
        return true;
    }
}
