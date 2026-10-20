using System.Collections.Concurrent;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.Tests.Application;

[Collection("Karte collaboration")]
public sealed class KarteSearchDetailsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RoutePinSelectionOpensCorrectDetailsEvenWithoutVisibleFreePin(bool partial)
    {
        var order = CreateOrder();
        order.Products = [new() { PositionId = "P", Name = "Produkt", Quantity = 10, UnitWeightKg = 5 }];
        if (partial)
            order.Deliveries = [new() { Id = "D1", SequenceNumber = 1, Lines = [new() { OrderProductPositionId = "P", Quantity = 3 }] },
                new() { Id = "D2", SequenceNumber = 2, Lines = [new() { OrderProductPositionId = "P", Quantity = 7 }] }];
        var vm = CreateViewModel(new FixedOrderStore(order));
        await vm.RefreshAsync();
        vm.MapOrders.Clear();
        var stop = new RouteStopItem { OrderId = order.Id, DeliveryId = partial ? "D2" : string.Empty,
            Customer = order.CustomerName, Latitude = 47, Longitude = 8 };
        vm.RouteStops.Add(stop);
        vm.SelectRouteStopByOrderId(stop.MarkerId);
        Assert.Same(stop, vm.SelectedRouteStop);
        Assert.Equal(order.Id, vm.SelectedOrder?.OrderId);
        if (partial) Assert.Equal("D2", vm.SelectedOrder?.DeliveryId);
        Assert.True(vm.IsDetailsOpen);
        vm.CloseDetailsCommand.Execute(null);
        Assert.Null(vm.SelectedOrder);
        Assert.Null(vm.SelectedRouteStop);
        Assert.True(vm.IsDetailsPanelExpanded);
        // Selecting the same pin again must keep its right-side details open.
        vm.SelectRouteStopByOrderId(stop.MarkerId);
        Assert.Same(stop, vm.SelectedRouteStop);
        Assert.True(vm.IsDetailsOpen);
    }
    [Fact]
    public async Task RouteInfoCardIncludesFullOrderDataWithoutVisibleFreePin()
    {
        var order = CreateOrder();
        order.DeliveryAddress = new() { Street = "Teststrasse", HouseNumber = "1", PostalCode = "8000", City = "Zürich" };
        order.Notes = "Zufahrt beachten";
        order.DeliveryPeriod = "Lieferung KW 44";
        order.Products = [new() { PositionId = "P", Name = "Produkt", Quantity = 2, UnitWeightKg = 20, WeightKg = 40 }];
        var vm = CreateViewModel(new FixedOrderStore(order));
        await vm.RefreshAsync();
        vm.MapOrders.Clear();
        var card = vm.ResolveRoutePinInfoCard(new() { OrderId = order.Id });
        Assert.Equal(order.CustomerName, card.Customer);
        Assert.Equal("8000 Zürich", card.PostalCodeCity);
        Assert.Equal(order.Notes, card.Notes);
        Assert.Equal(order.DeliveryPeriod, card.DeliveryPeriod);
        Assert.Contains(card.ProductLines, p => p.Contains("Produkt"));
        Assert.Equal("40", card.TotalWeightKgText);
    }

    [Fact]
    public async Task RouteInfoCardUsesExactPartialDeliveryEvenWithoutVisiblePin()
    {
        var order = CreateOrder();
        order.Products = [new() { PositionId = "P", Name = "Produkt", Quantity = 10, UnitWeightKg = 5, WeightKg = 50 }];
        order.Deliveries = [new() { Id = "D1", SequenceNumber = 1, Notes = "Teil eins", Lines = [new() { OrderProductPositionId = "P", Quantity = 3 }] },
            new() { Id = "D2", SequenceNumber = 2, Notes = "Teil zwei", Lines = [new() { OrderProductPositionId = "P", Quantity = 7 }] }];
        var vm = CreateViewModel(new FixedOrderStore(order));
        await vm.RefreshAsync();
        vm.MapOrders.Clear();
        var card = vm.ResolveRoutePinInfoCard(new() { OrderId = order.Id, DeliveryId = "D2" });
        Assert.Contains("Lieferung Teil 2", card.Customer);
        Assert.Equal("Lieferung 2/2: Teil zwei", card.Notes);
        Assert.Equal("35", card.TotalWeightKgText);
        Assert.Equal("7 × Produkt", Assert.Single(card.ProductLines));
    }

    [Fact]
    public void ManualRouteInfoCardRetainsItsOwnNameAndAddress()
    {
        var card = CreateViewModel().ResolveRoutePinInfoCard(new() { OrderId = "manual:1", IsManualStop = true,
            Customer = "Zwischenstopp", Address = "Hauptstrasse 5" });
        Assert.Equal("Zwischenstopp", card.Customer);
        Assert.Equal("Hauptstrasse 5", card.Street);
    }
    [Fact]
    public async Task PartialDeliveryWeightsMatchAcrossPinDetailsDraftAndSavedTour()
    {
        var order = CreateOrder();
        order.ManualTotalWeightKg = 900;
        order.Products = [new() { PositionId = "P", Name = "Produkt", Quantity = 10, UnitWeightKg = 5, WeightKg = 50 }];
        order.Deliveries = [new() { Id = "D1", SequenceNumber = 1, Lines = [new() { OrderProductPositionId = "P", Quantity = 3 }] },
            new() { Id = "D2", SequenceNumber = 2, Lines = [new() { OrderProductPositionId = "P", Quantity = 7 }] }];
        var vm = CreateViewModel(new FixedOrderStore(order));
        await vm.RefreshAsync();
        var pin = vm.MapOrders.Single(p => p.DeliveryId == "D1");
        Assert.Equal("15", pin.TotalWeightKgText);
        vm.SelectedOrder = pin;
        Assert.Equal("Gewicht dieser Teillieferung: 15 kg", vm.DetailProductsTotalWeightText);
        Assert.False(vm.IsWholeOrderWeightEditable);
        vm.RouteStops.Add(new() { OrderId = order.Id, DeliveryId = "D1", Customer = "Kunde", Latitude = 47, Longitude = 8 });
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(KarteSectionViewModel).GetMethod("UpdateRouteSummary", flags)!.Invoke(vm, null);
        Assert.Equal("Totalgewicht: 15 kg", vm.RouteTotalWeightText);
        var saved = new TourRecord { Id = 19, Stops = [new() { Auftragsnummer = order.Id, DeliveryId = "D1" }] };
        Assert.Equal(15, typeof(KarteSectionViewModel).GetMethod("ResolveSavedTourTotalWeightKg", flags)!.Invoke(vm, [saved]));
        vm.SelectedOrder = vm.MapOrders.Single(p => p.DeliveryId == "D2");
        Assert.Equal("Gewicht dieser Teillieferung: 35 kg", vm.DetailProductsTotalWeightText);
        vm.RouteStops.Add(new() { OrderId = order.Id, DeliveryId = "D2", Customer = "Kunde", Latitude = 47, Longitude = 8 });
        typeof(KarteSectionViewModel).GetMethod("UpdateRouteSummary", flags)!.Invoke(vm, null);
        Assert.Equal("Totalgewicht: 50 kg", vm.RouteTotalWeightText);
        // Duplicate representations of the same delivery must not add its load twice.
        vm.RouteStops.Add(new() { OrderId = order.Id, DeliveryId = "D1", Customer = "Kunde", Latitude = 47, Longitude = 8 });
        typeof(KarteSectionViewModel).GetMethod("UpdateRouteSummary", flags)!.Invoke(vm, null);
        Assert.Equal("Totalgewicht: 50 kg", vm.RouteTotalWeightText);
    }
    [Fact]
    public void ReloadedStreetGeometrySurvivesFailureOnlyWithMatchingSavedSignature()
    {
        var vm = CreateViewModel();
        SetPrivateField(vm, "_routeComputationCachePath", string.Empty);
        vm.RouteStops.Add(new RouteStopItem { Latitude = 47, Longitude = 8 });
        vm.RouteStops.Add(new RouteStopItem { Latitude = 48, Longitude = 9 });
        var tour = new TourRecord
        {
            RouteGeometrySignature = "saved-profile",
            RouteGeometryPoints = [new(47, 8), new(47.5, 8.7), new(48, 9)]
        };
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(KarteSectionViewModel).GetMethod("ShowSavedTourGeometryImmediately", flags)!.Invoke(vm, [tour]);
        var retain = typeof(KarteSectionViewModel).GetMethod("TryRetainStreetRoute", flags)!;
        object?[] args = ["saved-profile", null, 1, null, null];
        Assert.True((bool)retain.Invoke(vm, args)!);
        Assert.Equal(3, Assert.IsAssignableFrom<IReadOnlyList<GeoPoint>>(args[3]).Count);
        args[0] = "changed-profile";
        Assert.False((bool)retain.Invoke(vm, args)!);
        var saved = new TourRecord();
        typeof(KarteSectionViewModel).GetMethod("ApplyCurrentRouteTravelTimeCaches", flags)!.Invoke(vm, [saved]);
        Assert.Equal("saved-profile", saved.RouteGeometrySignature);
        Assert.Equal(3, saved.RouteGeometryPoints.Count);
    }

    [Fact]
    public void RetainingInMemoryStreetRouteRequiresSameSignatureAndReturnsDetachedPoints()
    {
        var vm = CreateViewModel();
        SetPrivateField(vm, "_routeComputationCachePath", string.Empty);
        SetPrivateField(vm, "_lastSuccessfulRouteSignature", "original-route");
        GetPrivateField<List<GeoPoint>>(vm, "_routeGeometryPoints").AddRange([new(47, 8), new(47.1, 8.2), new(48, 9)]);
        GetPrivateField<List<OsrmRouteLeg>>(vm, "_routeLegs").Add(new OsrmRouteLeg(10, 5));
        var method = typeof(KarteSectionViewModel).GetMethod("TryRetainStreetRoute", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        object?[] args = ["original-route", null, 1, null, null];
        Assert.True((bool)method.Invoke(vm, args)!);
        var retained = Assert.IsAssignableFrom<IReadOnlyList<GeoPoint>>(args[3]);
        Assert.Equal(3, retained.Count);
        GetPrivateField<List<GeoPoint>>(vm, "_routeGeometryPoints").Clear();
        Assert.Equal(3, retained.Count);
        args[0] = "changed-route";
        Assert.False((bool)method.Invoke(vm, args)!);
    }

    [Theory]
    [InlineData("TomTom keine Route (Schaetzung)", false)]
    [InlineData("Fallback (Schaetzung)", false)]
    [InlineData("TomTom", true)]
    public void RoutingCacheRejectsEstimatedRoutesEvenWhenFresh(string source, bool expected)
    {
        var vm = CreateViewModel();
        var path = Path.Combine(Path.GetTempPath(), $"route-review-{Guid.NewGuid():N}.json");
        SetPrivateField(vm, "_routeComputationCachePath", path);
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
        {
            Signature = "same-route", Source = source, SavedUtc = DateTime.UtcNow.ToString("O"),
            GeometryPoints = new[] { new { Latitude = 47d, Longitude = 8d }, new { Latitude = 48d, Longitude = 9d } },
            Legs = new[] { new { DurationMinutes = 10, DistanceKm = 5d } }
        }));
        try
        {
            var method = typeof(KarteSectionViewModel).GetMethod("TryLoadRouteComputationCache", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            object?[] args = ["same-route", null, null, null, null, null, false];
            Assert.Equal(expected, (bool)method.Invoke(vm, args)!);
            args[0] = "other-route";
            args[6] = true;
            Assert.False((bool)method.Invoke(vm, args)!);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FailedRefreshCanRetainExpiredStreetCacheOnlyForMatchingRoute()
    {
        var vm = CreateViewModel();
        var path = Path.Combine(Path.GetTempPath(), $"route-review-{Guid.NewGuid():N}.json");
        SetPrivateField(vm, "_routeComputationCachePath", path);
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
        {
            Signature = "same-route", Source = "TomTom", SavedUtc = DateTime.UtcNow.AddDays(-30).ToString("O"),
            GeometryPoints = new[] { new { Latitude = 47d, Longitude = 8d }, new { Latitude = 48d, Longitude = 9d } },
            Legs = new[] { new { DurationMinutes = 10, DistanceKm = 5d } }
        }));
        try
        {
            var method = typeof(KarteSectionViewModel).GetMethod("TryRetainStreetRoute", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            object?[] args = ["same-route", null, 1, null, null];
            Assert.True((bool)method.Invoke(vm, args)!);
            args[0] = "changed-stops-or-profile";
            Assert.False((bool)method.Invoke(vm, args)!);
            args[0] = "same-route";
            args[2] = 2;
            Assert.False((bool)method.Invoke(vm, args)!);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task EditingReloadsPreviewEvenWithoutAnySyncNotification()
    {
        var store = new ChangingPreviewTourStore();
        var order = CreateOrder();
        order.AssignedTourId = "42";
        var vm = CreateViewModel(new FixedOrderStore(order), store);
        await vm.RefreshAsync();
        await vm.FocusTourAsync(42);
        Assert.Equal("Alter Stand", vm.RouteName);
        store.Current = ChangingPreviewTourStore.Create("Neuer Stand", "v2", "09:15");
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        try
        {
            await (Task)typeof(KarteSectionViewModel).GetMethod("StartSelectedTourEditingAsync", flags)!.Invoke(vm, null)!;
            Assert.True(vm.IsLoadedTourEditing);
            Assert.Equal("Neuer Stand", vm.RouteName);
            Assert.Equal("09", vm.RouteStartHour);
            Assert.Equal("15", vm.RouteStartMinute);
            Assert.Equal("v2", GetPrivateField<string>(vm, "_loadedTourEditConcurrencyToken"));
            vm.EnsureTourEditVersionIsCurrent(store.Current);
            Assert.Throws<Tourenplaner.CSharp.Application.Common.ConcurrencyConflictException>(() =>
                vm.EnsureTourEditVersionIsCurrent(ChangingPreviewTourStore.Create("Später geändert", "v3", "10:00")));
        }
        finally { typeof(KarteSectionViewModel).GetMethod("ReleaseLoadedTourLeases", flags)!.Invoke(vm, null); }
    }

    [Fact]
    public async Task SavingStaleDraftDoesNotOverwriteNewerServerTour()
    {
        var store = new ChangingPreviewTourStore();
        var order = CreateOrder(); order.AssignedTourId = "42";
        var vm = CreateViewModel(new FixedOrderStore(order), store);
        await vm.RefreshAsync();
        await vm.FocusTourAsync(42);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        try
        {
            await (Task)typeof(KarteSectionViewModel).GetMethod("StartSelectedTourEditingAsync", flags)!.Invoke(vm, null)!;
            store.Current = ChangingPreviewTourStore.Create("Neuer Serverstand", "v2", "10:00");
            var savedBefore = store.SaveCount;
            await (Task)typeof(KarteSectionViewModel).GetMethod("UpdateExistingTourAsync", flags)!.Invoke(vm,
                [42, "Veralteter lokaler Entwurf", DateTime.Today.ToString("dd.MM.yyyy"), "08:00", null, null, null, null,
                    new List<string>(), new List<TourAdditionalMaterial>(), true])!;
            // Refresh may persist reconciliation; none of those writes may contain the stale draft.
            Assert.All(store.SavedNames.Skip(savedBefore), name => Assert.Equal("Neuer Serverstand", name));
            Assert.Equal("Neuer Serverstand", store.Current.Name);
            Assert.Equal("v1", GetPrivateField<string>(vm, "_loadedTourEditConcurrencyToken"));
        }
        finally { typeof(KarteSectionViewModel).GetMethod("ReleaseLoadedTourLeases", flags)!.Invoke(vm, null); }
    }

    [Fact]
    public async Task SwitchingTourWhileStartingEditingDoesNotKeepOldTourLocked()
    {
        var store = new ChangingPreviewTourStore();
        var order = CreateOrder(); order.AssignedTourId = "42";
        var vm = CreateViewModel(new FixedOrderStore(order), store);
        await vm.RefreshAsync();
        await vm.FocusTourAsync(42);
        store.DeferredLoad = new(TaskCreationOptions.RunContinuationsAsynchronously);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var start = (Task)typeof(KarteSectionViewModel).GetMethod("StartSelectedTourEditingAsync", flags)!.Invoke(vm, null)!;
        SetPrivateField(vm, "_activeTourId", 99);
        store.DeferredLoad.SetResult([store.Current]);
        await start;
        Assert.False(vm.IsLoadedTourEditing);
        Assert.Equal(0, GetPrivateField<int>(vm, "_loadedTourLeaseId"));
        Assert.Equal(99, vm.LoadedTourId);
    }

    [Fact]
    public async Task RemoteChangeReloadsPreviewButDefersDuringEditing()
    {
        var store = new ChangingPreviewTourStore();
        var order = CreateOrder();
        order.AssignedTourId = "42";
        var vm = CreateViewModel(new FixedOrderStore(order), store);
        await vm.RefreshAsync();
        await vm.FocusTourAsync(42);
        store.Current = ChangingPreviewTourStore.Create("Neuer Stand", "v2", "09:15");
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var refresh = typeof(KarteSectionViewModel).GetMethod("RefreshAfterRemoteChangeAsync", flags)!;
        var change = new AppDataChangedEventArgs(Guid.NewGuid(), AppDataKind.Tours);
        await (Task)refresh.Invoke(vm, [change])!;
        Assert.Equal("Neuer Stand", vm.RouteName);
        try
        {
            await (Task)typeof(KarteSectionViewModel).GetMethod("StartSelectedTourEditingAsync", flags)!.Invoke(vm, null)!;
            store.Current = ChangingPreviewTourStore.Create("Während Bearbeitung geändert", "v3", "10:00");
            await (Task)refresh.Invoke(vm, [change])!;
            Assert.Equal("Neuer Stand", vm.RouteName);
            Assert.Equal("v2", GetPrivateField<string>(vm, "_loadedTourEditConcurrencyToken"));
            Assert.Contains("Externe Änderungen", vm.StatusText);
        }
        finally { typeof(KarteSectionViewModel).GetMethod("ReleaseLoadedTourLeases", flags)!.Invoke(vm, null); }
    }

    private sealed class ChangingPreviewTourStore : ITourRecordStore
    {
        public TourRecord Current { get; set; } = Create("Alter Stand", "v1", "08:00");
        public TaskCompletionSource<IReadOnlyList<TourRecord>>? DeferredLoad { get; set; }
        public int SaveCount { get; private set; }
        public List<string> SavedNames { get; } = new();
        public static TourRecord Create(string name, string token, string startTime) => new()
        {
            Id = 42, Name = name, Date = DateTime.Today.ToString("dd.MM.yyyy"), StartTime = startTime,
            ConcurrencyToken = token, Stops = [new() { Id = "222016", Auftragsnummer = "222016", Order = 1 }]
        };
        public Task<IReadOnlyList<TourRecord>> LoadAsync(CancellationToken cancellationToken = default) => DeferredLoad?.Task ?? Task.FromResult<IReadOnlyList<TourRecord>>([Current]);
        public Task SaveAsync(IEnumerable<TourRecord> tours, CancellationToken cancellationToken = default) { SaveCount++; Current = tours.Single(); SavedNames.Add(Current.Name); return Task.CompletedTask; }
    }

    private static T GetPrivateField<T>(KarteSectionViewModel vm, string name) =>
        (T)typeof(KarteSectionViewModel).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(vm)!;

    [Fact]
    public async Task FailedTrackReloadRetainsDisplayedVehicleAndTrack()
    {
        var settings = new DeferredWebfleetSettingsStore();
        var vm = CreateViewModel(settingsRepository: settings);
        var vehicles = GetPrivateField<List<WebfleetVehicleSnapshot>>(vm, "_webfleetVehicles");
        vehicles.Add(new("1", "new-truck", "Neu", "", 47, 8, DateTimeOffset.Now, "", "", ""));
        var session = GetPrivateField<WebfleetLiveTrackSession>(vm, "_webfleetLiveTrack");
        var now = DateTimeOffset.Now;
        session.Begin("old-truck", DateOnly.FromDateTime(now.LocalDateTime), now);
        session.Points.Add(new(now.AddMinutes(-2), 47, 8, null, null));
        var load = vm.LoadWebfleetTrackAsync("new-truck", DateOnly.FromDateTime(now.LocalDateTime));
        Assert.Equal("old-truck", session.ObjectUid);
        Assert.Single(vm.GetWebfleetTrackSnapshot());
        settings.Completion.SetException(new IOException("Test offline"));
        await load;
        Assert.Equal("old-truck", session.ObjectUid);
        Assert.Single(vm.GetWebfleetTrackSnapshot());
        Assert.Contains("Test offline", vm.WebfleetTrackStatusText);
    }

    [Fact]
    public async Task LateLoadFailureDoesNotOverwriteClearedTrackStatus()
    {
        var settings = new DeferredWebfleetSettingsStore();
        var vm = CreateViewModel(settingsRepository: settings);
        GetPrivateField<List<WebfleetVehicleSnapshot>>(vm, "_webfleetVehicles")
            .Add(new("1", "truck", "Truck", "", 47, 8, DateTimeOffset.Now, "", "", ""));
        var load = vm.LoadWebfleetTrackAsync("truck", DateOnly.FromDateTime(DateTime.Today));
        vm.ClearWebfleetTrack();
        settings.Completion.SetException(new IOException("Veralteter Fehler"));
        await load;
        Assert.Empty(vm.GetWebfleetTrackSnapshot());
        Assert.Equal(string.Empty, vm.WebfleetTrackStatusText);
        Assert.Contains("ausgeblendet", vm.StatusText);
    }

    [Fact]
    public void TrackStatusRefreshPreservesPauseWarning()
    {
        var vm = CreateViewModel();
        var session = GetPrivateField<WebfleetLiveTrackSession>(vm, "_webfleetLiveTrack");
        var now = DateTimeOffset.Now;
        session.Begin("truck", DateOnly.FromDateTime(now.LocalDateTime), now);
        session.Points.AddRange([new(now.AddMinutes(-2), 47, 8, null, null), new(now.AddMinutes(-1), 47.1, 8.1, null, null)]);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(KarteSectionViewModel).GetField("_webfleetTrackWarning", flags)!.SetValue(vm, "Pausen konnten nicht geladen werden.");
        typeof(KarteSectionViewModel).GetMethod("PublishWebfleetTrackStatus", flags)!.Invoke(vm, null);
        Assert.Contains("Pausen konnten nicht geladen werden.", vm.WebfleetTrackStatusText);
    }

    private sealed class DeferredWebfleetSettingsStore : IAppSettingsStore
    {
        public TaskCompletionSource<AppSettings> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Completion.Task;
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task SingleDeliveryPinDoesNotClaimToBePartialDelivery()
    {
        var order = CreateOrder();
        order.Products = [new() { PositionId = "P1", Name = "Produkt", Quantity = 1 }];
        order.Deliveries = [new() { Id = "D1", SequenceNumber = 1,
            Lines = [new() { OrderProductPositionId = "P1", Quantity = 1 }] }];
        var vm = CreateViewModel(new FixedOrderStore(order));
        await vm.RefreshAsync();
        var pin = Assert.Single(vm.MapOrders);
        Assert.Equal(order.CustomerName, pin.Customer);
        Assert.Equal("D1", pin.DeliveryId);
    }

    [Fact]
    public async Task RemainingPartialDeliveryKeepsItsPartLabelAfterFirstDeliveryIsCompleted()
    {
        var order = CreateOrder();
        order.Products = [new() { PositionId = "P1", Name = "Produkt", Quantity = 2 }];
        order.Deliveries =
        [
            new() { Id = "D1", SequenceNumber = 1, Status = OrderDeliveryStatus.Delivered,
                Lines = [new() { OrderProductPositionId = "P1", Quantity = 1 }] },
            new() { Id = "D2", SequenceNumber = 2,
                Lines = [new() { OrderProductPositionId = "P1", Quantity = 1 }] }
        ];
        var vm = CreateViewModel(new FixedOrderStore(order));
        await vm.RefreshAsync();
        Assert.Contains("Lieferung Teil 2", vm.MapOrders.Single(x => x.DeliveryId == "D2").Customer);
    }

    [Fact]
    public async Task RefreshReloadsTourAfterMaintenanceConflictInsteadOfReportingFailedSave()
    {
        var store = new ConflictingMaintenanceTourStore();
        var vm = CreateViewModel(tourRepository: store);
        await vm.RefreshAsync();
        Assert.True(store.LoadCount >= 2);
        Assert.Contains(vm.SavedTours, x => x.TourId == 21 && x.Label.Contains("Aktueller Serverstand"));
        Assert.True(store.WriteAttempts > 0);
        Assert.True(store.LoadCount >= store.WriteAttempts * 2);
    }

    private sealed class ConflictingMaintenanceTourStore : ITourRecordStore, ITourRecordMutationStore
    {
        public int LoadCount { get; private set; }
        public int WriteAttempts { get; private set; }
        private static TourRecord Current() => new()
        {
            Id = 21, Name = "Aktueller Serverstand", Date = DateTime.Today.ToString("dd.MM.yyyy"),
            Stops = [new() { Id = "missing-order", Auftragsnummer = "missing-order", Order = 1 }]
        };
        public Task<IReadOnlyList<TourRecord>> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult<IReadOnlyList<TourRecord>>([Current()]);
        }
        public Task<TourRecord?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<TourRecord?>(Current());
        public Task SaveAsync(IEnumerable<TourRecord> tours, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task UpsertAsync(TourRecord tour, CancellationToken cancellationToken = default) => Conflict();
        public Task DeleteAsync(int id, string? concurrencyToken = null, CancellationToken cancellationToken = default) => Conflict();
        private Task Conflict()
        {
            WriteAttempts++;
            return Task.FromException(new Tourenplaner.CSharp.Application.Common.ConcurrencyConflictException("Tour", "21"));
        }
    }

    [Fact]
    public void SwitchingTourImmediatelyReplacesGeometryAndClearsItWhenNoSavedPathExists()
    {
        var vm = CreateViewModel();
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var apply = typeof(KarteSectionViewModel).GetMethod("ShowSavedTourGeometryImmediately", flags)!;
        apply.Invoke(vm, [new TourRecord { RouteGeometryPoints = [new GeoPoint(47, 8), new GeoPoint(48, 9)] }]);
        Assert.Equal(2, vm.RouteGeometryPoints.Count);
        apply.Invoke(vm, [new TourRecord { RouteGeometryPoints = [new GeoPoint(46, 7), new GeoPoint(45, 6)] }]);
        Assert.Equal(46, vm.RouteGeometryPoints[0].Latitude);
        Assert.Equal(7, vm.RouteGeometryPoints[0].Longitude);
        apply.Invoke(vm, [new TourRecord()]);
        Assert.Empty(vm.RouteGeometryPoints);
    }

    [Fact]
    public void LoadedTourDoesNotKeepPreviousOverviewRouteOverlay()
    {
        var vm = CreateViewModel();
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var type = typeof(KarteSectionViewModel);
        var tours = (List<TourRecord>)type.GetField("_savedTours", flags)!.GetValue(vm)!;
        tours.Add(new TourRecord
        {
            Id = 1, Name = "Vorherige Tour",
            RouteGeometryPoints = [new GeoPoint(47, 8), new GeoPoint(48, 9)]
        });
        type.GetField("_selectedTourOverviewId", flags)!.SetValue(vm, 1);
        var rebuild = type.GetMethod("RebuildPlannedTourRouteOverlays", flags)!;
        rebuild.Invoke(vm, null);
        Assert.Single(vm.GetPlannedTourRouteOverlaySnapshot());

        type.GetField("_activeTourId", flags)!.SetValue(vm, 2);
        rebuild.Invoke(vm, null);
        Assert.Empty(vm.GetPlannedTourRouteOverlaySnapshot());

        type.GetField("_isAllPlannedToursVisible", flags)!.SetValue(vm, true);
        rebuild.Invoke(vm, null);
        Assert.Single(vm.GetPlannedTourRouteOverlaySnapshot());
    }

    [Theory]
    [InlineData(900, true, 0)]
    [InlineData(900, false, 900)]
    [InlineData(20, false, 100)]
    [InlineData(15000, false, 10000)]
    public void RouteRecalculationDebounce_ImmediateUserActionsBypassDelay(
        int configuredDebounceMs,
        bool calculateImmediately,
        int expectedDebounceMs)
    {
        var result = KarteSectionViewModel.ResolveRouteRecalculationDebounceMs(
            configuredDebounceMs,
            calculateImmediately);

        Assert.Equal(expectedDebounceMs, result);
    }

    [Fact]
    public void PartiallyReadyAndInTransit_UseDistinctPinAndLegendColors()
    {
        var viewModel = CreateViewModel();

        var inTransitColor = viewModel.ResolveOrderStatusColor(Order.InTransitStatus, false);
        var partiallyReadyColor = viewModel.ResolveOrderStatusColor(Order.PartiallyReadyStatus, false);

        Assert.Equal(AppSettings.DefaultStatusColorOnTheWay, inTransitColor);
        Assert.Equal(AppSettings.DefaultStatusColorPartiallyReady, partiallyReadyColor);
        Assert.NotEqual(inTransitColor, partiallyReadyColor);
        Assert.Equal(partiallyReadyColor, viewModel.LegendStatusColorPartiallyReady);
    }

    [Fact]
    public void OrderedStatus_UsesPinkForPinLegendAndProductBackground()
    {
        var viewModel = CreateViewModel();

        var orderedColor = viewModel.ResolveOrderStatusColor(Order.OrderedStatus, false);
        ProductStatusVisualService.Configure(
            AppSettings.DefaultStatusColorNotSpecified,
            orderedColor,
            AppSettings.DefaultStatusColorOnTheWay,
            AppSettings.DefaultStatusColorInStock);

        Assert.Equal("#EC4899", orderedColor);
        Assert.Equal(orderedColor, viewModel.LegendStatusColorOrdered);
        Assert.Equal("#EC4899", ProductStatusVisualService.ResolveBorderColor(OrderProductInfo.OrderedStatus));
        Assert.Equal("#33EC4899", ProductStatusVisualService.ResolveBackgroundColor(OrderProductInfo.OrderedStatus));
    }

    [Fact]
    public void HiddenReturnLeg_TruncatesGeometryAtLastDeliveryStop()
    {
        IReadOnlyList<GeoPoint> completeRoute =
        [
            new(47.10, 8.10),
            new(47.20, 8.20),
            new(47.30001, 8.30001),
            new(47.40, 8.40),
            new(47.50, 8.50)
        ];

        var visibleRoute = KarteSectionViewModel.BuildVisibleRouteGeometry(
            completeRoute,
            latitude: 47.30,
            longitude: 8.30);

        Assert.Equal(3, visibleRoute.Count);
        Assert.Equal(completeRoute[2], visibleRoute[^1]);
    }

    [Fact]
    public void HiddenReturnLeg_DoesNotCutAtFinalStopPassedBeforePartialDelivery()
    {
        var start = new GeoPoint(47.1, 8.1);
        var finalStop = new GeoPoint(47.2, 8.2);
        var partialStop = new GeoPoint(47.3, 8.3);
        var geometry = new List<GeoPoint> { start, finalStop, partialStop, finalStop, start };
        var vm = CreateViewModel();
        vm.RouteStops.Add(new() { OrderId = "A", DeliveryId = "D1", Latitude = partialStop.Latitude, Longitude = partialStop.Longitude });
        vm.RouteStops.Add(new() { OrderId = "B", Latitude = finalStop.Latitude, Longitude = finalStop.Longitude });
        GetPrivateField<List<GeoPoint>>(vm, "_routeGeometryPoints").AddRange(geometry);

        Assert.Equal(geometry.Take(4), vm.GetRouteGeometrySnapshot());
        vm.ToggleReturnToCompanyLegVisibility();
        Assert.Equal(geometry, vm.GetRouteGeometrySnapshot());
    }

    [Fact]
    public void HiddenReturnLeg_RespectsAllEarlierStopsInOrder()
    {
        var a = new GeoPoint(47.1, 8.1);
        var b = new GeoPoint(47.2, 8.2);
        var c = new GeoPoint(47.3, 8.3);
        IReadOnlyList<GeoPoint> geometry = [c, b, a, b, c, a];
        var visible = KarteSectionViewModel.BuildVisibleRouteGeometry(geometry, c.Latitude, c.Longitude, [a, b]);
        Assert.Equal(geometry.Take(5), visible);
    }

    [Fact]
    public void HiddenReturnLeg_AllowsConsecutivePartialDeliveriesAtSameLocation()
    {
        var start = new GeoPoint(47, 8);
        var destination = new GeoPoint(47.1, 8.1);
        var visible = KarteSectionViewModel.BuildVisibleRouteGeometry([start, destination, start],
            destination.Latitude, destination.Longitude, [destination]);
        Assert.Equal(new[] { start, destination }, visible);
        Assert.Empty(KarteSectionViewModel.BuildVisibleRouteGeometry([], 47, 8, [destination]));
    }

    [Fact]
    public async Task ClosingDetails_ClearsSearchThatOpenedTheOrder()
    {
        var viewModel = CreateViewModel();
        await viewModel.RefreshAsync();

        await viewModel.ApplySearchSuggestionAsync(new MapSearchSuggestionItem(
            "Auftrag 222016 · Testkunde",
            "Teststrasse 1, 8000 Zürich",
            "Auftrag",
            47.3769,
            8.5417,
            "222016"));

        Assert.Equal("222016", viewModel.SelectedOrder?.OrderId);
        Assert.NotEmpty(viewModel.SearchText);

        viewModel.CloseDetailsCommand.Execute(null);

        Assert.Null(viewModel.SelectedOrder);
        Assert.Empty(viewModel.SearchText);
        Assert.All(viewModel.GetMapMarkerSnapshot(), marker => Assert.False(marker.IsDimmed));
    }

    [Fact]
    public async Task ClosingDetails_KeepsSearchWhenOrderWasOpenedDirectly()
    {
        var viewModel = CreateViewModel();
        await viewModel.RefreshAsync();
        viewModel.SearchText = "1";
        viewModel.SelectedOrder = Assert.Single(viewModel.MapOrders);

        viewModel.CloseDetailsCommand.Execute(null);

        Assert.Null(viewModel.SelectedOrder);
        Assert.Equal("1", viewModel.SearchText);
    }

    [Fact]
    public async Task ConcurrentRefreshes_AreSerializedAndDoNotDuplicateMapPins()
    {
        var repository = new ControlledOrderStore();
        var viewModel = CreateViewModel(repository);
        await viewModel.RefreshAsync();
        Assert.Single(viewModel.MapOrders);

        repository.DelayLoads = true;
        var completedCalls = repository.LoadCallCount;
        var firstRefresh = viewModel.RefreshAsync();
        await repository.WaitForLoadCallAsync(completedCalls + 1);

        var secondRefresh = viewModel.RefreshAsync();
        await Task.Delay(50);
        Assert.Equal(completedCalls + 1, repository.LoadCallCount);

        await repository.CompleteNextLoadAsync();
        await firstRefresh;
        await repository.WaitForLoadCallAsync(completedCalls + 2);
        await repository.CompleteNextLoadAsync();
        await secondRefresh;

        var marker = Assert.Single(viewModel.MapOrders);
        Assert.Equal("222016", marker.OrderId);
    }

    [Fact]
    public async Task SelectingAndRefreshingTourOverview_DoesNotRequestMapRecentering()
    {
        var viewModel = CreateViewModel(tourRepository: new SingleTourStore());
        await viewModel.RefreshAsync();

        var tour = Assert.Single(viewModel.TourOverviewItems);
        viewModel.SelectTourOverviewById(tour.TourId);
        var focusRevision = viewModel.TourOverviewFocusRevision;
        Assert.Equal(0, focusRevision);

        await viewModel.RefreshAsync();

        Assert.Equal(focusRevision, viewModel.TourOverviewFocusRevision);
        Assert.Equal(tour.TourId, viewModel.SelectedTourOverviewItem?.TourId);
    }

    [Fact]
    public async Task PartialDeliveries_UseOnlyTheirOwnProductStatusesForBadgeAndPinColor()
    {
        var order = CreateOrder();
        order.Products =
        [
            new OrderProductInfo { PositionId = "P1", Name = "Bereites Produkt", Quantity = 1, DeliveryStatus = OrderProductInfo.InStockStatus },
            new OrderProductInfo { PositionId = "P2", Name = "Bestelltes Produkt", Quantity = 1, DeliveryStatus = OrderProductInfo.OrderedStatus }
        ];
        order.Deliveries =
        [
            new OrderDelivery { Id = "D1", SequenceNumber = 1, Lines = [new OrderDeliveryLine { OrderProductPositionId = "P1", Quantity = 1 }] },
            new OrderDelivery { Id = "D2", SequenceNumber = 2, Lines = [new OrderDeliveryLine { OrderProductPositionId = "P2", Quantity = 1 }] }
        ];
        var viewModel = CreateViewModel(new FixedOrderStore(order));

        await viewModel.RefreshAsync();

        var delivery1 = viewModel.ResolveOrderVisualInfo(order.Id, "D1");
        var delivery2 = viewModel.ResolveOrderVisualInfo(order.Id, "D2");
        Assert.Equal(Order.ReadyToDeliverStatus, delivery1.StatusLabel);
        Assert.Equal(Order.OrderedStatus, delivery2.StatusLabel);
        Assert.Equal(viewModel.ResolveOrderStatusColor(Order.ReadyToDeliverStatus, false), delivery1.StatusColorHex);
        Assert.Equal(viewModel.ResolveOrderStatusColor(Order.OrderedStatus, false), delivery2.StatusColorHex);
        Assert.NotEqual(delivery1.StatusColorHex, delivery2.StatusColorHex);

        viewModel.SelectedOrder = viewModel.MapOrders.Single(x => x.DeliveryId == "D1");
        Assert.Equal(Order.ReadyToDeliverStatus, viewModel.DetailOrderStatus);
        viewModel.SelectedOrder = viewModel.MapOrders.Single(x => x.DeliveryId == "D2");
        Assert.Equal(Order.OrderedStatus, viewModel.DetailOrderStatus);
    }

    [Fact]
    public async Task DeliveryPeriod_IsAvailableOnMapCardAndSelectedOrderDetails()
    {
        var order = CreateOrder();
        order.DeliveryPeriod = "KW 43–44";
        var viewModel = CreateViewModel(new FixedOrderStore(order));
        await viewModel.RefreshAsync();

        var marker = Assert.Single(viewModel.MapOrders);
        Assert.Equal("KW 43–44", marker.DeliveryPeriod);
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        viewModel.SelectedOrder = marker;
        Assert.Equal("KW 43–44", viewModel.DetailDeliveryPeriod);
        Assert.Contains(nameof(viewModel.DetailDeliveryPeriod), notifications);

        viewModel.SelectedOrder = null;
        Assert.Equal(string.Empty, viewModel.DetailDeliveryPeriod);
    }

    [Fact]
    public void LoadedTourMode_ChangesFromPreviewToEditingAndDisablesStartCommand()
    {
        var viewModel = CreateViewModel();
        SetPrivateField(viewModel, "_activeTourId", 42);

        Assert.True(viewModel.IsLoadedTourReadOnly);
        Assert.False(viewModel.CanReorderRouteStops);
        Assert.False(viewModel.IsEditingTour(42));
        Assert.Equal("Bearbeitungsmodus für Tourstopps starten", viewModel.StartTourEditingToolTip);
        Assert.True(viewModel.StartSelectedTourEditingCommand.CanExecute(null));

        SetPrivateField(viewModel, "_loadedTourLeaseId", 42);
        SetPrivateField(viewModel, "_isLoadedTourEditing", true);

        Assert.True(viewModel.IsLoadedTourEditing);
        Assert.True(viewModel.CanReorderRouteStops);
        Assert.True(viewModel.IsEditingTour(42));
        Assert.False(viewModel.IsEditingTour(99));
        Assert.False(viewModel.IsLoadedTourReadOnly);
        Assert.Equal("Bearbeitungsmodus für Tourstopps ist aktiv", viewModel.StartTourEditingToolTip);
        Assert.False(viewModel.StartSelectedTourEditingCommand.CanExecute(null));
    }

    [Fact]
    public void StopEditingCommand_TargetsLoadedTourInsteadOfAnotherSelectedTour()
    {
        var viewModel = CreateViewModel();
        SetPrivateField(viewModel, "_activeTourId", 42);
        SetPrivateField(viewModel, "_selectedTourOverviewId", 99);

        Assert.Equal(42, viewModel.LoadedTourId);
        Assert.Equal(42, viewModel.ResolveTourIdForDetailsEditing());
        Assert.True(viewModel.StartSelectedTourEditingCommand.CanExecute(null));

        SetPrivateField(viewModel, "_loadedTourLeaseId", 42);
        SetPrivateField(viewModel, "_isLoadedTourEditing", true);

        Assert.True(viewModel.IsEditingTour(42));
        Assert.False(viewModel.IsEditingTour(99));
        Assert.False(viewModel.StartSelectedTourEditingCommand.CanExecute(null));
    }

    [Fact]
    public void LoadedTourMode_IsEmptyWhenNoTourIsLoaded()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.IsLoadedTourEditing);
        Assert.False(viewModel.IsLoadedTourReadOnly);
        Assert.True(viewModel.CanReorderRouteStops);
        Assert.False(viewModel.StartSelectedTourEditingCommand.CanExecute(null));
    }

    private static void SetPrivateField<T>(KarteSectionViewModel viewModel, string fieldName, T value)
    {
        var field = typeof(KarteSectionViewModel).GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(viewModel, value);
    }

    private static KarteSectionViewModel CreateViewModel(
        IOrderRepository? orderRepository = null,
        ITourRecordStore? tourRepository = null,
        IAppSettingsStore? settingsRepository = null) => new(
        orderRepository ?? new OrderStore(),
        tourRepository ?? new EmptyTourStore(),
        new EmptyEmployeeStore(),
        new EmptyVehicleStore(),
        settingsRepository ?? new SettingsStore(),
        Path.Combine(Path.GetTempPath(), "tourenplaner-tests", Guid.NewGuid().ToString("N")),
        new AppDataSyncService());

    private sealed class OrderStore : IOrderRepository
    {
        private readonly List<Order> _orders =
        [
            CreateOrder()
        ];

        public Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Order>>(_orders);

        public Task SaveAllAsync(IEnumerable<Order> orders, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FixedOrderStore(Order order) : IOrderRepository
    {
        public Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Order>>([order]);

        public Task SaveAllAsync(IEnumerable<Order> orders, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ControlledOrderStore : IOrderRepository
    {
        private readonly ConcurrentQueue<TaskCompletionSource<IReadOnlyList<Order>>> _pendingLoads = new();
        private int _loadCallCount;

        public bool DelayLoads { get; set; }
        public int LoadCallCount => Volatile.Read(ref _loadCallCount);

        public Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _loadCallCount);
            IReadOnlyList<Order> orders = [CreateOrder(), CreateOrder()];
            if (!DelayLoads)
            {
                return Task.FromResult(orders);
            }

            var completion = new TaskCompletionSource<IReadOnlyList<Order>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            _pendingLoads.Enqueue(completion);
            return completion.Task;
        }

        public Task SaveAllAsync(IEnumerable<Order> orders, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task WaitForLoadCallAsync(int expectedCall)
        {
            var timeout = DateTime.UtcNow.AddSeconds(5);
            while (LoadCallCount < expectedCall)
            {
                if (DateTime.UtcNow >= timeout)
                {
                    throw new TimeoutException("Die erwartete Kartenaktualisierung wurde nicht gestartet.");
                }

                await Task.Delay(10);
            }
        }

        public async Task CompleteNextLoadAsync()
        {
            var timeout = DateTime.UtcNow.AddSeconds(5);
            TaskCompletionSource<IReadOnlyList<Order>>? completion;
            while (!_pendingLoads.TryDequeue(out completion))
            {
                if (DateTime.UtcNow >= timeout)
                {
                    throw new TimeoutException("Die erwartete verzögerte Kartenaktualisierung wurde nicht registriert.");
                }

                await Task.Delay(10);
            }

            completion.SetResult([CreateOrder(), CreateOrder()]);
        }
    }

    private static Order CreateOrder() => new()
    {
        Id = "222016",
        CustomerName = "Testkunde",
        Address = "Teststrasse 1, 8000 Zürich",
        DeliveryType = "Frei Bordsteinkante",
        AssignedTourId = "19",
        Location = new GeoPoint(47.3769, 8.5417)
    };

    private sealed class EmptyTourStore : ITourRecordStore
    {
        public Task<IReadOnlyList<TourRecord>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TourRecord>>([]);

        public Task SaveAsync(IEnumerable<TourRecord> tours, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SingleTourStore : ITourRecordStore
    {
        public Task<IReadOnlyList<TourRecord>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TourRecord>>(
            [
                new TourRecord
                {
                    Id = 19,
                    Name = "Tour 19",
                    Date = DateTime.Today.AddDays(1).ToString("dd.MM.yyyy"),
                    StartTime = "07:30",
                    Stops =
                    [
                        new TourStopRecord { Id = "__company_start__", Auftragsnummer = "__company_start__", Order = 1 },
                        new TourStopRecord { Id = "222016", Auftragsnummer = "222016", Order = 2 },
                        new TourStopRecord { Id = "__company_end__", Auftragsnummer = "__company_end__", Order = 3 }
                    ]
                }
            ]);

        public Task SaveAsync(IEnumerable<TourRecord> tours, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class EmptyEmployeeStore : IEmployeeDataStore
    {
        public Task<IReadOnlyList<Employee>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Employee>>([]);

        public Task SaveAsync(IEnumerable<Employee> employees, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class EmptyVehicleStore : IVehicleDataStore
    {
        public Task<VehicleDataRecord> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new VehicleDataRecord());

        public Task SaveAsync(VehicleDataRecord payload, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SettingsStore : IAppSettingsStore
    {
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppSettings());

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}

[CollectionDefinition("Karte collaboration", DisableParallelization = true)]
public sealed class KarteCollaborationCollection { }
