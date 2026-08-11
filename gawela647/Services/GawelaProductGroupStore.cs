using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Gawela.ColorConfigurator.Models;

namespace Gawela.ColorConfigurator.Services;

public sealed class GawelaProductGroupStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly IWebHostEnvironment _environment;
    private readonly object _sync = new();

    public GawelaProductGroupStore(IWebHostEnvironment environment) => _environment = environment;

    public string RootPath => Path.Combine(_environment.ContentRootPath, "App_Data", "GawelaColorAssets");
    public string FilePath => Path.Combine(RootPath, "product-groups.json");

    public IReadOnlyList<GawelaProductGroup> Load()
    {
        lock (_sync)
        {
            if (!File.Exists(FilePath)) return Array.Empty<GawelaProductGroup>();
            try
            {
                var doc = JsonSerializer.Deserialize<GawelaProductGroupDocument>(File.ReadAllText(FilePath), JsonOptions);
                return (doc?.Groups ?? new())
                    .Where(x => x.MasterProductId > 0)
                    .Select(Normalize)
                    .ToList();
            }
            catch
            {
                return Array.Empty<GawelaProductGroup>();
            }
        }
    }

    public GawelaProductGroup FindByProduct(int productId)
        => Load().FirstOrDefault(x => x.ProductIds.Contains(productId));

    public GawelaProductGroup FindByMaster(int masterProductId)
        => Load().FirstOrDefault(x => x.MasterProductId == masterProductId);

    public int ResolveOwnerProductId(int productId)
        => FindByProduct(productId)?.MasterProductId ?? productId;

    public async Task SaveAsync(GawelaProductGroup group)
    {
        if (group == null || group.MasterProductId <= 0) throw new InvalidOperationException("Ungültige Produktgruppe.");
        group = Normalize(group);
        var groups = Load().Where(x => x.MasterProductId != group.MasterProductId).ToList();
        var conflicts = groups.Where(x => x.ProductIds.Any(id => group.ProductIds.Contains(id))).ToList();
        if (conflicts.Count > 0)
        {
            var names = string.Join(", ", conflicts.Select(x => x.Name).Distinct());
            throw new InvalidOperationException($"Mindestens ein Produkt ist bereits einer anderen Produktgruppe zugeordnet ({names}).");
        }
        groups.Add(group);
        await WriteAsync(groups);
    }

    public async Task DeleteAsync(int masterProductId)
    {
        var groups = Load().Where(x => x.MasterProductId != masterProductId).ToList();
        await WriteAsync(groups);
    }

    public async Task RemoveProductAsync(int productId)
    {
        var groups = Load().Select(x => new GawelaProductGroup
        {
            Key = x.Key,
            Name = x.Name,
            MasterProductId = x.MasterProductId,
            ProductIds = x.ProductIds.Where(id => id != productId || id == x.MasterProductId).ToList()
        }).ToList();
        await WriteAsync(groups);
    }

    private async Task WriteAsync(IEnumerable<GawelaProductGroup> groups)
    {
        Directory.CreateDirectory(RootPath);
        var doc = new GawelaProductGroupDocument { Groups = groups.Select(Normalize).OrderBy(x => x.Name).ToList() };
        var tmp = FilePath + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(doc, JsonOptions));
        File.Move(tmp, FilePath, true);
    }

    private static GawelaProductGroup Normalize(GawelaProductGroup x)
    {
        var master = x.MasterProductId;
        var ids = (x.ProductIds ?? new()).Where(id => id > 0).Distinct().ToList();
        if (!ids.Contains(master)) ids.Insert(0, master);
        var key = string.IsNullOrWhiteSpace(x.Key) ? "group-" + master : x.Key.Trim();
        var name = string.IsNullOrWhiteSpace(x.Name) ? "Produktgruppe " + master : x.Name.Trim();
        return new GawelaProductGroup { Key = key, Name = name, MasterProductId = master, ProductIds = ids };
    }
}
