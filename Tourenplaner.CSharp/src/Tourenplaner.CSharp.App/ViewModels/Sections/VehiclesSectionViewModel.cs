using System.Collections.ObjectModel;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class VehiclesSectionViewModel : SectionViewModelBase
{
    private readonly JsonVehicleDataRepository _repository;
    private readonly List<Vehicle> _vehicles = new();
    private readonly List<TrailerRecord> _trailers = new();
    private FleetDisplayMode _displayMode = FleetDisplayMode.Vehicles;
    private string _statusText = "Lade Fahrzeuge...";
    private string _modeCountText = string.Empty;

    public VehiclesSectionViewModel(string vehiclesJsonPath)
        : base("Fahrzeugverwaltung", "Zugfahrzeuge und Anhänger verwalten.")
    {
        _repository = new JsonVehicleDataRepository(vehiclesJsonPath);

        RefreshCommand = new AsyncCommand(RefreshAsync);
        ShowVehiclesCommand = new DelegateCommand(() => SetDisplayMode(FleetDisplayMode.Vehicles));
        ShowTrailersCommand = new DelegateCommand(() => SetDisplayMode(FleetDisplayMode.Trailers));

        _ = RefreshAsync();
    }

    public ObservableCollection<FleetEntryCardItem> Entries { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand ShowVehiclesCommand { get; }

    public ICommand ShowTrailersCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ModeCountText
    {
        get => _modeCountText;
        private set => SetProperty(ref _modeCountText, value);
    }

    public bool IsVehicleMode => _displayMode == FleetDisplayMode.Vehicles;

    public bool IsTrailerMode => _displayMode == FleetDisplayMode.Trailers;

    public string AddButtonText => IsVehicleMode ? "+ Fahrzeug" : "+ Anhänger";

    public VehicleEditorSeed CreateSeedForCreate()
    {
        var isTrailer = IsTrailerMode;
        return new VehicleEditorSeed(
            Id: null,
            IsTrailer: isTrailer,
            Type: isTrailer ? "trailer" : "truck",
            Name: string.Empty,
            LicensePlate: string.Empty,
            MaxPayloadKg: 0,
            MaxTrailerLoadKg: 0,
            VolumeM3: 0,
            LengthCm: 0,
            WidthCm: 0,
            HeightCm: 0,
            Notes: string.Empty,
            Active: true);
    }

    public VehicleEditorSeed CreateSeedForEdit(FleetEntryCardItem entry)
    {
        return new VehicleEditorSeed(
            Id: entry.Id,
            IsTrailer: entry.IsTrailer,
            Type: entry.Type,
            Name: entry.Name,
            LicensePlate: entry.LicensePlate,
            MaxPayloadKg: entry.MaxPayloadKg,
            MaxTrailerLoadKg: entry.MaxTrailerLoadKg,
            VolumeM3: entry.VolumeM3,
            LengthCm: entry.LengthCm,
            WidthCm: entry.WidthCm,
            HeightCm: entry.HeightCm,
            Notes: entry.Notes,
            Active: entry.Active);
    }

    public async Task ApplyEditorResultAsync(VehicleEditorResult result)
    {
        var id = string.IsNullOrWhiteSpace(result.Id) ? Guid.NewGuid().ToString() : result.Id.Trim();

        _vehicles.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        _trailers.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

        var loadingArea = BuildDimensions(result.LengthCm, result.WidthCm, result.HeightCm);
        if (result.IsTrailer)
        {
            _trailers.Add(new TrailerRecord
            {
                Id = id,
                Name = result.Name.Trim(),
                LicensePlate = result.LicensePlate.Trim(),
                MaxPayloadKg = Math.Max(0, result.MaxPayloadKg),
                VolumeM3 = Math.Max(0, result.VolumeM3),
                LoadingArea = loadingArea,
                Active = result.Active,
                Notes = result.Notes.Trim()
            });
            _displayMode = FleetDisplayMode.Trailers;
        }
        else
        {
            _vehicles.Add(new Vehicle
            {
                Id = id,
                Type = NormalizeVehicleType(result.Type),
                Name = result.Name.Trim(),
                LicensePlate = result.LicensePlate.Trim(),
                MaxPayloadKg = Math.Max(0, result.MaxPayloadKg),
                MaxTrailerLoadKg = Math.Max(0, result.MaxTrailerLoadKg),
                VolumeM3 = Math.Max(0, result.VolumeM3),
                LoadingArea = loadingArea,
                Active = result.Active,
                Notes = result.Notes.Trim()
            });
            _displayMode = FleetDisplayMode.Vehicles;
        }

        await SaveCurrentStateAsync();
    }

    public async Task DeleteEntryAsync(FleetEntryCardItem entry)
    {
        if (entry.IsTrailer)
        {
            _trailers.RemoveAll(x => string.Equals(x.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            _vehicles.RemoveAll(x => string.Equals(x.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        }

        await SaveCurrentStateAsync();
    }

    public async Task RefreshAsync()
    {
        var payload = await _repository.LoadAsync();
        _vehicles.Clear();
        _vehicles.AddRange(payload.Vehicles);
        _trailers.Clear();
        _trailers.AddRange(payload.Trailers);
        RebuildEntries();
        RaiseCommandStates();
    }

    private async Task SaveCurrentStateAsync()
    {
        await _repository.SaveAsync(new VehicleDataRecord
        {
            Vehicles = _vehicles.ToList(),
            Trailers = _trailers.ToList()
        });
        await RefreshAsync();
    }

    private void SetDisplayMode(FleetDisplayMode mode)
    {
        if (_displayMode == mode)
        {
            return;
        }

        _displayMode = mode;
        OnPropertyChanged(nameof(IsVehicleMode));
        OnPropertyChanged(nameof(IsTrailerMode));
        RebuildEntries();
        RaiseCommandStates();
    }

    private void RebuildEntries()
    {
        Entries.Clear();

        if (_displayMode == FleetDisplayMode.Vehicles)
        {
            foreach (var vehicle in _vehicles.OrderBy(x => !x.Active).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                Entries.Add(new FleetEntryCardItem
                {
                    Id = vehicle.Id,
                    IsTrailer = false,
                    Type = NormalizeVehicleType(vehicle.Type),
                    Name = vehicle.Name,
                    LicensePlate = vehicle.LicensePlate,
                    MaxPayloadKg = vehicle.MaxPayloadKg,
                    MaxTrailerLoadKg = vehicle.MaxTrailerLoadKg,
                    VolumeM3 = vehicle.VolumeM3,
                    LengthCm = vehicle.LoadingArea?.LengthCm ?? 0,
                    WidthCm = vehicle.LoadingArea?.WidthCm ?? 0,
                    HeightCm = vehicle.LoadingArea?.HeightCm ?? 0,
                    Notes = vehicle.Notes,
                    Active = vehicle.Active
                });
            }
        }
        else
        {
            foreach (var trailer in _trailers.OrderBy(x => !x.Active).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                Entries.Add(new FleetEntryCardItem
                {
                    Id = trailer.Id,
                    IsTrailer = true,
                    Type = "trailer",
                    Name = trailer.Name,
                    LicensePlate = trailer.LicensePlate,
                    MaxPayloadKg = trailer.MaxPayloadKg,
                    MaxTrailerLoadKg = 0,
                    VolumeM3 = trailer.VolumeM3,
                    LengthCm = trailer.LoadingArea?.LengthCm ?? 0,
                    WidthCm = trailer.LoadingArea?.WidthCm ?? 0,
                    HeightCm = trailer.LoadingArea?.HeightCm ?? 0,
                    Notes = trailer.Notes,
                    Active = trailer.Active
                });
            }
        }

        var activeVehicles = _vehicles.Count(x => x.Active);
        var activeTrailers = _trailers.Count(x => x.Active);
        StatusText = $"Zugfahrzeuge: {_vehicles.Count} (aktiv {activeVehicles}) | Anhänger: {_trailers.Count} (aktiv {activeTrailers})";
        ModeCountText = IsVehicleMode ? $"Zugfahrzeuge: {Entries.Count}" : $"Anhänger: {Entries.Count}";
        OnPropertyChanged(nameof(IsVehicleMode));
        OnPropertyChanged(nameof(IsTrailerMode));
        OnPropertyChanged(nameof(AddButtonText));
    }

    private static VehicleDimensions? BuildDimensions(int lengthCm, int widthCm, int heightCm)
    {
        lengthCm = Math.Max(0, lengthCm);
        widthCm = Math.Max(0, widthCm);
        heightCm = Math.Max(0, heightCm);
        if (lengthCm == 0 && widthCm == 0 && heightCm == 0)
        {
            return null;
        }

        return new VehicleDimensions
        {
            LengthCm = lengthCm,
            WidthCm = widthCm,
            HeightCm = heightCm
        };
    }

    private static string NormalizeVehicleType(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "truck" => "truck",
            "van" => "van",
            "car" => "car",
            "other" => "other",
            _ => "truck"
        };
    }

    private void RaiseCommandStates()
    {
        if (ShowVehiclesCommand is DelegateCommand showVehicles)
        {
            showVehicles.RaiseCanExecuteChanged();
        }

        if (ShowTrailersCommand is DelegateCommand showTrailers)
        {
            showTrailers.RaiseCanExecuteChanged();
        }
    }
}

public sealed class FleetEntryCardItem : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public bool IsTrailer { get; set; }
    public string Type { get; set; } = "truck";
    public string Name { get; set; } = string.Empty;
    public string LicensePlate { get; set; } = string.Empty;
    public int MaxPayloadKg { get; set; }
    public int MaxTrailerLoadKg { get; set; }
    public int VolumeM3 { get; set; }
    public int LengthCm { get; set; }
    public int WidthCm { get; set; }
    public int HeightCm { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool Active { get; set; } = true;

    public string ActiveLabel => Active ? "Aktiv" : "Inaktiv";

    public string HeaderTypeLabel => IsTrailer ? "Anhänger" : "Zugfahrzeug";

    public string DetailsLine
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(LicensePlate))
            {
                parts.Add($"Kennzeichen: {LicensePlate}");
            }

            if (MaxPayloadKg > 0)
            {
                parts.Add($"Nutzlast: {MaxPayloadKg} kg");
            }

            if (!IsTrailer && MaxTrailerLoadKg > 0)
            {
                parts.Add($"Anhängelast: {MaxTrailerLoadKg} kg");
            }

            if (VolumeM3 > 0)
            {
                parts.Add($"Volumen: {VolumeM3} m3");
            }

            if (LengthCm > 0 || WidthCm > 0 || HeightCm > 0)
            {
                parts.Add($"Ladefläche: {LengthCm} x {WidthCm} x {HeightCm} cm");
            }

            return string.Join(" | ", parts);
        }
    }
}

public enum FleetDisplayMode
{
    Vehicles,
    Trailers
}
