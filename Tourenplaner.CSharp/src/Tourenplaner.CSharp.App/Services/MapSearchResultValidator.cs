using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Tourenplaner.CSharp.App.Services;

public static class MapSearchResultValidator
{
    private static readonly HashSet<string> IgnoredTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "schweiz",
        "switzerland",
        "ch"
    };

    public static bool IsRelevant(string? query, AddressGeocodingResult? result)
    {
        if (result is null || string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var queryTokens = Tokenize(query)
            .Where(token => !IgnoredTokens.Contains(token))
            .ToArray();
        if (queryTokens.Length == 0)
        {
            return false;
        }

        var resultTokens = Tokenize(string.Join(' ', new[]
        {
            result.ResultPostalCode,
            result.ResultMunicipality,
            result.ResultStreetName,
            result.ResultFreeformAddress
        }.Where(value => !string.IsNullOrWhiteSpace(value)))).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return resultTokens.Count > 0 && queryTokens.All(resultTokens.Contains);
    }

    private static IEnumerable<string> Tokenize(string? value)
    {
        var normalized = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character)
                ? char.ToLowerInvariant(character)
                : ' ');
        }

        return Regex.Split(builder.ToString(), @"\s+")
            .Where(token => token.Length > 0);
    }
}
