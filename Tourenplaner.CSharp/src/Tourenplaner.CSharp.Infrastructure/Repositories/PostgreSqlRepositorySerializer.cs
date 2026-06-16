using System.Text.Json;

namespace Tourenplaner.CSharp.Infrastructure.Repositories;

internal static class PostgreSqlRepositorySerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, SerializerOptions);
    }

    public static T Deserialize<T>(string json, Func<T> fallbackFactory)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return fallbackFactory();
        }

        return JsonSerializer.Deserialize<T>(json, SerializerOptions) ?? fallbackFactory();
    }
}
