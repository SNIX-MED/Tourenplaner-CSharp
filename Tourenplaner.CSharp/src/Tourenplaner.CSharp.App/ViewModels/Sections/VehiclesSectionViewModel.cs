using System.Collections.ObjectModel;
using System.Windows.Input;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class VehiclesSectionViewModel : SectionViewModelBase
{
    private readonly JsonVehicleDataRepository _repository;
    private readonly JsonToursRepository _tourRepository;
    private readonly AppDataSyncService _dataSyncService;
    private readonly List<Vehicle> _vehicles = new();
    private readonly List<TrailerRecord> _trailers = new();
    private readonly List<VehicleCombinationRecord> _combinations = new();
    private readonly Guid _instanceId = Guid.NewGuid();
    private FleetDisplayMode _displayMode = FleetDisplayMode.Vehicles;
    private string _statusText = "Lade Fahrzeuge...";
    private string _modeCountText = string.Empty;

    public VehiclesSectionViewModel(string vehiclesJsonPath, string toursJsonPath, AppDataSyncService dataSyncService)
        : base("Fahrzeugverwaltung", "Zugfahrzeuge, Anhänger und Ausfälle verwalten.")
    {
        _repository = new JsonVehicleDataRepository(vehiclesJsonPath);
        _tourRepository = new JsonToursRepository(toursJsonPath);
        _dataSyncService = dataSyncService;

        RefreshCommand = new AsyncCommand(RefreshAsync);
        ShowVehiclesCommand = new DelegateCommand(() => SetDisplayMode(FleetDisplayMode.Vehicles));
        ShowTrailersCommand = new DelegateCommand(() => SetDisplayMode(FleetDisplayMode.Trailers));
        ShowCombinationsCommand = new DelegateCommand(() => SetDisplayMode(FleetDisplayMode.Combinations));
        RequestAddEntryCommand = new DelegateCommand(() => AddEntryRequested?.Invoke(this, EventArgs.Empty));
        _dataSyncService.DataChanged += OnDataChanged;

        _ = RefreshAsync();
    }

    public ObservableCollection<FleetEntryCardItem> Entries { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand ShowVehiclesCommand { get; }

    public ICommand ShowTrailersCommand { get; }

    public ICommand ShowCombinationsCommand { get; }
    
    public ICommand RequestAddEntryCommand { get; }

    public event EventHandler? AddEntryRequested;

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

    public bool IsCombinationMode => _displayMode == FleetDisplayMode.Combinations;

    public string AddButtonText => _displayMode switch
    {
        FleetDisplayMode.Vehicles => "+ Fahrzeug",
        FleetDisplayMode.Trailers => "+ Anhänger",
        FleetDisplayMode.Combinations => "+ Fahrzeugkombination",
        _ => "+ Eintrag"
    };

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
            RegisterOutage: false,
            OutageStartDate: string.Empty,
            OutageEndDate: string.Empty);
    }

    public VehicleEditorSeed CreateSeedForEdit(FleetEntryCardItem entry)
    {
        var sourceVehicle = _vehicles.FirstOrDefault(x => string.Equals(x.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        var sourceTrailer = _trailers.FirstOrDefault(x => string.Equals(x.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        var editablePeriod = GetEditablePeriod((entry.IsTrailer ? sourceTrailer?.UnavailabilityPeriods : sourceVehicle?.UnavailabilityPeriods) ?? []);
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
            RegisterOutage: editablePeriod is not null,
            OutageStartDate: editablePeriod?.StartDate.ToString("dd.MM.yyyy") ?? string.Empty,
            OutageEndDate: editablePeriod?.EndDate.ToString("dd.MM.yyyy") ?? string.Empty);
    }

    public VehicleCombinationEditorSeed CreateCombinationSeedForCreate()
    {
        return new VehicleCombinationEditorSeed(
            Id: null,
            VehicleId: string.Empty,
            TrailerId: string.Empty,
            VehiclePayloadKg: 0,
            TrailerLoadKg: 0,
            Notes: string.Empty,
            Active: true);
    }

    public VehicleCombinationEditorSeed CreateCombinationSeedForEdit(FleetEntryCardItem entry)
    {
        return new VehicleCombinationEditorSeed(
            Id: entry.Id,
            VehicleId: entry.VehicleId,
            TrailerId: entry.TrailerId,
            VehiclePayloadKg: entry.CombinationVehiclePayloadKg,
            TrailerLoadKg: entry.CombinationTrailerLoadKg,
            Notes: entry.Notes,
            Active: entry.Active);
    }

    public IReadOnlyList<VehicleCombinationOption> BuildCombinationOptions()
    {
        return _vehicles
            .Where(x => x.Active)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new VehicleCombinationOption(x.Id, BuildVehicleLabel(x)))
            .ToList();
    }

    public IReadOnlyList<VehicleCombinationOption> BuildTrailerOptions()
    {
        return _trailers
            .Where(x => x.Active)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new VehicleCombinationOption(x.Id, BuildTrailerLabel(x)))
            .ToList();
    }

    public async Task<string?> ApplyEditorResultAsync(VehicleEditorResult result)
    {
        var id = string.IsNullOrWhiteSpace(result.Id) ? Guid.NewGuid().ToString() : result.Id.Trim();
        ValidateVehicleResult(result, id);

        string? warning = null;

        var existingVehicle = _vehicles.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        var existingTrailer = _trailers.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

        _vehicles.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        _trailers.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

        var loadingArea = BuildDimensions(result.LengthCm, result.WidthCm, result.HeightCm);
        if (result.IsTrailer)
        {
            var periods = new List<ResourceUnavailabilityPeriod>();
            if (result.RegisterOutage)
            {
                AppendOutagePeriod(periods, result.OutageStartDate, result.OutageEndDate);
                warning = await BuildOutageAssignmentWarningAsync(
                    id,
                    result.Name,
                    result.OutageStartDate,
                    result.OutageEndDate,
                    isTrailer: true);
            }

            _trailers.Add(new TrailerRecord
            {
                Id = id,
                Name = result.Name.Trim(),
                LicensePlate = result.LicensePlate.Trim(),
                MaxPayloadKg = Math.Max(0, result.MaxPayloadKg),
                VolumeM3 = Math.Max(0, result.VolumeM3),
                LoadingArea = loadingArea,
                Active = existingTrailer?.Active ?? true,
                Notes = result.Notes.Trim(),
                UnavailabilityPeriods = periods
            });
            _displayMode = FleetDisplayMode.Trailers;
        }
        else
        {
            var periods = new List<ResourceUnavailabilityPeriod>();
            if (result.RegisterOutage)
            {
                AppendOutagePeriod(periods, result.OutageStartDate, result.OutageEndDate);
                warning = await BuildOutageAssignmentWarningAsync(
                    id,
                    result.Name,
                    result.OutageStartDate,
                    result.OutageEndDate,
                    isTrailer: false);
            }

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
                Active = existingVehicle?.Active ?? true,
                Notes = result.Notes.Trim(),
                UnavailabilityPeriods = periods
            });
            _displayMode = FleetDisplayMode.Vehicles;
        }

        await SaveCurrentStateAsync();
        return warning;
    }

    public async Task ApplyCombinationEditorResultAsync(VehicleCombinationEditorResult result)
    {
        var vehicleId = (result.VehicleId ?? string.Empty).Trim();
        var trailerId = (result.TrailerId ?? string.Empty).Trim();
        if (_vehicles.All(x => !string.Equals(x.Id, vehicleId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Das gewählte Zugfahrzeug existiert nicht mehr.");
        }

        if (_trailers.All(x => !string.Equals(x.Id, trailerId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Der gewählte Anhänger existiert nicht mehr.");
        }

        var id = string.IsNullOrWhiteSpace(result.Id) ? Guid.NewGuid().ToString() : result.Id.Trim();
        ValidateCombinationResult(result, id);
        _combinations.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        _combinations.Add(new VehicleCombinationRecord
        {
            Id = id,
            VehicleId = vehicleId,
            TrailerId = trailerId,
            VehiclePayloadKg = Math.Max(0, result.VehiclePayloadKg),
            TrailerLoadKg = Math.Max(0, result.TrailerLoadKg),
            Notes = (result.Notes ?? string.Empty).Trim(),
            Active = result.Active
        });

        _displayMode = FleetDisplayMode.Combinations;
        await SaveCurrentStateAsync();
    }

    private void ValidateVehicleResult(VehicleEditorResult result, string id)
    {
        var name = (result.Name ?? string.Empty).Trim();
        var plateKey = NormalizeLicensePlateKey(result.LicensePlate);

        if (result.IsTrailer)
        {
            if (_trailers.Any(x =>
                    !string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Es existiert bereits ein Anhänger mit diesem Namen.");
            }

            if (!string.IsNullOrWhiteSpace(plateKey) &&
                _trailers.Any(x =>
                    !string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(NormalizeLicensePlateKey(x.LicensePlate), plateKey, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Es existiert bereits ein Anhänger mit diesem Kennzeichen.");
            }

            return;
        }

        if (_vehicles.Any(x =>
                !string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Es existiert bereits ein Zugfahrzeug mit diesem Namen.");
        }

        if (!string.IsNullOrWhiteSpace(plateKey) &&
            _vehicles.Any(x =>
                !string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeLicensePlateKey(x.LicensePlate), plateKey, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Es existiert bereits ein Zugfahrzeug mit diesem Kennzeichen.");
        }
    }

    private void ValidateCombinationResult(VehicleCombinationEditorResult result, string id)
    {
        var vehicleId = (result.VehicleId ?? string.Empty).Trim();
        var trailerId = (result.TrailerId ?? string.Empty).Trim();

        if (_combinations.Any(x =>
                !string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.VehicleId, vehicleId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.TrailerId, trailerId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Diese Fahrzeugkombination ist bereits vorhanden.");
        }
    }

    private static string NormalizeLicensePlateKey(string? value)
    {
        return (value ?? string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();
    }

    public async Task DeleteEntryAsync(FleetEntryCardItem entry)
    {
        if (entry.IsCombination)
        {
            _combinations.RemoveAll(x => string.Equals(x.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        }
        else if (entry.IsTrailer)
        {
            _trailers.RemoveAll(x => string.Equals(x.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            _vehicles.RemoveAll(x => string.Equals(x.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
            _combinations.RemoveAll(x => string.Equals(x.VehicleId, entry.Id, StringComparison.OrdinalIgnoreCase));
        }

        if (entry.IsTrailer)
        {
            _combinations.RemoveAll(x => string.Equals(x.TrailerId, entry.Id, StringComparison.OrdinalIgnoreCase));
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
        _combinations.Clear();
        _combinations.AddRange(payload.VehicleCombinations);
        RebuildEntries();
        RaiseCommandStates();
    }

    private async Task SaveCurrentStateAsync()
    {
        await _repository.SaveAsync(new VehicleDataRecord
        {
            Vehicles = _vehicles.ToList(),
            Trailers = _trailers.ToList(),
            VehicleCombinations = _combinations.ToList()
        });
        _dataSyncService.PublishVehicles(_instanceId);
        await RefreshAsync();
    }

    private void OnDataChanged(object? sender, AppDataChangedEventArgs args)
    {
        if (args.SourceId == _instanceId || !args.Kinds.HasFlag(AppDataKind.Vehicles))
        {
            return;
        }

        _ = RefreshAsync();
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
        OnPropertyChanged(nameof(IsCombinationMode));
        RebuildEntries();
        RaiseCommandStates();
    }

    private void RebuildEntries()
    {
        Entries.Clear();

        var vehiclesById = _vehicles.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var trailersById = _trailers.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

        var today = DateOnly.FromDateTime(DateTime.Today);
        if (_displayMode == FleetDisplayMode.Vehicles)
        {
            foreach (var vehicle in _vehicles
                         .OrderByDescending(x => ResourceAvailabilityService.IsUnavailableOnDate(x.UnavailabilityPeriods, today))
                         .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var isUnavailableToday = ResourceAvailabilityService.IsUnavailableOnDate(vehicle.UnavailabilityPeriods, today);
                Entries.Add(new FleetEntryCardItem
                {
                    Id = vehicle.Id,
                    IsTrailer = false,
                    IsCombination = false,
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
                    Active = vehicle.Active,
                    IsUnavailableToday = isUnavailableToday,
                    NextUnavailabilityText = BuildNextUnavailabilityText(vehicle.UnavailabilityPeriods, today)
                });
            }
        }
        else if (_displayMode == FleetDisplayMode.Trailers)
        {
            foreach (var trailer in _trailers
                         .OrderByDescending(x => ResourceAvailabilityService.IsUnavailableOnDate(x.UnavailabilityPeriods, today))
                         .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var isUnavailableToday = ResourceAvailabilityService.IsUnavailableOnDate(trailer.UnavailabilityPeriods, today);
                Entries.Add(new FleetEntryCardItem
                {
                    Id = trailer.Id,
                    IsTrailer = true,
                    IsCombination = false,
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
                    Active = trailer.Active,
                    IsUnavailableToday = isUnavailableToday,
                    NextUnavailabilityText = BuildNextUnavailabilityText(trailer.UnavailabilityPeriods, today)
                });
            }
        }
        else
        {
            foreach (var combination in _combinations.OrderBy(x => !x.Active).ThenBy(x => ResolveVehicleName(vehiclesById, x.VehicleId), StringComparer.OrdinalIgnoreCase).ThenBy(x => ResolveTrailerName(trailersById, x.TrailerId), StringComparer.OrdinalIgnoreCase))
            {
                var vehicleName = ResolveVehicleName(vehiclesById, combination.VehicleId);
                var trailerName = ResolveTrailerName(trailersById, combination.TrailerId);
                Entries.Add(new FleetEntryCardItem
                {
                    Id = combination.Id,
                    IsCombination = true,
                    VehicleId = combination.VehicleId,
                    TrailerId = combination.TrailerId,
                    Name = $"{vehicleName} + {trailerName}",
                    VehicleName = vehicleName,
                    TrailerName = trailerName,
                    CombinationVehiclePayloadKg = combination.VehiclePayloadKg,
                    CombinationTrailerLoadKg = combination.TrailerLoadKg,
                    Notes = combination.Notes,
                    Active = combination.Active,
                    IsUnavailableToday = false,
                    NextUnavailabilityText = "Keine Ausfallplanung"
                });
            }
        }

        var unavailableVehicles = _vehicles.Count(x => ResourceAvailabilityService.IsUnavailableOnDate(x.UnavailabilityPeriods, today));
        var unavailableTrailers = _trailers.Count(x => ResourceAvailabilityService.IsUnavailableOnDate(x.UnavailabilityPeriods, today));
        StatusText = $"Zugfahrzeuge: {_vehicles.Count} (heute Ausfall {unavailableVehicles}) | Anhänger: {_trailers.Count} (heute Ausfall {unavailableTrailers}) | Kombinationen: {_combinations.Count}";
        ModeCountText = _displayMode switch
        {
            FleetDisplayMode.Vehicles => $"Zugfahrzeuge: {Entries.Count}",
            FleetDisplayMode.Trailers => $"Anhänger: {Entries.Count}",
            FleetDisplayMode.Combinations => $"Fahrzeugkombinationen: {Entries.Count}",
            _ => string.Empty
        };

        OnPropertyChanged(nameof(IsVehicleMode));
        OnPropertyChanged(nameof(IsTrailerMode));
        OnPropertyChanged(nameof(IsCombinationMode));
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

    private static string BuildVehicleLabel(Vehicle vehicle)
    {
        return string.IsNullOrWhiteSpace(vehicle.LicensePlate)
            ? vehicle.Name
            : $"{vehicle.Name} [{vehicle.LicensePlate}]";
    }

    private static string BuildTrailerLabel(TrailerRecord trailer)
    {
        return string.IsNullOrWhiteSpace(trailer.LicensePlate)
            ? trailer.Name
            : $"{trailer.Name} [{trailer.LicensePlate}]";
    }

    private static string ResolveVehicleName(IReadOnlyDictionary<string, Vehicle> vehiclesById, string vehicleId)
    {
        return vehiclesById.TryGetValue(vehicleId, out var vehicle) && !string.IsNullOrWhiteSpace(vehicle.Name)
            ? vehicle.Name
            : vehicleId;
    }

    private static string ResolveTrailerName(IReadOnlyDictionary<string, TrailerRecord> trailersById, string trailerId)
    {
        return trailersById.TryGetValue(trailerId, out var trailer) && !string.IsNullOrWhiteSpace(trailer.Name)
            ? trailer.Name
            : trailerId;
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

        if (ShowCombinationsCommand is DelegateCommand showCombinations)
        {
            showCombinations.RaiseCanExecuteChanged();
        }
    }

    private static void AppendOutagePeriod(List<ResourceUnavailabilityPeriod> periods, string? startRaw, string? endRaw)
    {
        var start = ResourceAvailabilityService.ParseDate(startRaw);
        var end = ResourceAvailabilityService.ParseDate(endRaw);
        if (!start.HasValue || !end.HasValue)
        {
            return;
        }

        var from = start.Value <= end.Value ? start.Value : end.Value;
        var to = start.Value <= end.Value ? end.Value : start.Value;
        periods.Add(new ResourceUnavailabilityPeriod
        {
            StartDate = from.ToString("yyyy-MM-dd"),
            EndDate = to.ToString("yyyy-MM-dd")
        });
    }

    private async Task<string?> BuildOutageAssignmentWarningAsync(
        string resourceId,
        string resourceName,
        string? startRaw,
        string? endRaw,
        bool isTrailer)
    {
        var start = ResourceAvailabilityService.ParseDate(startRaw);
        var end = ResourceAvailabilityService.ParseDate(endRaw);
        if (!start.HasValue || !end.HasValue)
        {
            return null;
        }

        var from = start.Value <= end.Value ? start.Value : end.Value;
        var to = start.Value <= end.Value ? end.Value : start.Value;

        var tours = await _tourRepository.LoadAsync();
        var affected = tours
            .Where(t =>
            {
                var date = ResourceAvailabilityService.ParseDate(t.Date);
                if (!date.HasValue || date.Value < from || date.Value > to)
                {
                    return false;
                }

                return isTrailer
                    ? string.Equals(t.TrailerId, resourceId, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(t.SecondaryTrailerId, resourceId, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(t.VehicleId, resourceId, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(t.SecondaryVehicleId, resourceId, StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(t => ResourceAvailabilityService.ParseDate(t.Date))
            .ThenBy(t => t.StartTime, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (affected.Count == 0)
        {
            return null;
        }

        var lines = affected
            .Take(6)
            .Select(t => $"{t.Date} {t.StartTime} - {t.Name}")
            .ToList();
        if (affected.Count > lines.Count)
        {
            lines.Add($"... und {affected.Count - lines.Count} weitere Tour(en).");
        }

        var label = isTrailer ? "Anhänger" : "Fahrzeug";
        return $"Achtung: {label} \"{resourceName}\" ist im gewählten Ausfallzeitraum bereits in Touren eingeplant:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
    }

    private static string BuildNextUnavailabilityText(IEnumerable<ResourceUnavailabilityPeriod>? periods, DateOnly today)
    {
        var upcoming = (periods ?? [])
            .Select(x => new
            {
                Start = ResourceAvailabilityService.ParseDate(x.StartDate),
                End = ResourceAvailabilityService.ParseDate(x.EndDate)
            })
            .Where(x => x.Start.HasValue && x.End.HasValue)
            .Select(x => new
            {
                Start = x.Start!.Value <= x.End!.Value ? x.Start.Value : x.End.Value,
                End = x.Start!.Value <= x.End!.Value ? x.End.Value : x.Start.Value
            })
            .Where(x => x.End >= today)
            .OrderBy(x => x.Start)
            .FirstOrDefault();

        if (upcoming is null)
        {
            return "Kein Ausfall geplant";
        }

        return $"Ausfall: {upcoming.Start:dd.MM.yyyy} - {upcoming.End:dd.MM.yyyy}";
    }

    private static (DateOnly StartDate, DateOnly EndDate)? GetEditablePeriod(IEnumerable<ResourceUnavailabilityPeriod>? periods)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var upcoming = (periods ?? [])
            .Select(x => new
            {
                Start = ResourceAvailabilityService.ParseDate(x.StartDate),
                End = ResourceAvailabilityService.ParseDate(x.EndDate)
            })
            .Where(x => x.Start.HasValue && x.End.HasValue)
            .Select(x => new
            {
                Start = x.Start!.Value <= x.End!.Value ? x.Start.Value : x.End.Value,
                End = x.Start!.Value <= x.End!.Value ? x.End.Value : x.Start.Value
            })
            .Where(x => x.End >= today)
            .OrderBy(x => x.Start)
            .FirstOrDefault();

        return upcoming is null ? null : (upcoming.Start, upcoming.End);
    }
}

public sealed class FleetEntryCardItem : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public bool IsTrailer { get; set; }
    public bool IsCombination { get; set; }
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
    public bool IsUnavailableToday { get; set; }
    public string NextUnavailabilityText { get; set; } = string.Empty;
    public string VehicleId { get; set; } = string.Empty;
    public string TrailerId { get; set; } = string.Empty;
    public string VehicleName { get; set; } = string.Empty;
    public string TrailerName { get; set; } = string.Empty;
    public int CombinationVehiclePayloadKg { get; set; }
    public int CombinationTrailerLoadKg { get; set; }

    public string AvailabilityLabel => IsUnavailableToday ? "Ausfall" : "Verfügbar";

    public string HeaderTypeLabel => IsCombination ? "Fahrzeugkombination" : (IsTrailer ? "Anhänger" : "Zugfahrzeug");

    public string DetailsLine
    {
        get
        {
            var parts = new List<string>();
            if (IsCombination)
            {
                parts.Add($"Zugfahrzeug: {VehicleName}");
                parts.Add($"Anhänger: {TrailerName}");

                if (CombinationVehiclePayloadKg > 0)
                {
                    parts.Add($"Ladegewicht Zugfahrzeug: {CombinationVehiclePayloadKg} kg");
                }

                if (CombinationTrailerLoadKg > 0)
                {
                    parts.Add($"Anhängelast: {CombinationTrailerLoadKg} kg");
                }

                return string.Join(" | ", parts);
            }

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

            if (!string.IsNullOrWhiteSpace(NextUnavailabilityText))
            {
                parts.Add(NextUnavailabilityText);
            }

            return string.Join(" | ", parts);
        }
    }
}

public enum FleetDisplayMode
{
    Vehicles,
    Trailers,
    Combinations
}

public sealed record VehicleCombinationOption(string Id, string Label)
{
    public override string ToString()
    {
        return Label;
    }
}
