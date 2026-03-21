using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

internal static class VehicleNormalizer
{
    private static readonly HashSet<string> ValidTypes = ["truck", "van", "car", "other"];

    public static Vehicle NormalizeVehicle(Vehicle source)
    {
        source.Id = string.IsNullOrWhiteSpace(source.Id) ? Guid.NewGuid().ToString() : source.Id.Trim();
        source.Type = NormalizeType(source.Type);
        source.Name = (source.Name ?? string.Empty).Trim();
        source.LicensePlate = (source.LicensePlate ?? string.Empty).Trim().ToUpperInvariant();
        source.MaxPayloadKg = NonNegative(source.MaxPayloadKg, "Nutzlast");
        source.MaxTrailerLoadKg = NonNegative(source.MaxTrailerLoadKg, "Anhaengelast");
        source.VolumeM3 = NonNegative(source.VolumeM3, "Volumen");
        source.LoadingArea = NormalizeDimensions(source.LoadingArea);
        source.CreatedAt = string.IsNullOrWhiteSpace(source.CreatedAt) ? Timestamp() : source.CreatedAt.Trim();
        source.UpdatedAt = Timestamp();
        source.Notes = (source.Notes ?? string.Empty).Trim();
        return source;
    }

    public static TrailerRecord NormalizeTrailer(TrailerRecord source)
    {
        source.Id = string.IsNullOrWhiteSpace(source.Id) ? Guid.NewGuid().ToString() : source.Id.Trim();
        source.Name = (source.Name ?? string.Empty).Trim();
        source.LicensePlate = (source.LicensePlate ?? string.Empty).Trim().ToUpperInvariant();
        source.MaxPayloadKg = NonNegative(source.MaxPayloadKg, "Nutzlast");
        source.VolumeM3 = NonNegative(source.VolumeM3, "Volumen");
        source.LoadingArea = NormalizeDimensions(source.LoadingArea);
        source.CreatedAt = string.IsNullOrWhiteSpace(source.CreatedAt) ? Timestamp() : source.CreatedAt.Trim();
        source.UpdatedAt = Timestamp();
        source.Notes = (source.Notes ?? string.Empty).Trim();
        return source;
    }

    public static VehicleDataRecord NormalizePayload(VehicleDataRecord payload)
    {
        var normalized = new VehicleDataRecord();
        var vehicleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var vehicleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var vehiclePlates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in payload.Vehicles ?? new List<Vehicle>())
        {
            var item = NormalizeVehicle(raw);
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                continue;
            }

            if (!vehicleIds.Add(item.Id))
            {
                item.Id = Guid.NewGuid().ToString();
                vehicleIds.Add(item.Id);
            }

            var plateKey = (item.LicensePlate ?? string.Empty).Replace(" ", string.Empty);
            if (!vehicleNames.Add(item.Name) || (!string.IsNullOrWhiteSpace(plateKey) && !vehiclePlates.Add(plateKey)))
            {
                continue;
            }

            normalized.Vehicles.Add(item);
        }

        var trailerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trailerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trailerPlates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in payload.Trailers ?? new List<TrailerRecord>())
        {
            var item = NormalizeTrailer(raw);
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                continue;
            }

            if (!trailerIds.Add(item.Id))
            {
                item.Id = Guid.NewGuid().ToString();
                trailerIds.Add(item.Id);
            }

            var plateKey = (item.LicensePlate ?? string.Empty).Replace(" ", string.Empty);
            if (!trailerNames.Add(item.Name) || (!string.IsNullOrWhiteSpace(plateKey) && !trailerPlates.Add(plateKey)))
            {
                continue;
            }

            normalized.Trailers.Add(item);
        }

        normalized.Vehicles = normalized.Vehicles
            .OrderBy(v => !v.Active)
            .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        normalized.Trailers = normalized.Trailers
            .OrderBy(v => !v.Active)
            .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalized;
    }

    private static string NormalizeType(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return ValidTypes.Contains(normalized) ? normalized : "other";
    }

    private static VehicleDimensions? NormalizeDimensions(VehicleDimensions? value)
    {
        if (value is null)
        {
            return null;
        }

        value.LengthCm = NonNegative(value.LengthCm, "Ladeflaeche Laenge");
        value.WidthCm = NonNegative(value.WidthCm, "Ladeflaeche Breite");
        value.HeightCm = NonNegative(value.HeightCm, "Ladeflaeche Hoehe");

        if (value.LengthCm == 0 && value.WidthCm == 0 && value.HeightCm == 0)
        {
            return null;
        }

        return value;
    }

    private static int NonNegative(int value, string fieldName)
    {
        if (value < 0)
        {
            throw new ArgumentException($"{fieldName} darf nicht negativ sein.");
        }

        return value;
    }

    private static string Timestamp() => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
}
