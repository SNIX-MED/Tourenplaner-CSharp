using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

public interface IEmployeeMutationStore
{
    Task UpsertAsync(Employee employee, CancellationToken cancellationToken = default);
    Task DeleteAsync(string employeeId, CancellationToken cancellationToken = default);
}
