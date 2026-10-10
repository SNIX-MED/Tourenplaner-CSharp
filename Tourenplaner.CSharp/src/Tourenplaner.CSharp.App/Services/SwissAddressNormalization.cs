using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Tourenplaner.CSharp.App.Services;

internal static class SwissAddressNormalization
{
    public static string NormalizeForComparison(string? value)
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
}
