using Npgsql;
using System.Text.Json;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.App.Services;

public sealed class PostgreSqlAppDataHistoryService : IAppDataHistoryService
{
    private sealed record RowState(string? Payload, DateTimeOffset? UpdatedAt)
    {
        public bool Exists => Payload is not null;
    }

    private sealed record ChangeItem(string TableName, string EntityId, RowState Before, RowState After);
    private sealed record HistoryEntry(string Description, IReadOnlyList<ChangeItem> Items);
    private sealed record AuditRow(
        long ChangeId,
        string TableName,
        string EntityId,
        string? BeforePayload,
        string? AfterPayload,
        DateTimeOffset? BeforeUpdatedAt,
        DateTimeOffset? AfterUpdatedAt);

    private const int MaxHistoryEntries = 80;
    private static readonly TimeSpan CaptureDebounce = TimeSpan.FromMilliseconds(160);
    private static readonly HashSet<string> AllowedTables =
    [
        "orders", "tour_records", "employees", "vehicles", "calendar_manual_entries", "singletons"
    ];

    private readonly object _gate = new();
    private readonly AppDataSyncService _dataSyncService;
    private readonly PostgreSqlStorageSettings _settings;
    private readonly PostgreSqlConnectionFactory _connectionFactory = new();
    private readonly PostgreSqlSchemaInitializer _schemaInitializer = new();
    private readonly Stack<HistoryEntry> _undoStack = new();
    private readonly Stack<HistoryEntry> _redoStack = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly Guid _sourceId = Guid.NewGuid();
    private CancellationTokenSource? _captureCts;
    private long _lastChangeId;
    private bool _initialized;
    private bool _restoring;
    private string _pendingDescription = "Änderung";

    public PostgreSqlAppDataHistoryService(
        AppDataSyncService dataSyncService,
        PostgreSqlStorageSettings settings,
        string? userName)
    {
        _dataSyncService = dataSyncService;
        _settings = settings;
        PostgreSqlClientContext.Configure(dataSyncService.ClientInstanceId, userName);
    }

    public event EventHandler? StateChanged;

    public bool CanUndo { get { lock (_gate) return _undoStack.Count > 0; } }
    public bool CanRedo { get { lock (_gate) return _redoStack.Count > 0; } }
    public string UndoDescription { get { lock (_gate) return _undoStack.TryPeek(out var item) ? item.Description : string.Empty; } }
    public string RedoDescription { get { lock (_gate) return _redoStack.TryPeek(out var item) ? item.Description : string.Empty; } }
    public bool HasPersistentHistory => true;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);

        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        var retentionDays = await ReadRetentionDaysAsync(connection, schema, cancellationToken);
        await CleanupExpiredEntriesCoreAsync(connection, schema, retentionDays, cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""SELECT COALESCE(MAX(change_id), 0) FROM "{schema}"."change_history" WHERE client_instance = @client;""";
            command.Parameters.AddWithValue("client", ClientId);
            _lastChangeId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        }

        _dataSyncService.DataChanged += OnDataChanged;
        _initialized = true;
        RaiseStateChanged();
    }

    public async Task<IReadOnlyList<AppDataHistoryDisplayItem>> LoadRecentEntriesAsync(
        int maximumCount = 100,
        CancellationToken cancellationToken = default)
    {
        var count = Math.Clamp(maximumCount, 1, 500);
        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);
        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT change_id, changed_at, user_name, operation, history_action, table_name, entity_id
            FROM "{schema}"."change_history"
            ORDER BY change_id DESC
            LIMIT @count;
            """;
        command.Parameters.AddWithValue("count", count);

        var result = new List<AppDataHistoryDisplayItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var tableName = reader.GetString(5);
            result.Add(new AppDataHistoryDisplayItem(
                reader.GetInt64(0),
                reader.GetFieldValue<DateTimeOffset>(1).ToLocalTime(),
                string.IsNullOrWhiteSpace(reader.GetString(2)) ? "Unbekannt" : reader.GetString(2),
                GetActionLabel(reader.GetString(3), reader.GetString(4)),
                GetEntityLabel(tableName),
                reader.GetString(6)));
        }
        return result;
    }

    public async Task<int> CleanupExpiredEntriesAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        var normalizedDays = Math.Clamp(retentionDays, 1, 3650);
        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
        await _schemaInitializer.EnsureSchemaAsync(connection, _settings, cancellationToken);
        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        return await CleanupExpiredEntriesCoreAsync(connection, schema, normalizedDays, cancellationToken);
    }

    public async Task ResetSessionAsync(string? userName, CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            PostgreSqlClientContext.Configure(_dataSyncService.ClientInstanceId, userName);
            lock (_gate)
            {
                _captureCts?.Cancel();
                _undoStack.Clear();
                _redoStack.Clear();
            }

            await using var connection = _connectionFactory.CreateConnection(_settings);
            await connection.OpenAsync(cancellationToken);
            var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""SELECT COALESCE(MAX(change_id), 0) FROM "{schema}"."change_history" WHERE client_instance = @client;""";
            command.Parameters.AddWithValue("client", ClientId);
            _lastChangeId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        }
        finally
        {
            _operationGate.Release();
        }
        RaiseStateChanged();
    }

    public Task UndoAsync() => RestoreTopAsync(_undoStack, _redoStack);

    public Task RedoAsync() => RestoreTopAsync(_redoStack, _undoStack);

    public void Dispose()
    {
        _dataSyncService.DataChanged -= OnDataChanged;
        lock (_gate)
        {
            _captureCts?.Cancel();
            _captureCts?.Dispose();
            _captureCts = null;
        }
        _operationGate.Dispose();
    }

    private string ClientId => _dataSyncService.ClientInstanceId.ToString("N");

    private void OnDataChanged(object? sender, AppDataChangedEventArgs args)
    {
        if (!_initialized || _restoring || args.ClientInstanceId != _dataSyncService.ClientInstanceId)
        {
            return;
        }

        ScheduleCapture(BuildDescriptionFromArgs(args));
    }

    private void ScheduleCapture(string description)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            _pendingDescription = string.IsNullOrWhiteSpace(description) ? "Änderung" : description.Trim();
            _captureCts?.Cancel();
            _captureCts?.Dispose();
            _captureCts = new CancellationTokenSource();
            cts = _captureCts;
        }

        CaptureAfterDelayAsync(cts).Forget();
    }

    private async Task CaptureAfterDelayAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(CaptureDebounce, cts.Token);
            await CaptureChangesAsync(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
    }

    private async Task CaptureChangesAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var rows = await LoadPendingRowsAsync(cancellationToken);
            if (rows.Count == 0)
            {
                return;
            }

            var items = CollapseRows(rows);
            _lastChangeId = rows[^1].ChangeId;
            if (items.Count == 0)
            {
                return;
            }

            string description;
            lock (_gate)
            {
                description = _pendingDescription;
                _undoStack.Push(new HistoryEntry(description, items));
                Trim(_undoStack);
                _redoStack.Clear();
            }
            RaiseStateChanged();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<List<AuditRow>> LoadPendingRowsAsync(CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync(cancellationToken);
        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT change_id, table_name, entity_id,
                   before_payload::text, after_payload::text,
                   before_updated_at, after_updated_at
            FROM "{schema}"."change_history"
            WHERE client_instance = @client AND change_id > @after AND history_action = 'normal'
            ORDER BY change_id;
            """;
        command.Parameters.AddWithValue("client", ClientId);
        command.Parameters.AddWithValue("after", _lastChangeId);

        var result = new List<AuditRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new AuditRow(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6)));
        }
        return result;
    }

    private static IReadOnlyList<ChangeItem> CollapseRows(IReadOnlyList<AuditRow> rows)
    {
        var order = new List<string>();
        var items = new Dictionary<string, ChangeItem>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!AllowedTables.Contains(row.TableName)) continue;
            var key = $"{row.TableName}\0{row.EntityId}";
            if (!items.TryGetValue(key, out var existing))
            {
                order.Add(key);
                items[key] = new ChangeItem(
                    row.TableName,
                    row.EntityId,
                    new RowState(row.BeforePayload, row.BeforeUpdatedAt),
                    new RowState(row.AfterPayload, row.AfterUpdatedAt));
            }
            else
            {
                items[key] = existing with
                {
                    After = new RowState(row.AfterPayload, row.AfterUpdatedAt)
                };
            }
        }

        return order
            .Select(key => items[key])
            .Where(item => !StatesEquivalent(item.Before, item.After))
            .ToList();
    }

    private async Task RestoreTopAsync(Stack<HistoryEntry> source, Stack<HistoryEntry> destination)
    {
        await _operationGate.WaitAsync();
        try
        {
            HistoryEntry entry;
            lock (_gate)
            {
                if (!source.TryPeek(out entry!)) return;
            }

            _restoring = true;
            IReadOnlyList<ChangeItem> inverseItems;
            try
            {
                var historyAction = ReferenceEquals(source, _undoStack) ? "undo" : "redo";
                inverseItems = await ApplyEntryAsync(entry, historyAction);
            }
            finally
            {
                _restoring = false;
            }

            lock (_gate)
            {
                source.Pop();
                destination.Push(new HistoryEntry(entry.Description, inverseItems));
                Trim(destination);
            }

            PublishFullReload();
            RaiseStateChanged();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<IReadOnlyList<ChangeItem>> ApplyEntryAsync(HistoryEntry entry, string historyAction)
    {
        await using var connection = _connectionFactory.CreateConnection(_settings);
        await connection.OpenAsync();
        var schema = PostgreSqlSchemaInitializer.NormalizeSchema(_settings.Schema);
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var context = connection.CreateCommand())
        {
            context.Transaction = transaction;
            context.CommandText = "SELECT set_config('tourenplaner.history_action', @action, true);";
            context.Parameters.AddWithValue("action", historyAction);
            await context.ExecuteNonQueryAsync();
        }

        var inverse = new List<ChangeItem>();
        try
        {
            foreach (var item in entry.Items.Reverse())
            {
                var restored = await RestoreItemAsync(connection, transaction, schema, item);
                inverse.Insert(0, new ChangeItem(item.TableName, item.EntityId, item.After, restored));
            }
            await transaction.CommitAsync();
            return inverse;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<RowState> RestoreItemAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string schema,
        ChangeItem item)
    {
        if (!AllowedTables.Contains(item.TableName))
        {
            throw new InvalidOperationException("Unbekannter Datentyp im Änderungsverlauf.");
        }

        var keyColumn = item.TableName == "singletons" ? "key" : "id";
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        if (!item.Before.Exists && item.After.Exists)
        {
            command.CommandText = $"""
                DELETE FROM "{schema}"."{item.TableName}"
                WHERE "{keyColumn}" = @id AND updated_at = @expectedAt
                  AND payload = CAST(@expectedPayload AS jsonb)
                RETURNING updated_at;
                """;
            AddExpectedParameters(command, item);
            var deleted = await command.ExecuteScalarAsync();
            EnsureAffected(deleted, item);
            return new RowState(null, null);
        }

        if (item.Before.Exists && !item.After.Exists)
        {
            command.CommandText = $"""
                INSERT INTO "{schema}"."{item.TableName}" ("{keyColumn}", payload, updated_at)
                VALUES (@id, CAST(@payload AS jsonb), @updatedAt)
                ON CONFLICT ("{keyColumn}") DO NOTHING
                RETURNING updated_at;
                """;
            var next = DateTimeOffset.UtcNow;
            command.Parameters.AddWithValue("id", item.EntityId);
            command.Parameters.AddWithValue("payload", item.Before.Payload!);
            command.Parameters.AddWithValue("updatedAt", next);
            var inserted = await command.ExecuteScalarAsync();
            EnsureAffected(inserted, item);
            return new RowState(item.Before.Payload, ReadTimestamp(inserted!));
        }

        command.CommandText = $"""
            UPDATE "{schema}"."{item.TableName}"
            SET payload = CAST(@payload AS jsonb), updated_at = @updatedAt
            WHERE "{keyColumn}" = @id AND updated_at = @expectedAt
              AND payload = CAST(@expectedPayload AS jsonb)
            RETURNING updated_at;
            """;
        var updatedAt = DateTimeOffset.UtcNow;
        command.Parameters.AddWithValue("payload", item.Before.Payload!);
        command.Parameters.AddWithValue("updatedAt", updatedAt);
        AddExpectedParameters(command, item);
        var updated = await command.ExecuteScalarAsync();
        EnsureAffected(updated, item);
        return new RowState(item.Before.Payload, ReadTimestamp(updated!));
    }

    private static void AddExpectedParameters(NpgsqlCommand command, ChangeItem item)
    {
        command.Parameters.AddWithValue("id", item.EntityId);
        command.Parameters.AddWithValue("expectedAt", item.After.UpdatedAt!.Value);
        command.Parameters.AddWithValue("expectedPayload", item.After.Payload!);
    }

    private static void EnsureAffected(object? result, ChangeItem item)
    {
        if (result is null)
        {
            throw new PostgreSqlHistoryConflictException(
                $"Die Änderung kann nicht rückgängig gemacht werden, weil {GetEntityLabel(item.TableName)} " +
                $"{item.EntityId} zwischenzeitlich verändert wurde.");
        }
    }

    private static DateTimeOffset ReadTimestamp(object value) => value switch
    {
        DateTimeOffset offset => offset,
        DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
        _ => throw new InvalidOperationException("Unerwarteter PostgreSQL-Zeitstempel.")
    };

    private void PublishFullReload()
    {
        const AppDataKind all = AppDataKind.Orders | AppDataKind.Tours | AppDataKind.Employees |
                                AppDataKind.Vehicles | AppDataKind.Settings;
        _restoring = true;
        try
        {
            _dataSyncService.Publish(new AppDataChangedEventArgs(_sourceId, all));
        }
        finally
        {
            _restoring = false;
        }
    }

    private static bool StatesEquivalent(RowState left, RowState right)
        => left.Exists == right.Exists && string.Equals(left.Payload, right.Payload, StringComparison.Ordinal);

    private static string GetEntityLabel(string tableName) => tableName switch
    {
        "orders" => "Auftrag",
        "tour_records" => "Tour",
        "employees" => "Mitarbeiter",
        "vehicles" => "Fahrzeug",
        "calendar_manual_entries" => "Kalendereintrag",
        "singletons" => "Einstellung",
        _ => "Datensatz"
    };

    private static string GetActionLabel(string operation, string historyAction)
    {
        var action = operation switch
        {
            "I" => "Erstellt",
            "U" => "Aktualisiert",
            "D" => "Gelöscht",
            _ => "Geändert"
        };
        return historyAction switch
        {
            "undo" => $"Rückgängig: {action}",
            "redo" => $"Wiederholt: {action}",
            _ => action
        };
    }

    private async Task<int> ReadRetentionDaysAsync(
        NpgsqlConnection connection,
        string schema,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT payload::text FROM "{schema}"."singletons" WHERE key = 'app_settings';""";
        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(payload, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                var configured = settings?.PostgreSqlStorage?.ChangeHistoryRetentionDays ?? 0;
                if (configured > 0) return Math.Clamp(configured, 1, 3650);
            }
            catch (JsonException)
            {
                // Fall back to the local bootstrap setting when shared settings are malformed.
            }
        }

        return Math.Clamp(
            _settings.ChangeHistoryRetentionDays <= 0
                ? PostgreSqlStorageSettings.DefaultChangeHistoryRetentionDays
                : _settings.ChangeHistoryRetentionDays,
            1,
            3650);
    }

    private static async Task<int> CleanupExpiredEntriesCoreAsync(
        NpgsqlConnection connection,
        string schema,
        int retentionDays,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE FROM "{schema}"."change_history"
            WHERE changed_at < timezone('utc', now()) - (@days * interval '1 day');
            """;
        command.Parameters.AddWithValue("days", Math.Clamp(retentionDays, 1, 3650));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Trim(Stack<HistoryEntry> stack)
    {
        if (stack.Count <= MaxHistoryEntries) return;
        var kept = stack.Take(MaxHistoryEntries).Reverse().ToArray();
        stack.Clear();
        foreach (var item in kept) stack.Push(item);
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private static string BuildDescriptionFromArgs(AppDataChangedEventArgs args)
    {
        if (args.Kinds == AppDataKind.Settings) return "Einstellungen ändern";
        if (args.Kinds == AppDataKind.Orders) return DescribeEntity("Auftrag", "Aufträge", args);
        if (args.Kinds == AppDataKind.Tours) return DescribeEntity("Tour", "Touren", args);
        if (args.Kinds == AppDataKind.Employees) return DescribeEntity("Mitarbeiter", "Mitarbeiter", args);
        if (args.Kinds == AppDataKind.Vehicles) return DescribeEntity("Fahrzeug", "Fahrzeuge", args);
        return "Daten ändern";
    }

    private static string DescribeEntity(string singular, string plural, AppDataChangedEventArgs args)
    {
        var hadPrevious = !string.IsNullOrWhiteSpace(args.PreviousId);
        var hasCurrent = !string.IsNullOrWhiteSpace(args.CurrentId);
        if (!hadPrevious && hasCurrent) return $"{singular} erstellen";
        if (hadPrevious && !hasCurrent) return $"{singular} löschen";
        if (hadPrevious && hasCurrent)
        {
            return string.Equals(args.PreviousId, args.CurrentId, StringComparison.OrdinalIgnoreCase)
                ? $"{singular} aktualisieren"
                : $"{singular} umbenennen";
        }
        return $"{plural} ändern";
    }
}
