using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public static class GpxRouteExportService
{
    private const string Creator = "Tourenplaner";
    private static readonly XNamespace Gpx = "http://www.topografix.com/GPX/1/1";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    public static bool TryBuild(RouteExportSnapshot snapshot, out string gpx, out string error)
    {
        gpx = string.Empty;
        error = string.Empty;

        var validRoutePointCount = snapshot.GoogleMapsPoints.Count(point => IsValid(point.Latitude, point.Longitude));
        if (validRoutePointCount < 2)
        {
            error = "F\u00FCr den GPX-Export werden mindestens zwei g\u00FCltige Stopps mit Koordinaten ben\u00F6tigt.";
            return false;
        }

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                Gpx + "gpx",
                new XAttribute("version", "1.1"),
                new XAttribute("creator", Creator),
                new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
                new XAttribute(
                    Xsi + "schemaLocation",
                    "http://www.topografix.com/GPX/1/1 https://www.topografix.com/GPX/1/1/gpx.xsd"),
                BuildMetadata(snapshot),
                BuildWaypoints(snapshot),
                BuildTrack(snapshot)));

        gpx = WriteDocument(document);
        return true;
    }

    private static XElement BuildMetadata(RouteExportSnapshot snapshot)
    {
        var descriptionParts = new[]
        {
            string.IsNullOrWhiteSpace(snapshot.TourDate) ? null : $"Datum: {snapshot.TourDate.Trim()}",
            string.IsNullOrWhiteSpace(snapshot.StartTime) ? null : $"Start: {snapshot.StartTime.Trim()}",
            string.IsNullOrWhiteSpace(snapshot.VehicleLabel) ? null : $"Fahrzeug: {snapshot.VehicleLabel.Trim()}",
            string.IsNullOrWhiteSpace(snapshot.TrailerLabel) ? null : $"Anh\u00E4nger: {snapshot.TrailerLabel.Trim()}"
        }.Where(x => !string.IsNullOrWhiteSpace(x));

        return new XElement(
            Gpx + "metadata",
            new XElement(Gpx + "name", CleanXmlText(BuildTourName(snapshot))),
            new XElement(Gpx + "desc", CleanXmlText(string.Join(" | ", descriptionParts))),
            new XElement(Gpx + "time", DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture)));
    }

    private static IEnumerable<XElement> BuildWaypoints(RouteExportSnapshot snapshot)
    {
        if (snapshot.Company is not null && IsValid(snapshot.Company.Latitude, snapshot.Company.Longitude))
        {
            yield return BuildPointElement(
                "wpt",
                snapshot.Company.Latitude,
                snapshot.Company.Longitude,
                snapshot.Company.Name,
                snapshot.Company.Address,
                "Flag, Blue");
        }

        foreach (var stop in snapshot.Stops.Where(x => IsValid(x.Latitude, x.Longitude)))
        {
            var description = string.Join(
                Environment.NewLine,
                new[]
                {
                    string.IsNullOrWhiteSpace(stop.OrderNumber) ? null : $"Auftrag: {stop.OrderNumber.Trim()}",
                    string.IsNullOrWhiteSpace(stop.Address) ? null : stop.Address.Trim(),
                    string.IsNullOrWhiteSpace(stop.DeliveryType) ? null : stop.DeliveryType.Trim(),
                    string.IsNullOrWhiteSpace(stop.TimeWindow) ? null : $"Zeitfenster: {stop.TimeWindow.Trim()}",
                    string.IsNullOrWhiteSpace(stop.Arrival) ? null : $"Ankunft: {stop.Arrival.Trim()}",
                    string.IsNullOrWhiteSpace(stop.WeightText) ? null : stop.WeightText.Trim(),
                    string.IsNullOrWhiteSpace(stop.EmployeeInfoText) ? null : stop.EmployeeInfoText.Trim()
                }.Where(x => !string.IsNullOrWhiteSpace(x)));

            yield return BuildPointElement(
                "wpt",
                stop.Latitude,
                stop.Longitude,
                BuildStopName(stop),
                description,
                "Flag, Green");
        }
    }

    private static XElement? BuildTrack(RouteExportSnapshot snapshot)
    {
        var trackPoints = snapshot.GeometryPoints
            .Where(point => IsValid(point.Latitude, point.Longitude))
            .ToList();

        if (trackPoints.Count < 2)
        {
            return null;
        }

        return new XElement(
            Gpx + "trk",
            new XElement(Gpx + "name", CleanXmlText(BuildTourName(snapshot))),
            new XElement(
                Gpx + "trkseg",
                trackPoints.Select(point => BuildPointElement("trkpt", point.Latitude, point.Longitude, string.Empty, string.Empty, null))));
    }

    private static XElement BuildPointElement(
        string elementName,
        double latitude,
        double longitude,
        string name,
        string description,
        string? symbol)
    {
        var children = new List<object>();
        if (!string.IsNullOrWhiteSpace(name))
        {
            children.Add(new XElement(Gpx + "name", CleanXmlText(name)));
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            children.Add(new XElement(Gpx + "desc", CleanXmlText(description)));
            children.Add(new XElement(Gpx + "cmt", CleanXmlText(description)));
        }

        if (!string.IsNullOrWhiteSpace(symbol))
        {
            children.Add(new XElement(Gpx + "sym", symbol));
        }

        return new XElement(
            Gpx + elementName,
            new XAttribute("lat", FormatCoordinate(latitude)),
            new XAttribute("lon", FormatCoordinate(longitude)),
            children);
    }

    private static bool IsValid(double latitude, double longitude)
    {
        return !double.IsNaN(latitude) &&
               !double.IsNaN(longitude) &&
               latitude is >= -90d and <= 90d &&
               longitude is >= -180d and <= 180d;
    }

    private static string BuildTourName(RouteExportSnapshot snapshot)
    {
        return string.IsNullOrWhiteSpace(snapshot.TourName) ? "Tour" : snapshot.TourName.Trim();
    }

    private static string BuildStopName(RouteExportStopInfo stop)
    {
        var label = string.IsNullOrWhiteSpace(stop.Label) ? stop.Position.ToString(CultureInfo.InvariantCulture) : stop.Label.Trim();
        var name = string.IsNullOrWhiteSpace(stop.Name) ? stop.Address : stop.Name;
        return $"{label} - {name}".Trim();
    }

    private static string FormatCoordinate(double value)
    {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static string CleanXmlText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (XmlConvert.IsXmlChar(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static string WriteDocument(XDocument document)
    {
        using var stringWriter = new Utf8StringWriter(CultureInfo.InvariantCulture);
        document.Save(stringWriter, SaveOptions.None);
        return stringWriter.ToString();
    }

    private sealed class Utf8StringWriter(IFormatProvider formatProvider) : StringWriter(formatProvider)
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
