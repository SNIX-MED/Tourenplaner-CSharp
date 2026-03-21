using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Repositories;

public sealed class JsonEmployeeRepository : JsonRepositoryBase<Employee>, IEmployeeRepository
{
    public JsonEmployeeRepository(string filePath) : base(filePath)
    {
    }

    public Task<IReadOnlyList<Employee>> GetAllAsync(CancellationToken cancellationToken = default)
        => ReadListAsync(cancellationToken);

    public Task SaveAllAsync(IEnumerable<Employee> employees, CancellationToken cancellationToken = default)
        => WriteListAsync(employees, cancellationToken);
}
