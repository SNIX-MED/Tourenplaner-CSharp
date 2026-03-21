using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Abstractions;

public interface IEmployeeRepository
{
    Task<IReadOnlyList<Employee>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAllAsync(IEnumerable<Employee> employees, CancellationToken cancellationToken = default);
}
