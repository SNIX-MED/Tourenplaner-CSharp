using System.IO.Compression;
using System.Text;
using Tourenplaner.CSharp.App.Services;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class PostgreSqlBackupRestoreServiceTests
{
    private static readonly string[] Tables =
    [
        "orders", "tours", "employees", "vehicles", "tour_records", "calendar_manual_entries", "singletons"
    ];

    [Fact]
    public void ValidateBackupFile_AcceptsCompletePostgreSqlBackup()
    {
        var path = CreateBackup(includeAllTables: true);
        try
        {
            PostgreSqlBackupRestoreService.ValidateBackupFile(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ValidateBackupFile_RejectsIncompletePostgreSqlBackup()
    {
        var path = CreateBackup(includeAllTables: false);
        try
        {
            var exception = Assert.Throws<InvalidDataException>(() =>
                PostgreSqlBackupRestoreService.ValidateBackupFile(path));
            Assert.Contains("unvollständig", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateBackup(bool includeAllTables)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tourenplaner-restore-test-{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "metadata.json", "{\"StorageMode\":\"PostgreSql\"}");
        foreach (var table in includeAllTables ? Tables : Tables.Take(Tables.Length - 1))
        {
            WriteEntry(archive, $"postgresql/{table}.json", "[]");
        }

        return path;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
