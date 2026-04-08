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

                foreach (var vehicleId in GetAssignedResourceIds(a.Tour.VehicleId, a.Tour.SecondaryVehicleId)
                    .Intersect(GetAssignedResourceIds(b.Tour.VehicleId, b.Tour.SecondaryVehicleId), StringComparer.OrdinalIgnoreCase))
                {
                    conflicts.Add(BuildConflict("vehicle", vehicleId, a, b));
                }

                foreach (var trailerId in GetAssignedResourceIds(a.Tour.TrailerId, a.Tour.SecondaryTrailerId)
                    .Intersect(GetAssignedResourceIds(b.Tour.TrailerId, b.Tour.SecondaryTrailerId), StringComparer.OrdinalIgnoreCase))
                {
                    conflicts.Add(BuildConflict("trailer", trailerId, a, b));
                }

                foreach (var employeeId in a.Tour.EmployeeIds.Intersect(b.Tour.EmployeeIds, StringComparer.OrdinalIgnoreCase))
                {
                    conflicts.Add(BuildConflict("employee", employeeId, a, b));
                }
            }
        }

        return conflicts;
    }

    public IReadOnlyList<TourAssignmentConflict> FindSameDayAssignmentConflicts(IEnumerable<TourRecord> tours)
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

                foreach (var vehicleId in GetAssignedResourceIds(a.Tour.VehicleId, a.Tour.SecondaryVehicleId)
                    .Intersect(GetAssignedResourceIds(b.Tour.VehicleId, b.Tour.SecondaryVehicleId), StringComparer.OrdinalIgnoreCase))
                {
                    conflicts.Add(BuildConflict("Fahrzeug", vehicleId, a, b));
                }

                foreach (var trailerId in GetAssignedResourceIds(a.Tour.TrailerId, a.Tour.SecondaryTrailerId)
                    .Intersect(GetAssignedResourceIds(b.Tour.TrailerId, b.Tour.SecondaryTrailerId), StringComparer.OrdinalIgnoreCase))
                {
                    conflicts.Add(BuildConflict("Anhänger", trailerId, a, b));
                }

                foreach (var employeeId in a.Tour.EmployeeIds.Intersect(b.Tour.EmployeeIds, StringComparer.OrdinalIgnoreCase))
                {
                    conflicts.Add(BuildConflict("Mitarbeiter", employeeId, a, b));
                }
            }
        }

        return conflicts;
    }

    private static bool IsOverlapping(DateTime startA, DateTime endA, DateTime startB, DateTime endB)
    {
        return startA < endB && startB < endA;
    }

    private static IReadOnlyList<string> GetAssignedResourceIds(params string?[] ids)
    {
        return ids
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static TourAssignmentConflict BuildConflict(
        string resourceType,
        string resourceId,
        (TourRecord Tour, TourScheduleResult Schedule) left,
        (TourRecord Tour, TourScheduleResult Schedule) right)
    {
        var text = $"{resourceType} '{resourceId}' ist am {left.Schedule.Start:dd.MM.yyyy} doppelt eingeplant (Tour {left.Tour.Id} und Tour {right.Tour.Id}).";
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
