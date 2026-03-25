using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public static class VehicleCombinationDisplayResolver
{
    public static VehicleCombinationDisplayInfo Resolve(VehicleDataRecord? vehicleData, string? vehicleId, string? trailerId)
    {
        var payload = vehicleData ?? new VehicleDataRecord();
        var normalizedVehicleId = NormalizeId(vehicleId);
        var normalizedTrailerId = NormalizeId(trailerId);

        var vehicle = payload.Vehicles
            .FirstOrDefault(x => string.Equals(x.Id, normalizedVehicleId, StringComparison.OrdinalIgnoreCase));
        var trailer = payload.Trailers
            .FirstOrDefault(x => string.Equals(x.Id, normalizedTrailerId, StringComparison.OrdinalIgnoreCase));

        var vehicleLabel = vehicle is null ? string.Empty : BuildVehicleLabel(vehicle);
        var trailerLabel = trailer is null ? string.Empty : BuildTrailerLabel(trailer);

        var combination = payload.VehicleCombinations
            .FirstOrDefault(x =>
                string.Equals(x.VehicleId, normalizedVehicleId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.TrailerId, normalizedTrailerId, StringComparison.OrdinalIgnoreCase));

        return new VehicleCombinationDisplayInfo(
            VehicleLabel: vehicleLabel,
            TrailerLabel: trailerLabel,
            VehiclePayloadKg: combination?.VehiclePayloadKg ?? (vehicle is null ? null : vehicle.MaxPayloadKg),
            TrailerLoadKg: combination?.TrailerLoadKg);
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

    private static string NormalizeId(string? value)
    {
        return (value ?? string.Empty).Trim();
    }
}

public sealed record VehicleCombinationDisplayInfo(
    string VehicleLabel,
    string TrailerLabel,
    int? VehiclePayloadKg,
    int? TrailerLoadKg)
{
    public bool HasCombination => VehiclePayloadKg.HasValue && TrailerLoadKg.HasValue;
    public bool HasVehiclePayload => VehiclePayloadKg.HasValue;
}
