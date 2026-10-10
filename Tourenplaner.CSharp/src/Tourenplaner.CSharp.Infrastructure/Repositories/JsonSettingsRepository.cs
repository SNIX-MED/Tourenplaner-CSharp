using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Repositories;

public sealed class JsonSettingsRepository : JsonRepositoryBase<AppSettings>, ISettingsRepository
{
    public JsonSettingsRepository(string filePath) : base(filePath)
    {
    }

    public async Task<AppSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await ReadSingleAsync(new AppSettings(), cancellationToken);
        if (settings.ApplyStayMinutesDefaultsMigration())
        {
            await WriteSingleAsync(settings, cancellationToken);
        }

        return settings;
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        => WriteSingleAsync(settings, cancellationToken);
}
