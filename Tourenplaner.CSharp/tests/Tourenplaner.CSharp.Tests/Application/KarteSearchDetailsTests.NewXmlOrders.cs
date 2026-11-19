using System.Text.Json;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed partial class KarteSearchDetailsTests
{
    [Fact]
    public async Task MarkAllNewOrdersReadIncludesFilteredAndUnlocatedOrdersAndHidesButton()
    {
        var first = NewXmlOrder("FIRST");
        var second = NewXmlOrder("SECOND");
        second.Location = null;
        var orders = new List<Order> { first, second };
        var store = new FullWarningOrderStore(orders);
        var settings = new NewXmlPreferencesStore();
        var vm = CreateViewModel(store, settingsRepository: settings);
        var visibilityChanges = 0;
        vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.HasNewXmlOrders)) visibilityChanges++; };
        await vm.RefreshAsync();
        vm.SearchText = "FIRST";
        Assert.True(vm.HasNewXmlOrders);
        Assert.True(vm.MarkAllNewXmlOrdersSeenCommand.CanExecute(null));
        vm.MarkAllNewXmlOrdersSeenCommand.Execute(null);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (vm.HasNewXmlOrders) await Task.Delay(10, timeout.Token);
        Assert.Equal(0, vm.NewXmlOrdersCount);
        Assert.False(vm.MarkAllNewXmlOrdersSeenCommand.CanExecute(null));
        Assert.All(vm.GetMapMarkerSnapshot(), pin => Assert.False(pin.IsNewXmlImport));
        Assert.True(visibilityChanges >= 2);
        var reloaded = CreateViewModel(store, settingsRepository: settings);
        await reloaded.RefreshAsync();
        Assert.False(reloaded.HasNewXmlOrders);
        orders.Add(NewXmlOrder("LATER"));
        await vm.RefreshAsync();
        Assert.True(vm.HasNewXmlOrders);
        Assert.Equal(1, vm.NewXmlOrdersCount);
        Assert.True(vm.MarkAllNewXmlOrdersSeenCommand.CanExecute(null));
    }

    [Fact]
    public async Task NewMarkersFilterAndFocusPreserveExistingTourAndPinIndicators()
    {
        var (orders, tours) = RemoteStores();
        var imported = NewXmlOrder("C");
        imported.IstVorauszahlung = true;
        imported.AvisoStatus = "Bestätigt";
        orders.Items.Add(imported);
        var vm = CreateViewModel(orders, tours);
        await vm.RefreshAsync();
        await vm.FocusTourAsync(42);
        var geometry = vm.RouteGeometryPoints.ToArray();
        var marker = Assert.Single(vm.GetMapMarkerSnapshot(), pin => pin.OrderId == "C");
        var color = marker.StatusColorHex;
        Assert.True(marker.IsNewXmlImport);
        Assert.True(marker.IstVorauszahlung);
        Assert.Equal("Bestätigt", marker.AvisoStatusLabel);
        Assert.Equal(1, vm.NewXmlOrdersCount);
        Assert.Equal(0, vm.NewXmlFocusRevision);
        orders.Items.Add(NewXmlOrder("D"));
        await vm.RefreshAfterRemoteChangeAsync(new(Guid.NewGuid(), AppDataKind.Orders));
        Assert.Equal(2, vm.NewXmlOrdersCount);
        Assert.Equal(0, vm.NewXmlFocusRevision);
        Assert.Equal(geometry, vm.RouteGeometryPoints.ToArray());
        vm.ShowOnlyNewXmlOrders = true;
        Assert.Equal(new[] { "C", "D" }, vm.GetMapMarkerSnapshot().Select(pin => pin.OrderId).OrderBy(id => id));
        Assert.Equal(color, vm.GetMapMarkerSnapshot().Single(pin => pin.OrderId == "C").StatusColorHex);
        vm.ShowNewXmlOrdersCommand.Execute(null);
        Assert.Equal(1, vm.NewXmlFocusRevision);
        Assert.Equal(2, vm.NewXmlFocusPoints.Count);
        Assert.Equal(42, vm.LoadedTourId);
        Assert.Contains(vm.RouteStops, stop => stop.OrderId == "A");
    }

    [Fact]
    public async Task SeenStatePersistsPerUserAndDoesNotChangeOrderData()
    {
        LocalUserSessionService.Initialize(string.Empty);
        var settings = new NewXmlPreferencesStore();
        var order = NewXmlOrder("NEW");
        var repository = new FullWarningOrderStore([order]);
        try
        {
            await LocalUserSessionService.SaveAsync("Anna");
            var anna = CreateViewModel(repository, settingsRepository: settings);
            await anna.RefreshAsync();
            var before = JsonSerializer.Serialize(order);
            await anna.MarkNewXmlOrderSeenAsync("NEW");
            Assert.Equal(0, anna.NewXmlOrdersCount);
            Assert.False(Assert.Single(anna.GetMapMarkerSnapshot()).IsNewXmlImport);
            Assert.Equal(before, JsonSerializer.Serialize(order));
            typeof(Tourenplaner.CSharp.App.ViewModels.Sections.KarteSectionViewModel).GetMethod("LoadNewXmlOrderPreference", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(anna, ["Anna", new UserAppPreference()]);
            anna.ShowOnlyNewXmlOrders = true;
            Assert.Equal(0, anna.NewXmlOrdersCount);
            var reloadedAnna = CreateViewModel(repository, settingsRepository: settings);
            await reloadedAnna.RefreshAsync();
            Assert.Equal(0, reloadedAnna.NewXmlOrdersCount);
            await LocalUserSessionService.SaveAsync("Bob");
            var bob = CreateViewModel(repository, settingsRepository: settings);
            await bob.RefreshAsync();
            Assert.Equal(1, bob.NewXmlOrdersCount);
            Assert.True(Assert.Single(bob.GetMapMarkerSnapshot()).IsNewXmlImport);
            Assert.Empty(settings.Value.ResolveUserPreference("Bob").SeenXmlImportOrderKeys);
        }
        finally { await LocalUserSessionService.SaveAsync(string.Empty); }
    }

    [Fact]
    public async Task DeferredCoordinatesKeepNewFlagAndBulkSeenRespectsCurrentFilters()
    {
        var first = NewXmlOrder("FIRST");
        first.Location = null;
        var second = NewXmlOrder("SECOND");
        var settings = new NewXmlPreferencesStore();
        var vm = CreateViewModel(new FullWarningOrderStore([first, second]), settingsRepository: settings);
        await vm.RefreshAsync();
        Assert.Equal(2, vm.NewXmlOrdersCount);
        Assert.DoesNotContain(vm.GetMapMarkerSnapshot(), pin => pin.OrderId == "FIRST");
        first.Location = new(47.2, 8.2);
        await vm.RefreshAsync();
        Assert.True(vm.GetMapMarkerSnapshot().Single(pin => pin.OrderId == "FIRST").IsNewXmlImport);
        vm.SearchText = "FIRST";
        vm.MarkVisibleNewXmlOrdersSeenCommand.Execute(null);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (vm.NewXmlOrdersCount != 1) await Task.Delay(10, timeout.Token);
        Assert.True(vm.GetMapMarkerSnapshot().Single(pin => pin.OrderId == "SECOND").IsNewXmlImport);
        Assert.False(vm.GetMapMarkerSnapshot().Single(pin => pin.OrderId == "FIRST").IsNewXmlImport);
    }

    [Fact]
    public async Task ConcurrentPreferenceWriteIsMergedWhenMarkingSeen()
    {
        var settings = new NewXmlPreferencesStore { ConflictOnce = true };
        var vm = CreateViewModel(new FullWarningOrderStore([NewXmlOrder("NEW")]), settingsRepository: settings);
        await vm.RefreshAsync();
        await vm.MarkNewXmlOrderSeenAsync("NEW");
        Assert.Equal(0, vm.NewXmlOrdersCount);
        Assert.Equal("Dark", settings.Value.ResolveUserPreference("Other").AppearanceMode);
        Assert.Equal(2, settings.SaveAttempts);
    }

    private static Order NewXmlOrder(string id) => new()
    {
        Id = id, CustomerName = "Kunde " + id, Address = "Hauptstrasse 1, 8000 Zürich", Location = new(47.1, 8.1),
        DeliveryType = "Frei Bordsteinkante", IsXmlImported = true, XmlCreatedAtUtc = DateTimeOffset.UtcNow,
        Products = [new() { PositionId = "P" + id, Name = "Ware", Quantity = 1, UnitWeightKg = 10, DeliveryStatus = OrderProductInfo.InStockStatus }]
    };

    [Fact]
    public async Task FailedSeenSaveLeavesNewMarkerVisible()
    {
        var settings = new NewXmlPreferencesStore { FailSaves = true };
        var vm = CreateViewModel(new FullWarningOrderStore([NewXmlOrder("NEW")]), settingsRepository: settings);
        await vm.RefreshAsync();
        await Assert.ThrowsAsync<IOException>(() => vm.MarkNewXmlOrderSeenAsync("NEW"));
        Assert.Equal(1, vm.NewXmlOrdersCount);
        Assert.True(Assert.Single(vm.GetMapMarkerSnapshot()).IsNewXmlImport);
    }

    private sealed class NewXmlPreferencesStore : IAppSettingsStore
    {
        public AppSettings Value { get; private set; } = new();
        public bool ConflictOnce;
        public bool FailSaves;
        public int SaveAttempts;
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Clone(Value));
        public Task SaveAsync(AppSettings value, CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            if (FailSaves) throw new IOException("Test save failure");
            if (ConflictOnce)
            {
                ConflictOnce = false;
                var other = Value.ResolveUserPreference("Other");
                other.AppearanceMode = "Dark";
                Value.SetUserPreference("Other", other);
                throw new ConcurrencyConflictException("Einstellungen", "test");
            }
            Value = Clone(value);
            return Task.CompletedTask;
        }
        private static AppSettings Clone(AppSettings value) => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(value))!;
    }
}
