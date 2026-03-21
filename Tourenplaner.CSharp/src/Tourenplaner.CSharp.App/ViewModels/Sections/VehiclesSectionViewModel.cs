using System.Collections.ObjectModel;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class VehiclesSectionViewModel : SectionViewModelBase
{
    private readonly JsonVehicleDataRepository _repository;
    private VehicleItem? _selectedVehicle;
    private TrailerItem? _selectedTrailer;
    private string _statusText = "Lade Fahrzeuge...";

    public VehiclesSectionViewModel(string vehiclesJsonPath)
        : base("Vehicles", "Fahrzeuge und Anhänger mit Kapazitäten, Status und Notizen.")
    {
        _repository = new JsonVehicleDataRepository(vehiclesJsonPath);

        RefreshCommand = new AsyncCommand(RefreshAsync);
        SaveCommand = new AsyncCommand(SaveAsync, () => Vehicles.Count > 0 || Trailers.Count > 0);
        AddVehicleCommand = new DelegateCommand(AddVehicle);
        RemoveVehicleCommand = new DelegateCommand(RemoveSelectedVehicle, () => SelectedVehicle is not null);
        AddTrailerCommand = new DelegateCommand(AddTrailer);
        RemoveTrailerCommand = new DelegateCommand(RemoveSelectedTrailer, () => SelectedTrailer is not null);

        _ = RefreshAsync();
    }

    public ObservableCollection<VehicleItem> Vehicles { get; } = new();

    public ObservableCollection<TrailerItem> Trailers { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand AddVehicleCommand { get; }

    public ICommand RemoveVehicleCommand { get; }

    public ICommand AddTrailerCommand { get; }

    public ICommand RemoveTrailerCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public VehicleItem? SelectedVehicle
    {
        get => _selectedVehicle;
        set
        {
            if (SetProperty(ref _selectedVehicle, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public TrailerItem? SelectedTrailer
    {
        get => _selectedTrailer;
        set
        {
            if (SetProperty(ref _selectedTrailer, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public async Task RefreshAsync()
    {
        var payload = await _repository.LoadAsync();
        Vehicles.Clear();
        Trailers.Clear();

        foreach (var vehicle in payload.Vehicles)
        {
            Vehicles.Add(new VehicleItem
            {
                Id = vehicle.Id,
                Type = vehicle.Type,
                Name = vehicle.Name,
                LicensePlate = vehicle.LicensePlate,
                MaxPayloadKg = vehicle.MaxPayloadKg,
                MaxTrailerLoadKg = vehicle.MaxTrailerLoadKg,
                VolumeM3 = vehicle.VolumeM3,
                Active = vehicle.Active,
                Notes = vehicle.Notes
            });
        }

        foreach (var trailer in payload.Trailers)
        {
            Trailers.Add(new TrailerItem
            {
                Id = trailer.Id,
                Name = trailer.Name,
                LicensePlate = trailer.LicensePlate,
                MaxPayloadKg = trailer.MaxPayloadKg,
                VolumeM3 = trailer.VolumeM3,
                Active = trailer.Active,
                Notes = trailer.Notes
            });
        }

        SelectedVehicle = Vehicles.FirstOrDefault();
        SelectedTrailer = Trailers.FirstOrDefault();
        UpdateStatusText();
        RaiseCommandStates();
    }

    public async Task SaveAsync()
    {
        var payload = new VehicleDataRecord
        {
            Vehicles = Vehicles
                .Where(v => !string.IsNullOrWhiteSpace(v.Name))
                .Select(v => new Vehicle
                {
                    Id = string.IsNullOrWhiteSpace(v.Id) ? Guid.NewGuid().ToString() : v.Id.Trim(),
                    Type = (v.Type ?? "other").Trim(),
                    Name = (v.Name ?? string.Empty).Trim(),
                    LicensePlate = (v.LicensePlate ?? string.Empty).Trim(),
                    MaxPayloadKg = Math.Max(0, v.MaxPayloadKg),
                    MaxTrailerLoadKg = Math.Max(0, v.MaxTrailerLoadKg),
                    VolumeM3 = Math.Max(0, v.VolumeM3),
                    Active = v.Active,
                    Notes = (v.Notes ?? string.Empty).Trim()
                })
                .ToList(),
            Trailers = Trailers
                .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                .Select(t => new TrailerRecord
                {
                    Id = string.IsNullOrWhiteSpace(t.Id) ? Guid.NewGuid().ToString() : t.Id.Trim(),
                    Name = (t.Name ?? string.Empty).Trim(),
                    LicensePlate = (t.LicensePlate ?? string.Empty).Trim(),
                    MaxPayloadKg = Math.Max(0, t.MaxPayloadKg),
                    VolumeM3 = Math.Max(0, t.VolumeM3),
                    Active = t.Active,
                    Notes = (t.Notes ?? string.Empty).Trim()
                })
                .ToList()
        };

        await _repository.SaveAsync(payload);
        await RefreshAsync();
    }

    private void AddVehicle()
    {
        var item = new VehicleItem
        {
            Id = Guid.NewGuid().ToString(),
            Type = "truck",
            Name = "Neues Fahrzeug",
            Active = true
        };

        Vehicles.Add(item);
        SelectedVehicle = item;
        UpdateStatusText();
        RaiseCommandStates();
    }

    private void RemoveSelectedVehicle()
    {
        if (SelectedVehicle is null)
        {
            return;
        }

        Vehicles.Remove(SelectedVehicle);
        SelectedVehicle = Vehicles.FirstOrDefault();
        UpdateStatusText();
        RaiseCommandStates();
    }

    private void AddTrailer()
    {
        var item = new TrailerItem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Neuer Anhänger",
            Active = true
        };

        Trailers.Add(item);
        SelectedTrailer = item;
        UpdateStatusText();
        RaiseCommandStates();
    }

    private void RemoveSelectedTrailer()
    {
        if (SelectedTrailer is null)
        {
            return;
        }

        Trailers.Remove(SelectedTrailer);
        SelectedTrailer = Trailers.FirstOrDefault();
        UpdateStatusText();
        RaiseCommandStates();
    }

    private void UpdateStatusText()
    {
        var activeVehicles = Vehicles.Count(v => v.Active);
        var activeTrailers = Trailers.Count(t => t.Active);
        StatusText = $"Fahrzeuge: {Vehicles.Count} (aktiv {activeVehicles}) | Anhänger: {Trailers.Count} (aktiv {activeTrailers})";
    }

    private void RaiseCommandStates()
    {
        if (SaveCommand is AsyncCommand save)
        {
            save.RaiseCanExecuteChanged();
        }

        if (RemoveVehicleCommand is DelegateCommand removeVehicle)
        {
            removeVehicle.RaiseCanExecuteChanged();
        }

        if (RemoveTrailerCommand is DelegateCommand removeTrailer)
        {
            removeTrailer.RaiseCanExecuteChanged();
        }
    }
}

public sealed class VehicleItem : ObservableObject
{
    private string _id = string.Empty;
    private string _type = "other";
    private string _name = string.Empty;
    private string _licensePlate = string.Empty;
    private int _maxPayloadKg;
    private int _maxTrailerLoadKg;
    private int _volumeM3;
    private bool _active = true;
    private string _notes = string.Empty;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Type
    {
        get => _type;
        set => SetProperty(ref _type, value);
    }

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

    public int MaxPayloadKg
    {
        get => _maxPayloadKg;
        set => SetProperty(ref _maxPayloadKg, value);
    }

    public int MaxTrailerLoadKg
    {
        get => _maxTrailerLoadKg;
        set => SetProperty(ref _maxTrailerLoadKg, value);
    }

    public int VolumeM3
    {
        get => _volumeM3;
        set => SetProperty(ref _volumeM3, value);
    }

    public bool Active
    {
        get => _active;
        set => SetProperty(ref _active, value);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }
}

public sealed class TrailerItem : ObservableObject
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string _licensePlate = string.Empty;
    private int _maxPayloadKg;
    private int _volumeM3;
    private bool _active = true;
    private string _notes = string.Empty;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

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

    public int MaxPayloadKg
    {
        get => _maxPayloadKg;
        set => SetProperty(ref _maxPayloadKg, value);
    }

    public int VolumeM3
    {
        get => _volumeM3;
        set => SetProperty(ref _volumeM3, value);
    }

    public bool Active
    {
        get => _active;
        set => SetProperty(ref _active, value);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }
}
