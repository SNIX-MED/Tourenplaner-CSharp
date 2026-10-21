namespace Tourenplaner.CSharp.Application.Common;

public sealed record MapRouteStop(
    int Position,
    string OrderId,
    string Customer,
    string Address,
    double Latitude,
    double Longitude,
    int ServiceMinutes = 10,
    string EmployeeInfoText = "",
    string StopKind = "",
    string DeliveryId = "",
    int? DeliverySequenceNumber = null, string ManualAvisoStatus = "Keine Avisierung nötig", string ManualWindowStart = "", string ManualWindowEnd = "");
