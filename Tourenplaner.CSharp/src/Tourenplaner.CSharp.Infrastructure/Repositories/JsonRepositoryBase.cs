using System.Text.Json;

using Tourenplaner.CSharp.Infrastructure.Storage;

namespace Tourenplaner.CSharp.Infrastructure.Repositories;

public abstract class JsonRepositoryBase<T>
{
    private const int FileOpenRetryAttempts = 6;
    private static readonly JsonSerializerOptions SerializerOptions = RepositoryJsonSerializerOptions.FileRepository;

    protected JsonRepositoryBase(string filePath)
    {
        FilePath = filePath;
    }

    protected string FilePath { get; }

    protected async Task<IReadOnlyList<T>> ReadListAsync(CancellationToken cancellationToken)
    {
        var gate = ResolveFileGate();
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureParentDirectory();
            if (!File.Exists(FilePath))
            {
                await File.WriteAllTextAsync(FilePath, "[]", cancellationToken);
            }

            await using var stream = await OpenFileWithRetryAsync(
                FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, cancellationToken);
            var result = await JsonSerializer.DeserializeAsync<List<T>>(stream, SerializerOptions, cancellationToken);
            return result ?? new List<T>();
        }
        finally
        {
            gate.Release();
        }
    }

    protected async Task WriteListAsync(IEnumerable<T> values, CancellationToken cancellationToken)
    {
        var gate = ResolveFileGate();
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureParentDirectory();
            await using var stream = await OpenFileWithRetryAsync(
                FilePath, FileMode.Create, FileAccess.Write, FileShare.None, cancellationToken);
            await JsonSerializer.SerializeAsync(stream, values.ToList(), SerializerOptions, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    protected async Task MutateListAsync(Action<List<T>> mutation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var gate = ResolveFileGate();
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureParentDirectory();
            List<T> items;
            if (!File.Exists(FilePath))
            {
                items = [];
            }
            else
            {
                await using var readStream = await OpenFileWithRetryAsync(
                    FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, cancellationToken);
                items = await JsonSerializer.DeserializeAsync<List<T>>(readStream, SerializerOptions, cancellationToken) ?? [];
            }

            mutation(items);
            await using var writeStream = await OpenFileWithRetryAsync(
                FilePath, FileMode.Create, FileAccess.Write, FileShare.None, cancellationToken);
            await JsonSerializer.SerializeAsync(writeStream, items, SerializerOptions, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    protected async Task<T> ReadSingleAsync(T fallback, CancellationToken cancellationToken)
    {
        var gate = ResolveFileGate();
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureParentDirectory();
            if (!File.Exists(FilePath))
            {
                await using var createStream = await OpenFileWithRetryAsync(
                    FilePath, FileMode.Create, FileAccess.Write, FileShare.None, cancellationToken);
                await JsonSerializer.SerializeAsync(createStream, fallback, SerializerOptions, cancellationToken);
                return fallback;
            }

            await using var stream = await OpenFileWithRetryAsync(
                FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, cancellationToken);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken);
            return value ?? fallback;
        }
        finally
        {
            gate.Release();
        }
    }

    protected async Task WriteSingleAsync(T value, CancellationToken cancellationToken)
    {
        var gate = ResolveFileGate();
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureParentDirectory();
            await using var stream = await OpenFileWithRetryAsync(
                FilePath, FileMode.Create, FileAccess.Write, FileShare.None, cancellationToken);
            await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private SemaphoreSlim ResolveFileGate() =>
        JsonPathAccessCoordinator.Resolve(FilePath);

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
