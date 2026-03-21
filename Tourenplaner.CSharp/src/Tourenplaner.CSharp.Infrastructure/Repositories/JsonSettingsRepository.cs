using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Repositories;

public sealed class JsonSettingsRepository : JsonRepositoryBase<AppSettings>, ISettingsRepository
{
    public JsonSettingsRepository(string filePath) : base(filePath)
    {
    }

    public Task<AppSettings> GetAsync(CancellationToken cancellationToken = default)
        => ReadSingleAsync(new AppSettings(), cancellationToken);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        => WriteSingleAsync(settings, cancellationToken);
}
