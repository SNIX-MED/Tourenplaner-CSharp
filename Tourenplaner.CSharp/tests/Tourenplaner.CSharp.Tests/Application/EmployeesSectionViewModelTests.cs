using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class EmployeesSectionViewModelTests
{
    [Fact]
    public async Task OverlappingStartupAndRefreshDoNotDuplicateEmployees()
    {
        var store = new ControlledEmployeeStore();
        var model = new EmployeesSectionViewModel(store, null!, null!, new AppDataSyncService());
        var refresh = model.RefreshAsync();

        // The constructor's initial load is still pending.
        Assert.Equal(1, store.LoadCount);
        store.FirstLoad.SetResult([new Employee { Id = "1", DisplayName = "Anna" }]);
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, store.LoadCount);
        Assert.Equal("Anna", Assert.Single(model.Entries).Name);
        Assert.Equal("Mitarbeiter: 1", model.CountText);
    }

    [Fact]
    public async Task FailedRefreshPreservesEmployeesAndAllowsRetry()
    {
        var store = new ControlledEmployeeStore();
        var model = new EmployeesSectionViewModel(store, null!, null!, new AppDataSyncService());
        store.FirstLoad.SetResult([new Employee { Id = "1", DisplayName = "Anna" }]);
        await model.RefreshAsync();

        store.FailLoad = true;
        await Assert.ThrowsAsync<IOException>(() => model.RefreshAsync());
        Assert.Single(model.Entries);
        store.FailLoad = false;
        await model.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(model.Entries);
    }

    private sealed class ControlledEmployeeStore : IEmployeeDataStore
    {
        public TaskCompletionSource<IReadOnlyList<Employee>> FirstLoad { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int LoadCount { get; private set; }
        public bool FailLoad { get; set; }

        public Task<IReadOnlyList<Employee>> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            if (FailLoad) throw new IOException("Load failed");
            return LoadCount == 1
                ? FirstLoad.Task
                : Task.FromResult<IReadOnlyList<Employee>>([new Employee { Id = "1", DisplayName = "Anna" }]);
        }

        public Task SaveAsync(IEnumerable<Employee> employees, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
