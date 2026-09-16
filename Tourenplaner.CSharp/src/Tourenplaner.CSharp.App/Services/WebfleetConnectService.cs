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
    DateTimeOffset? PlannedArrival);

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

    public async Task SendDestinationOrderAsync(WebfleetConnectionSettings settings, WebfleetDestinationOrderRequest order, CancellationToken cancellationToken = default)
    {
        EnsureCredentials(settings);
        if (string.IsNullOrWhiteSpace(order.ObjectUid) || string.IsNullOrWhiteSpace(order.OrderId)) throw new ArgumentException("WEBFLEET-Fahrzeug und Auftragsnummer sind erforderlich.");
        var query = new Dictionary<string, string?>
        {
            ["account"] = settings.AccountName, ["apikey"] = settings.ApiKey,
            ["lang"] = "de", ["useUTF8"] = "true", ["action"] = "sendDestinationOrderExtern", ["objectuid"] = order.ObjectUid,
            ["orderid"] = order.OrderId, ["ordertext"] = order.OrderText, ["ordertype"] = "3",
            ["latitude"] = Math.Round(order.Latitude * 1_000_000d).ToString(CultureInfo.InvariantCulture), ["longitude"] = Math.Round(order.Longitude * 1_000_000d).ToString(CultureInfo.InvariantCulture),
            ["country"] = order.Country, ["zip"] = order.PostalCode, ["city"] = order.City, ["street"] = order.Street,
            ["useISO8601"] = order.PlannedArrival.HasValue ? "true" : null,
            ["orderdate"] = order.PlannedArrival?.ToString("yyyy-MM-ddzzz", CultureInfo.InvariantCulture), ["ordertime"] = order.PlannedArrival?.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
        };
        _ = await SendAsync(settings, query, cancellationToken);
    }

    private static async Task<string> SendAsync(WebfleetConnectionSettings settings, IReadOnlyDictionary<string, string?> query, CancellationToken cancellationToken)
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
