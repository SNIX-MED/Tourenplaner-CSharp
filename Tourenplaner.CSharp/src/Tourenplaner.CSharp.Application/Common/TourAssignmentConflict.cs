namespace Tourenplaner.CSharp.Application.Common;

public sealed record TourAssignmentConflict(
    string ResourceType,
    string ResourceId,
    int TourIdA,
    int TourIdB,
    DateTime StartA,
    DateTime EndA,
    DateTime StartB,
    DateTime EndB,
    string Message);
