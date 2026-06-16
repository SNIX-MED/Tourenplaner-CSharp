using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

public interface IEmployeeDataStore
{
    Task<IReadOnlyList<Employee>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IEnumerable<Employee> employees, CancellationToken cancellationToken = default);
}
