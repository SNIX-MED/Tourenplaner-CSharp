namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed record VehicleEditorSeed(
    string? Id,
    bool IsTrailer,
    string Type,
    string Name,
    string LicensePlate,
    int MaxPayloadKg,
    int MaxTrailerLoadKg,
    int VolumeM3,
    int LengthCm,
    int WidthCm,
    int HeightCm,
    string Notes,
    bool Active);

public sealed record VehicleEditorResult(
    string? Id,
    bool IsTrailer,
    string Type,
    string Name,
    string LicensePlate,
    int MaxPayloadKg,
    int MaxTrailerLoadKg,
    int VolumeM3,
    int LengthCm,
    int WidthCm,
    int HeightCm,
    string Notes,
    bool Active);

public sealed record VehicleCombinationEditorSeed(
    string? Id,
    string VehicleId,
    string TrailerId,
    int VehiclePayloadKg,
    int TrailerLoadKg,
    string Notes,
    bool Active);

public sealed record VehicleCombinationEditorResult(
    string? Id,
    string VehicleId,
    string TrailerId,
    int VehiclePayloadKg,
    int TrailerLoadKg,
    string Notes,
    bool Active);
