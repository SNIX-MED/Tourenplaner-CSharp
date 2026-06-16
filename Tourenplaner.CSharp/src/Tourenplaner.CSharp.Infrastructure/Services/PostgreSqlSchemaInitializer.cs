using Npgsql;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Services;

public sealed class PostgreSqlSchemaInitializer
{
    public async Task EnsureSchemaAsync(NpgsqlConnection connection, PostgreSqlStorageSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(settings);

        var schema = NormalizeSchema(settings.Schema);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE SCHEMA IF NOT EXISTS "{schema}";

            CREATE TABLE IF NOT EXISTS "{schema}"."orders" (
                id text PRIMARY KEY,
                payload jsonb NOT NULL,
                updated_at timestamptz NOT NULL DEFAULT timezone('utc', now())
            );

            CREATE TABLE IF NOT EXISTS "{schema}"."tours" (
                id text PRIMARY KEY,
                payload jsonb NOT NULL,
                updated_at timestamptz NOT NULL DEFAULT timezone('utc', now())
            );

            CREATE TABLE IF NOT EXISTS "{schema}"."employees" (
                id text PRIMARY KEY,
                payload jsonb NOT NULL,
                updated_at timestamptz NOT NULL DEFAULT timezone('utc', now())
            );

            CREATE TABLE IF NOT EXISTS "{schema}"."vehicles" (
                id text PRIMARY KEY,
                payload jsonb NOT NULL,
                updated_at timestamptz NOT NULL DEFAULT timezone('utc', now())
            );

            CREATE TABLE IF NOT EXISTS "{schema}"."tour_records" (
                id text PRIMARY KEY,
                payload jsonb NOT NULL,
                updated_at timestamptz NOT NULL DEFAULT timezone('utc', now())
            );

            CREATE TABLE IF NOT EXISTS "{schema}"."calendar_manual_entries" (
                id text PRIMARY KEY,
                payload jsonb NOT NULL,
                updated_at timestamptz NOT NULL DEFAULT timezone('utc', now())
            );

            CREATE TABLE IF NOT EXISTS "{schema}"."singletons" (
                key text PRIMARY KEY,
                payload jsonb NOT NULL,
                updated_at timestamptz NOT NULL DEFAULT timezone('utc', now())
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static string NormalizeSchema(string? schema)
    {
        var value = (schema ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(value) ? "app" : value;
    }
}
