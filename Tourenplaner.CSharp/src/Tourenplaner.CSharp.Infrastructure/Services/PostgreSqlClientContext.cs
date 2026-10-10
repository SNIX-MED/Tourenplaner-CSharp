namespace Tourenplaner.CSharp.Infrastructure.Services;

public static class PostgreSqlClientContext
{
    private static readonly object Gate = new();
    private static string _clientInstanceId = string.Empty;
    private static string _userName = string.Empty;

    public static void Configure(Guid clientInstanceId, string? userName)
    {
        lock (Gate)
        {
            _clientInstanceId = clientInstanceId.ToString("N");
            _userName = NormalizeUserName(userName);
        }
    }

    public static string BuildApplicationName()
    {
        lock (Gate)
        {
            return string.IsNullOrWhiteSpace(_clientInstanceId)
                ? "GAWELA"
                : $"GAWELA|{_clientInstanceId}|{_userName}";
        }
    }

    private static string NormalizeUserName(string? value)
    {
        var normalized = string.Concat((value ?? string.Empty)
            .Trim()
            .Where(character => character != '|'));
        return normalized.Length <= 20 ? normalized : normalized[..20];
    }
}
