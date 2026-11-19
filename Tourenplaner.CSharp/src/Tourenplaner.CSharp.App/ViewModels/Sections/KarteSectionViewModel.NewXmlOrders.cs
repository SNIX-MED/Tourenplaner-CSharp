using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed partial class KarteSectionViewModel
{
    private readonly HashSet<string> _seenXmlImportOrderKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _newXmlReviewSaveGate = new(1, 1);
    private string _newXmlReviewUser = string.Empty;
    private bool _showOnlyNewXmlOrders;
    private int _newXmlOrdersCount;
    private int _newXmlFocusRevision;
    public ICommand ShowNewXmlOrdersCommand { get; private set; } = null!;
    public ICommand MarkVisibleNewXmlOrdersSeenCommand { get; private set; } = null!;
    public ICommand MarkAllNewXmlOrdersSeenCommand { get; private set; } = null!;
    public bool HasNewXmlOrders => NewXmlOrdersCount > 0;
    public int NewXmlOrdersCount => _newXmlOrdersCount;
    public string NewXmlOrdersLabel => $"Neue Aufträge · {NewXmlOrdersCount}";
    public int NewXmlFocusRevision => _newXmlFocusRevision;
    public IReadOnlyList<GeoPoint> NewXmlFocusPoints { get; private set; } = [];

    public bool ShowOnlyNewXmlOrders
    {
        get => _showOnlyNewXmlOrders;
        set
        {
            if (SetProperty(ref _showOnlyNewXmlOrders, value))
                RebuildOrderGrid(preserveSelectedOrderInstance: true);
        }
    }

    private void InitializeNewXmlOrderCommands()
    {
        ShowNewXmlOrdersCommand = new DelegateCommand(() =>
        {
            NewXmlFocusPoints = VisibleNewXmlPins().Select(pin => new GeoPoint(pin.Latitude, pin.Longitude)).ToArray();
            _newXmlFocusRevision++;
            OnPropertyChanged(nameof(NewXmlFocusRevision));
        }, () => VisibleNewXmlPins().Count > 0);
        MarkAllNewXmlOrdersSeenCommand = new AsyncCommand(
            () => MarkNewXmlOrdersSeenAsync(_allOrders.Where(IsNewXmlOrder).Select(order => order.Id)),
            () => HasNewXmlOrders);
        MarkVisibleNewXmlOrdersSeenCommand = new AsyncCommand(
            () => MarkNewXmlOrdersSeenAsync(VisibleNewXmlPins().Select(pin => pin.OrderId)),
            () => VisibleNewXmlPins().Count > 0);
    }

    private bool IsNewXmlOrder(Order order) =>
        NewXmlOrderReviewService.IsPending(order, _seenXmlImportOrderKeys) && !IsOrderAssignedOrInDraftRoute(order);

    private IReadOnlyList<MapOrderItem> VisibleNewXmlPins() => GetMapMarkerSnapshot()
        .Where(pin => pin.IsNewXmlImport && !pin.IsDimmed && double.IsFinite(pin.Latitude) && double.IsFinite(pin.Longitude) &&
            pin.Latitude is >= -90 and <= 90 && pin.Longitude is >= -180 and <= 180).ToList();

    private void LoadNewXmlOrderPreference(string userName, UserAppPreference preference)
    {
        if (!string.Equals(_newXmlReviewUser, userName, StringComparison.OrdinalIgnoreCase)) _seenXmlImportOrderKeys.Clear();
        _newXmlReviewUser = userName;
        _seenXmlImportOrderKeys.UnionWith(preference.SeenXmlImportOrderKeys ?? []);
    }

    private void UpdateNewXmlOrderReviewState()
    {
        _newXmlOrdersCount = _allOrders.Count(IsNewXmlOrder);
        OnPropertyChanged(nameof(NewXmlOrdersCount));
        OnPropertyChanged(nameof(HasNewXmlOrders));
        RaiseCanExecuteChangedIfSupported(MarkAllNewXmlOrdersSeenCommand);
        OnPropertyChanged(nameof(NewXmlOrdersLabel));
        RaiseCanExecuteChangedIfSupported(ShowNewXmlOrdersCommand);
        RaiseCanExecuteChangedIfSupported(MarkVisibleNewXmlOrdersSeenCommand);
    }

    public Task MarkNewXmlOrderSeenAsync(string markerId)
    {
        var order = _allOrders.FirstOrDefault(order => string.Equals(order.Id, markerId, StringComparison.OrdinalIgnoreCase) ||
            (order.Deliveries ?? []).Any(delivery => string.Equals($"{order.Id}::delivery::{delivery.Id}", markerId, StringComparison.OrdinalIgnoreCase)));
        return order is null ? Task.CompletedTask : MarkNewXmlOrdersSeenAsync([order.Id]);
    }

    private async Task MarkNewXmlOrdersSeenAsync(IEnumerable<string> orderIds)
    {
        var ids = orderIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keys = _allOrders.Where(order => ids.Contains(order.Id) && IsNewXmlOrder(order))
            .Select(NewXmlOrderReviewService.ReviewKey).ToArray();
        if (keys.Length == 0) return;
        var userName = _newXmlReviewUser;
        await _newXmlReviewSaveGate.WaitAsync();
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                if (!string.Equals(_newXmlReviewUser, userName, StringComparison.OrdinalIgnoreCase)) return;
                var settings = await _settingsRepository.LoadAsync();
                var preference = settings.ResolveUserPreference(userName);
                preference.SeenXmlImportOrderKeys = (preference.SeenXmlImportOrderKeys ?? []).Concat(keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                settings.SetUserPreference(userName, preference);
                try { await _settingsRepository.SaveAsync(settings); break; }
                catch (ConcurrencyConflictException) when (attempt < 2) { }
            }
            if (string.Equals(_newXmlReviewUser, userName, StringComparison.OrdinalIgnoreCase))
            {
                _seenXmlImportOrderKeys.UnionWith(keys);
                RebuildOrderGrid(preserveSelectedOrderInstance: true);
            }
            _dataSyncService.PublishSettings(_instanceId);
        }
        finally { _newXmlReviewSaveGate.Release(); }
    }
}
