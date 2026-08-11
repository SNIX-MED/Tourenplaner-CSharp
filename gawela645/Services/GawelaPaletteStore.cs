using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace Gawela.ColorConfigurator.Services;

public sealed class GawelaPaletteStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly IWebHostEnvironment _environment;

    public GawelaPaletteStore(IWebHostEnvironment environment) => _environment = environment;

    public string PalettePath => Path.Combine(_environment.ContentRootPath, "App_Data", "GawelaColorAssets", "palette.json");

    public IReadOnlyList<GawelaPaletteEntry> Load()
    {
        var defaults = CreateDefaults();
        if (!File.Exists(PalettePath)) return defaults;

        try
        {
            var doc = JsonSerializer.Deserialize<GawelaPaletteDocument>(File.ReadAllText(PalettePath), JsonOptions);
            if (doc?.Colors == null || doc.Colors.Count == 0) return defaults;
            var map = doc.Colors.Where(x => !string.IsNullOrWhiteSpace(x.Ral)).ToDictionary(x => x.Ral.Trim(), StringComparer.OrdinalIgnoreCase);
            foreach (var d in defaults)
            {
                if (!map.TryGetValue(d.Ral, out var custom)) continue;
                if (TryNormalizeHex(custom.Hex, out var hex))
                {
                    d.Hex = hex;
                    (d.R, d.G, d.B) = HexToRgb(hex);
                }
                else if (IsRgb(custom.R, custom.G, custom.B))
                {
                    d.R = custom.R; d.G = custom.G; d.B = custom.B;
                    d.Hex = RgbToHex(d.R, d.G, d.B);
                }
            }
            return defaults;
        }
        catch
        {
            return defaults;
        }
    }

    public async Task SaveAsync(IEnumerable<GawelaPaletteEntry> submitted)
    {
        var incoming = (submitted ?? Array.Empty<GawelaPaletteEntry>()).ToDictionary(x => x.Ral ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        var result = CreateDefaults();
        foreach (var row in result)
        {
            if (!incoming.TryGetValue(row.Ral, out var input)) continue;
            if (!TryNormalizeHex(input.Hex, out var hex)) throw new InvalidOperationException($"RAL {row.Ral}: HEX muss im Format #RRGGBB eingegeben werden.");
            row.Hex = hex;
            (row.R, row.G, row.B) = HexToRgb(hex);
        }

        var dir = Path.GetDirectoryName(PalettePath)!;
        Directory.CreateDirectory(dir);
        var tmp = PalettePath + ".tmp";
        var doc = new GawelaPaletteDocument { Colors = result.ToList() };
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(doc, JsonOptions));
        File.Move(tmp, PalettePath, true);
    }

    public void Reset()
    {
        if (File.Exists(PalettePath)) File.Delete(PalettePath);
    }

    public static bool TryNormalizeHex(string value, out string hex)
    {
        var s = (value ?? string.Empty).Trim();
        if (!s.StartsWith('#')) s = "#" + s;
        if (s.Length == 7 && s.Skip(1).All(Uri.IsHexDigit))
        {
            hex = s.ToUpperInvariant();
            return true;
        }
        hex = null;
        return false;
    }

    public static string RgbToHex(int r, int g, int b) => $"#{Clamp(r):X2}{Clamp(g):X2}{Clamp(b):X2}";

    public static (int R, int G, int B) HexToRgb(string hex)
    {
        var s = hex.TrimStart('#');
        return (Convert.ToInt32(s[..2], 16), Convert.ToInt32(s.Substring(2, 2), 16), Convert.ToInt32(s.Substring(4, 2), 16));
    }

    private static bool IsRgb(int r, int g, int b) => r is >= 0 and <= 255 && g is >= 0 and <= 255 && b is >= 0 and <= 255;
    private static int Clamp(int v) => Math.Max(0, Math.Min(255, v));

    private static List<GawelaPaletteEntry> CreateDefaults() => new()
    {
        new("1015","Hellelfenbein","#E6D2B5",230,210,181),
        new("1023","Verkehrsgelb","#FAD201",250,210,1),
        new("2004","Reinorange","#F44611",244,70,17),
        new("3005","Weinrot","#59191F",89,25,31),
        new("3020","Verkehrsrot","#CC0605",204,6,5),
        new("5010","Enzianblau","#004F7C",0,79,124),
        new("5012","Lichtblau","#0089B6",0,137,182),
        new("5018","Türkisblau","#048B8B",4,139,139),
        new("6011","Resedagrün","#6C7C59",108,124,89),
        new("6033","Minttürkis","#428C78",66,140,120),
        new("7016","Anthrazitgrau","#383E42",56,62,66),
        new("7032","Kieselgrau","#B8B799",184,183,153),
        new("7035","Lichtgrau","#CBD0CC",203,208,204),
        new("8016","Mahagonibraun","#4C2F27",76,47,39),
        new("9005","Tiefschwarz","#0A0A0D",10,10,13),
        new("9010","Reinweiss","#F1ECE1",241,236,225)
    };
}

public sealed class GawelaPaletteDocument
{
    public List<GawelaPaletteEntry> Colors { get; set; } = new();
}

public sealed class GawelaPaletteEntry
{
    public GawelaPaletteEntry() { }
    public GawelaPaletteEntry(string ral, string name, string hex, int r, int g, int b)
    {
        Ral = ral; Name = name; Hex = hex; R = r; G = g; B = b;
    }
    public string Ral { get; set; }
    public string Name { get; set; }
    public string Hex { get; set; }
    public int R { get; set; }
    public int G { get; set; }
    public int B { get; set; }
}
