using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

public interface ICalendarManualEntryStore
{
    Task<IReadOnlyList<CalendarManualEntry>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IEnumerable<CalendarManualEntry> entries, CancellationToken cancellationToken = default);
}
