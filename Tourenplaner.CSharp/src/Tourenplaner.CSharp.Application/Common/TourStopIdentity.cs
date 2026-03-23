using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Common;

public static class TourStopIdentity
{
    public const string CompanyStartStopId = "company:start";
    public const string CompanyEndStopId = "company:end";
    public const string CompanyStartOrderNumber = "FIRMA-START";
    public const string CompanyEndOrderNumber = "FIRMA-ENDE";

    public static bool IsCompanyStop(TourStopRecord stop)
    {
        return IsCompanyStop(stop.Id, stop.Auftragsnummer);
    }

    public static bool IsCompanyStop(string? stopId, string? orderNumber)
    {
        var id = (stopId ?? string.Empty).Trim();
        var order = (orderNumber ?? string.Empty).Trim();
        return string.Equals(id, CompanyStartStopId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(id, CompanyEndStopId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(order, CompanyStartOrderNumber, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(order, CompanyEndOrderNumber, StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeCompanyStopDisplayName(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.EndsWith("(Start)", StringComparison.OrdinalIgnoreCase))
        {
            return text[..^"(Start)".Length].Trim();
        }

        if (text.EndsWith("(Ende)", StringComparison.OrdinalIgnoreCase))
        {
            return text[..^"(Ende)".Length].Trim();
        }

        return text;
    }
}
