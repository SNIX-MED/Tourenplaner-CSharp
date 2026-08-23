using System.Text.Json;
using Gawela.RackConfig.Settings;

namespace Gawela.RackConfig.Models;

public class EntityDisplayModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Secondary { get; set; } = string.Empty;
}

public class AccessoryMappingData
{
    public string Key { get; set; } = string.Empty;
    public int Width { get; set; }
    public int ProductId { get; set; }
}

public class AccessoryMappingModel : AccessoryMappingData
{
    public string Index { get; set; } = Guid.NewGuid().ToString("N");
    public string ProductName { get; set; } = string.Empty;
    public string ProductSku { get; set; } = string.Empty;
}

public class ConfigurationModel
{
    public bool Enabled { get; set; }
    public string CategoryIds { get; set; } = string.Empty;
    public int MaxPalletWeight { get; set; }
    public int DefaultDepth { get; set; }
    public int MaxVariants { get; set; }
    public int MinLevelsLow { get; set; }
    public int MinLevelsHigh { get; set; }
    public List<EntityDisplayModel> SelectedCategories { get; set; } = [];
    public List<AccessoryMappingModel> Accessories { get; set; } = [];
    public int[] CategorySelectedIds => RackConfigMapping.ParseIds(CategoryIds);
}

public static class RackConfigMapping
{
    public static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "spanplatte", "gitterrost", "stahlpanel", "drahtgitter", "durchschub",
        "schutz_eck", "schutz_mittel76", "schutz_mittel100"
    };

    public static int[] ParseIds(string? value)
        => (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, out var id) ? id : 0)
            .Where(x => x > 0)
            .Distinct()
            .ToArray();

    public static string NormalizeIds(string? value) => string.Join(',', ParseIds(value));

    public static List<AccessoryMappingModel> ParseMappings(RackConfigSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.AccessoryMappingsJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<AccessoryMappingData>>(settings.AccessoryMappingsJson) ?? [];
                return parsed
                    .Where(x => x != null && KnownKeys.Contains(x.Key ?? string.Empty))
                    .Select(x => new AccessoryMappingModel
                    {
                        Key = (x.Key ?? string.Empty).Trim().ToLowerInvariant(),
                        Width = x.Width,
                        ProductId = x.ProductId,
                        Index = Guid.NewGuid().ToString("N")
                    })
                    .ToList();
            }
            catch { }
        }

        var result = new List<AccessoryMappingModel>();
        void Add(string key, int width, int id)
        {
            if (id > 0)
                result.Add(new AccessoryMappingModel { Key = key, Width = width, ProductId = id });
        }
        Add("spanplatte",1825,settings.Spanplatte1825Id); Add("spanplatte",2700,settings.Spanplatte2700Id); Add("spanplatte",3600,settings.Spanplatte3600Id);
        Add("gitterrost",1825,settings.Gitterrost1825Id); Add("gitterrost",2700,settings.Gitterrost2700Id); Add("gitterrost",3600,settings.Gitterrost3600Id);
        Add("stahlpanel",1825,settings.Stahlpanel1825Id); Add("stahlpanel",2700,settings.Stahlpanel2700Id); Add("stahlpanel",3600,settings.Stahlpanel3600Id);
        Add("drahtgitter",1825,settings.Drahtgitter1825Id); Add("drahtgitter",2700,settings.Drahtgitter2700Id); Add("drahtgitter",3600,settings.Drahtgitter3600Id);
        Add("durchschub",1825,settings.Durchschub1825Id); Add("durchschub",2700,settings.Durchschub2700Id); Add("durchschub",3600,settings.Durchschub3600Id);
        Add("schutz_eck",0,settings.EckRammschutzId); Add("schutz_mittel76",0,settings.MittelRammschutz76Id); Add("schutz_mittel100",0,settings.MittelRammschutz100Id);
        return result;
    }

    public static string LabelFor(string? key) => key switch
    {
        "spanplatte" => "Spanplattenauflagen",
        "gitterrost" => "Gitterrostauflagen",
        "stahlpanel" => "Stahlpanelauflagen",
        "drahtgitter" => "Drahtgitterauflagen",
        "durchschub" => "Durchschubsicherungen",
        "schutz_eck" => "Eck-Rammschutz",
        "schutz_mittel76" => "Mittelstützen-Rammschutz 76 mm",
        "schutz_mittel100" => "Mittelstützen-Rammschutz 100 mm",
        _ => key ?? string.Empty
    };
}
