namespace Tourenplaner.CSharp.App.Services;

[Flags]
public enum AppDataKind
{
    None = 0,
    Orders = 1,
    Tours = 2,
    Vehicles = 4,
    Employees = 8,
    Settings = 16
}

public sealed record AppDataChangedEventArgs(
    Guid SourceId,
    AppDataKind Kinds,
    string? PreviousId = null,
    string? CurrentId = null,
    Guid? ClientInstanceId = null);

public sealed record OrderChangedEventArgs(
    Guid SourceId,
    string? PreviousOrderId,
    string? CurrentOrderId);

public sealed record AppDataSyncDiagnosticsSnapshot(
    bool IsRemoteSyncEnabled,
    bool IsRemoteSyncConnected,
    string StatusText,
    DateTime? LastStatusChangeUtc,
    DateTime? LastLocalPublishUtc,
    DateTime? LastRemoteChangeUtc,
    DateTime? LastAnyChangeUtc,
    string? LastErrorMessage);

public sealed class AppDataSyncService
{
    private readonly Guid _clientInstanceId;
    private readonly IAppDataSyncBridge? _bridge;
    private readonly bool _isRemoteSyncEnabled;
    private bool _isRemoteSyncConnected;
    private string _statusText;
    private DateTime? _lastStatusChangeUtc;
    private DateTime? _lastLocalPublishUtc;
    private DateTime? _lastRemoteChangeUtc;
    private DateTime? _lastAnyChangeUtc;
    private string? _lastErrorMessage;

    public AppDataSyncService(IAppDataSyncBridge? bridge = null, Guid? clientInstanceId = null)
    {
        _bridge = bridge;
        _clientInstanceId = clientInstanceId ?? Guid.NewGuid();
        _isRemoteSyncEnabled = bridge is not null;
        _statusText = _isRemoteSyncEnabled
            ? "PostgreSQL-Sync wird vorbereitet."
            : "Lokaler Dateibetrieb ohne Live-Sync.";
        if (_bridge is not null)
        {
            _bridge.RemoteDataChanged += OnRemoteDataChanged;
            _bridge.StatusChanged += OnBridgeStatusChanged;
        }
    }

    public event EventHandler<AppDataChangedEventArgs>? DataChanged;
    public event EventHandler<OrderChangedEventArgs>? OrdersChanged;
    public event EventHandler<AppDataSyncDiagnosticsSnapshot>? StatusChanged;

    public AppDataSyncDiagnosticsSnapshot GetDiagnosticsSnapshot()
        => new(
            _isRemoteSyncEnabled,
            _isRemoteSyncConnected,
            _statusText,
            _lastStatusChangeUtc,
            _lastLocalPublishUtc,
            _lastRemoteChangeUtc,
            _lastAnyChangeUtc,
            _lastErrorMessage);

    public void Publish(AppDataChangedEventArgs args)
    {
        var normalized = NormalizeArgs(args);
        var timestamp = DateTime.UtcNow;
        _lastLocalPublishUtc = timestamp;
        _lastAnyChangeUtc = timestamp;
        if (!_isRemoteSyncEnabled)
        {
            UpdateStatus("Lokale Aenderung gespeichert.", false, null, timestamp);
        }
        else
        {
            UpdateStatus("Lokale Aenderung wird synchronisiert.", _isRemoteSyncConnected, null, timestamp);
        }
        PublishCore(normalized);
        _ = _bridge?.BroadcastAsync(normalized);
    }

    private void PublishRemote(AppDataChangedEventArgs args)
    {
        var normalized = NormalizeArgs(args);
        if (normalized.ClientInstanceId == _clientInstanceId)
        {
            return;
        }

        var timestamp = DateTime.UtcNow;
        _lastRemoteChangeUtc = timestamp;
        _lastAnyChangeUtc = timestamp;
        UpdateStatus("Externe Aenderung empfangen.", _isRemoteSyncConnected, null, timestamp);
        PublishCore(normalized);
    }

    private void PublishCore(AppDataChangedEventArgs args)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(() => PublishCore(args));
            return;
        }

        DataChanged?.Invoke(this, args);

        if (args.Kinds.HasFlag(AppDataKind.Orders))
        {
            OrdersChanged?.Invoke(this, new OrderChangedEventArgs(args.SourceId, args.PreviousId, args.CurrentId));
        }
    }

    private AppDataChangedEventArgs NormalizeArgs(AppDataChangedEventArgs args)
        => args.ClientInstanceId.HasValue
            ? args
            : args with { ClientInstanceId = _clientInstanceId };

    private void OnRemoteDataChanged(object? sender, AppDataChangedEventArgs args)
        => PublishRemote(args);

    private void OnBridgeStatusChanged(object? sender, AppDataSyncBridgeStatusChangedEventArgs args)
        => UpdateStatus(args.StatusText, args.IsConnected, args.ErrorMessage, args.OccurredAtUtc);

    public void PublishOrders(Guid sourceId, string? previousOrderId = null, string? currentOrderId = null)
    {
        Publish(new AppDataChangedEventArgs(sourceId, AppDataKind.Orders, previousOrderId, currentOrderId));
    }

    public void PublishTours(Guid sourceId, string? previousTourId = null, string? currentTourId = null)
    {
        Publish(new AppDataChangedEventArgs(sourceId, AppDataKind.Tours, previousTourId, currentTourId));
    }

    public void PublishVehicles(Guid sourceId, string? previousVehicleId = null, string? currentVehicleId = null)
    {
        Publish(new AppDataChangedEventArgs(sourceId, AppDataKind.Vehicles, previousVehicleId, currentVehicleId));
    }

    public void PublishEmployees(Guid sourceId, string? previousEmployeeId = null, string? currentEmployeeId = null)
    {
        Publish(new AppDataChangedEventArgs(sourceId, AppDataKind.Employees, previousEmployeeId, currentEmployeeId));
    }

    public void PublishSettings(Guid sourceId)
    {
        Publish(new AppDataChangedEventArgs(sourceId, AppDataKind.Settings));
    }

    private void UpdateStatus(string statusText, bool isConnected, string? errorMessage, DateTime occurredAtUtc)
    {
        _isRemoteSyncConnected = isConnected;
        _statusText = statusText;
        _lastStatusChangeUtc = occurredAtUtc;
        _lastErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage;
        StatusChanged?.Invoke(this, GetDiagnosticsSnapshot());
    }
}
