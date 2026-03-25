using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public static class TourCapacityWarningService
{
    public static TourCapacityWarningResult Evaluate(
        VehicleDataRecord? vehicleData,
        string? vehicleId,
        string? trailerId,
        int totalWeightKg)
    {
        var payload = vehicleData ?? new VehicleDataRecord();
        var normalizedVehicleId = NormalizeId(vehicleId);
        var normalizedTrailerId = NormalizeId(trailerId);

        var vehicle = payload.Vehicles
            .FirstOrDefault(x => string.Equals(x.Id, normalizedVehicleId, StringComparison.OrdinalIgnoreCase));
        var trailerSelected = !string.IsNullOrWhiteSpace(normalizedTrailerId);
        var combination = payload.VehicleCombinations
            .FirstOrDefault(x =>
                string.Equals(x.VehicleId, normalizedVehicleId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.TrailerId, normalizedTrailerId, StringComparison.OrdinalIgnoreCase));

        var vehiclePayloadKg = combination?.VehiclePayloadKg ?? vehicle?.MaxPayloadKg;
        int? trailerLoadKg = null;
        if (trailerSelected)
        {
            trailerLoadKg = combination?.TrailerLoadKg ?? vehicle?.MaxTrailerLoadKg;
        }

        var allowedWeightKg = vehiclePayloadKg.GetValueOrDefault();
        if (trailerLoadKg.HasValue)
        {
            allowedWeightKg += trailerLoadKg.Value;
        }

        if (!vehiclePayloadKg.HasValue && !trailerLoadKg.HasValue)
        {
            return new TourCapacityWarningResult(false, totalWeightKg, null, null, null);
        }

        return new TourCapacityWarningResult(
            IsOverCapacity: totalWeightKg > allowedWeightKg,
            TotalWeightKg: totalWeightKg,
            VehiclePayloadKg: vehiclePayloadKg,
            TrailerLoadKg: trailerLoadKg,
            AllowedWeightKg: allowedWeightKg);
    }

    private static string NormalizeId(string? value)
    {
        return (value ?? string.Empty).Trim();
    }
}

public sealed record TourCapacityWarningResult(
    bool IsOverCapacity,
    int TotalWeightKg,
    int? VehiclePayloadKg,
    int? TrailerLoadKg,
    int? AllowedWeightKg)
{
    public string BuildWarningMessage()
    {
        var lines = new List<string>
        {
            "Das Totalgewicht dieser Tour überschreitet die zulässige Kapazität.",
            string.Empty,
            $"Totalgewicht: {TotalWeightKg} kg"
        };

        if (VehiclePayloadKg.HasValue)
        {
            lines.Add($"Ladegewicht: {VehiclePayloadKg.Value} kg");
        }

        if (TrailerLoadKg.HasValue)
        {
            lines.Add($"Anhängelast: {TrailerLoadKg.Value} kg");
        }

        if (AllowedWeightKg.HasValue)
        {
            lines.Add($"Zulässige Gesamtkapazität: {AllowedWeightKg.Value} kg");
            lines.Add($"Überladung: {Math.Max(0, TotalWeightKg - AllowedWeightKg.Value)} kg");
        }

        lines.Add(string.Empty);
        lines.Add("Möchtest du trotzdem speichern?");
        return string.Join(Environment.NewLine, lines);
    }
}
