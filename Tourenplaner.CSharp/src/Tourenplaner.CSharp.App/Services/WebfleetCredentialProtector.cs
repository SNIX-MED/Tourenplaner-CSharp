using System.Security.Cryptography;
using System.Text;

namespace Tourenplaner.CSharp.App.Services;

internal static class WebfleetCredentialProtector
{
    private const string Prefix = "dpapi:";

    public static string Protect(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.StartsWith(Prefix, StringComparison.Ordinal)) return value ?? string.Empty;
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(bytes);
    }

    public static string Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith(Prefix, StringComparison.Ordinal)) return value ?? string.Empty;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(value[Prefix.Length..]), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }
    }
}
