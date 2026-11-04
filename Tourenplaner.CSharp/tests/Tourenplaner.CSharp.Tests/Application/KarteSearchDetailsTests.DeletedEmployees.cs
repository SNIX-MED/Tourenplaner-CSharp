using System.Reflection;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed partial class KarteSearchDetailsTests
{
    [Fact]
    public async Task DeletedEmployeeKeepsHistoricalLabelButCannotOverrideReassignedWebfleetDevice()
    {
        var store = new ReassignedDeviceEmployeeStore();
        var model = CreateViewModel(employeeRepository: store);
        await model.RefreshAsync();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var names = (Dictionary<string, string>)typeof(KarteSectionViewModel)
            .GetField("_employeeLabelsById", flags)!.GetValue(model)!;
        var deviceNames = (Dictionary<string, string>)typeof(KarteSectionViewModel)
            .GetField("_employeeLabelsByWebfleetObjectUid", flags)!.GetValue(model)!;
        var deviceIds = (Dictionary<string, string>)typeof(KarteSectionViewModel)
            .GetField("_employeeIdsByWebfleetObjectUid", flags)!.GetValue(model)!;
        Assert.Equal("Z Old", names["old"]);
        Assert.Equal("Anna New", deviceNames["device"]);
        Assert.Equal("new", deviceIds["device"]);
        Assert.DoesNotContain("old-only", deviceNames.Keys);
        Assert.DoesNotContain("old-only", deviceIds.Keys);
    }

    private sealed class ReassignedDeviceEmployeeStore : IEmployeeDataStore
    {
        public Task<IReadOnlyList<Employee>> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Employee>>([
                new() { Id = "new", DisplayName = "Anna New", WebfleetObjectUid = "device", WebfleetDriverUid = "new-driver" },
                new() { Id = "old", DisplayName = "Z Old", WebfleetObjectUid = "device", WebfleetDriverUid = "old-driver", IsDeleted = true, Active = false },
                new() { Id = "old2", DisplayName = "Old Only", WebfleetObjectUid = "old-only", IsDeleted = true, Active = false }
            ]);

        public Task SaveAsync(IEnumerable<Employee> employees, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Refresh must be read-only for employees.");
    }
}
