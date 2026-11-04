using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class ArchivedResourceDeletionTests
{
    [Theory]
    [InlineData(true, -1, false)]
    [InlineData(false, -1, true)]
    [InlineData(true, 0, true)]
    [InlineData(true, 1, true)]
    public async Task EmployeeDeletionPreservesHistoryButBlocksCurrentAssignments(bool archived, int days, bool blocked)
    {
        var store = new EmployeeStore();
        var tour = MakeTour(archived, days);
        tour.EmployeeIds = [" EMP "];
        var model = new EmployeesSectionViewModel(store, new TourStore(tour), null!, new AppDataSyncService());
        await model.RefreshAsync();
        var error = await model.DeleteEntryAsync(Assert.Single(model.Entries));
        Assert.Equal(blocked, error is not null);
        Assert.Equal(!blocked, store.Item.IsDeleted);
        Assert.Equal(blocked, store.Item.Active);
        Assert.Equal(blocked, store.Item.HasProgramProfile);
        Assert.Equal("Anna", store.Item.DisplayName);
        Assert.Equal(" EMP ", Assert.Single(tour.EmployeeIds));
        Assert.Equal(blocked ? 1 : 0, model.Entries.Count);
        Assert.Equal(blocked ? 0 : 1, store.Upserts);
        Assert.Equal(0, store.Deletes);
    }

    [Theory]
    [InlineData(false, false, true, -1, false)]
    [InlineData(false, true, true, -1, false)]
    [InlineData(true, false, true, -1, false)]
    [InlineData(true, true, true, -1, false)]
    [InlineData(false, false, false, -1, true)]
    [InlineData(true, true, false, -1, true)]
    [InlineData(false, true, true, 0, true)]
    [InlineData(true, false, true, 1, true)]
    public async Task FleetDeletionIncludesBothGespanneAndPreservesDisplay(bool trailer, bool secondary, bool archived, int days, bool blocked)
    {
        var store = new FleetStore();
        var tour = MakeTour(archived, days);
        if (trailer) { if (secondary) tour.SecondaryTrailerId = "tr"; else tour.TrailerId = "tr"; }
        else { if (secondary) tour.SecondaryVehicleId = "v"; else tour.VehicleId = "v"; }
        var model = new VehiclesSectionViewModel(store, new TourStore(tour), new AppDataSyncService());
        await model.RefreshAsync();
        if (trailer) model.ShowTrailersCommand.Execute(null);
        var error = await model.DeleteEntryAsync(Assert.Single(model.Entries));
        Assert.Equal(blocked, error is not null);
        Assert.Equal(!blocked, trailer ? store.Data.Trailers[0].IsDeleted : store.Data.Vehicles[0].IsDeleted);
        Assert.Equal(blocked ? 1 : 0, model.Entries.Count);
        Assert.Equal(!blocked, store.Data.VehicleCombinations[0].IsDeleted);
        Assert.Equal(blocked, store.Data.VehicleCombinations[0].Active);
        var display = VehicleCombinationDisplayResolver.Resolve(store.Data, "v", "tr");
        Assert.Equal("Truck [ZH1]", display.VehicleLabel);
        Assert.Equal("Trailer [ZH2]", display.TrailerLabel);
        Assert.Equal(1500, display.VehiclePayloadKg);
        Assert.Equal(2000, display.TrailerLoadKg);
        model.ShowCombinationsCommand.Execute(null);
        Assert.Equal(blocked ? 1 : 0, model.Entries.Count);
    }

    [Fact]
    public void InvalidArchiveDateStillBlocksDeletion()
        => Assert.True(ResourceAvailabilityService.BlocksResourceDeletion(new TourRecord { IsArchived = true, Date = "invalid" }, DateOnly.FromDateTime(DateTime.Today)));

    [Fact]
    public async Task FailedEmployeeDeletionLeavesLocalAndStoredStateUnchanged()
    {
        var store = new EmployeeStore { FailSave = true };
        var tour = MakeTour(true, -1);
        tour.EmployeeIds = ["emp"];
        var model = new EmployeesSectionViewModel(store, new TourStore(tour), null!, new AppDataSyncService());
        await model.RefreshAsync();
        var entry = Assert.Single(model.Entries);
        await Assert.ThrowsAsync<IOException>(() => model.DeleteEntryAsync(entry));
        Assert.False(store.Item.IsDeleted);
        Assert.True(store.Item.Active);
        Assert.True(store.Item.HasProgramProfile);
        Assert.True(Assert.Single(model.Entries).Active);
        store.FailSave = false;
        Assert.Null(await model.DeleteEntryAsync(entry));
        Assert.Empty(model.Entries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedFleetDeletionLeavesResourcesAndCombinationsUnchanged(bool trailer)
    {
        var store = new FleetStore { FailSave = true };
        var tour = MakeTour(true, -1);
        tour.VehicleId = "v";
        tour.TrailerId = "tr";
        var model = new VehiclesSectionViewModel(store, new TourStore(tour), new AppDataSyncService());
        await model.RefreshAsync();
        if (trailer) model.ShowTrailersCommand.Execute(null);
        var entry = Assert.Single(model.Entries);
        await Assert.ThrowsAsync<IOException>(() => model.DeleteEntryAsync(entry));
        Assert.False(store.Data.Vehicles[0].IsDeleted);
        Assert.False(store.Data.Trailers[0].IsDeleted);
        Assert.False(store.Data.VehicleCombinations[0].IsDeleted);
        Assert.True(Assert.Single(model.Entries).Active);
        store.FailSave = false;
        Assert.Null(await model.DeleteEntryAsync(entry));
        Assert.Empty(model.Entries);
    }

    [Fact]
    public async Task StaleEmployeeEditorCannotResurrectDeletedEmployee()
    {
        var store = new EmployeeStore { Item = new Employee { Id = "emp", DisplayName = "Anna", IsDeleted = true, Active = false } };
        var model = new EmployeesSectionViewModel(store, new TourStore(MakeTour(true, -1)), null!, new AppDataSyncService());
        await model.RefreshAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => model.ApplyEditorResultAsync(
            new EmployeeEditorResult("emp", "Changed", "", "", true, false, false, "", "")));
        Assert.Equal("Anna", store.Item.DisplayName);
        Assert.True(store.Item.IsDeleted);
        Assert.Empty(model.Entries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeletedFleetResourcesCannotBeEditedOrUsedInNewCombinations(bool trailer)
    {
        var store = new FleetStore();
        if (trailer) store.Data.Trailers[0].IsDeleted = true;
        else store.Data.Vehicles[0].IsDeleted = true;
        var model = new VehiclesSectionViewModel(store, new TourStore(MakeTour(true, -1)), new AppDataSyncService());
        await model.RefreshAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => model.ApplyEditorResultAsync(
            new VehicleEditorResult(trailer ? "tr" : "v", trailer, "truck", "Changed", "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", false, "", "")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => model.ApplyCombinationEditorResultAsync(
            new VehicleCombinationEditorResult(null, "v", "tr", 100, 100, "", true)));
        Assert.Equal("Truck", store.Data.Vehicles[0].Name);
        Assert.Equal("Trailer", store.Data.Trailers[0].Name);
        Assert.Single(store.Data.VehicleCombinations);
    }

    [Fact]
    public async Task DeletedResourcesSurviveRepositoryReloadAndDoNotReserveNamesOrPlates()
    {
        var directory = Path.Combine(Path.GetTempPath(), "resource-deletion-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var employees = new JsonEmployeesRepository(Path.Combine(directory, "employees.json"));
            await employees.SaveAsync([new Employee { Id = "old", DisplayName = "Anna", IsDeleted = true }]);
            var employee = Assert.Single(await employees.LoadAsync());
            Assert.True(employee.IsDeleted);
            Assert.False(employee.Active);
            Assert.False(employee.HasProgramProfile);

            var fleet = new JsonVehicleDataRepository(Path.Combine(directory, "vehicles.json"));
            var data = new FleetStore().Data;
            data.Vehicles[0].IsDeleted = true;
            data.Trailers[0].IsDeleted = true;
            data.VehicleCombinations[0].IsDeleted = true;
            data.Vehicles.Add(new Vehicle { Id = "new-v", Name = "Truck", LicensePlate = "ZH1" });
            data.Trailers.Add(new TrailerRecord { Id = "new-tr", Name = "Trailer", LicensePlate = "ZH2" });
            await fleet.SaveAsync(data);
            var reloaded = await fleet.LoadAsync();
            Assert.Equal(2, reloaded.Vehicles.Count);
            Assert.Equal(2, reloaded.Trailers.Count);
            Assert.False(reloaded.Vehicles.Single(x => x.IsDeleted).Active);
            Assert.False(reloaded.Trailers.Single(x => x.IsDeleted).Active);
            Assert.True(Assert.Single(reloaded.VehicleCombinations).IsDeleted);
            var display = VehicleCombinationDisplayResolver.Resolve(reloaded, "v", "tr");
            Assert.Equal("Truck [ZH1]", display.VehicleLabel);
            Assert.Equal(2000, display.TrailerLoadKg);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmployeeDeletionWorksWithJsonStoreAndKeepsOtherEmployees(bool hasHistory)
    {
        var directory = Path.Combine(Path.GetTempPath(), "employee-deletion-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var store = new JsonEmployeesRepository(Path.Combine(directory, "employees.json"));
            await store.SaveAsync([
                new Employee { Id = "emp", DisplayName = "Anna" },
                new Employee { Id = "other", DisplayName = "Other" }
            ]);
            var tour = MakeTour(true, -1);
            if (hasHistory) tour.EmployeeIds = ["emp"];
            var model = new EmployeesSectionViewModel(store, new TourStore(tour), null!, new AppDataSyncService());
            await model.RefreshAsync();
            Assert.Null(await model.DeleteEntryAsync(model.Entries.Single(x => x.Id == "emp")));
            Assert.Equal("other", Assert.Single(model.Entries).Id);
            var reloaded = await store.LoadAsync();
            Assert.Contains(reloaded, x => x.Id == "other" && !x.IsDeleted);
            if (hasHistory) Assert.True(reloaded.Single(x => x.Id == "emp").IsDeleted);
            else Assert.DoesNotContain(reloaded, x => x.Id == "emp");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static TourRecord MakeTour(bool archived, int days) => new()
    {
        Id = 1, Name = "History", IsArchived = archived,
        Date = DateTime.Today.AddDays(days).ToString("yyyy-MM-dd")
    };

    private sealed class TourStore(TourRecord tour) : ITourRecordStore
    {
        public Task<IReadOnlyList<TourRecord>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TourRecord>>([tour]);
        public Task SaveAsync(IEnumerable<TourRecord> tours, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Deleting resources must not rewrite tours.");
    }
    private sealed class EmployeeStore : IEmployeeDataStore, IEmployeeMutationStore
    {
        public Employee Item = new() { Id = "emp", DisplayName = "Anna" };
        public int Upserts;
        public int Deletes;
        public bool FailSave;
        public Task<IReadOnlyList<Employee>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Employee>>([Item]);
        public Task SaveAsync(IEnumerable<Employee> employees, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Use targeted mutation.");
        public Task UpsertAsync(Employee employee, CancellationToken cancellationToken = default) { if (FailSave) throw new IOException("Save failed"); Item = employee; Upserts++; return Task.CompletedTask; }
        public Task DeleteAsync(string employeeId, CancellationToken cancellationToken = default) { Deletes++; return Task.CompletedTask; }
    }
    private sealed class FleetStore : IVehicleDataStore
    {
        public bool FailSave;
        public VehicleDataRecord Data = new()
        {
            Vehicles = [new() { Id = "v", Name = "Truck", LicensePlate = "ZH1" }],
            Trailers = [new() { Id = "tr", Name = "Trailer", LicensePlate = "ZH2" }],
            VehicleCombinations = [new() { Id = "c", VehicleId = "v", TrailerId = "tr", VehiclePayloadKg = 1500, TrailerLoadKg = 2000 }]
        };
        public Task<VehicleDataRecord> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Data);
        public Task SaveAsync(VehicleDataRecord data, CancellationToken cancellationToken = default) { if (FailSave) throw new IOException("Save failed"); Data = data; return Task.CompletedTask; }
    }
}
