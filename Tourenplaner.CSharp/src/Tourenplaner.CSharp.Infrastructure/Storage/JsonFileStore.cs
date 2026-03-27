using System.Text.Json;

namespace Tourenplaner.CSharp.Infrastructure.Storage;

public class JsonStorageException : Exception
{
    public JsonStorageException(string message, Exception? innerException = null) : base(message, innerException)
    {
    }
}

public sealed class InvalidJsonFileException : JsonStorageException
{
    public InvalidJsonFileException(string message, Exception? innerException = null) : base(message, innerException)
    {
    }
}

public sealed class JsonFileStore
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    public async Task AtomicWriteAsync<T>(string path, T payload, CancellationToken cancellationToken = default)
    {
        var target = new FileInfo(path);
        target.Directory?.Create();

        var tempPath = Path.Combine(target.DirectoryName ?? string.Empty, $"{target.Name}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, payload, WriteOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            PromoteTempFile(tempPath, path);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // ignored
                }
            }
        }
    }

    public async Task<T> LoadAsync<T>(
        string path,
        Func<T> defaultFactory,
        bool createIfMissing = false,
        bool backupInvalid = false,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            var fallback = defaultFactory();
            if (createIfMissing)
            {
                await AtomicWriteAsync(path, fallback, cancellationToken);
            }

            return fallback;
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, ReadOptions, cancellationToken);
            return value is null ? defaultFactory() : value;
        }
        catch (JsonException ex)
        {
            if (backupInvalid)
            {
                BackupCorruptFile(path);
            }

            throw new InvalidJsonFileException($"Invalid JSON in {path}", ex);
        }
        catch (IOException ex)
        {
            throw new JsonStorageException($"Could not read JSON file {path}", ex);
        }
    }

    private static void PromoteTempFile(string tempPath, string targetPath)
    {
        if (File.Exists(targetPath))
        {
            File.Replace(tempPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            return;
        }

        File.Move(tempPath, targetPath);
    }

    public string? BackupCorruptFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var source = new FileInfo(path);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupPath = Path.Combine(source.DirectoryName ?? string.Empty, $"{Path.GetFileNameWithoutExtension(source.Name)}.corrupt_{timestamp}{source.Extension}");
        File.Copy(path, backupPath, overwrite: false);
        return backupPath;
    }
}
