using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Common;

public static class ArrivalWindowFixation
{
    public static bool IsFixedStatus(string? status) =>
        string.Equals(status?.Trim(), "Informiert", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status?.Trim(), "Bestätigt", StringComparison.OrdinalIgnoreCase);

    public static void Apply(TourStopRecord stop, bool shouldFix, string start, string end)
    {
        if (shouldFix && !stop.IsArrivalWindowFixed)
        {
            stop.PlannedArrivalOptimistic = start;
            stop.PlannedArrivalPessimistic = end;
            stop.ArrivalWindowStart = null;
            stop.ArrivalWindowEnd = null;
        }
        stop.IsArrivalWindowFixed = shouldFix;
    }
}
