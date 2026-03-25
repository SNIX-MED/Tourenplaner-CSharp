using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using Tourenplaner.CSharp.App.ViewModels;
using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class VehicleCombinationEditorDialogWindow : Window
{
    public VehicleCombinationEditorDialogWindow(
        VehicleCombinationEditorSeed seed,
        IReadOnlyList<VehicleCombinationOption> vehicleOptions,
        IReadOnlyList<VehicleCombinationOption> trailerOptions)
    {
        InitializeComponent();
        ViewModel = new VehicleCombinationEditorDialogViewModel(seed, vehicleOptions, trailerOptions);
        DataContext = ViewModel;
    }

    public VehicleCombinationEditorDialogViewModel ViewModel { get; }

    public VehicleCombinationEditorResult? Result { get; private set; }

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
}

public sealed class VehicleCombinationEditorDialogViewModel : ObservableObject
{
    private readonly string? _id;
    private VehicleCombinationOption? _selectedVehicle;
    private VehicleCombinationOption? _selectedTrailer;
    private string _vehiclePayloadKgText;
    private string _trailerLoadKgText;
    private string _notes;
    private bool _active;

    public VehicleCombinationEditorDialogViewModel(
        VehicleCombinationEditorSeed seed,
        IReadOnlyList<VehicleCombinationOption> vehicleOptions,
        IReadOnlyList<VehicleCombinationOption> trailerOptions)
    {
        _id = seed.Id;
        VehicleOptions = new ObservableCollection<VehicleCombinationOption>(vehicleOptions);
        TrailerOptions = new ObservableCollection<VehicleCombinationOption>(trailerOptions);
        _selectedVehicle = VehicleOptions.FirstOrDefault(x => x.Id == seed.VehicleId);
        _selectedTrailer = TrailerOptions.FirstOrDefault(x => x.Id == seed.TrailerId);
        _vehiclePayloadKgText = seed.VehiclePayloadKg <= 0 ? string.Empty : seed.VehiclePayloadKg.ToString(CultureInfo.InvariantCulture);
        _trailerLoadKgText = seed.TrailerLoadKg <= 0 ? string.Empty : seed.TrailerLoadKg.ToString(CultureInfo.InvariantCulture);
        _notes = seed.Notes ?? string.Empty;
        _active = seed.Active;
    }

    public ObservableCollection<VehicleCombinationOption> VehicleOptions { get; }

    public ObservableCollection<VehicleCombinationOption> TrailerOptions { get; }

    public string Heading => "Fahrzeugkombination";

    public VehicleCombinationOption? SelectedVehicle
    {
        get => _selectedVehicle;
        set => SetProperty(ref _selectedVehicle, value);
    }

    public VehicleCombinationOption? SelectedTrailer
    {
        get => _selectedTrailer;
        set => SetProperty(ref _selectedTrailer, value);
    }

    public string VehiclePayloadKgText
    {
        get => _vehiclePayloadKgText;
        set => SetProperty(ref _vehiclePayloadKgText, value);
    }

    public string TrailerLoadKgText
    {
        get => _trailerLoadKgText;
        set => SetProperty(ref _trailerLoadKgText, value);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public bool Active
    {
        get => _active;
        set => SetProperty(ref _active, value);
    }

    public bool TryBuildResult(out VehicleCombinationEditorResult result, out string error)
    {
        result = default!;
        error = string.Empty;

        if (SelectedVehicle is null)
        {
            error = "Bitte ein Zugfahrzeug auswählen.";
            return false;
        }

        if (SelectedTrailer is null)
        {
            error = "Bitte einen Anhänger auswählen.";
            return false;
        }

        if (!TryParseNonNegative(VehiclePayloadKgText, out var vehiclePayloadKg, out error))
        {
            return false;
        }

        if (!TryParseNonNegative(TrailerLoadKgText, out var trailerLoadKg, out error))
        {
            return false;
        }

        result = new VehicleCombinationEditorResult(
            Id: _id,
            VehicleId: SelectedVehicle.Id,
            TrailerId: SelectedTrailer.Id,
            VehiclePayloadKg: vehiclePayloadKg,
            TrailerLoadKg: trailerLoadKg,
            Notes: (Notes ?? string.Empty).Trim(),
            Active: Active);
        return true;
    }

    private static bool TryParseNonNegative(string raw, out int value, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = 0;
            return true;
        }

        if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < 0)
        {
            value = 0;
            error = "Bitte nur nicht-negative Ganzzahlen eingeben.";
            return false;
        }

        return true;
    }
}
