using System.ComponentModel;
using System.Globalization;
using System.Windows;
using Tourenplaner.CSharp.App.ViewModels;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class CreateTourDialogWindow : Window
{
    private CollaborationResourceLease? _editLease;
    private string _lockConflictUserName = string.Empty;

    public CreateTourDialogWindow(
        string routeDate,
        string routeName,
        string routeStartHour,
        string routeStartMinute,
        IReadOnlyList<TourLookupOption> vehicleOptions,
        IReadOnlyList<TourLookupOption> trailerOptions,
        IReadOnlyList<TourEmployeeOption> employeeOptions,
        IReadOnlyList<string>? singleEmployeeWarningDeliveryTypes = null,
        string? selectedVehicleId = null,
        string? selectedTrailerId = null,
        string? selectedSecondaryVehicleId = null,
        string? selectedSecondaryTrailerId = null,
        IReadOnlyList<string>? selectedEmployeeIds = null,
        bool showOpenOnMapButton = false,
        IReadOnlyList<AdditionalMaterialGroup>? additionalMaterialGroups = null,
        IReadOnlyList<TourAdditionalMaterial>? selectedAdditionalMaterials = null,
        int? editTourId = null)
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
            singleEmployeeWarningDeliveryTypes,
            selectedVehicleId,
            selectedTrailerId,
            selectedSecondaryVehicleId,
            selectedSecondaryTrailerId,
            selectedEmployeeIds,
            additionalMaterialGroups,
            selectedAdditionalMaterials);
        DataContext = ViewModel;
        ShowOpenOnMapButton = showOpenOnMapButton;
        OpenOnMapButton.Visibility = showOpenOnMapButton ? Visibility.Visible : Visibility.Collapsed;
        if (editTourId is > 0)
        {
            var lockAttempt = Task.Run(
                    () => CollaborationSessionService.TryAcquireResourceAsync(
                        "tour",
                        editTourId.Value.ToString(CultureInfo.InvariantCulture)))
                .GetAwaiter()
                .GetResult();
            _editLease = lockAttempt.Lease;
            _lockConflictUserName = lockAttempt.Result.LockedByUserName;
            if (_editLease is null) Loaded += OnEditLockConflictLoaded;
            Closed += (_, _) => _editLease?.Dispose();
        }
    }

    public CreateTourDialogViewModel ViewModel { get; }

    public CreateTourDialogResult? Result { get; private set; }

    public bool ShowOpenOnMapButton { get; }

    public bool OpenOnMapRequested { get; private set; }

    private void OnEditLockConflictLoaded(object sender, RoutedEventArgs e)
    {
        Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
            this,
            $"Diese Liefertour wird aktuell von {_lockConflictUserName} bearbeitet.",
            "Liefertour gesperrt",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        Close();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnCreateClicked(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryBuildResult(out var result, out var validationError))
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(this, validationError, "Eingabe prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
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
    private bool _isNameAutoManaged = true;
    private bool _suppressNameAutoDetection;
    private string _selectedHour = "07";
    private string _selectedMinute = "30";
    private TourLookupOption? _selectedVehicle;
    private TourLookupOption? _selectedTrailer;
    private bool _useSecondaryVehicle;
    private TourLookupOption? _selectedSecondaryVehicle;
    private TourLookupOption? _selectedSecondaryTrailer;

    public CreateTourDialogViewModel(
        string routeDate,
        string routeName,
        string routeStartHour,
        string routeStartMinute,
        IReadOnlyList<TourLookupOption> vehicleOptions,
        IReadOnlyList<TourLookupOption> trailerOptions,
        IReadOnlyList<TourEmployeeOption> employeeOptions,
        IReadOnlyList<string>? singleEmployeeWarningDeliveryTypes = null,
        string? selectedVehicleId = null,
        string? selectedTrailerId = null,
        string? selectedSecondaryVehicleId = null,
        string? selectedSecondaryTrailerId = null,
        IReadOnlyList<string>? selectedEmployeeIds = null,
        IReadOnlyList<AdditionalMaterialGroup>? additionalMaterialGroups = null,
        IReadOnlyList<TourAdditionalMaterial>? selectedAdditionalMaterials = null)
    {
        HourOptions = Enumerable.Range(0, 24).Select(x => x.ToString("00", CultureInfo.InvariantCulture)).ToList();
        MinuteOptions = Enumerable.Range(0, 12).Select(x => (x * 5).ToString("00", CultureInfo.InvariantCulture)).ToList();

        if (DateTime.TryParseExact(routeDate, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            _dateText = parsedDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        }

        _name = (routeName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(_name))
        {
            _isNameAutoManaged = true;
            SetNameFromDate(_dateText);
        }
        else
        {
            _isNameAutoManaged = string.Equals(
                _name,
                BuildDefaultTourName(_dateText),
                StringComparison.OrdinalIgnoreCase);
        }
        var normalizedStartHour = routeStartHour ?? string.Empty;
        var normalizedStartMinute = routeStartMinute ?? string.Empty;
        _selectedHour = HourOptions.Contains(normalizedStartHour) ? normalizedStartHour : "07";
        _selectedMinute = MinuteOptions.Contains(normalizedStartMinute)
            ? normalizedStartMinute
            : NormalizeMinuteToFiveStep(normalizedStartMinute);

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
        var normalizedSecondaryVehicleId = (selectedSecondaryVehicleId ?? string.Empty).Trim();
        SelectedSecondaryVehicle = VehicleOptions.FirstOrDefault(x =>
            string.Equals(x.Id, normalizedSecondaryVehicleId, StringComparison.OrdinalIgnoreCase)) ?? VehicleOptions.FirstOrDefault();
        var normalizedSecondaryTrailerId = (selectedSecondaryTrailerId ?? string.Empty).Trim();
        SelectedSecondaryTrailer = TrailerOptions.FirstOrDefault(x =>
            string.Equals(x.Id, normalizedSecondaryTrailerId, StringComparison.OrdinalIgnoreCase)) ?? TrailerOptions.FirstOrDefault();
        UseSecondaryVehicle = !string.IsNullOrWhiteSpace(normalizedSecondaryVehicleId) ||
                              !string.IsNullOrWhiteSpace(normalizedSecondaryTrailerId);

        Employees = (employeeOptions ?? [])
            .Select(x => new SelectableEmployee(x.Id, x.Label, x.IsFavorite))
            .OrderByDescending(x => x.IsFavorite)
            .ThenBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (selectedEmployeeIds is not null)
        {
            ApplySelectedEmployees(selectedEmployeeIds);
        }

        SingleEmployeeWarningDeliveryTypes = (singleEmployeeWarningDeliveryTypes ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var employee in Employees)
        {
            employee.PropertyChanged += OnEmployeePropertyChanged;
        }
        RefreshEmployeesSummary();

        var selectedMaterials = selectedAdditionalMaterials ?? [];
        var selectedMaterialIds = selectedMaterials.Select(x => x.GroupId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var availableGroups = (additionalMaterialGroups ?? []).ToList();
        foreach (var selected in selectedMaterials.Where(x => !availableGroups.Any(g => string.Equals(g.Id, x.GroupId, StringComparison.OrdinalIgnoreCase))))
        {
            availableGroups.Add(new AdditionalMaterialGroup
            {
                Id = selected.GroupId,
                Name = selected.GroupName,
                TotalWeightKg = selected.WeightKg,
                Items = (selected.Items ?? []).Select(x => new AdditionalMaterialItem { Name = x.Name, WeightKg = x.WeightKg }).ToList()
            });
        }
        AdditionalMaterialOptions = availableGroups
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => new SelectableAdditionalMaterial(x, selectedMaterialIds.Contains(x.Id)))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> HourOptions { get; }

    public IReadOnlyList<string> MinuteOptions { get; }

    public List<TourLookupOption> VehicleOptions { get; }

    public List<TourLookupOption> TrailerOptions { get; }

    public List<SelectableEmployee> Employees { get; }

    public IReadOnlyList<string> SingleEmployeeWarningDeliveryTypes { get; }
    public List<SelectableAdditionalMaterial> AdditionalMaterialOptions { get; }
    public bool HasAdditionalMaterialOptions => AdditionalMaterialOptions.Count > 0;

    public string DateText
    {
        get => _dateText;
        set
        {
            if (!SetProperty(ref _dateText, value))
            {
                return;
            }

            if (_isNameAutoManaged)
            {
                SetNameFromDate(value);
            }
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            if (!SetProperty(ref _name, value))
            {
                return;
            }

            if (_suppressNameAutoDetection)
            {
                return;
            }

            _isNameAutoManaged = string.Equals(
                (_name ?? string.Empty).Trim(),
                BuildDefaultTourName(DateText),
                StringComparison.OrdinalIgnoreCase);
        }
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

    private static string NormalizeMinuteToFiveStep(string? rawMinute)
    {
        if (!int.TryParse((rawMinute ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minute))
        {
            return "30";
        }

        minute = Math.Clamp(minute, 0, 59);
        var rounded = (int)Math.Round(minute / 5d, MidpointRounding.AwayFromZero) * 5;
        if (rounded >= 60)
        {
            rounded = 55;
        }

        return rounded.ToString("00", CultureInfo.InvariantCulture);
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

    public bool UseSecondaryVehicle
    {
        get => _useSecondaryVehicle;
        set => SetProperty(ref _useSecondaryVehicle, value);
    }

    public TourLookupOption? SelectedSecondaryVehicle
    {
        get => _selectedSecondaryVehicle;
        set => SetProperty(ref _selectedSecondaryVehicle, value);
    }

    public TourLookupOption? SelectedSecondaryTrailer
    {
        get => _selectedSecondaryTrailer;
        set => SetProperty(ref _selectedSecondaryTrailer, value);
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

        if (parsedDate.Date < DateTime.Today)
        {
            error = $"Touren können nicht in der Vergangenheit geplant werden. Bitte ein Datum ab {DateTime.Today:dd.MM.yyyy} wählen.";
            return false;
        }

        var primaryVehicleId = (SelectedVehicle?.Id ?? string.Empty).Trim();
        var secondaryVehicleId = (SelectedSecondaryVehicle?.Id ?? string.Empty).Trim();
        var secondaryTrailerId = (SelectedSecondaryTrailer?.Id ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(primaryVehicleId))
        {
            error = "Bitte ein Fahrzeug auswählen.";
            return false;
        }

        if (UseSecondaryVehicle && string.IsNullOrWhiteSpace(secondaryVehicleId))
        {
            error = "Bitte für das zweite Fahrzeug eine Auswahl treffen.";
            return false;
        }

        if (UseSecondaryVehicle &&
            string.Equals(primaryVehicleId, secondaryVehicleId, StringComparison.OrdinalIgnoreCase))
        {
            error = "Das zweite Fahrzeug muss sich vom ersten Fahrzeug unterscheiden.";
            return false;
        }

        var employees = Employees.Where(x => x.IsSelected).Select(x => x.Id).ToList();
        if (employees.Count < 1)
        {
            error = "Bitte mindestens 1 Mitarbeiter auswählen.";
            return false;
        }

        if (UseSecondaryVehicle && employees.Count < 2)
        {
            error = "Bei zwei Fahrzeugen müssen mindestens 2 Mitarbeiter ausgewählt werden.";
            return false;
        }

        var tourDate = parsedDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        var tourName = string.IsNullOrWhiteSpace(Name) ? BuildDefaultTourName(tourDate) : Name.Trim();
        var startTime = $"{(SelectedHour ?? "07").PadLeft(2, '0')}:{(SelectedMinute ?? "30").PadLeft(2, '0')}";

        result = new CreateTourDialogResult(
            tourName,
            tourDate,
            startTime,
            primaryVehicleId,
            SelectedTrailer?.Id,
            UseSecondaryVehicle ? secondaryVehicleId : null,
            UseSecondaryVehicle && !string.IsNullOrWhiteSpace(secondaryTrailerId) ? secondaryTrailerId : null,
            employees,
            AdditionalMaterialOptions.Where(x => x.IsSelected).Select(x => x.CreateSnapshot()).ToList());
        return true;
    }

    public bool TryBuildSingleEmployeeWarning(IReadOnlyList<string> employeeIds, out string warning)
    {
        return Tourenplaner.CSharp.App.Services.TourStaffingWarningService.TryBuildSingleEmployeeWarning(
            employeeIds,
            SingleEmployeeWarningDeliveryTypes,
            out warning);
    }

    private void RefreshEmployeesSummary()
    {
        var count = Employees.Count(x => x.IsSelected);
        SelectedEmployeesSummary = count == 0
            ? "Keine Mitarbeiter ausgew\u00E4hlt"
            : $"{count} Mitarbeiter ausgew\u00E4hlt";
    }

    private void OnEmployeePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectableEmployee.IsSelected))
        {
            RefreshEmployeesSummary();
        }
    }

    private void SetNameFromDate(string? dateText)
    {
        var autoName = BuildDefaultTourName(dateText);
        _suppressNameAutoDetection = true;
        try
        {
            SetProperty(ref _name, autoName, nameof(Name));
        }
        finally
        {
            _suppressNameAutoDetection = false;
        }
    }

    private static string BuildDefaultTourName(string? dateText)
    {
        var normalized = (dateText ?? string.Empty).Trim();
        if (!DateTime.TryParseExact(normalized, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            parsedDate = DateTime.Today;
        }

        return $"Tour {parsedDate:dd.MM.yyyy}";
    }
}

public sealed class SelectableEmployee : ObservableObject
{
    private bool _isSelected;

    public SelectableEmployee(string id, string label, bool isFavorite = false)
    {
        Id = id;
        Label = label;
        IsFavorite = isFavorite;
    }

    public string Id { get; }
    public string Label { get; }
    public bool IsFavorite { get; }

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
public sealed record TourEmployeeOption(string Id, string Label, bool IsFavorite);
public sealed record CreateTourDialogResult(
    string RouteName,
    string RouteDate,
    string StartTime,
    string? VehicleId,
    string? TrailerId,
    string? SecondaryVehicleId,
    string? SecondaryTrailerId,
    IReadOnlyList<string> EmployeeIds,
    IReadOnlyList<TourAdditionalMaterial> AdditionalMaterials);

public sealed class SelectableAdditionalMaterial : ObservableObject
{
    private bool _isSelected;
    private readonly AdditionalMaterialGroup _source;
    public SelectableAdditionalMaterial(AdditionalMaterialGroup source, bool isSelected) { _source = source; _isSelected = isSelected; }
    public string Name => _source.Name;
    public double WeightKg => _source.ResolveWeightKg();
    public string Label => $"{Name} ({WeightKg:0.##} kg)";
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    public TourAdditionalMaterial CreateSnapshot() => new()
    {
        GroupId = _source.Id,
        GroupName = _source.Name,
        WeightKg = WeightKg,
        Items = (_source.Items ?? []).Select(x => new AdditionalMaterialItem { Name = x.Name, WeightKg = x.WeightKg }).ToList()
    };
}



