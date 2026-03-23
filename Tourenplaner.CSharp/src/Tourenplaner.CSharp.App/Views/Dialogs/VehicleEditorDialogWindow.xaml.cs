using System.Globalization;
using System.Windows;
using Tourenplaner.CSharp.App.ViewModels;
using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class VehicleEditorDialogWindow : Window
{
    public VehicleEditorDialogWindow(VehicleEditorSeed seed)
    {
        InitializeComponent();
        ViewModel = new VehicleEditorDialogViewModel(seed);
        DataContext = ViewModel;
    }

    public VehicleEditorDialogViewModel ViewModel { get; }

    public VehicleEditorResult? Result { get; private set; }

    private void OnSetVehicleTypeClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.SetTrailer(false);
    }

    private void OnSetTrailerTypeClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.SetTrailer(true);
    }

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

public sealed class VehicleEditorDialogViewModel : ObservableObject
{
    private readonly string? _id;
    private bool _isTrailer;
    private string _type;
    private string _name;
    private string _licensePlate;
    private string _maxPayloadKgText;
    private string _maxTrailerLoadKgText;
    private string _volumeM3Text;
    private string _lengthCmText;
    private string _widthCmText;
    private string _heightCmText;
    private string _notes;
    private bool _active;

    public VehicleEditorDialogViewModel(VehicleEditorSeed seed)
    {
        _id = seed.Id;
        _isTrailer = seed.IsTrailer;
        _type = NormalizeType(seed.Type, seed.IsTrailer);
        _name = seed.Name ?? string.Empty;
        _licensePlate = seed.LicensePlate ?? string.Empty;
        _maxPayloadKgText = seed.MaxPayloadKg <= 0 ? string.Empty : seed.MaxPayloadKg.ToString(CultureInfo.InvariantCulture);
        _maxTrailerLoadKgText = seed.MaxTrailerLoadKg <= 0 ? string.Empty : seed.MaxTrailerLoadKg.ToString(CultureInfo.InvariantCulture);
        _volumeM3Text = seed.VolumeM3 <= 0 ? string.Empty : seed.VolumeM3.ToString(CultureInfo.InvariantCulture);
        _lengthCmText = seed.LengthCm <= 0 ? string.Empty : seed.LengthCm.ToString(CultureInfo.InvariantCulture);
        _widthCmText = seed.WidthCm <= 0 ? string.Empty : seed.WidthCm.ToString(CultureInfo.InvariantCulture);
        _heightCmText = seed.HeightCm <= 0 ? string.Empty : seed.HeightCm.ToString(CultureInfo.InvariantCulture);
        _notes = seed.Notes ?? string.Empty;
        _active = seed.Active;
    }

    public string Heading => IsTrailer ? "Anhänger" : "Zugfahrzeug";

    public bool IsTrailer
    {
        get => _isTrailer;
        private set
        {
            if (SetProperty(ref _isTrailer, value))
            {
                _type = NormalizeType(_type, value);
                OnPropertyChanged(nameof(Heading));
                OnPropertyChanged(nameof(VehicleOnlyVisibility));
            }
        }
    }

    public Visibility VehicleOnlyVisibility => IsTrailer ? Visibility.Collapsed : Visibility.Visible;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string LicensePlate
    {
        get => _licensePlate;
        set => SetProperty(ref _licensePlate, value);
    }

    public string MaxPayloadKgText
    {
        get => _maxPayloadKgText;
        set => SetProperty(ref _maxPayloadKgText, value);
    }

    public string MaxTrailerLoadKgText
    {
        get => _maxTrailerLoadKgText;
        set => SetProperty(ref _maxTrailerLoadKgText, value);
    }

    public string VolumeM3Text
    {
        get => _volumeM3Text;
        set => SetProperty(ref _volumeM3Text, value);
    }

    public string LengthCmText
    {
        get => _lengthCmText;
        set => SetProperty(ref _lengthCmText, value);
    }

    public string WidthCmText
    {
        get => _widthCmText;
        set => SetProperty(ref _widthCmText, value);
    }

    public string HeightCmText
    {
        get => _heightCmText;
        set => SetProperty(ref _heightCmText, value);
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

    public void SetTrailer(bool isTrailer)
    {
        IsTrailer = isTrailer;
    }

    public bool TryBuildResult(out VehicleEditorResult result, out string error)
    {
        result = default!;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "Bitte einen Namen eingeben.";
            return false;
        }

        if (!TryParseNonNegative(MaxPayloadKgText, out var payloadKg, out error))
        {
            return false;
        }

        if (!TryParseNonNegative(MaxTrailerLoadKgText, out var trailerLoadKg, out error))
        {
            return false;
        }

        if (!TryParseNonNegative(VolumeM3Text, out var volumeM3, out error))
        {
            return false;
        }

        if (!TryParseNonNegative(LengthCmText, out var lengthCm, out error))
        {
            return false;
        }

        if (!TryParseNonNegative(WidthCmText, out var widthCm, out error))
        {
            return false;
        }

        if (!TryParseNonNegative(HeightCmText, out var heightCm, out error))
        {
            return false;
        }

        if (IsTrailer)
        {
            trailerLoadKg = 0;
        }

        result = new VehicleEditorResult(
            Id: _id,
            IsTrailer: IsTrailer,
            Type: NormalizeType(_type, IsTrailer),
            Name: Name.Trim(),
            LicensePlate: (LicensePlate ?? string.Empty).Trim().ToUpperInvariant(),
            MaxPayloadKg: payloadKg,
            MaxTrailerLoadKg: trailerLoadKg,
            VolumeM3: volumeM3,
            LengthCm: lengthCm,
            WidthCm: widthCm,
            HeightCm: heightCm,
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

    private static string NormalizeType(string? raw, bool isTrailer)
    {
        if (isTrailer)
        {
            return "trailer";
        }

        var normalized = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "truck" => "truck",
            "van" => "van",
            "car" => "car",
            _ => "truck"
        };
    }
}
