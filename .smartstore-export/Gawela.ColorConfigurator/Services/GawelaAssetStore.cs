using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Gawela.ColorConfigurator.Models;

namespace Gawela.ColorConfigurator.Services;

public sealed class GawelaAssetStore
{
    private const long MaxFileSize = 20 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly IWebHostEnvironment _environment;

    public GawelaAssetStore(IWebHostEnvironment environment) => _environment = environment;

    public string RootPath => Path.Combine(_environment.ContentRootPath, "App_Data", "GawelaColorAssets");
    public string GetProductDirectory(int productId) => Path.Combine(RootPath, productId.ToString());
    public string GetConfigPath(int productId) => Path.Combine(GetProductDirectory(productId), "config.json");

    public string GetAssetPath(int productId, string kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return null;
        var k = kind.Trim().ToLowerInvariant();
        var fileName = k switch
        {
            "base" => "base.webp",
            "corpus" => "mask-corpus.png",
            "doors" => "mask-doors.png",
            _ when k.StartsWith("layer-") && IsSafeKey(k[6..]) => $"mask-{k}.png",
            _ => null
        };
        return fileName == null ? null : Path.Combine(GetProductDirectory(productId), fileName);
    }

    public bool Exists(int productId, string kind)
    {
        var p = GetAssetPath(productId, kind);
        return p != null && File.Exists(p);
    }

    public IEnumerable<int> GetConfiguredProductIds()
    {
        if (!Directory.Exists(RootPath)) yield break;
        foreach (var d in Directory.EnumerateDirectories(RootPath))
            if (int.TryParse(Path.GetFileName(d), out var id)) yield return id;
    }

    public GawelaProductConfig LoadConfig(int productId)
    {
        var path = GetConfigPath(productId);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<GawelaProductConfig>(File.ReadAllText(path), JsonOptions); }
        catch { return null; }
    }

    public GawelaProductConfig LoadEffectiveConfig(int productId)
    {
        var cfg = LoadConfig(productId);
        if (cfg?.Layers?.Count > 0) return cfg;
        if (Exists(productId, "base") && Exists(productId, "corpus") && Exists(productId, "doors"))
        {
            return new GawelaProductConfig
            {
                ProductId = productId,
                Layers = new()
                {
                    new() { Key="legacy-corpus", Name="Korpus", AttributeLabel="Farben Korpus/Gestell ML", AssetKind="corpus", BaseRal="7035", DefaultRal="7035" },
                    new() { Key="legacy-doors", Name="Türen", AttributeLabel="Farben Türen/Schubladen ML", AssetKind="doors", BaseRal="7035", DefaultRal="7035" }
                }
            };
        }
        return null;
    }

    public bool HasBaseReference(int productId, GawelaProductConfig config = null)
    {
        config ??= LoadEffectiveConfig(productId);
        return config?.BaseMediaFileId.GetValueOrDefault() > 0 || Exists(productId, "base");
    }

    public bool HasLayerMaskReference(int productId, GawelaLayerConfig layer)
        => layer != null && (layer.MaskMediaFileId.GetValueOrDefault() > 0 || Exists(productId, layer.AssetKind));

    public bool IsComplete(int productId)
    {
        var cfg = LoadEffectiveConfig(productId);
        return cfg != null
            && HasBaseReference(productId, cfg)
            && cfg.Layers.Count > 0
            && cfg.Layers.All(x => HasLayerMaskReference(productId, x));
    }

    // Legacy/local upload helpers remain for backwards compatibility with existing installations.
    public async Task SaveBaseAsync(int productId, IFormFile file) => await SaveAsync(productId, "base", file, ".webp");
    public async Task SaveLayerMaskAsync(int productId, string key, IFormFile file) => await SaveAsync(productId, "layer-" + NormalizeKey(key), file, ".png");

    public async Task SaveConfigAsync(GawelaProductConfig config)
    {
        var dir = GetProductDirectory(config.ProductId); Directory.CreateDirectory(dir);
        var tmp = GetConfigPath(config.ProductId) + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(config, JsonOptions));
        File.Move(tmp, GetConfigPath(config.ProductId), true);
    }

    public void CopyLegacyMaskIfNeeded(int productId, string legacyKind, string targetKey)
    {
        var source = GetAssetPath(productId, legacyKind);
        var target = GetAssetPath(productId, "layer-" + NormalizeKey(targetKey));
        if (source != null && target != null && File.Exists(source) && !File.Exists(target)) File.Copy(source, target, true);
    }

    public void DeleteUnusedLayerMasks(int productId, IEnumerable<string> activeKeys)
    {
        var dir = GetProductDirectory(productId); if (!Directory.Exists(dir)) return;
        var keep = activeKeys.Select(x => "mask-layer-" + NormalizeKey(x) + ".png").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.EnumerateFiles(dir, "mask-layer-*.png")) if (!keep.Contains(Path.GetFileName(f))) File.Delete(f);
    }

    public void DeleteProductAssets(int productId)
    {
        var d = GetProductDirectory(productId); if (Directory.Exists(d)) Directory.Delete(d, true);
    }

    public void ValidateBase(IFormFile file) => Validate(file, ".webp", "Das Basisbild muss eine WebP-Datei (.webp) sein.");
    public void ValidateMask(IFormFile file) => Validate(file, ".png", "Masken müssen PNG-Dateien (.png) sein.");

    private async Task SaveAsync(int productId, string kind, IFormFile file, string ext)
    {
        Validate(file, ext, $"Ungültiges Dateiformat für {kind}.");
        var dir = GetProductDirectory(productId); Directory.CreateDirectory(dir);
        var target = GetAssetPath(productId, kind) ?? throw new InvalidOperationException("Ungültiger Bildschlüssel.");
        var tmp = target + ".tmp";
        await using (var s = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true)) await file.CopyToAsync(s);
        File.Move(tmp, target, true);
    }

    public static string NormalizeKey(string key)
    {
        var value = new string((key ?? string.Empty).ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
        return string.IsNullOrWhiteSpace(value) ? "layer" : value;
    }

    private static bool IsSafeKey(string key) => key.Length is > 0 and <= 80 && key.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');

    private static void Validate(IFormFile file, string ext, string message)
    {
        if (file == null || file.Length <= 0) throw new InvalidOperationException("Die ausgewählte Datei ist leer.");
        if (file.Length > MaxFileSize) throw new InvalidOperationException("Eine Bilddatei darf maximal 20 MB gross sein.");
        if (!Path.GetExtension(file.FileName).Equals(ext, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(message);
    }
}
