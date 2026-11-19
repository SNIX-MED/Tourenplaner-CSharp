using System.Text.Json;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed partial class ToursSectionViewModelTests
{
    [Fact]
    public async Task CancellingMoveIntoFullTourKeepsBothToursUnchanged()
    {
        var target = CreateTour(20);
        target.Stops = [new() { Id = "manual:pickup", StopKind = "manual", Name = "Abholung" }];
        target.IsFull = true;
        var vm = new ToursSectionViewModel(new ControlledTourStore([CreateTour(19), target]), new SingleOrderRepository(),
            new EmptyEmployeeStore(), new EmptyVehicleStore(), new DefaultSettingsStore());
        await vm.RefreshAsync();
        vm.SelectedTour = vm.Tours.Single(t => t.TourId == 19);
        var source = vm.SelectedTourStops.Single(s => s.Source.Auftragsnummer == "A-1");
        var before = JsonSerializer.Serialize(vm.Tours.Select(t => t.Source));
        var confirmations = 0;
        vm.ConfirmFullTourWarning = tour => { Assert.True(tour!.IsFull); confirmations++; return false; };
        Assert.False(await vm.MoveStopToTourAsync(source, vm.Tours.Single(t => t.TourId == 20)));
        Assert.Equal(1, confirmations);
        Assert.Equal(before, JsonSerializer.Serialize(vm.Tours.Select(t => t.Source)));
    }

    [Fact]
    public async Task FailedSaveRestoresStoredMarkerAndKeepsEditorChangesForRetry()
    {
        var store = new FailingFullStatusStore(CreateTour(9127));
        var vm = new ToursSectionViewModel(store, new SingleOrderRepository(),
            new EmptyEmployeeStore(), new EmptyVehicleStore(), new DefaultSettingsStore());
        await vm.RefreshAsync();
        vm.SelectedTour = Assert.Single(vm.Tours);
        vm.EditorIsFull = true;
        store.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => vm.SaveFullStatusAsync());
        Assert.False(store.Tour.IsFull);
        Assert.Empty(store.Tour.FullNote);
        Assert.True(vm.EditorIsFull);
        Assert.True(vm.HasUnsavedChanges);
    }

    private sealed class FailingFullStatusStore(TourRecord tour) : ITourRecordStore
    {
        public TourRecord Tour { get; } = tour;
        public bool Fail { get; set; }
        public Task<IReadOnlyList<TourRecord>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TourRecord>>([Tour]);
        public Task SaveAsync(IEnumerable<TourRecord> tours, CancellationToken cancellationToken = default) =>
            Fail ? throw new IOException("Test save failure") : Task.CompletedTask;
    }

    [Fact]
    public async Task FullMarkerCanBeSavedReloadedAndClearedWithoutChangingOtherTourData()
    {
        var root = Path.Combine(Path.GetTempPath(), "tourenplaner-tests", Guid.NewGuid().ToString("N"));
        var repository = new JsonToursRepository(Path.Combine(root, "tours.json"));
        try
        {
            await repository.SaveAsync([CreateTour(19)]);
            var sync = new AppDataSyncService();
            var vm = new ToursSectionViewModel(repository, new SingleOrderRepository(),
                new EmptyEmployeeStore(), new EmptyVehicleStore(), new DefaultSettingsStore(), dataSyncService: sync);
            await vm.RefreshAsync();
            vm.SelectedTour = Assert.Single(vm.Tours);
            static string Content(TourRecord tour)
            {
                var node = JsonSerializer.SerializeToNode(tour)!.AsObject();
                node.Remove(nameof(TourRecord.ConcurrencyToken));
                return node.ToJsonString();
            }
            var original = Content(vm.SelectedTour.Source);
            var notifications = 0;
            sync.DataChanged += (_, change) => { if (change.Kinds.HasFlag(AppDataKind.Tours)) notifications++; };

            vm.EditorIsFull = true;
            Assert.True(vm.HasUnsavedChanges);
            await vm.SaveFullStatusAsync();
            Assert.False(vm.HasUnsavedChanges);
            Assert.True(vm.SelectedTour!.IsFull);
            Assert.Contains("voll markiert", vm.SelectedTour.FullDescription);
            Assert.True(Assert.Single(await new JsonToursRepository(Path.Combine(root, "tours.json")).LoadAsync()).IsFull);
            Assert.True(notifications > 0);

            vm.EditorIsFull = false;
            await vm.SaveFullStatusAsync();
            Assert.False(vm.SelectedTour!.IsFull);
            Assert.Equal(original, Content(vm.SelectedTour.Source));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MapEditingPreventsFullMarkerSave()
    {
        var vm = CreateViewModelWithTour(19);
        await vm.RefreshAsync();
        vm.SelectedTour = Assert.Single(vm.Tours);
        vm.ConfigureMapLoadedTourProvider(() => 19, () => true);
        Assert.False(vm.SaveFullStatusCommand.CanExecute(null));
        vm.EditorIsFull = true;
        await vm.SaveFullStatusAsync();
        Assert.False(vm.SelectedTour.Source.IsFull);
    }

    [Fact]
    public async Task RemovingStopDoesNotClearFullMarker()
    {
        var root = Path.Combine(Path.GetTempPath(), "tourenplaner-tests", Guid.NewGuid().ToString("N"));
        var repository = new JsonToursRepository(Path.Combine(root, "tours.json"));
        try
        {
            var tour = CreateTour(19);
            tour.IsFull = true;
            tour.FullReason = "Platz ausgeschöpft";
            tour.Stops.Insert(2, new() { Id = "manual:pickup", StopKind = "manual", Name = "Abholung" });
            await repository.SaveAsync([tour]);
            var vm = new ToursSectionViewModel(repository, new SingleOrderRepository(),
                new EmptyEmployeeStore(), new EmptyVehicleStore(), new DefaultSettingsStore());
            await vm.RefreshAsync();
            vm.SelectedTour = Assert.Single(vm.Tours);
            vm.SelectedTourStop = vm.SelectedTourStops.Single(s => s.Source.Id == "manual:pickup");
            await vm.RemoveSelectedTourStopAsync();
            var stored = Assert.Single(await repository.LoadAsync());
            Assert.DoesNotContain(stored.Stops, stop => stop.Id == "manual:pickup");
            Assert.True(stored.IsFull);
            Assert.Equal("Platz ausgeschöpft", stored.FullReason);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
