using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Tourenplaner.CSharp.Application.Common;

public enum OrderAddressComparisonProfile
{
    XmlReimport,
    SwissPinValidation
}

public static class OrderAddressFormatting
{
    public static string BuildStreetLine(
        string? street,
        string? houseNumber,
        bool trimComponents = true) =>
        JoinNonEmpty(" ", trimComponents, street, houseNumber);

    public static string BuildPostalCityLine(
        string? postalCode,
        string? city,
        bool trimComponents = true) =>
        JoinNonEmpty(" ", trimComponents, postalCode, city);

    public static string BuildAddressLine(
        string? street,
        string? houseNumber,
        string? postalCode,
        string? city,
        bool trimComponents = true)
    {
        var streetLine = BuildStreetLine(street, houseNumber, trimComponents);
        var postalCityLine = BuildPostalCityLine(postalCode, city, trimComponents);
        return JoinNonEmpty(", ", trimComponents: false, streetLine, postalCityLine);
    }

    public static string BuildStableKey(
        string? street,
        string? houseNumber,
        string? postalCode,
        string? city,
        OrderAddressComparisonProfile comparisonProfile) =>
        string.Join("|", new[]
        {
            NormalizeKeyComponent(street, comparisonProfile),
            NormalizeKeyComponent(houseNumber, comparisonProfile),
            NormalizeKeyComponent(postalCode, comparisonProfile),
            NormalizeKeyComponent(city, comparisonProfile)
        });

    public static string NormalizeForComparison(
        string? value,
        OrderAddressComparisonProfile comparisonProfile) =>
        comparisonProfile switch
        {
            OrderAddressComparisonProfile.XmlReimport => NormalizeForXmlReimport(value),
            OrderAddressComparisonProfile.SwissPinValidation => NormalizeForSwissPinValidation(value),
            _ => throw new ArgumentOutOfRangeException(nameof(comparisonProfile), comparisonProfile, null)
        };

    public static string NormalizeForXmlReimport(string? value) =>
        (value ?? string.Empty).Trim();

    public static string NormalizeForSwissPinValidation(string? value)
    {
        var decomposed = (value ?? string.Empty)
            .Replace("ß", "ss", StringComparison.OrdinalIgnoreCase)
            .Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.IsLetterOrDigit(character)
                    ? char.ToLowerInvariant(character)
                    : ' ');
            }
        }

        var normalized = Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
        return Regex.Replace(normalized, @"\bst\b", "sankt", RegexOptions.CultureInvariant);
    }

    private static string NormalizeKeyComponent(
        string? value,
        OrderAddressComparisonProfile comparisonProfile)
    {
        var normalized = NormalizeForComparison(value, comparisonProfile);
        return comparisonProfile == OrderAddressComparisonProfile.SwissPinValidation
            ? normalized.Replace(" ", string.Empty, StringComparison.Ordinal)
            : normalized;
    }

    private static string JoinNonEmpty(
        string separator,
        bool trimComponents,
        params string?[] components) =>
        string.Join(
            separator,
            components
                .Where(component => !string.IsNullOrWhiteSpace(component))
                .Select(component => trimComponents ? component!.Trim() : component!));
}
