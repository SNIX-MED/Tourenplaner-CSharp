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

            CREATE TABLE IF NOT EXISTS "{schema}"."collaboration_locks" (
                resource_kind text NOT NULL,
                resource_id text NOT NULL,
                user_name text NOT NULL,
                session_id text NOT NULL,
                acquired_at timestamptz NOT NULL,
                heartbeat_at timestamptz NOT NULL,
                expires_at timestamptz NOT NULL,
                PRIMARY KEY (resource_kind, resource_id)
            );

            CREATE INDEX IF NOT EXISTS collaboration_locks_session_idx
                ON "{schema}"."collaboration_locks" (session_id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static string NormalizeSchema(string? schema)
    {
        var value = (schema ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(value) ? "app" : value;
    }
}
