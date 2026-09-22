using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public sealed record WebfleetVehicleSnapshot(
    string ObjectNumber,
    string ObjectUid,
    string Name,
    string DriverName,
    double? Latitude,
    double? Longitude,
    DateTimeOffset? PositionTime,
    string PositionText,
    string CurrentOrderId,
    string DestinationText);

public sealed record WebfleetDestinationOrderRequest(
    string ObjectUid,
    string OrderId,
    string OrderText,
    double Latitude,
    double Longitude,
    string Country,
    string PostalCode,
    string City,
    string Street,
    DateOnly ScheduledDate,
    DateTimeOffset? PlannedArrival,
    int? ArrivalToleranceMinutes);

public sealed record WebfleetTrackPoint(
    DateTimeOffset PositionTime,
    double Latitude,
    double Longitude,
    int? SpeedKmh,
    int? CourseDegrees);

public sealed record WebfleetDriverSnapshot(string DriverNumber, string DriverUid, string Name);

public sealed record WebfleetWorkingTimeInterval(
    DateTimeOffset StartTime,
    DateTimeOffset? EndTime,
    double? Latitude,
    double? Longitude,
    int WorkState);

public sealed record WebfleetOrderSnapshot(
    string OrderId,
    string OrderText,
    double? Latitude,
    double? Longitude,
    string Street,
    DateOnly? ScheduledDate,
    TimeOnly? PlannedArrivalTime,
    int? ArrivalToleranceMinutes);

/// <summary>Small, deliberately isolated CSV client for WEBFLEET.connect.</summary>
public sealed class WebfleetConnectService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly string[] ObjectReportColumns =
    [
        "objectno",
        "objectuid",
        "objectname",
        "drivername",
        "latitude_mdeg",
        "longitude_mdeg",
        "pos_time",
        "postext_short",
        "orderno",
        "dest_text"
    ];

    public async Task<IReadOnlyList<WebfleetVehicleSnapshot>> GetVehiclesAsync(
        WebfleetConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        EnsureCredentials(settings);
        var query = new Dictionary<string, string?>
        {
            ["account"] = settings.AccountName,
            ["apikey"] = settings.ApiKey,
            ["lang"] = "de",
            ["useUTF8"] = "true",
            ["action"] = "showObjectReportExtern",
            ["outputformat"] = "csv",
            ["columnfilter"] = string.Join(',', ObjectReportColumns)
        };
        var csv = await SendAsync(settings, query, cancellationToken);
        return ParseVehicles(csv);
    }

    public async Task<string> TestConnectionAsync(WebfleetConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        var vehicles = await GetVehiclesAsync(settings, cancellationToken);
        return $"Verbindung erfolgreich. {vehicles.Count} WEBFLEET-Fahrzeug(e) gefunden.";
    }

    public async Task<IReadOnlyList<WebfleetDriverSnapshot>> GetDriversAsync(
        WebfleetConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        EnsureCredentials(settings);
        var query = new Dictionary<string, string?>
        {
            ["account"] = settings.AccountName,
            ["apikey"] = settings.ApiKey,
            ["lang"] = "de",
            ["useUTF8"] = "true",
            ["action"] = "showDriverReportExtern",
            ["outputformat"] = "csv",
            ["columnfilter"] = "driverno,driveruid,name1,name2,name3"
        };
        return ParseDrivers(await SendAsync(settings, query, cancellationToken, treatNoDataAsEmpty: true));
    }

    public async Task<IReadOnlyList<WebfleetTrackPoint>> GetTrackAsync(
        WebfleetConnectionSettings settings,
        string objectUid,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        EnsureCredentials(settings);
        if (string.IsNullOrWhiteSpace(objectUid)) throw new ArgumentException("WEBFLEET-Objekt ist erforderlich.", nameof(objectUid));
        if (to <= from || to - from > TimeSpan.FromDays(2)) throw new ArgumentException("Ein Positionsverlauf darf maximal zwei Tage umfassen.");

        var query = new Dictionary<string, string?>
        {
            ["account"] = settings.AccountName,
            ["apikey"] = settings.ApiKey,
            ["lang"] = "de",
            ["useUTF8"] = "true",
            ["useISO8601"] = "true",
            ["action"] = "showTracks",
            ["outputformat"] = "csv",
            ["objectuid"] = objectUid.Trim(),
            ["range_pattern"] = "ud",
            ["rangefrom_string"] = from.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["rangeto_string"] = to.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["columnfilter"] = "pos_time,latitude,longitude,speed,course"
        };
        return ParseTrackPoints(await SendAsync(settings, query, cancellationToken, treatNoDataAsEmpty: true));
    }

    public async Task<IReadOnlyList<WebfleetWorkingTimeInterval>> GetWorkingTimesAsync(
        WebfleetConnectionSettings settings,
        string driverUid,
        string? objectUid,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        EnsureCredentials(settings);
        if (string.IsNullOrWhiteSpace(driverUid)) throw new ArgumentException("WEBFLEET-Fahrer ist erforderlich.", nameof(driverUid));
        if (to <= from || to - from > TimeSpan.FromDays(31)) throw new ArgumentException("Arbeitszeiten dürfen maximal einen Monat umfassen.");

        var query = new Dictionary<string, string?>
        {
            ["account"] = settings.AccountName,
            ["apikey"] = settings.ApiKey,
            ["lang"] = "de",
            ["useUTF8"] = "true",
            ["useISO8601"] = "true",
            ["action"] = "showWorkingTimes",
            ["outputformat"] = "csv",
            ["driveruid"] = driverUid.Trim(),
            ["objectuid"] = string.IsNullOrWhiteSpace(objectUid) ? null : objectUid.Trim(),
            ["timelineType"] = "3",
            ["range_pattern"] = "ud",
            ["rangefrom_string"] = from.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["rangeto_string"] = to.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["columnfilter"] = "start_time,end_time,start_latitude,start_longitude,workstate"
        };
        return ParseWorkingTimes(await SendAsync(settings, query, cancellationToken, treatNoDataAsEmpty: true));
    }

    public Task SendDestinationOrderAsync(WebfleetConnectionSettings settings, WebfleetDestinationOrderRequest order, CancellationToken cancellationToken = default)
        => SendDestinationOrderAsync(settings, order, "sendDestinationOrderExtern", cancellationToken);

    public Task UpdateDestinationOrderAsync(WebfleetConnectionSettings settings, WebfleetDestinationOrderRequest order, CancellationToken cancellationToken = default)
        => SendDestinationOrderAsync(settings, order, "updateDestinationOrderExtern", cancellationToken);

    public async Task DeleteOrderAsync(WebfleetConnectionSettings settings, string orderId, CancellationToken cancellationToken = default)
    {
        EnsureCredentials(settings);
        if (string.IsNullOrWhiteSpace(orderId)) throw new ArgumentException("WEBFLEET-Auftragsnummer ist erforderlich.", nameof(orderId));
        _ = await SendAsync(settings, new Dictionary<string, string?>
        {
            ["account"] = settings.AccountName, ["apikey"] = settings.ApiKey,
            ["lang"] = "de", ["useUTF8"] = "true", ["action"] = "deleteOrderExtern",
            ["orderid"] = orderId, ["mark_deleted"] = "1"
        }, cancellationToken);
    }

    private async Task SendDestinationOrderAsync(WebfleetConnectionSettings settings, WebfleetDestinationOrderRequest order, string action, CancellationToken cancellationToken)
    {
        EnsureCredentials(settings);
        if (string.IsNullOrWhiteSpace(order.ObjectUid) || string.IsNullOrWhiteSpace(order.OrderId)) throw new ArgumentException("WEBFLEET-Fahrzeug und Auftragsnummer sind erforderlich.");
        var query = BuildDestinationOrderQuery(settings, order, action);
        _ = await SendAsync(settings, query, cancellationToken);
    }

    private static Dictionary<string, string?> BuildDestinationOrderQuery(WebfleetConnectionSettings settings, WebfleetDestinationOrderRequest order, string action)
    {
        return new Dictionary<string, string?>
        {
            ["account"] = settings.AccountName, ["apikey"] = settings.ApiKey,
            ["lang"] = "de", ["useUTF8"] = "true", ["action"] = action, ["objectuid"] = order.ObjectUid,
            ["orderid"] = order.OrderId, ["ordertext"] = order.OrderText, ["ordertype"] = "3",
            ["latitude"] = Math.Round(order.Latitude * 1_000_000d).ToString(CultureInfo.InvariantCulture), ["longitude"] = Math.Round(order.Longitude * 1_000_000d).ToString(CultureInfo.InvariantCulture),
            ["country"] = order.Country, ["zip"] = order.PostalCode, ["city"] = order.City, ["street"] = order.Street,
            ["useISO8601"] = "true",
            // A tour day is a calendar date, not midnight in the sending computer's time zone.
            ["orderdate"] = order.ScheduledDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["ordertime"] = order.PlannedArrival?.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            ["arrivaltolerance"] = order.ArrivalToleranceMinutes?.ToString(CultureInfo.InvariantCulture)
        };
    }

    /// <summary>Returns the requested order IDs that still exist for an object on the specified day.</summary>
    public async Task<IReadOnlySet<string>> GetExistingOrderIdsAsync(
        WebfleetConnectionSettings settings,
        string objectUid,
        DateOnly date,
        IEnumerable<string> orderIds,
        CancellationToken cancellationToken = default)
    {
        var requestedOrderIds = orderIds.Where(orderId => !string.IsNullOrWhiteSpace(orderId)).ToHashSet(StringComparer.Ordinal);
        if (requestedOrderIds.Count == 0) return new HashSet<string>(StringComparer.Ordinal);
        var orders = await GetOrdersAsync(settings, objectUid, date, cancellationToken);
        return orders.Select(order => order.OrderId).Where(requestedOrderIds.Contains).ToHashSet(StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<WebfleetOrderSnapshot>> GetOrdersAsync(
        WebfleetConnectionSettings settings,
        string objectUid,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        EnsureCredentials(settings);
        if (string.IsNullOrWhiteSpace(objectUid)) throw new ArgumentException("WEBFLEET-Objekt ist erforderlich.", nameof(objectUid));

        var query = new Dictionary<string, string?>
        {
            ["account"] = settings.AccountName,
            ["apikey"] = settings.ApiKey,
            ["lang"] = "de",
            ["useUTF8"] = "true",
            ["useISO8601"] = "true",
            ["action"] = "showOrderReportExtern",
            ["outputformat"] = "csv",
            ["objectuid"] = objectUid.Trim(),
            ["range_pattern"] = "ud",
            ["rangefrom_string"] = date.ToDateTime(TimeOnly.MinValue).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            ["rangeto_string"] = date.ToDateTime(TimeOnly.MaxValue).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)
        };
        var csv = await SendAsync(settings, query, cancellationToken, treatNoDataAsEmpty: true);
        return ParseOrders(csv);
    }

    public async Task<WebfleetOrderSnapshot?> GetOrderAsync(
        WebfleetConnectionSettings settings,
        string orderId,
        CancellationToken cancellationToken = default)
    {
        EnsureCredentials(settings);
        if (string.IsNullOrWhiteSpace(orderId)) throw new ArgumentException("WEBFLEET-Auftragsnummer ist erforderlich.", nameof(orderId));

        var query = new Dictionary<string, string?>
        {
            ["account"] = settings.AccountName,
            ["apikey"] = settings.ApiKey,
            ["lang"] = "de",
            ["useUTF8"] = "true",
            ["action"] = "showOrderReportExtern",
            ["outputformat"] = "csv",
            ["orderid"] = orderId
        };
        var csv = await SendAsync(settings, query, cancellationToken, treatNoDataAsEmpty: true);
        return ParseOrders(csv).FirstOrDefault(order => string.Equals(order.OrderId, orderId, StringComparison.Ordinal));
    }

    private static async Task<string> SendAsync(WebfleetConnectionSettings settings, IReadOnlyDictionary<string, string?> query, CancellationToken cancellationToken, bool treatNoDataAsEmpty = false)
    {
        var endpoint = string.IsNullOrWhiteSpace(settings.CsvEndpoint) ? WebfleetConnectionSettings.DefaultCsvEndpoint : settings.CsvEndpoint.Trim();
        var queryString = string.Join("&", query.Where(x => !string.IsNullOrWhiteSpace(x.Value)).Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value!)}"));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}?{queryString}");
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.UserName}:{settings.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        using var response = await Client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"WEBFLEET antwortete mit HTTP {(int)response.StatusCode}: {content}");
        }
        if (treatNoDataAsEmpty && IsWebfleetNoDataResponse(content))
        {
            return string.Empty;
        }
        if (content.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("WFC_", StringComparison.OrdinalIgnoreCase) ||
            IsWebfleetErrorResponse(content))
        {
            throw new InvalidOperationException($"WEBFLEET-Verbindung fehlgeschlagen: {content}");
        }
        return content;
    }

    private static bool IsWebfleetErrorResponse(string content)
    {
        var firstLine = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return false;
        }

        var separatorIndex = firstLine.IndexOf(',');
        return separatorIndex > 0 && int.TryParse(firstLine[..separatorIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }

    private static bool IsWebfleetNoDataResponse(string content)
    {
        var firstLine = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        return firstLine is not null && firstLine.StartsWith("63,", StringComparison.Ordinal);
    }

    private static IReadOnlyList<WebfleetVehicleSnapshot> ParseVehicles(string csv)
    {
        var rows = ParseCsv(csv);
        if (rows.Count == 0) return [];
        var hasHeaderRow = rows[0].Any(value => string.Equals(value.Trim().TrimStart('\uFEFF'), "objectno", StringComparison.OrdinalIgnoreCase));
        IEnumerable<string> headerValues = hasHeaderRow ? rows[0] : ObjectReportColumns;
        var headers = headerValues
            .Select((value, index) => new { value = value.Trim().TrimStart('\uFEFF'), index })
            .ToDictionary(x => x.value, x => x.index, StringComparer.OrdinalIgnoreCase);
        string Get(IReadOnlyList<string> row, string name) => headers.TryGetValue(name, out var index) && index < row.Count ? row[index] : string.Empty;
        return rows.Skip(hasHeaderRow ? 1 : 0).Where(row => row.Count > 0).Select(row => new WebfleetVehicleSnapshot(
            Get(row, "objectno"), Get(row, "objectuid"), Get(row, "objectname"), Get(row, "drivername"),
            ParseCoordinate(Get(row, "latitude_mdeg"), Get(row, "latitude")), ParseCoordinate(Get(row, "longitude_mdeg"), Get(row, "longitude")),
            DateTimeOffset.TryParse(Get(row, "pos_time"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var positionTime) ? positionTime : null,
            Get(row, "postext_short"), Get(row, "orderno"), Get(row, "dest_text"))).ToList();
    }

    private static IReadOnlyList<WebfleetTrackPoint> ParseTrackPoints(string csv)
    {
        var rows = ParseCsv(csv);
        if (rows.Count == 0) return [];
        var hasHeaderRow = rows[0].Any(value => string.Equals(value.Trim().TrimStart('\uFEFF'), "pos_time", StringComparison.OrdinalIgnoreCase));
        IEnumerable<string> headerValues = hasHeaderRow ? rows[0] : ["pos_time", "latitude", "longitude", "speed", "course"];
        var headers = headerValues
            .Select((value, index) => new { Value = value.Trim().TrimStart('\uFEFF'), index })
            .ToDictionary(x => x.Value, x => x.index, StringComparer.OrdinalIgnoreCase);
        string Get(IReadOnlyList<string> row, string name) => headers.TryGetValue(name, out var index) && index < row.Count ? row[index] : string.Empty;
        return rows.Skip(hasHeaderRow ? 1 : 0)
            .Select(row =>
            {
                var latitude = ParseCoordinate(Get(row, "latitude"), string.Empty);
                var longitude = ParseCoordinate(Get(row, "longitude"), string.Empty);
                return DateTimeOffset.TryParse(Get(row, "pos_time"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time) && latitude.HasValue && longitude.HasValue
                    ? new WebfleetTrackPoint(time, latitude.Value, longitude.Value,
                        int.TryParse(Get(row, "speed"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var speed) ? speed : null,
                        int.TryParse(Get(row, "course"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var course) ? course : null)
                    : null;
            })
            .Where(x => x is not null)
            .Cast<WebfleetTrackPoint>()
            .OrderBy(x => x.PositionTime)
            .ToList();
    }

    private static IReadOnlyList<WebfleetDriverSnapshot> ParseDrivers(string csv)
    {
        var rows = ParseCsv(csv);
        if (rows.Count == 0) return [];
        var hasHeaderRow = rows[0].Any(value => string.Equals(value.Trim().TrimStart('\uFEFF'), "driveruid", StringComparison.OrdinalIgnoreCase));
        IEnumerable<string> headerValues = hasHeaderRow ? rows[0] : ["driverno", "driveruid", "name1", "name2", "name3"];
        var headers = headerValues
            .Select((value, index) => new { Value = value.Trim().TrimStart('\uFEFF'), index })
            .ToDictionary(x => x.Value, x => x.index, StringComparer.OrdinalIgnoreCase);
        string Get(IReadOnlyList<string> row, string name) => headers.TryGetValue(name, out var index) && index < row.Count ? row[index].Trim() : string.Empty;
        return rows.Skip(hasHeaderRow ? 1 : 0)
            .Select(row =>
            {
                var name = string.Join(' ', new[] { Get(row, "name1"), Get(row, "name2"), Get(row, "name3") }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
                return new WebfleetDriverSnapshot(Get(row, "driverno"), Get(row, "driveruid"),
                    string.IsNullOrWhiteSpace(name) ? Get(row, "drivername") : name);
            })
            .Where(driver => !string.IsNullOrWhiteSpace(driver.DriverUid))
            .OrderBy(driver => driver.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<WebfleetWorkingTimeInterval> ParseWorkingTimes(string csv)
    {
        var rows = ParseCsv(csv);
        if (rows.Count == 0) return [];
        var hasHeaderRow = rows[0].Any(value => string.Equals(value.Trim().TrimStart('\uFEFF'), "workstate", StringComparison.OrdinalIgnoreCase));
        IEnumerable<string> headerValues = hasHeaderRow ? rows[0] : ["start_time", "end_time", "start_latitude", "start_longitude", "workstate"];
        var headers = headerValues
            .Select((value, index) => new { Value = value.Trim().TrimStart('\uFEFF'), index })
            .ToDictionary(x => x.Value, x => x.index, StringComparer.OrdinalIgnoreCase);
        string Get(IReadOnlyList<string> row, string name) => headers.TryGetValue(name, out var index) && index < row.Count ? row[index].Trim() : string.Empty;
        return rows.Skip(hasHeaderRow ? 1 : 0)
            .Select(row =>
            {
                var hasStart = DateTimeOffset.TryParse(Get(row, "start_time"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var start);
                var hasEnd = DateTimeOffset.TryParse(Get(row, "end_time"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var end);
                var latitude = ParseCoordinate(Get(row, "start_latitude"), string.Empty);
                var longitude = ParseCoordinate(Get(row, "start_longitude"), string.Empty);
                return hasStart && int.TryParse(Get(row, "workstate"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var workState)
                    ? new WebfleetWorkingTimeInterval(start, hasEnd ? end : null, latitude, longitude, workState)
                    : null;
            })
            .Where(interval => interval is not null)
            .Cast<WebfleetWorkingTimeInterval>()
            .OrderBy(interval => interval.StartTime)
            .ToList();
    }

    private static IReadOnlyList<string> ParseOrderIds(string csv)
    {
        var rows = ParseCsv(csv);
        if (rows.Count == 0 || !rows[0].Any(value => string.Equals(value.Trim().TrimStart('\uFEFF'), "orderid", StringComparison.OrdinalIgnoreCase))) return [];

        var orderIdIndex = rows[0]
            .Select((value, index) => new { Value = value.Trim().TrimStart('\uFEFF'), Index = index })
            .FirstOrDefault(value => string.Equals(value.Value, "orderid", StringComparison.OrdinalIgnoreCase))?.Index;
        if (orderIdIndex is null) return [];

        return rows.Skip(1)
            .Where(row => orderIdIndex.Value < row.Count && !string.IsNullOrWhiteSpace(row[orderIdIndex.Value]))
            .Select(row => row[orderIdIndex.Value].Trim())
            .ToList();
    }

    private static IReadOnlyList<WebfleetOrderSnapshot> ParseOrders(string csv)
    {
        var rows = ParseCsv(csv);
        if (rows.Count == 0 || !rows[0].Any(value => string.Equals(value.Trim().TrimStart('\uFEFF'), "orderid", StringComparison.OrdinalIgnoreCase))) return [];

        var headers = rows[0]
            .Select((value, index) => new { Value = value.Trim().TrimStart('\uFEFF'), Index = index })
            .ToDictionary(value => value.Value, value => value.Index, StringComparer.OrdinalIgnoreCase);
        string Get(IReadOnlyList<string> row, string name) => headers.TryGetValue(name, out var index) && index < row.Count ? row[index].Trim() : string.Empty;

        return rows.Skip(1)
            .Select(row => new WebfleetOrderSnapshot(
                Get(row, "orderid"),
                Get(row, "ordertext"),
                ParseCoordinate(Get(row, "latitude"), string.Empty),
                ParseCoordinate(Get(row, "longitude"), string.Empty),
                Get(row, "street"),
                DateOnly.TryParse(Get(row, "orderdate"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var orderDate) ? orderDate : null,
                TimeOnly.TryParse(Get(row, "planned_arrival_time"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var plannedArrivalTime) ? plannedArrivalTime : null,
                int.TryParse(Get(row, "arrivaltolerance"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var arrivalTolerance) ? arrivalTolerance : null))
            .Where(order => !string.IsNullOrWhiteSpace(order.OrderId))
            .ToList();
    }

    private static double? ParseCoordinate(string microDegrees, string formatted)
    {
        if (double.TryParse(microDegrees, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return value / 1_000_000d;
        return double.TryParse(formatted, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : null;
    }

    private static List<List<string>> ParseCsv(string value)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var field = new StringBuilder(); var quoted = false;
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (current == '"') { if (quoted && index + 1 < value.Length && value[index + 1] == '"') { field.Append(current); index++; } else quoted = !quoted; }
            else if (current == ';' && !quoted) { row.Add(field.ToString()); field.Clear(); }
            else if ((current == '\n' || current == '\r') && !quoted) { if (current == '\r' && index + 1 < value.Length && value[index + 1] == '\n') index++; row.Add(field.ToString()); field.Clear(); if (row.Any(x => !string.IsNullOrWhiteSpace(x))) rows.Add(row); row = new(); }
            else field.Append(current);
        }
        row.Add(field.ToString()); if (row.Any(x => !string.IsNullOrWhiteSpace(x))) rows.Add(row);
        return rows;
    }

    private static void EnsureCredentials(WebfleetConnectionSettings settings)
    {
        if (settings is null || !settings.HasCredentials) throw new InvalidOperationException("Bitte Webfleet-Account, Benutzer, API-Key und Passwort in den Einstellungen hinterlegen.");
    }
}
