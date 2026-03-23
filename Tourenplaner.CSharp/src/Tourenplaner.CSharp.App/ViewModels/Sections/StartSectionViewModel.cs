using System.Collections.ObjectModel;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class StartSectionViewModel : SectionViewModelBase
{
    private readonly AppSnapshotService _snapshotService;
    private readonly JsonVehicleDataRepository _vehicleRepository;
    private string _snapshot = "Loading snapshot...";
    private string _kpiOrders = "-";
    private string _kpiTours = "-";
    private string _kpiEmployees = "-";
    private string _kpiVehicles = "-";
    private string _fleetSubtitle = "Keine Fahrzeuge erfasst.";

    public StartSectionViewModel(AppSnapshotService snapshotService, string vehiclesJsonPath)
        : base("Start", "Operational cockpit with live planning indicators.")
    {
        _snapshotService = snapshotService;
        _vehicleRepository = new JsonVehicleDataRepository(vehiclesJsonPath);

        WeeklyBars =
        [
            new MetricPoint("Mon", 52),
            new MetricPoint("Tue", 61),
            new MetricPoint("Wed", 48),
            new MetricPoint("Thu", 74),
            new MetricPoint("Fri", 67),
            new MetricPoint("Sat", 40),
            new MetricPoint("Sun", 33)
        ];
    }

    public ObservableCollection<FleetVehicleOverviewItem> FleetVehicles { get; } = new();

    public string Snapshot
    {
        get => _snapshot;
        private set => SetProperty(ref _snapshot, value);
    }

    public string KpiOrders
    {
        get => _kpiOrders;
        private set => SetProperty(ref _kpiOrders, value);
    }

    public string KpiTours
    {
        get => _kpiTours;
        private set => SetProperty(ref _kpiTours, value);
    }

    public string KpiEmployees
    {
        get => _kpiEmployees;
        private set => SetProperty(ref _kpiEmployees, value);
    }

    public string KpiVehicles
    {
        get => _kpiVehicles;
        private set => SetProperty(ref _kpiVehicles, value);
    }

    public string FleetSubtitle
    {
        get => _fleetSubtitle;
        private set => SetProperty(ref _fleetSubtitle, value);
    }

    public ObservableCollection<MetricPoint> WeeklyBars { get; }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var snapshotTask = _snapshotService.CreateAsync(cancellationToken);
        var vehiclesTask = _vehicleRepository.LoadAsync(cancellationToken);
        await Task.WhenAll(snapshotTask, vehiclesTask);

        AppSnapshot value = await snapshotTask;
        KpiOrders = value.OrderCount.ToString();
        KpiTours = value.TourCount.ToString();
        KpiEmployees = value.EmployeeCount.ToString();
        KpiVehicles = value.VehicleCount.ToString();

        Snapshot =
            $"Orders: {value.OrderCount} (Non-Map: {value.NonMapOrderCount}) | Tours: {value.TourCount} | Employees: {value.EmployeeCount} | Vehicles: {value.VehicleCount}";

        var payload = await vehiclesTask;
        FleetVehicles.Clear();
        foreach (var vehicle in payload.Vehicles
                     .Where(v => v.Active)
                     .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                     .Take(6))
        {
            FleetVehicles.Add(new FleetVehicleOverviewItem
            {
                Name = vehicle.Name,
                Meta = BuildMeta(vehicle.LicensePlate, vehicle.MaxPayloadKg, vehicle.VolumeM3),
                Kind = "Zugfahrzeug"
            });
        }

        foreach (var trailer in payload.Trailers
                     .Where(t => t.Active)
                     .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                     .Take(6))
        {
            FleetVehicles.Add(new FleetVehicleOverviewItem
            {
                Name = trailer.Name,
                Meta = BuildMeta(trailer.LicensePlate, trailer.MaxPayloadKg, trailer.VolumeM3),
                Kind = "Anhänger"
            });
        }

        FleetSubtitle = FleetVehicles.Count == 0
            ? "Keine aktiven Fahrzeuge verfügbar."
            : $"{FleetVehicles.Count} aktive Fahrzeuge/Anhänger";
    }

    private static string BuildMeta(string? licensePlate, int payloadKg, int volumeM3)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(licensePlate))
        {
            parts.Add(licensePlate.Trim());
        }

        if (payloadKg > 0)
        {
            parts.Add($"{payloadKg} kg");
        }

        if (volumeM3 > 0)
        {
            parts.Add($"{volumeM3} m3");
        }

        return string.Join(" | ", parts);
    }
}

public sealed record MetricPoint(string Label, double Value);

public sealed class FleetVehicleOverviewItem
{
    public string Name { get; set; } = string.Empty;
    public string Meta { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
}
