namespace Tourenplaner.CSharp.Application.Common;

public sealed record AppSnapshot(
    DateTimeOffset CreatedAt,
    int OrderCount,
    int NonMapOrderCount,
    int TourCount,
    int EmployeeCount,
    int VehicleCount);
