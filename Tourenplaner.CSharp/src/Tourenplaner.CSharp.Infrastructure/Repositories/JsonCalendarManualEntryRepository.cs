using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Repositories;

public sealed class JsonCalendarManualEntryRepository : JsonRepositoryBase<CalendarManualEntry>
{
    public JsonCalendarManualEntryRepository(string filePath)
        : base(filePath)
    {
    }

    public async Task<IReadOnlyList<CalendarManualEntry>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var entries = await ReadListAsync(cancellationToken);
        return entries
            .Where(x => x is not null)
            .Select(Normalize)
            .ToList();
    }

    public Task SaveAsync(IEnumerable<CalendarManualEntry> entries, CancellationToken cancellationToken = default)
    {
        var normalized = (entries ?? Array.Empty<CalendarManualEntry>())
            .Where(x => x is not null)
            .Select(Normalize)
            .ToList();
        return WriteListAsync(normalized, cancellationToken);
    }

    private static CalendarManualEntry Normalize(CalendarManualEntry entry)
    {
        return new CalendarManualEntry
        {
            Id = (entry.Id ?? string.Empty).Trim(),
            Date = (entry.Date ?? string.Empty).Trim(),
            Time = (entry.Time ?? string.Empty).Trim(),
            Title = (entry.Title ?? string.Empty).Trim(),
            Description = (entry.Description ?? string.Empty).Trim(),
            ColorHex = NormalizeColor(entry.ColorHex)
        };
    }

    private static string NormalizeColor(string? raw)
    {
        var candidate = (raw ?? string.Empty).Trim().ToUpperInvariant();
        return candidate.Length == 7 && candidate.StartsWith('#') ? candidate : "#0EA5E9";
    }
}
