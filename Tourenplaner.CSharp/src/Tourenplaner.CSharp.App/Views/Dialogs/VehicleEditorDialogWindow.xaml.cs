using System.Globalization;
using System.Windows;
using Tourenplaner.CSharp.App.Services;
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
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(this, error, "Eingabe prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
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
    private string _grossWeightKgText;
    private string _maxPayloadKgText;
    private string _maxTrailerLoadKgText;
    private string _volumeM3Text;
    private string _externalLengthMetersText;
    private string _externalWidthMetersText;
    private string _externalHeightMetersText;
    private string _lengthMetersText;
    private string _widthMetersText;
    private string _heightMetersText;
    private string _notes;
    private bool _registerOutage;
    private string _outageStartDate;
    private string _outageEndDate;

    public VehicleEditorDialogViewModel(VehicleEditorSeed seed)
    {
        _id = seed.Id;
        _isTrailer = seed.IsTrailer;
        _type = NormalizeType(seed.Type, seed.IsTrailer);
        _name = seed.Name ?? string.Empty;
        _licensePlate = seed.LicensePlate ?? string.Empty;
        _grossWeightKgText = seed.GrossWeightKg <= 0 ? string.Empty : seed.GrossWeightKg.ToString(CultureInfo.InvariantCulture);
        _maxPayloadKgText = seed.MaxPayloadKg <= 0 ? string.Empty : seed.MaxPayloadKg.ToString(CultureInfo.InvariantCulture);
        _maxTrailerLoadKgText = seed.MaxTrailerLoadKg <= 0 ? string.Empty : seed.MaxTrailerLoadKg.ToString(CultureInfo.InvariantCulture);
        _volumeM3Text = seed.VolumeM3 <= 0 ? string.Empty : seed.VolumeM3.ToString(CultureInfo.InvariantCulture);
        _externalLengthMetersText = FormatMetersFromCentimeters(seed.ExternalLengthCm);
        _externalWidthMetersText = FormatMetersFromCentimeters(seed.ExternalWidthCm);
        _externalHeightMetersText = FormatMetersFromCentimeters(seed.ExternalHeightCm);
        _lengthMetersText = FormatMetersFromCentimeters(seed.LengthCm);
        _widthMetersText = FormatMetersFromCentimeters(seed.WidthCm);
        _heightMetersText = FormatMetersFromCentimeters(seed.HeightCm);
        _notes = seed.Notes ?? string.Empty;
        _registerOutage = seed.RegisterOutage;
        _outageStartDate = seed.OutageStartDate ?? string.Empty;
        _outageEndDate = seed.OutageEndDate ?? string.Empty;
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

    public string GrossWeightKgText
    {
        get => _grossWeightKgText;
        set => SetProperty(ref _grossWeightKgText, value);
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

    public string LengthMetersText
    {
        get => _lengthMetersText;
        set => SetProperty(ref _lengthMetersText, value);
    }

    public string ExternalLengthMetersText
    {
        get => _externalLengthMetersText;
        set => SetProperty(ref _externalLengthMetersText, value);
    }

    public string ExternalWidthMetersText
    {
        get => _externalWidthMetersText;
        set => SetProperty(ref _externalWidthMetersText, value);
    }

    public string ExternalHeightMetersText
    {
        get => _externalHeightMetersText;
        set => SetProperty(ref _externalHeightMetersText, value);
    }

    public string WidthMetersText
    {
        get => _widthMetersText;
        set => SetProperty(ref _widthMetersText, value);
    }

    public string HeightMetersText
    {
        get => _heightMetersText;
        set => SetProperty(ref _heightMetersText, value);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public bool RegisterOutage
    {
        get => _registerOutage;
        set => SetProperty(ref _registerOutage, value);
    }

    public string OutageStartDate
    {
        get => _outageStartDate;
        set => SetProperty(ref _outageStartDate, value);
    }

    public string OutageEndDate
    {
        get => _outageEndDate;
        set => SetProperty(ref _outageEndDate, value);
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

        if (!TryParseNonNegative(GrossWeightKgText, out var grossWeightKg, out error))
        {
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

        if (!TryParseMetersAsCentimeters(ExternalLengthMetersText, out var externalLengthCm, out error))
        {
            return false;
        }

        if (!TryParseMetersAsCentimeters(ExternalWidthMetersText, out var externalWidthCm, out error))
        {
            return false;
        }

        if (!TryParseMetersAsCentimeters(ExternalHeightMetersText, out var externalHeightCm, out error))
        {
            return false;
        }

        if (!TryParseMetersAsCentimeters(LengthMetersText, out var lengthCm, out error))
        {
            return false;
        }

        if (!TryParseMetersAsCentimeters(WidthMetersText, out var widthCm, out error))
        {
            return false;
        }

        if (!TryParseMetersAsCentimeters(HeightMetersText, out var heightCm, out error))
        {
            return false;
        }

        if (IsTrailer)
        {
            trailerLoadKg = 0;
        }

        if (RegisterOutage)
        {
            var start = ResourceAvailabilityService.ParseDate(OutageStartDate);
            var end = ResourceAvailabilityService.ParseDate(OutageEndDate);
            if (!start.HasValue || !end.HasValue)
            {
                error = "Bitte für den Ausfall ein gültiges Start- und Enddatum im Format DD.MM.YYYY eingeben.";
                return false;
            }
        }

        result = new VehicleEditorResult(
            Id: _id,
            IsTrailer: IsTrailer,
            Type: NormalizeType(_type, IsTrailer),
            Name: Name.Trim(),
            LicensePlate: (LicensePlate ?? string.Empty).Trim().ToUpperInvariant(),
            GrossWeightKg: grossWeightKg,
            MaxPayloadKg: payloadKg,
            MaxTrailerLoadKg: trailerLoadKg,
            VolumeM3: volumeM3,
            ExternalLengthCm: externalLengthCm,
            ExternalWidthCm: externalWidthCm,
            ExternalHeightCm: externalHeightCm,
            LengthCm: lengthCm,
            WidthCm: widthCm,
            HeightCm: heightCm,
            Notes: (Notes ?? string.Empty).Trim(),
            RegisterOutage: RegisterOutage,
            OutageStartDate: (OutageStartDate ?? string.Empty).Trim(),
            OutageEndDate: (OutageEndDate ?? string.Empty).Trim());
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

    private static string FormatMetersFromCentimeters(int centimeters)
    {
        if (centimeters <= 0)
        {
            return string.Empty;
        }

        var meters = centimeters / 100d;
        return meters.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static bool TryParseMetersAsCentimeters(string raw, out int centimeters, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            centimeters = 0;
            return true;
        }

        var normalized = raw.Trim().Replace(',', '.');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var meters) || meters < 0d)
        {
            centimeters = 0;
            error = "Bitte nur nicht-negative Zahlen in Metern eingeben (z.B. 2.2).";
            return false;
        }

        centimeters = (int)Math.Round(meters * 100d, MidpointRounding.AwayFromZero);
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


