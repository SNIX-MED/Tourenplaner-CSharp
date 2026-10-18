using System.Globalization;

namespace Tourenplaner.CSharp.Infrastructure.Repositories;

internal static class RepositoryConcurrencyToken
{
    public static string Create()
    {
        return Format(DateTimeOffset.UtcNow);
    }

    public static string Format(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    public static DateTimeOffset? Parse(string? token)
    {
        return string.IsNullOrWhiteSpace(token)
            ? null
            : DateTimeOffset.Parse(token.Trim());
    }

    public static DateTimeOffset ReadTimestamp(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value switch
        {
            DateTimeOffset dateTimeOffset => dateTimeOffset,
            DateTime dateTime when dateTime.Kind == DateTimeKind.Unspecified =>
                new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            DateTime dateTime => new DateTimeOffset(dateTime.ToUniversalTime(), TimeSpan.Zero),
            _ => throw new InvalidOperationException(
                $"Unerwarteter PostgreSQL-Zeitstempeltyp: {value.GetType().FullName}")
        };
    }
}
