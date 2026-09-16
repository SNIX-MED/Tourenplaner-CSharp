using System.Globalization;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public sealed class WebfleetTourDispatchService
{
    public IReadOnlyList<string> Validate(TourRecord tour, Vehicle? vehicle)
    {
        var errors = new List<string>();
        if (tour is null) return ["Die Tour fehlt."];
        if (vehicle is null || string.IsNullOrWhiteSpace(vehicle.WebfleetObjectUid)) errors.Add("Dem Tourfahrzeug ist noch kein WEBFLEET-Fahrzeug zugeordnet.");
        foreach (var stop in tour.Stops.Where(x => !string.Equals(x.StopKind, "company", StringComparison.OrdinalIgnoreCase) && !string.Equals(x.StopKind, "pause", StringComparison.OrdinalIgnoreCase)))
        {
            if (stop.Lat is null || (stop.Lon ?? stop.Lng) is null) errors.Add($"Stopp {stop.Order}: Koordinaten fehlen.");
            if (string.IsNullOrWhiteSpace(stop.Auftragsnummer)) errors.Add($"Stopp {stop.Order}: Auftragsnummer fehlt.");
        }
        return errors;
    }

    public async Task<WebfleetDispatchRecord> DispatchAsync(TourRecord tour, Vehicle vehicle, WebfleetConnectionSettings settings, WebfleetConnectService client, CancellationToken cancellationToken = default)
    {
        var errors = Validate(tour, vehicle);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        var dispatch = tour.WebfleetDispatch ??= new WebfleetDispatchRecord();
        if (dispatch.Stops.Any(x => !string.Equals(x.StateLabel, "Nicht gesendet", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Diese Tour wurde bereits an WEBFLEET gesendet.");
        dispatch.ObjectUid = vehicle.WebfleetObjectUid;
        dispatch.ObjectName = vehicle.Name;
        dispatch.State = "sending";
        dispatch.Stops.Clear();
        foreach (var stop in tour.Stops.Where(x => !string.Equals(x.StopKind, "company", StringComparison.OrdinalIgnoreCase) && !string.Equals(x.StopKind, "pause", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Order))
        {
            var orderId = BuildOrderId(tour.Id, stop.Order, stop.Auftragsnummer);
            var request = new WebfleetDestinationOrderRequest(vehicle.WebfleetObjectUid, orderId, BuildOrderText(tour, stop), stop.Lat!.Value, (stop.Lon ?? stop.Lng)!.Value, "CH", string.Empty, string.Empty, stop.Address, ParseArrival(tour, stop));
            await client.SendDestinationOrderAsync(settings, request, cancellationToken);
            dispatch.Stops.Add(new WebfleetStopDispatchRecord { StopId = stop.Id, WebfleetOrderId = orderId, StateCode = 100, StateLabel = "Gesendet", StateChangedAtUtc = DateTimeOffset.UtcNow });
        }
        dispatch.State = "sent";
        dispatch.SentAtUtc = DateTimeOffset.UtcNow;
        dispatch.LastMessage = $"{dispatch.Stops.Count} Auftrag/Aufträge an {vehicle.Name} gesendet.";
        return dispatch;
    }

    private static string BuildOrderId(int tourId, int stopOrder, string sourceOrder) => $"T{tourId:D5}-{stopOrder:D3}-{sourceOrder}".Length <= 20 ? $"T{tourId:D5}-{stopOrder:D3}-{sourceOrder}" : $"T{tourId:D5}-{stopOrder:D3}";
    private static string BuildOrderText(TourRecord tour, TourStopRecord stop) => $"{tour.Name} · Stopp {stop.Order}: {stop.Name}";
    private static DateTimeOffset? ParseArrival(TourRecord tour, TourStopRecord stop)
    {
        if (DateTime.TryParse($"{tour.Date} {stop.PlannedArrival}", CultureInfo.GetCultureInfo("de-CH"), DateTimeStyles.AssumeLocal, out var result)) return new DateTimeOffset(result);
        return null;
    }
}
