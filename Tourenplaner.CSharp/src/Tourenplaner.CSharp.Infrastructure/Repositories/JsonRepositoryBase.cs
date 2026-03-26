using System.Text.Json;

namespace Tourenplaner.CSharp.Infrastructure.Repositories;

public abstract class JsonRepositoryBase<T>
{
    private const int FileOpenRetryAttempts = 6;
    private readonly JsonSerializerOptions _serializerOptions;

    protected JsonRepositoryBase(string filePath)
    {
        FilePath = filePath;
        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }

    protected string FilePath { get; }

    protected async Task<IReadOnlyList<T>> ReadListAsync(CancellationToken cancellationToken)
    {
        EnsureParentDirectory();
        if (!File.Exists(FilePath))
        {
            await File.WriteAllTextAsync(FilePath, "[]", cancellationToken);
        }

        await using var stream = await OpenFileWithRetryAsync(
            FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<List<T>>(stream, _serializerOptions, cancellationToken);
        return result ?? new List<T>();
    }

    protected async Task WriteListAsync(IEnumerable<T> values, CancellationToken cancellationToken)
    {
        EnsureParentDirectory();
        await using var stream = await OpenFileWithRetryAsync(
            FilePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            cancellationToken);
        await JsonSerializer.SerializeAsync(stream, values.ToList(), _serializerOptions, cancellationToken);
    }

    protected async Task<T> ReadSingleAsync(T fallback, CancellationToken cancellationToken)
    {
        EnsureParentDirectory();
        if (!File.Exists(FilePath))
        {
            await WriteSingleAsync(fallback, cancellationToken);
            return fallback;
        }

        await using var stream = await OpenFileWithRetryAsync(
            FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            cancellationToken);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, _serializerOptions, cancellationToken);
        return value ?? fallback;
    }

    protected async Task WriteSingleAsync(T value, CancellationToken cancellationToken)
    {
        EnsureParentDirectory();
        await using var stream = await OpenFileWithRetryAsync(
            FilePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            cancellationToken);
        await JsonSerializer.SerializeAsync(stream, value, _serializerOptions, cancellationToken);
    }

    private static async Task<FileStream> OpenFileWithRetryAsync(
        string path,
        FileMode mode,
        FileAccess access,
        FileShare share,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return File.Open(path, mode, access, share);
            }
            catch (IOException) when (attempt < FileOpenRetryAttempts)
            {
                await Task.Delay(40 * attempt, cancellationToken);
            }
        }
    }

    private void EnsureParentDirectory()
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
