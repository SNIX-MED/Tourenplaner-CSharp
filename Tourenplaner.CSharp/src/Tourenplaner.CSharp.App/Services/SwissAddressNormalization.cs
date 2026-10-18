using Tourenplaner.CSharp.Application.Common;

namespace Tourenplaner.CSharp.App.Services;

internal static class SwissAddressNormalization
{
    public static string NormalizeForComparison(string? value)
        => OrderAddressFormatting.NormalizeForSwissPinValidation(value);
}
