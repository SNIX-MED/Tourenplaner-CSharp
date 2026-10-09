using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

public interface ICalendarManualEntryMutationStore
{
    Task UpsertAsync(CalendarManualEntry entry, CancellationToken cancellationToken = default);
    Task DeleteAsync(string entryId, CancellationToken cancellationToken = default);
}
