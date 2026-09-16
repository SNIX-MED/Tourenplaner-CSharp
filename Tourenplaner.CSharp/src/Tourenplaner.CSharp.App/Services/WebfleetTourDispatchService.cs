using System.Globalization;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public sealed record WebfleetTourSyncResult(int CreateCount, int UpdateCount, int UnchangedCount, int DeleteCount)
{
    public bool HasChanges => CreateCount + UpdateCount + DeleteCount > 0;
}

public sealed class WebfleetTourDispatchService
{
    public IReadOnlyList<string> Validate(TourRecord tour, Vehicle? vehicle)
    {
        var errors = new List<string>();
        if (tour is null) return ["Die Tour fehlt."];
        if (vehicle is null || string.IsNullOrWhiteSpace(vehicle.WebfleetObjectUid)) errors.Add("Dem Tourfahrzeug ist noch kein WEBFLEET-Fahrzeug zugeordnet.");
        foreach (var stop in tour.Stops.Where(IsDispatchableStop))
        {
            if (stop.Lat is null || (stop.Lon ?? stop.Lng) is null) errors.Add($"Stopp {stop.Order}: Koordinaten fehlen.");
            if (!IsCompanyEndStop(stop) && string.IsNullOrWhiteSpace(stop.Auftragsnummer)) errors.Add($"Stopp {stop.Order}: Auftragsnummer fehlt.");
        }
        return errors;
    }

    public async Task<WebfleetTourSyncResult> PreviewSynchronizationAsync(TourRecord tour, Vehicle vehicle, WebfleetConnectionSettings settings, WebfleetConnectService client, CancellationToken cancellationToken = default)
    {
        var errors = Validate(tour, vehicle);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        var dispatch = tour.WebfleetDispatch ??= new WebfleetDispatchRecord();
        EnsureSameObject(dispatch, vehicle);
        var stopsToDispatch = tour.Stops.Where(IsDispatchableStop).OrderBy(x => x.Order).ToList();
        var plan = await BuildSynchronizationPlanAsync(tour, vehicle, settings, client, dispatch, stopsToDispatch, cancellationToken);
        return plan.Result;
    }

    public async Task<WebfleetDispatchRecord> DispatchAsync(TourRecord tour, Vehicle vehicle, WebfleetConnectionSettings settings, WebfleetConnectService client, CancellationToken cancellationToken = default)
    {
        var errors = Validate(tour, vehicle);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        var dispatch = tour.WebfleetDispatch ??= new WebfleetDispatchRecord();
        EnsureSameObject(dispatch, vehicle);
        var stopsToDispatch = tour.Stops.Where(IsDispatchableStop).OrderBy(x => x.Order).ToList();
        var plan = await BuildSynchronizationPlanAsync(tour, vehicle, settings, client, dispatch, stopsToDispatch, cancellationToken);
        dispatch.ObjectUid = vehicle.WebfleetObjectUid;
        dispatch.ObjectName = vehicle.Name;
        dispatch.State = "sending";
        foreach (var orderId in plan.OrderIdsToDelete)
        {
            await client.DeleteOrderAsync(settings, orderId, cancellationToken);
        }
        foreach (var item in plan.Items)
        {
            if (item.Action == SynchronizationAction.Create)
            {
                try
                {
                    await client.SendDestinationOrderAsync(settings, item.Request, cancellationToken);
                }
                catch (InvalidOperationException exception) when (IsOrderAlreadyPresent(exception))
                {
                    await client.UpdateDestinationOrderAsync(settings, item.Request, cancellationToken);
                    item.Action = SynchronizationAction.Update;
                }
            }
            else if (item.Action == SynchronizationAction.Update)
            {
                await client.UpdateDestinationOrderAsync(settings, item.Request, cancellationToken);
            }
        }

        dispatch.Stops = plan.Items.Select(item => new WebfleetStopDispatchRecord
        {
            StopId = item.Stop.Id,
            WebfleetOrderId = item.Request.OrderId,
            StateCode = 100,
            StateLabel = item.Action == SynchronizationAction.Unchanged ? "Bereits vorhanden" : "Gesendet",
            StateChangedAtUtc = DateTimeOffset.UtcNow
        }).ToList();
        dispatch.State = "sent";
        dispatch.SentAtUtc = DateTimeOffset.UtcNow;
        var result = new WebfleetTourSyncResult(
            plan.Items.Count(item => item.Action == SynchronizationAction.Create),
            plan.Items.Count(item => item.Action == SynchronizationAction.Update),
            plan.Items.Count(item => item.Action == SynchronizationAction.Unchanged),
            plan.OrderIdsToDelete.Count);
        dispatch.LastMessage = BuildSynchronizationMessage(result, vehicle.Name);
        return dispatch;
    }

    private async Task<SynchronizationPlan> BuildSynchronizationPlanAsync(
        TourRecord tour,
        Vehicle vehicle,
        WebfleetConnectionSettings settings,
        WebfleetConnectService client,
        WebfleetDispatchRecord dispatch,
        IReadOnlyList<TourStopRecord> stops,
        CancellationToken cancellationToken)
    {
        var priorOrderIdsByStopId = dispatch.Stops
            .Where(stop => !string.IsNullOrWhiteSpace(stop.StopId) && !string.IsNullOrWhiteSpace(stop.WebfleetOrderId))
            .GroupBy(stop => stop.StopId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().WebfleetOrderId, StringComparer.OrdinalIgnoreCase);
        var items = stops.Select((stop, dispatchPosition) =>
        {
            var sourceOrder = IsCompanyEndStop(stop) ? "ENDE" : stop.Auftragsnummer ?? string.Empty;
            var orderId = priorOrderIdsByStopId.TryGetValue(stop.Id, out var priorOrderId)
                ? priorOrderId
                : BuildOrderId(tour.Id, stop.Order, sourceOrder);
            var arrivalWindow = BuildWebfleetArrivalWindow(tour, stop);
            return new SynchronizationItem(stop, new WebfleetDestinationOrderRequest(
                vehicle.WebfleetObjectUid,
                orderId,
                BuildOrderText(tour, stop, dispatchPosition + 1),
                stop.Lat!.Value,
                (stop.Lon ?? stop.Lng)!.Value,
                "CH",
                string.Empty,
                string.Empty,
                stop.Address,
                arrivalWindow.PlannedArrival,
                arrivalWindow.ArrivalToleranceMinutes));
        }).ToList();

        var remoteOrdersById = (await client.GetOrdersAsync(settings, vehicle.WebfleetObjectUid, ParseTourDate(tour.Date), cancellationToken))
            .GroupBy(order => order.OrderId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var item in items)
        {
            item.Action = !remoteOrdersById.TryGetValue(item.Request.OrderId, out var remoteOrder)
                ? SynchronizationAction.Create
                : IsChanged(remoteOrder, item.Request) ? SynchronizationAction.Update : SynchronizationAction.Unchanged;
        }

        var desiredOrderIds = items.Select(item => item.Request.OrderId).ToHashSet(StringComparer.Ordinal);
        var orderIdsToDelete = dispatch.Stops
            .Select(stop => stop.WebfleetOrderId)
            .Where(orderId => !string.IsNullOrWhiteSpace(orderId) && !desiredOrderIds.Contains(orderId) && remoteOrdersById.ContainsKey(orderId))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var startStop in tour.Stops.Where(IsCompanyStartStop))
        {
            var legacyStartOrderId = BuildOrderId(tour.Id, startStop.Order, startStop.Auftragsnummer ?? TourStopIdentity.CompanyStartOrderNumber);
            if (!desiredOrderIds.Contains(legacyStartOrderId) && await client.GetOrderAsync(settings, legacyStartOrderId, cancellationToken) is not null)
            {
                orderIdsToDelete.Add(legacyStartOrderId);
            }
        }
        orderIdsToDelete = orderIdsToDelete.Distinct(StringComparer.Ordinal).ToList();
        var result = new WebfleetTourSyncResult(
            items.Count(item => item.Action == SynchronizationAction.Create),
            items.Count(item => item.Action == SynchronizationAction.Update),
            items.Count(item => item.Action == SynchronizationAction.Unchanged),
            orderIdsToDelete.Count);
        return new SynchronizationPlan(items, orderIdsToDelete, result);
    }

    private static bool IsChanged(WebfleetOrderSnapshot remoteOrder, WebfleetDestinationOrderRequest desiredOrder) =>
        !string.Equals(remoteOrder.OrderText, desiredOrder.OrderText, StringComparison.Ordinal) ||
        !AreSameCoordinates(remoteOrder.Latitude, desiredOrder.Latitude) ||
        !AreSameCoordinates(remoteOrder.Longitude, desiredOrder.Longitude) ||
        (!string.IsNullOrWhiteSpace(remoteOrder.Street) && !string.Equals(remoteOrder.Street, desiredOrder.Street, StringComparison.Ordinal)) ||
        (desiredOrder.PlannedArrival.HasValue &&
            (remoteOrder.ScheduledDate != DateOnly.FromDateTime(desiredOrder.PlannedArrival.Value.Date) ||
             remoteOrder.PlannedArrivalTime != TimeOnly.FromDateTime(desiredOrder.PlannedArrival.Value.DateTime) ||
             remoteOrder.ArrivalToleranceMinutes != desiredOrder.ArrivalToleranceMinutes));

    private static bool AreSameCoordinates(double? first, double second) => first.HasValue && Math.Abs(first.Value - second) < 0.000001d;

    private static bool IsOrderAlreadyPresent(InvalidOperationException exception) =>
        exception.Message.Contains("2515", StringComparison.Ordinal) ||
        exception.Message.Contains("order already exists", StringComparison.OrdinalIgnoreCase);

    private static void EnsureSameObject(WebfleetDispatchRecord dispatch, Vehicle vehicle)
    {
        if (dispatch.Stops.Count > 0 && !string.IsNullOrWhiteSpace(dispatch.ObjectUid) && !string.Equals(dispatch.ObjectUid, vehicle.WebfleetObjectUid, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Diese Tour wurde bereits an ein anderes WEBFLEET-Gerät gesendet. Bitte die bestehende Zuweisung nicht überschreiben.");
        }
    }

    private static string BuildSynchronizationMessage(WebfleetTourSyncResult result, string vehicleName) =>
        $"WEBFLEET synchronisiert: {result.CreateCount} neu, {result.UpdateCount} aktualisiert, {result.UnchangedCount} unverändert, {result.DeleteCount} gelöscht ({vehicleName}).";

    private sealed record SynchronizationPlan(IReadOnlyList<SynchronizationItem> Items, IReadOnlyList<string> OrderIdsToDelete, WebfleetTourSyncResult Result);

    private sealed class SynchronizationItem(TourStopRecord stop, WebfleetDestinationOrderRequest request)
    {
        public TourStopRecord Stop { get; } = stop;
        public WebfleetDestinationOrderRequest Request { get; } = request;
        public SynchronizationAction Action { get; set; }
    }

    private enum SynchronizationAction
    {
        Create,
        Update,
        Unchanged
    }

    private static string BuildOrderId(int tourId, int stopOrder, string sourceOrder) => $"T{tourId:D5}-{stopOrder:D3}-{sourceOrder}".Length <= 20 ? $"T{tourId:D5}-{stopOrder:D3}-{sourceOrder}" : $"T{tourId:D5}-{stopOrder:D3}";
    private static string BuildOrderText(TourRecord tour, TourStopRecord stop, int dispatchPosition) => $"{tour.Name} · Stopp {dispatchPosition}: {stop.Name}";
    private static bool IsDispatchableStop(TourStopRecord stop) =>
        !IsCompanyStartStop(stop) &&
        !string.Equals(stop.StopKind, "pause", StringComparison.OrdinalIgnoreCase);

    private static bool IsCompanyStartStop(TourStopRecord stop) =>
        string.Equals(stop.Id, "__company_start__", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(stop.Id, TourStopIdentity.CompanyStartStopId, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(stop.Auftragsnummer, TourStopIdentity.CompanyStartOrderNumber, StringComparison.OrdinalIgnoreCase);

    private static bool IsCompanyEndStop(TourStopRecord stop) =>
        string.Equals(stop.Id, "__company_end__", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(stop.Id, TourStopIdentity.CompanyEndStopId, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(stop.Auftragsnummer, TourStopIdentity.CompanyEndOrderNumber, StringComparison.OrdinalIgnoreCase);

    private static DateOnly ParseTourDate(string tourDate)
    {
        if (DateOnly.TryParse(tourDate, CultureInfo.GetCultureInfo("de-CH"), DateTimeStyles.None, out var result)) return result;
        if (DateOnly.TryParse(tourDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out result)) return result;
        throw new InvalidOperationException("Das Tourdatum ist ungültig. Der WEBFLEET-Versand kann nicht auf bestehende Aufträge geprüft werden.");
    }

    private static (DateTimeOffset? PlannedArrival, int? ArrivalToleranceMinutes) BuildWebfleetArrivalWindow(TourRecord tour, TourStopRecord stop)
    {
        if (TryParseTourTime(stop.PlannedArrivalOptimistic, out var optimistic) &&
            TryParseTourTime(stop.PlannedArrivalPessimistic, out var pessimistic))
        {
            var realistic = TryParseTourTime(stop.PlannedArrival, out var parsedRealistic) ? parsedRealistic : optimistic;
            var date = ParseTourDate(tour.Date);
            var range = TourArrivalDisplayFormatter.BuildDisplayedArrivalRange(
                date.ToDateTime(optimistic),
                date.ToDateTime(realistic),
                date.ToDateTime(pessimistic));
            var tolerance = (int)(range.Pessimistic - range.Optimistic).TotalMinutes;
            return (new DateTimeOffset(range.Optimistic), tolerance);
        }

        var plannedArrival = ParseArrival(tour, stop);
        return (plannedArrival, plannedArrival.HasValue ? 0 : null);
    }

    private static bool TryParseTourTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParse(value, CultureInfo.GetCultureInfo("de-CH"), DateTimeStyles.None, out time) ||
        TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static DateTimeOffset? ParseArrival(TourRecord tour, TourStopRecord stop)
    {
        if (string.IsNullOrWhiteSpace(stop.PlannedArrival)) return null;
        if (!TryParseTourTime(stop.PlannedArrival, out var time)) return null;
        var result = ParseTourDate(tour.Date).ToDateTime(time);
        return new DateTimeOffset(result);
    }
}
