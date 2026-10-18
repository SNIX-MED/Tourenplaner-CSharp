using System.Text.Json;

namespace Tourenplaner.CSharp.Infrastructure.Repositories;

internal static class RepositoryJsonSerializerOptions
{
    public static JsonSerializerOptions FileRepository { get; } = Create(
        propertyNamingPolicy: JsonNamingPolicy.CamelCase,
        propertyNameCaseInsensitive: true,
        writeIndented: true);

    public static JsonSerializerOptions PostgreSqlPayload { get; } = Create(
        propertyNamingPolicy: null,
        propertyNameCaseInsensitive: true,
        writeIndented: false);

    public static JsonSerializerOptions FileStoreRead { get; } = Create(
        propertyNamingPolicy: null,
        propertyNameCaseInsensitive: true,
        writeIndented: false);

    public static JsonSerializerOptions FileStoreWrite { get; } = Create(
        propertyNamingPolicy: null,
        propertyNameCaseInsensitive: false,
        writeIndented: true);

    private static JsonSerializerOptions Create(
        JsonNamingPolicy? propertyNamingPolicy,
        bool propertyNameCaseInsensitive,
        bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = propertyNamingPolicy,
            PropertyNameCaseInsensitive = propertyNameCaseInsensitive,
            WriteIndented = writeIndented
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
