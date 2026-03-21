using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Abstractions;

public interface ISettingsRepository
{
    Task<AppSettings> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
