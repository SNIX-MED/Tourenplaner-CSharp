using System.Windows;
using System.Windows.Input;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using System.Globalization;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class EmployeeEditorDialogWindow : Window
{
    public EmployeeEditorDialogWindow(EmployeeEditorSeed seed, IReadOnlyList<WebfleetVehicleSnapshot>? webfleetVehicles = null, IReadOnlyList<WebfleetDriverSnapshot>? webfleetDrivers = null, string? noWebfleetVehiclesMessage = null, bool hasDuplicateWebfleetAssignment = false)
    {
        InitializeComponent();
        ViewModel = new EmployeeEditorDialogViewModel(seed, webfleetVehicles, webfleetDrivers, noWebfleetVehiclesMessage, hasDuplicateWebfleetAssignment);
        DataContext = ViewModel;
    }

    public EmployeeEditorDialogViewModel ViewModel { get; }

    public EmployeeEditorResult? Result { get; private set; }

    public bool DeleteRequested { get; private set; }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        DeleteRequested = true;
        DialogResult = false;
        Close();
    }

    private void OnFavoriteClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.IsFavorite = !ViewModel.IsFavorite;
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryBuildResult(out var result, out var error))
        {
            AppMessageBox.Show(this, error, "Eingabe prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
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
    private bool _hasProgramProfile;
    private bool _isFavorite;
    private bool _registerAbsence;
    private string _absenceStartDate;
    private string _absenceEndDate;
    private string _webfleetObjectUid;
    private string _webfleetObjectNumber;
    private string _webfleetDriverUid;
    private string _webfleetDriverNumber;
    private string _webfleetDriverName;
    private WebfleetVehicleOption? _selectedWebfleetVehicle;
    private WebfleetEmployeeDriverOption? _selectedWebfleetDriver;

    public EmployeeEditorDialogViewModel(EmployeeEditorSeed seed, IReadOnlyList<WebfleetVehicleSnapshot>? webfleetVehicles = null, IReadOnlyList<WebfleetDriverSnapshot>? webfleetDrivers = null, string? noWebfleetVehiclesMessage = null, bool hasDuplicateWebfleetAssignment = false)
    {
        _id = seed.Id;
        _name = seed.Name ?? string.Empty;
        _shortCode = seed.ShortCode ?? string.Empty;
        _phone = seed.Phone ?? string.Empty;
        _hasProgramProfile = seed.HasProgramProfile;
        _isFavorite = seed.IsFavorite;
        _registerAbsence = seed.RegisterAbsence;
        _absenceStartDate = seed.AbsenceStartDate ?? string.Empty;
        _absenceEndDate = seed.AbsenceEndDate ?? string.Empty;
        _webfleetObjectUid = seed.WebfleetObjectUid ?? string.Empty;
        _webfleetObjectNumber = seed.WebfleetObjectNumber ?? string.Empty;
        _webfleetDriverUid = seed.WebfleetDriverUid ?? string.Empty;
        _webfleetDriverNumber = seed.WebfleetDriverNumber ?? string.Empty;
        _webfleetDriverName = seed.WebfleetDriverName ?? string.Empty;
        var availableWebfleetVehicles = (webfleetVehicles ?? [])
            .Select(WebfleetVehicleOption.Create)
            .OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        HasAvailableWebfleetVehicles = availableWebfleetVehicles.Count > 0;
        WebfleetVehicles = [WebfleetVehicleOption.None, .. availableWebfleetVehicles];
        _selectedWebfleetVehicle = WebfleetVehicles.FirstOrDefault(x =>
            !x.IsNone && string.Equals(x.ObjectUid, _webfleetObjectUid, StringComparison.OrdinalIgnoreCase));
        HasDuplicateWebfleetAssignment = hasDuplicateWebfleetAssignment;
        if (HasDuplicateWebfleetAssignment)
        {
            _webfleetObjectNumber = string.Empty;
            _webfleetObjectUid = string.Empty;
            _selectedWebfleetVehicle = WebfleetVehicleOption.None;
        }
        else if (HasAvailableWebfleetVehicles && _selectedWebfleetVehicle is null)
        {
            _webfleetObjectNumber = string.Empty;
            _webfleetObjectUid = string.Empty;
        }
        NoWebfleetVehiclesMessage = string.IsNullOrWhiteSpace(noWebfleetVehiclesMessage)
            ? "Keine WEBFLEET-Objekte geladen. Bitte Verbindung und Berechtigungen prüfen."
            : noWebfleetVehiclesMessage;
        var availableWebfleetDrivers = (webfleetDrivers ?? [])
            .Select(WebfleetEmployeeDriverOption.Create)
            .OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        HasAvailableWebfleetDrivers = availableWebfleetDrivers.Count > 0;
        WebfleetDrivers = [WebfleetEmployeeDriverOption.None, .. availableWebfleetDrivers];
        _selectedWebfleetDriver = WebfleetDrivers.FirstOrDefault(x =>
            !x.IsNone && string.Equals(x.DriverUid, _webfleetDriverUid, StringComparison.OrdinalIgnoreCase));
        if (HasAvailableWebfleetDrivers && _selectedWebfleetDriver is null)
        {
            _webfleetDriverUid = string.Empty;
            _webfleetDriverNumber = string.Empty;
            _webfleetDriverName = string.Empty;
        }
    }

    public bool HasExistingEntry => !string.IsNullOrWhiteSpace(_id);

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

    public bool RegisterAbsence
    {
        get => _registerAbsence;
        set => SetProperty(ref _registerAbsence, value);
    }

    public bool HasProgramProfile
    {
        get => _hasProgramProfile;
        set => SetProperty(ref _hasProgramProfile, value);
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetProperty(ref _isFavorite, value);
    }

    public string AbsenceStartDate
    {
        get => _absenceStartDate;
        set => SetProperty(ref _absenceStartDate, value);
    }

    public string AbsenceEndDate
    {
        get => _absenceEndDate;
        set => SetProperty(ref _absenceEndDate, value);
    }

    public string WebfleetObjectUid { get => _webfleetObjectUid; set => SetProperty(ref _webfleetObjectUid, value); }
    public string WebfleetObjectNumber { get => _webfleetObjectNumber; set => SetProperty(ref _webfleetObjectNumber, value); }
    public string WebfleetDriverUid { get => _webfleetDriverUid; set => SetProperty(ref _webfleetDriverUid, value); }
    public string WebfleetDriverNumber { get => _webfleetDriverNumber; set => SetProperty(ref _webfleetDriverNumber, value); }
    public string WebfleetDriverName { get => _webfleetDriverName; set => SetProperty(ref _webfleetDriverName, value); }
    public IReadOnlyList<WebfleetVehicleOption> WebfleetVehicles { get; }
    public IReadOnlyList<WebfleetEmployeeDriverOption> WebfleetDrivers { get; }
    public bool HasAvailableWebfleetVehicles { get; }
    public bool HasAvailableWebfleetDrivers { get; }
    public bool HasDuplicateWebfleetAssignment { get; }
    public string NoWebfleetVehiclesMessage { get; }
    public string NoWebfleetDriversMessage => "Keine WEBFLEET-Fahrer geladen. Bitte Verbindung und Berechtigungen prüfen.";

    public WebfleetVehicleOption? SelectedWebfleetVehicle
    {
        get => _selectedWebfleetVehicle;
        set
        {
            if (!SetProperty(ref _selectedWebfleetVehicle, value) || value is null)
            {
                return;
            }

            WebfleetObjectNumber = value.ObjectNumber;
            WebfleetObjectUid = value.ObjectUid;
        }
    }

    public WebfleetEmployeeDriverOption? SelectedWebfleetDriver
    {
        get => _selectedWebfleetDriver;
        set
        {
            if (!SetProperty(ref _selectedWebfleetDriver, value) || value is null)
            {
                return;
            }

            WebfleetDriverNumber = value.DriverNumber;
            WebfleetDriverUid = value.DriverUid;
            WebfleetDriverName = value.Name;
        }
    }

    public DateTime? AbsenceStartSelectedDate
    {
        get => ParseDateTime(_absenceStartDate);
        set
        {
            var normalized = FormatDate(value);
            if (SetProperty(ref _absenceStartDate, normalized))
            {
                OnPropertyChanged(nameof(AbsenceStartDate));
            }
        }
    }

    public DateTime? AbsenceEndSelectedDate
    {
        get => ParseDateTime(_absenceEndDate);
        set
        {
            var normalized = FormatDate(value);
            if (SetProperty(ref _absenceEndDate, normalized))
            {
                OnPropertyChanged(nameof(AbsenceEndDate));
            }
        }
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

        if (RegisterAbsence)
        {
            var start = ResourceAvailabilityService.ParseDate(AbsenceStartDate);
            var end = ResourceAvailabilityService.ParseDate(AbsenceEndDate);
            if (!start.HasValue || !end.HasValue)
            {
                error = "Bitte für die Abwesenheit ein gültiges Start- und Enddatum im Format DD.MM.YYYY eingeben.";
                return false;
            }
        }

        result = new EmployeeEditorResult(
            Id: _id,
            Name: Name.Trim(),
            ShortCode: (ShortCode ?? string.Empty).Trim(),
            Phone: (Phone ?? string.Empty).Trim(),
            HasProgramProfile: HasProgramProfile,
            IsFavorite: IsFavorite,
            RegisterAbsence: RegisterAbsence,
            AbsenceStartDate: (AbsenceStartDate ?? string.Empty).Trim(),
            AbsenceEndDate: (AbsenceEndDate ?? string.Empty).Trim(),
            WebfleetObjectUid: (WebfleetObjectUid ?? string.Empty).Trim(),
            WebfleetObjectNumber: (WebfleetObjectNumber ?? string.Empty).Trim(),
            WebfleetDriverUid: (WebfleetDriverUid ?? string.Empty).Trim(),
            WebfleetDriverNumber: (WebfleetDriverNumber ?? string.Empty).Trim(),
            WebfleetDriverName: (WebfleetDriverName ?? string.Empty).Trim());
        return true;
    }

    private static DateTime? ParseDateTime(string? raw)
    {
        var parsed = ResourceAvailabilityService.ParseDate(raw);
        return parsed.HasValue
            ? parsed.Value.ToDateTime(TimeOnly.MinValue)
            : null;
    }

    private static string FormatDate(DateTime? value)
    {
        return value.HasValue
            ? DateOnly.FromDateTime(value.Value).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
            : string.Empty;
    }
}

public sealed record WebfleetVehicleOption(string ObjectNumber, string ObjectUid, string Label, bool IsNone = false)
{
    public static WebfleetVehicleOption None { get; } = new(string.Empty, string.Empty, "Keine WEBFLEET-Zuordnung", true);

    public static WebfleetVehicleOption Create(WebfleetVehicleSnapshot source)
    {
        var name = string.IsNullOrWhiteSpace(source.Name) ? "Ohne Bezeichnung" : source.Name;
        return new WebfleetVehicleOption(source.ObjectNumber, source.ObjectUid, $"{source.ObjectNumber} – {name}");
    }
}

public sealed record WebfleetEmployeeDriverOption(string DriverNumber, string DriverUid, string Name, string Label, bool IsNone = false)
{
    public static WebfleetEmployeeDriverOption None { get; } = new(string.Empty, string.Empty, string.Empty, "Keine WEBFLEET-Fahrerzuordnung", true);

    public static WebfleetEmployeeDriverOption Create(WebfleetDriverSnapshot source)
    {
        var name = string.IsNullOrWhiteSpace(source.Name) ? "Ohne Bezeichnung" : source.Name;
        return new WebfleetEmployeeDriverOption(source.DriverNumber, source.DriverUid, name, $"{source.DriverNumber} – {name}");
    }
}
