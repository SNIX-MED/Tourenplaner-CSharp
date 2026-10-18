namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed partial class KarteSectionViewModel
{
    private static string BuildCompanyAddressText(string? street, string? postalCode, string? city)
    {
        return OrderSectionSharedHelpers.BuildCompanyAddress(street, postalCode, city);
    }

    private static string BuildStreetLine(string? street, string? houseNumber)
    {
        return OrderSectionSharedHelpers.BuildStreetLine(street, houseNumber);
    }

    internal static string BuildRouteStopAddressLine(
        string? street,
        string? postalCodeCity,
        string? fallbackAddress)
    {
        var normalizedStreet = NormalizeUiText(street);
        var normalizedPostalCodeCity = NormalizeUiText(postalCodeCity);
        var normalizedFallbackAddress = NormalizeUiText(fallbackAddress);

        if (string.IsNullOrWhiteSpace(normalizedStreet) && !string.IsNullOrWhiteSpace(normalizedFallbackAddress))
        {
            var commaIndex = normalizedFallbackAddress.IndexOf(',');
            normalizedStreet = commaIndex < 0
                ? normalizedFallbackAddress
                : normalizedFallbackAddress[..commaIndex].Trim();
        }

        normalizedStreet = StripTrailingStandaloneNumber(normalizedStreet);

        if (string.IsNullOrWhiteSpace(normalizedPostalCodeCity) && !string.IsNullOrWhiteSpace(normalizedFallbackAddress))
        {
            var commaIndex = normalizedFallbackAddress.IndexOf(',');
            if (commaIndex >= 0 && commaIndex + 1 < normalizedFallbackAddress.Length)
            {
                normalizedPostalCodeCity = normalizedFallbackAddress[(commaIndex + 1)..].Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(normalizedStreet))
        {
            return normalizedPostalCodeCity;
        }

        if (string.IsNullOrWhiteSpace(normalizedPostalCodeCity))
        {
            return normalizedStreet;
        }

        return $"{normalizedStreet}, {normalizedPostalCodeCity}";
    }

    private static int ResolveTrafficBufferPercent(int explicitValue, int legacyValue, int fallbackValue)
    {
        return OrderSectionSharedHelpers.ResolveTrafficBufferPercent(explicitValue, legacyValue, fallbackValue);
    }
}
