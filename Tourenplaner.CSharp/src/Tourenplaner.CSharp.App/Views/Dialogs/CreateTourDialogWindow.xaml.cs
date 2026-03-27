using System.ComponentModel;
using System.Globalization;
using System.Windows;
using Tourenplaner.CSharp.App.ViewModels;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class CreateTourDialogWindow : Window
{
    public CreateTourDialogWindow(
        string routeDate,
        string routeName,
        string routeStartHour,
        string routeStartMinute,
        IReadOnlyList<TourLookupOption> vehicleOptions,
        IReadOnlyList<TourLookupOption> trailerOptions,
        IReadOnlyList<TourEmployeeOption> employeeOptions,
        string? selectedVehicleId = null,
        string? selectedTrailerId = null,
        IReadOnlyList<string>? selectedEmployeeIds = null,
        bool showOpenOnMapButton = false)
    {
        InitializeComponent();
        ViewModel = new CreateTourDialogViewModel(
            routeDate,
            routeName,
            routeStartHour,
            routeStartMinute,
            vehicleOptions,
            trailerOptions,
            employeeOptions,
            selectedVehicleId,
            selectedTrailerId,
            selectedEmployeeIds);
        DataContext = ViewModel;
        ShowOpenOnMapButton = showOpenOnMapButton;
        OpenOnMapButton.Visibility = showOpenOnMapButton ? Visibility.Visible : Visibility.Collapsed;
    }

    public CreateTourDialogViewModel ViewModel { get; }

    public CreateTourDialogResult? Result { get; private set; }

    public bool ShowOpenOnMapButton { get; }

    public bool OpenOnMapRequested { get; private set; }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnSelectEmployeesClicked(object sender, RoutedEventArgs e)
    {
        var picker = new SelectEmployeesDialogWindow(ViewModel.Employees.ToList())
        {
            Owner = this
        };

        if (picker.ShowDialog() != true || picker.SelectedEmployeeIds is null)
        {
            return;
        }

        ViewModel.ApplySelectedEmployees(picker.SelectedEmployeeIds);
    }

    private void OnCreateClicked(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryBuildResult(out var result, out var validationError))
        {
            MessageBox.Show(this, validationError, "Eingabe prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = result;
        DialogResult = true;
        Close();
    }

    private void OnOpenOnMapClicked(object sender, RoutedEventArgs e)
    {
        OpenOnMapRequested = true;
        DialogResult = false;
        Close();
    }
}

public sealed class CreateTourDialogViewModel : ObservableObject
{
    private string _dateText = DateTime.Today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    private string _name = string.Empty;
    private string _selectedHour = "08";
    private string _selectedMinute = "00";
    private TourLookupOption? _selectedVehicle;
    private TourLookupOption? _selectedTrailer;

    public CreateTourDialogViewModel(
        string routeDate,
        string routeName,
        string routeStartHour,
        string routeStartMinute,
        IReadOnlyList<TourLookupOption> vehicleOptions,
        IReadOnlyList<TourLookupOption> trailerOptions,
        IReadOnlyList<TourEmployeeOption> employeeOptions,
        string? selectedVehicleId = null,
        string? selectedTrailerId = null,
        IReadOnlyList<string>? selectedEmployeeIds = null)
    {
        HourOptions = Enumerable.Range(0, 24).Select(x => x.ToString("00", CultureInfo.InvariantCulture)).ToList();
        MinuteOptions = Enumerable.Range(0, 60).Select(x => x.ToString("00", CultureInfo.InvariantCulture)).ToList();

        if (DateTime.TryParseExact(routeDate, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            _dateText = parsedDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        }

        _name = routeName ?? string.Empty;
        var normalizedStartHour = routeStartHour ?? string.Empty;
        var normalizedStartMinute = routeStartMinute ?? string.Empty;
        _selectedHour = HourOptions.Contains(normalizedStartHour) ? normalizedStartHour : "08";
        _selectedMinute = MinuteOptions.Contains(normalizedStartMinute) ? normalizedStartMinute : "00";

        VehicleOptions = new List<TourLookupOption> { new(string.Empty, "Bitte wählen") };
        VehicleOptions.AddRange(vehicleOptions ?? []);
        var normalizedVehicleId = (selectedVehicleId ?? string.Empty).Trim();
        SelectedVehicle = VehicleOptions.FirstOrDefault(x =>
            string.Equals(x.Id, normalizedVehicleId, StringComparison.OrdinalIgnoreCase)) ?? VehicleOptions.FirstOrDefault();

        TrailerOptions = new List<TourLookupOption> { new(string.Empty, "Kein Anhänger") };
        TrailerOptions.AddRange(trailerOptions ?? []);
        var normalizedTrailerId = (selectedTrailerId ?? string.Empty).Trim();
        SelectedTrailer = TrailerOptions.FirstOrDefault(x =>
            string.Equals(x.Id, normalizedTrailerId, StringComparison.OrdinalIgnoreCase)) ?? TrailerOptions.FirstOrDefault();

        Employees = (employeeOptions ?? []).Select(x => new SelectableEmployee(x.Id, x.Label)).ToList();
        if (selectedEmployeeIds is not null)
        {
            ApplySelectedEmployees(selectedEmployeeIds);
        }
        RefreshEmployeesSummary();
    }

    public IReadOnlyList<string> HourOptions { get; }

    public IReadOnlyList<string> MinuteOptions { get; }

    public List<TourLookupOption> VehicleOptions { get; }

    public List<TourLookupOption> TrailerOptions { get; }

    public List<SelectableEmployee> Employees { get; }

    public string DateText
    {
        get => _dateText;
        set => SetProperty(ref _dateText, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string SelectedHour
    {
        get => _selectedHour;
        set => SetProperty(ref _selectedHour, value);
    }

    public string SelectedMinute
    {
        get => _selectedMinute;
        set => SetProperty(ref _selectedMinute, value);
    }

    public TourLookupOption? SelectedVehicle
    {
        get => _selectedVehicle;
        set => SetProperty(ref _selectedVehicle, value);
    }

    public TourLookupOption? SelectedTrailer
    {
        get => _selectedTrailer;
        set => SetProperty(ref _selectedTrailer, value);
    }

    private string _selectedEmployeesSummary = "Keine Mitarbeiter ausgewählt";
    public string SelectedEmployeesSummary
    {
        get => _selectedEmployeesSummary;
        private set => SetProperty(ref _selectedEmployeesSummary, value);
    }

    public void ApplySelectedEmployees(IReadOnlyList<string> selectedIds)
    {
        var selectedSet = new HashSet<string>(selectedIds ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (var employee in Employees)
        {
            employee.IsSelected = selectedSet.Contains(employee.Id);
        }

        RefreshEmployeesSummary();
    }

    public bool TryBuildResult(out CreateTourDialogResult result, out string error)
    {
        result = default!;
        error = string.Empty;

        var normalizedDateText = (DateText ?? string.Empty).Trim();
        if (!DateTime.TryParseExact(normalizedDateText, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            error = "Bitte ein gültiges Datum im Format DD.MM.YYYY eingeben.";
            return false;
        }

        if (SelectedVehicle is null || string.IsNullOrWhiteSpace(SelectedVehicle.Id))
        {
            error = "Bitte ein Fahrzeug auswählen.";
            return false;
        }

        var employees = Employees.Where(x => x.IsSelected).Select(x => x.Id).ToList();
        if (employees.Count is < 1 or > 2)
        {
            error = "Bitte 1 bis 2 Mitarbeiter auswählen.";
            return false;
        }

        var tourName = string.IsNullOrWhiteSpace(Name) ? "Neue Karte-Tour" : Name.Trim();
        var tourDate = parsedDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        var startTime = $"{(SelectedHour ?? "08").PadLeft(2, '0')}:{(SelectedMinute ?? "00").PadLeft(2, '0')}";

        result = new CreateTourDialogResult(
            tourName,
            tourDate,
            startTime,
            SelectedVehicle.Id,
            SelectedTrailer?.Id,
            employees);
        return true;
    }

    private void RefreshEmployeesSummary()
    {
        var count = Employees.Count(x => x.IsSelected);
        SelectedEmployeesSummary = count == 0
            ? "Keine Mitarbeiter ausgewählt"
            : $"{count} Mitarbeiter ausgewählt";
    }
}

public sealed class SelectableEmployee : ObservableObject
{
    private bool _isSelected;

    public SelectableEmployee(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public string Id { get; }
    public string Label { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed record TourLookupOption(string Id, string Label)
{
    public override string ToString()
    {
        return Label;
    }
}
public sealed record TourEmployeeOption(string Id, string Label);
public sealed record CreateTourDialogResult(
    string RouteName,
    string RouteDate,
    string StartTime,
    string? VehicleId,
    string? TrailerId,
    IReadOnlyList<string> EmployeeIds);

