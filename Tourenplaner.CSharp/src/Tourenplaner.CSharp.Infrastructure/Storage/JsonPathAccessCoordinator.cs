using System.Collections.Concurrent;

namespace Tourenplaner.CSharp.Infrastructure.Storage;

internal static class JsonPathAccessCoordinator
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);

    public static SemaphoreSlim Resolve(string path) =>
        Gates.GetOrAdd(Path.GetFullPath(path), static _ => new SemaphoreSlim(1, 1));
}
