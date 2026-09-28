using System.Text.RegularExpressions;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public sealed record PinAddressDifference(string Field, string Original, string Found)
{
    public string Status => string.Equals(Original.Trim(), Found.Trim(), StringComparison.OrdinalIgnoreCase)
        ? "Gleich" : string.IsNullOrWhiteSpace(Found) ? "Nicht geliefert" : "Abweichend";
}

public static class PinAddressComparison
{
    public static string Format(DeliveryAddressInfo address) =>
        $"{address.Street} {address.HouseNumber}, {address.PostalCode} {address.City}".Trim();

    // Read only the street part, never mistake the postal code for a house number.
    public static DeliveryAddressInfo GetFoundAddress(AddressGeocodingResult result)
    {
        var street = result.ResultStreetName?.Trim() ?? string.Empty;
        var firstPart = (result.ResultFreeformAddress ?? string.Empty).Split(',')[0].Trim();
        var remainder = !string.IsNullOrEmpty(street) && firstPart.StartsWith(street, StringComparison.OrdinalIgnoreCase)
            ? firstPart[street.Length..].Trim() : string.Empty;
        var number = Regex.IsMatch(remainder, @"^\d+[\p{L}]?(?:\s*[-/]\s*\d+[\p{L}]?)*$")
            ? remainder : string.Empty;
        return new DeliveryAddressInfo
        {
            Street = street, HouseNumber = number,
            PostalCode = result.ResultPostalCode ?? string.Empty,
            City = result.ResultMunicipality ?? string.Empty
        };
    }

    public static IReadOnlyList<PinAddressDifference> Compare(DeliveryAddressInfo original, DeliveryAddressInfo found)
    {
        var street = original.Street.Trim();
        var number = original.HouseNumber.Trim();
        if (string.IsNullOrEmpty(number))
        {
            var match = Regex.Match(street, @"\s+(\d+[\p{L}]?(?:\s*[-/]\s*\d+[\p{L}]?)*)$");
            if (match.Success)
            {
                number = match.Groups[1].Value;
                street = street[..match.Index].Trim();
            }
        }
        return [new("Strasse", street, found.Street), new("Hausnummer", number, found.HouseNumber),
            new("Postleitzahl", original.PostalCode, found.PostalCode), new("Ort / Gemeinde", original.City, found.City)];
    }

    public static bool CanUseFoundAddress(AddressGeocodingResult? result) => result is not null &&
        !string.IsNullOrWhiteSpace(result.ResultStreetName) &&
        !string.IsNullOrWhiteSpace(result.ResultPostalCode) &&
        !string.IsNullOrWhiteSpace(result.ResultMunicipality) &&
        (result.MatchType != "Point Address" || !string.IsNullOrWhiteSpace(GetFoundAddress(result).HouseNumber));

    public static void ApplyFoundAddress(Order order, AddressGeocodingResult result)
    {
        if (!CanUseFoundAddress(result))
            throw new InvalidOperationException("Der Treffer enthält keine vollständig übernehmbare Adresse.");
        var found = GetFoundAddress(result);
        // Preserve recipient, contact and delivery instructions.
        order.DeliveryAddress.Street = found.Street;
        order.DeliveryAddress.HouseNumber = found.HouseNumber;
        order.DeliveryAddress.PostalCode = found.PostalCode;
        order.DeliveryAddress.City = found.City;
        order.Address = Format(found);
        order.Location = result.Location;
    }

    public static string GoogleMapsUrl(string address) =>
        "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(address + ", Schweiz");

    // Plan.TomTom's search page reads the address from the q parameter.
    public static string TomTomUrl(string address) =>
        "https://plan.tomtom.com/de?q=" + Uri.EscapeDataString(address + ", Schweiz");
}
