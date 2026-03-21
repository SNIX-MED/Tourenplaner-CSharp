using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Storage;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

public sealed class JsonEmployeesRepository
{
    private readonly JsonFileStore _store;
    private readonly string _path;

    public JsonEmployeesRepository(string path, JsonFileStore? store = null)
    {
        _path = path;
        _store = store ?? new JsonFileStore();
    }

    public async Task<IReadOnlyList<Employee>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var payload = await _store.LoadAsync(_path, () => new List<Employee>(), createIfMissing: true, backupInvalid: true, cancellationToken: cancellationToken);
        var normalized = new List<Employee>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in payload.Where(x => x is not null).Select(EmployeeNormalizer.Normalize))
        {
            if (string.IsNullOrWhiteSpace(entry.DisplayName))
            {
                continue;
            }

            if (!seen.Add(entry.Id))
            {
                entry.Id = Guid.NewGuid().ToString();
                seen.Add(entry.Id);
            }

            normalized.Add(entry);
        }

        normalized = normalized
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await _store.AtomicWriteAsync(_path, normalized, cancellationToken);
        return normalized;
    }

    public async Task SaveAsync(IEnumerable<Employee> employees, CancellationToken cancellationToken = default)
    {
        var normalized = (employees ?? Array.Empty<Employee>())
            .Where(x => x is not null)
            .Select(EmployeeNormalizer.Normalize)
            .Where(x => !string.IsNullOrWhiteSpace(x.DisplayName))
            .ToList();

        await _store.AtomicWriteAsync(_path, normalized, cancellationToken);
    }
}
