using System.Text;

namespace Tourenplaner.CSharp.Infrastructure.Services;

public static class PostgreSqlClientContext
{
    private static readonly object Gate = new();
    private static string _clientInstanceId = string.Empty;
    private static string _userName = string.Empty;
    private static readonly string _lockSessionId = Guid.NewGuid().ToString("N")[..8];

    public static string LockSessionId => _lockSessionId;

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
                ? $"GAWELA|||{_lockSessionId}"
                : $"GAWELA|{_clientInstanceId}|{_userName}|{_lockSessionId}";
        }
    }

    private static string NormalizeUserName(string? value)
    {
        var normalized = string.Concat((value ?? string.Empty)
            .Trim()
            .Where(character => character != '|'));
        // PostgreSQL truncates application_name at 63 bytes, not 64 characters.
        // "GAWELA|" + client GUID + two separators + session ID occupy 49 ASCII
        // bytes. Reserve the full session suffix so our own locks stay recognizable.
        const int userNameByteBudget = 63 - (7 + 32 + 1 + 1 + 8);
        var result = new StringBuilder();
        var byteCount = 0;
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (byteCount + rune.Utf8SequenceLength > userNameByteBudget) break;
            result.Append(rune.ToString());
            byteCount += rune.Utf8SequenceLength;
        }
        return result.ToString();
    }
}
