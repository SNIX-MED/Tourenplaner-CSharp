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

            CREATE TABLE IF NOT EXISTS "{schema}"."change_history" (
                change_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                transaction_id bigint NOT NULL,
                client_instance text NOT NULL,
                user_name text NOT NULL DEFAULT '',
                table_name text NOT NULL,
                entity_id text NOT NULL,
                operation char(1) NOT NULL,
                history_action text NOT NULL DEFAULT 'normal',
                before_payload jsonb NULL,
                after_payload jsonb NULL,
                before_updated_at timestamptz NULL,
                after_updated_at timestamptz NULL,
                changed_at timestamptz NOT NULL DEFAULT timezone('utc', now())
            );

            ALTER TABLE "{schema}"."change_history"
                ADD COLUMN IF NOT EXISTS history_action text NOT NULL DEFAULT 'normal';

            CREATE INDEX IF NOT EXISTS change_history_client_change_idx
                ON "{schema}"."change_history" (client_instance, change_id);

            CREATE INDEX IF NOT EXISTS change_history_changed_at_idx
                ON "{schema}"."change_history" (changed_at);

            CREATE OR REPLACE FUNCTION "{schema}"."capture_change_history"()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $history$
            DECLARE
                app_name text := current_setting('application_name', true);
                client_id text;
                actor_name text;
                entity_key text;
            BEGIN
                IF app_name NOT LIKE 'GAWELA|%' THEN
                    IF TG_OP = 'DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
                END IF;

                client_id := split_part(app_name, '|', 2);
                actor_name := split_part(app_name, '|', 3);
                IF client_id = '' THEN
                    IF TG_OP = 'DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
                END IF;

                IF TG_ARGV[0] = 'key' THEN
                    entity_key := CASE WHEN TG_OP = 'DELETE' THEN OLD.key ELSE NEW.key END;
                ELSE
                    entity_key := CASE WHEN TG_OP = 'DELETE' THEN OLD.id ELSE NEW.id END;
                END IF;

                INSERT INTO "{schema}"."change_history" (
                    transaction_id, client_instance, user_name, table_name, entity_id, operation, history_action,
                    before_payload, after_payload, before_updated_at, after_updated_at)
                VALUES (
                    txid_current(), client_id, actor_name, TG_TABLE_NAME, entity_key,
                    CASE TG_OP WHEN 'INSERT' THEN 'I' WHEN 'UPDATE' THEN 'U' ELSE 'D' END,
                    COALESCE(NULLIF(current_setting('tourenplaner.history_action', true), ''), 'normal'),
                    CASE WHEN TG_OP = 'INSERT' THEN NULL ELSE OLD.payload END,
                    CASE WHEN TG_OP = 'DELETE' THEN NULL ELSE NEW.payload END,
                    CASE WHEN TG_OP = 'INSERT' THEN NULL ELSE OLD.updated_at END,
                    CASE WHEN TG_OP = 'DELETE' THEN NULL ELSE NEW.updated_at END);

                IF TG_OP = 'DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
            END;
            $history$;

            DO $triggers$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'orders_change_history' AND tgrelid = '"{schema}"."orders"'::regclass AND NOT tgisinternal) THEN
                    CREATE TRIGGER orders_change_history AFTER INSERT OR UPDATE OR DELETE ON "{schema}"."orders"
                    FOR EACH ROW EXECUTE FUNCTION "{schema}"."capture_change_history"('id');
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'tour_records_change_history' AND tgrelid = '"{schema}"."tour_records"'::regclass AND NOT tgisinternal) THEN
                    CREATE TRIGGER tour_records_change_history AFTER INSERT OR UPDATE OR DELETE ON "{schema}"."tour_records"
                    FOR EACH ROW EXECUTE FUNCTION "{schema}"."capture_change_history"('id');
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'employees_change_history' AND tgrelid = '"{schema}"."employees"'::regclass AND NOT tgisinternal) THEN
                    CREATE TRIGGER employees_change_history AFTER INSERT OR UPDATE OR DELETE ON "{schema}"."employees"
                    FOR EACH ROW EXECUTE FUNCTION "{schema}"."capture_change_history"('id');
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'vehicles_change_history' AND tgrelid = '"{schema}"."vehicles"'::regclass AND NOT tgisinternal) THEN
                    CREATE TRIGGER vehicles_change_history AFTER INSERT OR UPDATE OR DELETE ON "{schema}"."vehicles"
                    FOR EACH ROW EXECUTE FUNCTION "{schema}"."capture_change_history"('id');
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'calendar_entries_change_history' AND tgrelid = '"{schema}"."calendar_manual_entries"'::regclass AND NOT tgisinternal) THEN
                    CREATE TRIGGER calendar_entries_change_history AFTER INSERT OR UPDATE OR DELETE ON "{schema}"."calendar_manual_entries"
                    FOR EACH ROW EXECUTE FUNCTION "{schema}"."capture_change_history"('id');
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'singletons_change_history' AND tgrelid = '"{schema}"."singletons"'::regclass AND NOT tgisinternal) THEN
                    CREATE TRIGGER singletons_change_history AFTER INSERT OR UPDATE OR DELETE ON "{schema}"."singletons"
                    FOR EACH ROW EXECUTE FUNCTION "{schema}"."capture_change_history"('key');
                END IF;
            END
            $triggers$;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static string NormalizeSchema(string? schema)
    {
        var value = (schema ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(value) ? "app" : value;
    }
}
