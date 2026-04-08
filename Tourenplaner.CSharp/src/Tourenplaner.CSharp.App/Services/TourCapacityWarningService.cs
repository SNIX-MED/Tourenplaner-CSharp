using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public static class TourCapacityWarningService
{
    public static TourFleetCapacityWarningResult EvaluateFleet(
        VehicleDataRecord? vehicleData,
        IReadOnlyList<(string VehicleId, string TrailerId)> assignments,
        int totalWeightKg)
    {
        var normalizedAssignments = (assignments ?? [])
            .Select(x => (NormalizeId(x.VehicleId), NormalizeId(x.TrailerId)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Item1) || !string.IsNullOrWhiteSpace(x.Item2))
            .GroupBy(x => $"{x.Item1}|{x.Item2}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var units = new List<TourFleetCapacityUnit>();
        foreach (var assignment in normalizedAssignments)
        {
            var warning = Evaluate(vehicleData, assignment.Item1, assignment.Item2, totalWeightKg);
            units.Add(new TourFleetCapacityUnit(
                assignment.Item1,
                assignment.Item2,
                warning.VehiclePayloadKg,
                warning.TrailerLoadKg,
                warning.AllowedWeightKg));
        }

        var hasKnownCapacity = units.Any(x => x.AllowedWeightKg.HasValue);
        if (!hasKnownCapacity)
        {
            return new TourFleetCapacityWarningResult(false, totalWeightKg, null, units);
        }

        var allowedWeightKg = units
            .Where(x => x.AllowedWeightKg.HasValue)
            .Sum(x => x.AllowedWeightKg!.Value);

        return new TourFleetCapacityWarningResult(
            IsOverCapacity: totalWeightKg > allowedWeightKg,
            TotalWeightKg: totalWeightKg,
            AllowedWeightKg: allowedWeightKg,
            Units: units);
    }

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

public sealed record TourFleetCapacityUnit(
    string VehicleId,
    string TrailerId,
    int? VehiclePayloadKg,
    int? TrailerLoadKg,
    int? AllowedWeightKg)
{
    public string BuildLabel()
    {
        if (!string.IsNullOrWhiteSpace(VehicleId) && !string.IsNullOrWhiteSpace(TrailerId))
        {
            return $"{VehicleId} + {TrailerId}";
        }

        if (!string.IsNullOrWhiteSpace(VehicleId))
        {
            return VehicleId;
        }

        if (!string.IsNullOrWhiteSpace(TrailerId))
        {
            return TrailerId;
        }

        return "(unbekannt)";
    }
}

public sealed record TourFleetCapacityWarningResult(
    bool IsOverCapacity,
    int TotalWeightKg,
    int? AllowedWeightKg,
    IReadOnlyList<TourFleetCapacityUnit> Units)
{
    public string BuildWarningMessage()
    {
        var lines = new List<string>
        {
            "Das Totalgewicht dieser Tour überschreitet die zulässige Kapazität.",
            string.Empty,
            $"Totalgewicht: {TotalWeightKg} kg"
        };

        if (AllowedWeightKg.HasValue)
        {
            lines.Add($"Zulässige Gesamtkapazität: {AllowedWeightKg.Value} kg");
            lines.Add($"Überladung: {Math.Max(0, TotalWeightKg - AllowedWeightKg.Value)} kg");
        }

        if (Units.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Fahrzeugzuordnungen:");
            foreach (var unit in Units)
            {
                var allowed = unit.AllowedWeightKg.HasValue ? $"{unit.AllowedWeightKg.Value} kg" : "unbekannt";
                lines.Add($"- {unit.BuildLabel()}: {allowed}");
            }
        }

        lines.Add(string.Empty);
        lines.Add("Möchtest du trotzdem speichern?");
        return string.Join(Environment.NewLine, lines);
    }
}
