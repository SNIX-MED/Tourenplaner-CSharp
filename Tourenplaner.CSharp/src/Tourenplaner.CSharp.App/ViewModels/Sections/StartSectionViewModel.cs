using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class StartSectionViewModel : SectionViewModelBase
{
    private readonly AppSnapshotService _snapshotService;

    private string _snapshot = "Loading snapshot...";

    public StartSectionViewModel(AppSnapshotService snapshotService)
        : base("Start", "Overview of current planning status.")
    {
        _snapshotService = snapshotService;
    }

    public string Snapshot
    {
        get => _snapshot;
        private set => SetProperty(ref _snapshot, value);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        AppSnapshot value = await _snapshotService.CreateAsync(cancellationToken);
        Snapshot =
            $"Orders: {value.OrderCount} (Non-Map: {value.NonMapOrderCount}) | Tours: {value.TourCount} | Employees: {value.EmployeeCount} | Vehicles: {value.VehicleCount}";
    }
}
