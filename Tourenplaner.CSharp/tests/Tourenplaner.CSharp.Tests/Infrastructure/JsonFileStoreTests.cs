using System.Text.Json;
using Tourenplaner.CSharp.Infrastructure.Storage;

namespace Tourenplaner.CSharp.Tests.Infrastructure;

public class JsonFileStoreTests
{
    [Fact]
    public async Task AtomicWriteAsync_CreatesNewFile()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "settings.json");

        try
        {
            var store = new JsonFileStore();
            await store.AtomicWriteAsync(path, new { Name = "GAWELA", Count = 1 });

            Assert.True(File.Exists(path));
            var json = await File.ReadAllTextAsync(path);
            Assert.Contains("\"Name\": \"GAWELA\"", json);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task AtomicWriteAsync_ReplacesExistingFile()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "settings.json");

        try
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { Name = "Alt", Count = 1 }));

            var store = new JsonFileStore();
            await store.AtomicWriteAsync(path, new { Name = "Neu", Count = 2 });

            var json = await File.ReadAllTextAsync(path);
            Assert.Contains("\"Name\": \"Neu\"", json);
            Assert.DoesNotContain("\"Name\": \"Alt\"", json);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "tourenplaner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
