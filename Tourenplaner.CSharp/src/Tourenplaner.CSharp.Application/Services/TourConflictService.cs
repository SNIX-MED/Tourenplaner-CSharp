using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Services;

public sealed class TourConflictService
{
    private readonly TourScheduleService _scheduleService;

    public TourConflictService(TourScheduleService? scheduleService = null)
    {
        _scheduleService = scheduleService ?? new TourScheduleService();
    }

    public IReadOnlyList<TourAssignmentConflict> FindAssignmentConflicts(IEnumerable<TourRecord> tours)
    {
        var items = (tours ?? [])
            .Where(t => t is not null)
            .Select(t => (Tour: t, Schedule: _scheduleService.BuildSchedule(t)))
            .ToList();

        var conflicts = new List<TourAssignmentConflict>();

        for (var i = 0; i < items.Count; i++)
        {
            for (var j = i + 1; j < items.Count; j++)
            {
                var a = items[i];
                var b = items[j];

                if (a.Schedule.Start.Date != b.Schedule.Start.Date)
                {
                    continue;
                }

                if (!IsOverlapping(a.Schedule.Start, a.Schedule.End, b.Schedule.Start, b.Schedule.End))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(a.Tour.VehicleId) &&
                    string.Equals(a.Tour.VehicleId, b.Tour.VehicleId, StringComparison.OrdinalIgnoreCase))
                {
                    conflicts.Add(BuildConflict("vehicle", a.Tour.VehicleId!, a, b));
                }

                if (!string.IsNullOrWhiteSpace(a.Tour.TrailerId) &&
                    string.Equals(a.Tour.TrailerId, b.Tour.TrailerId, StringComparison.OrdinalIgnoreCase))
                {
                    conflicts.Add(BuildConflict("trailer", a.Tour.TrailerId!, a, b));
                }

                foreach (var employeeId in a.Tour.EmployeeIds.Intersect(b.Tour.EmployeeIds, StringComparer.OrdinalIgnoreCase))
                {
                    conflicts.Add(BuildConflict("employee", employeeId, a, b));
                }
            }
        }

        return conflicts;
    }

    private static bool IsOverlapping(DateTime startA, DateTime endA, DateTime startB, DateTime endB)
    {
        return startA < endB && startB < endA;
    }

    private static TourAssignmentConflict BuildConflict(
        string resourceType,
        string resourceId,
        (TourRecord Tour, TourScheduleResult Schedule) left,
        (TourRecord Tour, TourScheduleResult Schedule) right)
    {
        var text = $"Konflikt fuer {resourceType} '{resourceId}' zwischen Tour {left.Tour.Id} und Tour {right.Tour.Id}.";
        return new TourAssignmentConflict(
            resourceType,
            resourceId,
            left.Tour.Id,
            right.Tour.Id,
            left.Schedule.Start,
            left.Schedule.End,
            right.Schedule.Start,
            right.Schedule.End,
            text);
    }
}
