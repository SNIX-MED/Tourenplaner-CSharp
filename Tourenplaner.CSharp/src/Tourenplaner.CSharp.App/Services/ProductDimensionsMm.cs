using System.Globalization;
using System.Text.RegularExpressions;

namespace Tourenplaner.CSharp.App.Services;

public readonly record struct ProductDimensionsMm(int Width, int Depth, int Height)
{
    private static readonly Regex NumberPattern = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex OrderPattern = new(@"(?<order>[BHTL]{3})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string DisplayText => Height > 0
        ? $"{Width} × {Depth} × {Height} mm (B × T × H)"
        : $"{Width} × {Depth} mm (B × T)";

    public static bool TryParse(string? value, out ProductDimensionsMm dimensions)
    {
        dimensions = default;
        var text = (value ?? string.Empty).Trim();
        var numbers = NumberPattern.Matches(text)
            .Select(match => int.Parse(match.Value, CultureInfo.InvariantCulture))
            .Take(3)
            .ToArray();
        if (numbers.Length is < 2 or > 3 || numbers.Any(number => number <= 0))
        {
            return false;
        }

        if (numbers.Length == 2)
        {
            dimensions = new ProductDimensionsMm(numbers[0], numbers[1], 0);
            return true;
        }

        var compact = Regex.Replace(text.ToUpperInvariant(), @"[^BHTL]", string.Empty);
        var orderMatch = OrderPattern.Match(compact);
        var order = orderMatch.Success ? orderMatch.Groups["order"].Value.Replace('L', 'T') : "BTH";
        var width = 0;
        var depth = 0;
        var height = 0;
        for (var index = 0; index < 3; index++)
        {
            switch (order[index])
            {
                case 'B': width = numbers[index]; break;
                case 'T': depth = numbers[index]; break;
                case 'H': height = numbers[index]; break;
            }
        }

        if (width <= 0 || depth <= 0 || height <= 0)
        {
            return false;
        }

        dimensions = new ProductDimensionsMm(width, depth, height);
        return true;
    }
}
