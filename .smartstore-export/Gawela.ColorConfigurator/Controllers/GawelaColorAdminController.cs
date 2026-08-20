using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Content.Media;
using Smartstore.Core.Data;
using Smartstore.Web.Controllers;
using Gawela.ColorConfigurator.Models;
using Gawela.ColorConfigurator.Services;

namespace Gawela.ColorConfigurator.Controllers;

public class GawelaColorAdminController : AdminController
{
    private const int MaxLayers = 8;

    private readonly SmartDbContext _db;
    private readonly IMediaService _mediaService;
    private readonly GawelaAssetStore _assetStore;
    private readonly GawelaPaletteStore _paletteStore;
    private readonly GawelaProductGroupStore _groupStore;

    public GawelaColorAdminController(
        SmartDbContext db,
        IMediaService mediaService,
        GawelaAssetStore assetStore,
        GawelaPaletteStore paletteStore,
        GawelaProductGroupStore groupStore)
    {
        _db = db;
        _mediaService = mediaService;
        _assetStore = assetStore;
        _paletteStore = paletteStore;
        _groupStore = groupStore;
    }

    public async Task<IActionResult> Configure(
        string tab = "products",
        int? configuratorId = null,
        bool add = false,
        int? productId = null,
        string configuratorName = null)
    {
        var normalizedTab = NormalizeTab(tab);
        if (normalizedTab == "colors")
        {
            var paletteModel = new GawelaAssetAdminModel
            {
                ActiveTab = "colors",
                Palette = BuildPaletteAdminModel()
            };
            return View(paletteModel);
        }

        // Compatibility for older admin links that used ?productId=...
        if (!add && !configuratorId.HasValue && productId.GetValueOrDefault() > 0)
        {
            var membership = _groupStore.FindByProduct(productId.Value);
            configuratorId = membership?.MasterProductId ?? productId.Value;
        }

        GawelaAssetAdminModel model;
        if (add)
        {
            if (productId.GetValueOrDefault() > 0)
            {
                var existingGroup = _groupStore.FindByProduct(productId.Value);
                var existingConfig = _assetStore.LoadEffectiveConfig(productId.Value);
                if (existingGroup != null || existingConfig != null)
                {
                    var ownerId = existingGroup?.MasterProductId ?? productId.Value;
                    TempData["GawelaColor.Error"] = "Der gewählte Basis-Artikel gehört bereits zu einem Farbkonfigurator. Der bestehende Konfigurator wurde geöffnet.";
                    return RedirectToAction(nameof(Configure), new { configuratorId = ownerId, tab = "products" });
                }

                model = await BuildNewEditorAsync(productId.Value, configuratorName);
            }
            else
            {
                model = new GawelaAssetAdminModel
                {
                    ActiveTab = "products",
                    IsEditor = true,
                    IsNew = true,
                    ConfiguratorName = configuratorName
                };
            }
        }
        else if (configuratorId.GetValueOrDefault() > 0)
        {
            model = await BuildExistingEditorAsync(configuratorId.Value);
            if (!model.IsEditor)
            {
                TempData["GawelaColor.Error"] = "Der gewünschte Farbkonfigurator wurde nicht gefunden.";
                return RedirectToAction(nameof(Configure), new { tab = "products" });
            }
        }
        else
        {
            model = await BuildOverviewAsync();
        }

        model.ActiveTab = "products";
        model.Palette = BuildPaletteAdminModel();
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> SaveConfigurator(GawelaAssetAdminModel model)
    {
        model.ActiveTab = "products";
        model.IsEditor = true;

        var master = await FindProductByIdAsync(model.ProductId);
        if (master == null)
        {
            ModelState.AddModelError(nameof(model.ProductId), "Der Basis-Artikel wurde nicht gefunden.");
            return View("Configure", await RebuildPostedEditorAsync(model));
        }

        if (model.OriginalMasterProductId > 0 && model.OriginalMasterProductId != master.Id)
        {
            ModelState.AddModelError(nameof(model.ProductId), "Der Basis-Artikel eines bestehenden Farbkonfigurators kann nicht geändert werden. Legen Sie dafür bitte einen neuen Konfigurator an.");
        }

        model.ConfiguratorName = (model.ConfiguratorName ?? string.Empty).Trim();
        if (model.ConfiguratorName.Length == 0)
            ModelState.AddModelError(nameof(model.ConfiguratorName), "Bitte einen eindeutigen Namen für den Farbkonfigurator eingeben.");
        else if (model.ConfiguratorName.Length > 160)
            ModelState.AddModelError(nameof(model.ConfiguratorName), "Der Name darf maximal 160 Zeichen lang sein.");

        model.ThumbnailLabel = string.IsNullOrWhiteSpace(model.ThumbnailLabel) ? "Farbe konfigurieren" : model.ThumbnailLabel.Trim();
        var attrs = await GetAttributesAsync(master.Id);
        model.AvailableAttributes = attrs;

        var existingConfig = _assetStore.LoadConfig(master.Id) ?? _assetStore.LoadEffectiveConfig(master.Id);
        var currentGroup = _groupStore.FindByMaster(master.Id);
        var currentMemberIds = (currentGroup?.ProductIds ?? new List<int> { master.Id }).ToHashSet();

        MediaFileInfo baseMedia = null;
        if (model.BaseMediaFileId.GetValueOrDefault() > 0)
        {
            baseMedia = await ValidateMediaAsync(model.BaseMediaFileId, ".webp", nameof(model.BaseMediaFileId), "Basisbild");
        }
        else if (!_assetStore.Exists(master.Id, "base"))
        {
            ModelState.AddModelError(nameof(model.BaseMediaFileId), "Bitte ein WebP-Basisbild aus dem Smartstore-Medienkatalog auswählen oder hochladen.");
        }

        var submittedLayers = (model.Layers ?? new List<GawelaLayerAdminModel>())
            .Where(x => x.IsActive && x.ProductVariantAttributeId > 0)
            .Take(MaxLayers + 1)
            .ToList();

        if (submittedLayers.Count == 0)
            ModelState.AddModelError(nameof(model.Layers), "Mindestens eine Visualisierungsebene ist erforderlich.");
        if (submittedLayers.Count > MaxLayers)
            ModelState.AddModelError(nameof(model.Layers), $"Maximal {MaxLayers} Visualisierungsebenen sind zulässig.");

        var allowedRals = _paletteStore.Load().Select(x => x.Ral).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var configLayers = new List<GawelaLayerConfig>();

        foreach (var layer in submittedLayers.Take(MaxLayers))
        {
            var attr = attrs.FirstOrDefault(x => x.Id == layer.ProductVariantAttributeId);
            if (attr == null)
            {
                ModelState.AddModelError(nameof(model.Layers), "Eine ausgewählte Visualisierungsebene verweist auf ein Attribut, das nicht zum Basis-Artikel gehört.");
                continue;
            }

            layer.Key = CreateLayerKey(layer.Key, attr.Id, usedKeys);
            layer.Name = string.IsNullOrWhiteSpace(layer.Name) ? attr.Name : layer.Name.Trim();
            layer.BaseRal = NormalizeRal(layer.BaseRal, "7035");
            layer.DefaultRal = NormalizeRal(layer.DefaultRal, layer.BaseRal);

            if (!allowedRals.Contains(layer.BaseRal))
                ModelState.AddModelError(nameof(model.Layers), $"Basis-RAL {layer.BaseRal} ist in der zentralen RAL-Liste nicht vorhanden.");
            if (!allowedRals.Contains(layer.DefaultRal))
                ModelState.AddModelError(nameof(model.Layers), $"Fallback-RAL {layer.DefaultRal} ist in der zentralen RAL-Liste nicht vorhanden.");

            var assetKind = "layer-" + layer.Key;
            MediaFileInfo maskMedia = null;
            if (layer.MaskMediaFileId.GetValueOrDefault() > 0)
          {
                maskMedia = await ValidateMediaAsync(layer.MaskMediaFileId, ".png", nameof(model.Layers), $"Maske „{layer.Name}“");
            }
            else if (!_assetStore.Exists(master.Id, assetKind))
            {
                var legacyKind = LegacyKind(attr.Name);
                if (legacyKind != null && _assetStore.Exists(master.Id, legacyKind))
                    _assetStore.CopyLegacyMaskIfNeeded(master.Id, legacyKind, layer.Key);
            }

            if (layer.MaskMediaFileId.GetValueOrDefault() <= 0 && !_assetStore.Exists(master.Id, assetKind))
                ModelState.AddModelError(nameof(model.Layers), $"Für Ebene „{layer.Name}“ bitte eine PNG-Maske aus dem Smartstore-Medienkatalog auswählen oder hochladen.");

            if (baseMedia != null && maskMedia != null
                && baseMedia.Size.Width > 0 && baseMedia.Size.Height > 0
                && maskMedia.Size.Width > 0 && maskMedia.Size.Height > 0
                && (baseMedia.Size.Width != maskMedia.Size.Width || baseMedia.Size.Height != maskMedia.Size.Height))
            {
                ModelState.AddModelError(nameof(model.Layers), $"Die Maske „{layer.Name}“ hat {maskMedia.Size.Width}×{maskMedia.Size.Height} Pixel, das Basisbild aber {baseMedia.Size.Width}×{baseMedia.Size.Height} Pixel. Beide müssen exakt gleich gross sein.");
            }

            configLayers.Add(new GawelaLayerConfig
            {
                Key = layer.Key,
                Name = layer.Name,
                ProductVariantAttributeId = attr.Id,
                AttributeLabel = attr.Name,
                AssetKind = assetKind,
                BaseRal = layer.BaseRal,
                DefaultRal = layer.DefaultRal,
                MaskMediaFileId = layer.MaskMediaFileId
            });
        }

        var existingAdditionalIds = currentMemberIds
            .Where(x => x != master.Id)
            .Distinct()
            .ToList();

        // 6.4.23: AdditionalProductIds is the exact, authoritative assignment list.
        // The SKU textarea is only used client-side to resolve and append products before saving.
        var additionalIds = ParseProductIds(model.AdditionalProductIds)
            .Where(x => x != master.Id)
            .Distinct()
            .ToList();
        var memberRows = additionalIds.Count == 0
            ? new List<ProductLookupResult>()
            : await _db.Products.AsNoTracking()
                .Where(x => additionalIds.Contains(x.Id))
                .Select(x => new ProductLookupResult { Id = x.Id, Sku = x.Sku, Name = x.Name })
                .ToListAsync();
        var missingMemberIds = additionalIds.Except(memberRows.Select(x => x.Id)).ToList();
        if (missingMemberIds.Count > 0)
            ModelState.AddModelError(nameof(model.AdditionalProductIds), "Mindestens ein zugeordneter Artikel wurde im Produktkatalog nicht gefunden.");

        model.AdditionalProductIds = string.Join(',', additionalIds);
        model.AdditionalProductSkus = string.Empty;

        var otherGroups = _groupStore.Load().Where(x => x.MasterProductId != master.Id).ToList();
        var newMemberIds = additionalIds.Except(existingAdditionalIds).ToHashSet();
        foreach (var member in memberRows.Where(x => newMemberIds.Contains(x.Id)))
        {
            var conflictingGroup = otherGroups.FirstOrDefault(x => (x.ProductIds ?? new List<int>()).Contains(member.Id));
            if (conflictingGroup != null)
            {
                ModelState.AddModelError(nameof(model.AdditionalProductIds), $"{member.Sku} gehört bereits zum Farbkonfigurator „{conflictingGroup.Name}“.");
                continue;
            }

            if (!currentMemberIds.Contains(member.Id) && _assetStore.LoadEffectiveConfig(member.Id) != null)
            {
                ModelState.AddModelError(nameof(model.AdditionalProductIds), $"{member.Sku} besitzt bereits einen eigenen Farbkonfigurator. Ein Produkt kann nur einem Farbkonfigurator zugeordnet sein.");
                continue;
            }

            var memberAttrs = await GetAttributesAsync(member.Id);
            var missing = configLayers
                .Where(layer => !memberAttrs.Any(a => NamesMatch(a.Name, layer.AttributeLabel)))
                .Select(layer => layer.AttributeLabel ?? layer.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (missing.Count > 0)
                ModelState.AddModelError(nameof(model.AdditionalProductIds), $"{member.Sku}: folgende benötigten Attribute des Basis-Artikels fehlen: {string.Join(", ", missing)}.");
        }

        if (!ModelState.IsValid)
            return View("Configure", await RebuildPostedEditorAsync(model));

        var config = new GawelaProductConfig
        {
            ProductId = master.Id,
            Name = model.ConfiguratorName,
            ThumbnailLabel = model.ThumbnailLabel,
            BaseMediaFileId = model.BaseMediaFileId,
            Layers = configLayers
        };

        await _assetStore.SaveConfigAsync(config);
        _assetStore.DeleteUnusedLayerMasks(master.Id, config.Layers.Select(x => x.Key));

        var allProductIds = new[] { master.Id }.Concat(additionalIds).Distinct().ToList();
        await _groupStore.SaveAsync(new GawelaProductGroup
        {
            Key = currentGroup?.Key,
            Name = model.ConfiguratorName,
            MasterProductId = master.Id,
            ProductIds = allProductIds
        });

        TempData["GawelaColor.Success"] = $"Farbkonfigurator „{model.ConfiguratorName}“ wurde gespeichert. Basis-Artikel: {master.Sku}; zugeordnete Artikel: {allProductIds.Count}; Ebenen: {configLayers.Count}.";
        return RedirectToAction(nameof(Configure), new { configuratorId = master.Id, tab = "products" });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteConfigurator(int masterProductId)
    {
        var group = _groupStore.FindByMaster(masterProductId);
        var config = _assetStore.LoadEffectiveConfig(masterProductId);
        var name = config?.Name ?? group?.Name ?? $"Produkt-ID {masterProductId}";

        await _groupStore.DeleteAsync(masterProductId);
        _assetStore.DeleteProductAssets(masterProductId);

        // Media Manager files are deliberately not deleted: they may be reused elsewhere.
        TempData["GawelaColor.Success"] = $"Farbkonfigurator „{name}“ wurde gelöscht. Dateien im Smartstore-Medienkatalog bleiben erhalten.";
        return RedirectToAction(nameof(Configure), new { tab = "products" });
    }

    public async Task<IActionResult> ProductSummaries(string ids)
    {
        var productIds = ParseProductIds(ids).Distinct().Take(500).ToArray();
        if (productIds.Length == 0) return Json(Array.Empty<object>());

        var rows = await _db.Products.AsNoTracking()
            .Where(x => productIds.Contains(x.Id))
            .Select(x => new { id = x.Id, sku = x.Sku, name = x.Name })
            .ToListAsync();

        var order = productIds.Select((id, index) => new { id, index }).ToDictionary(x => x.id, x => x.index);
        return Json(rows.OrderBy(x => order.TryGetValue(x.id, out var i) ? i : int.MaxValue));
    }


    public async Task<IActionResult> ProductSummariesBySkus(string skus)
    {
        var submitted = ParseProductSkus(skus)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .ToList();
        var normalized = submitted
            .Select(NormalizeSkuLookup)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (normalized.Count == 0)
            return Json(new { rows = Array.Empty<object>(), missingSkus = Array.Empty<string>(), duplicateSkus = Array.Empty<string>() });

        var matches = await _db.Products.AsNoTracking()
            .Where(x => x.Sku != null && normalized.Contains(x.Sku.ToUpper()))
            .Select(x => new ProductLookupResult { Id = x.Id, Sku = x.Sku, Name = x.Name })
            .ToListAsync();
        var grouped = matches
            .GroupBy(x => NormalizeSkuLookup(x.Sku), StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.Ordinal);

        var missing = submitted
            .Where(x => !grouped.ContainsKey(NormalizeSkuLookup(x)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var duplicates = submitted
            .Where(x => grouped.TryGetValue(NormalizeSkuLookup(x), out var rows) && rows.Count > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var valid = submitted
            .Select(x => grouped.TryGetValue(NormalizeSkuLookup(x), out var rows) && rows.Count == 1 ? rows[0] : null)
            .Where(x => x != null)
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .Select(x => new { id = x.Id, sku = x.Sku, name = x.Name })
            .ToList();

        return Json(new { rows = valid, missingSkus = missing, duplicateSkus = duplicates });
    }

    [HttpPost]
    public async Task<IActionResult> SavePalette(GawelaAssetAdminModel model)
    {
        var defaults = _paletteStore.Load();
        var known = defaults.ToDictionary(x => x.Ral, StringComparer.OrdinalIgnoreCase);
        var submitted = model.Palette?.Colors ?? new();

        foreach (var row in submitted)
        {
            if (string.IsNullOrWhiteSpace(row.Ral) || !known.ContainsKey(row.Ral))
            {
                ModelState.AddModelError("Palette", "Unbekannte RAL-Farbe in der übermittelten Liste.");
                continue;
            }
            if (!GawelaPaletteStore.TryNormalizeHex(row.Hex, out var hex))
            {
                ModelState.AddModelError("Palette", $"RAL {row.Ral}: HEX muss im Format #RRGGBB eingegeben werden.");
                continue;
            }
            row.Hex = hex;
            var rgb = GawelaPaletteStore.HexToRgb(hex);
            row.R = rgb.R; row.G = rgb.G; row.B = rgb.B;
        }

        if (!ModelState.IsValid)
        {
            var vm = new GawelaAssetAdminModel { ActiveTab = "colors", Palette = model.Palette ?? BuildPaletteAdminModel() };
            return View("Configure", vm);
        }

        var entries = submitted.Select(x => new GawelaPaletteEntry(x.Ral, known[x.Ral].Name, x.Hex, x.R, x.G, x.B));
        await _paletteStore.SaveAsync(entries);
        TempData["GawelaColor.Success"] = "RAL-Farbwerte wurden gespeichert. Die Änderungen gelten zentral für alle Farbkonfiguratoren.";
        return RedirectToAction(nameof(Configure), new { tab = "colors" });
    }

    [HttpPost]
    public IActionResult ResetPalette()
    {
        _paletteStore.Reset();
        TempData["GawelaColor.Success"] = "RAL-Farbwerte wurden auf die mitgelieferten Standardwerte zurückgesetzt.";
        return RedirectToAction(nameof(Configure), new { tab = "colors" });
    }

    private async Task<GawelaAssetAdminModel> BuildOverviewAsync()
    {
        var model = new GawelaAssetAdminModel { ActiveTab = "products" };
        var groups = _groupStore.Load().ToList();
        var allGroupMemberIds = groups.SelectMany(x => x.ProductIds ?? new List<int>()).ToHashSet();
        var configuredIds = _assetStore.GetConfiguredProductIds().Distinct().ToHashSet();

        var masterIds = groups.Select(x => x.MasterProductId)
            .Concat(configuredIds.Where(x => !allGroupMemberIds.Contains(x)))
            .Where(x => x > 0)
            .Distinct()
            .ToArray();

        var productRows = masterIds.Length == 0
            ? new List<ProductLookupResult>()
            : await _db.Products.AsNoTracking()
                .Where(x => masterIds.Contains(x.Id))
                .Select(x => new ProductLookupResult { Id = x.Id, Sku = x.Sku, Name = x.Name })
                .ToListAsync();
        var productMap = productRows.ToDictionary(x => x.Id);

        foreach (var masterId in masterIds)
        {
            var group = groups.FirstOrDefault(x => x.MasterProductId == masterId);
            var config = _assetStore.LoadEffectiveConfig(masterId);
            productMap.TryGetValue(masterId, out var product);

            var displayName = FirstValue(config?.Name, group?.Name, !string.IsNullOrWhiteSpace(product?.Sku) ? $"Farbkonfigurator – {product.Sku}" : $"Farbkonfigurator {masterId}");
            model.Configurators.Add(new GawelaConfiguratorOverviewModel
            {
                Name = displayName,
                MasterProductId = masterId,
                MasterSku = product?.Sku ?? "(Produkt fehlt)",
                MasterProductName = product?.Name ?? string.Empty,
                ProductCount = Math.Max(1, group?.ProductIds?.Distinct().Count() ?? 1),
                LayerCount = config?.Layers?.Count ?? 0,
                HasBaseImage = _assetStore.HasBaseReference(masterId, config),
                UsesSmartstoreMedia = config?.BaseMediaFileId.GetValueOrDefault() > 0 || (config?.Layers?.Any(x => x.MaskMediaFileId.GetValueOrDefault() > 0) ?? false),
                IsComplete = _assetStore.IsComplete(masterId),
                IsLegacy = _assetStore.LoadConfig(masterId) == null || string.IsNullOrWhiteSpace(_assetStore.LoadConfig(masterId)?.Name)
            });
        }

        model.Configurators = model.Configurators
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.MasterSku, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return model;
    }

    private async Task<GawelaAssetAdminModel> BuildNewEditorAsync(int productId, string name)
    {
        var product = await FindProductByIdAsync(productId);
        if (product == null) return new GawelaAssetAdminModel { ActiveTab = "products" };

        var attrs = await GetAttributesAsync(product.Id);
        var layers = attrs
            .Where(x => LooksLikeColorAttribute(x.Name))
            .Take(MaxLayers)
            .Select(x => new GawelaLayerAdminModel
            {
                IsActive = true,
                Key = null,
                Name = FriendlyLayerName(x.Name),
                ProductVariantAttributeId = x.Id,
                BaseRal = "7035",
                DefaultRal = "7035"
            })
            .ToList();
        EnsureLayerSlots(layers);

        return new GawelaAssetAdminModel
        {
            ActiveTab = "products",
            IsEditor = true,
            IsNew = true,
            ProductId = product.Id,
            ProductSku = product.Sku,
            ProductName = product.Name,
            ConfiguratorName = string.IsNullOrWhiteSpace(name) ? SuggestedConfiguratorName(product) : name.Trim(),
            ThumbnailLabel = "Farbe konfigurieren",
            AvailableAttributes = attrs,
            Layers = layers,
            HasExistingBaseImage = false,
            HasLocalBaseImage = false
        };
    }

    private async Task<GawelaAssetAdminModel> BuildExistingEditorAsync(int requestedId)
    {
        var membership = _groupStore.FindByProduct(requestedId);
        var masterId = membership?.MasterProductId ?? requestedId;
        var product = await FindProductByIdAsync(masterId);
        var config = _assetStore.LoadEffectiveConfig(masterId);
        if (product == null || config == null)
            return new GawelaAssetAdminModel { ActiveTab = "products" };

        var group = _groupStore.FindByMaster(masterId);
        var attrs = await GetAttributesAsync(masterId);
        var layers = new List<GawelaLayerAdminModel>();

        foreach (var sourceLayer in config.Layers.Take(MaxLayers))
        {
            var match = sourceLayer.ProductVariantAttributeId > 0
                ? attrs.FirstOrDefault(x => x.Id == sourceLayer.ProductVariantAttributeId)
                : null;
            match ??= attrs.FirstOrDefault(x => NamesMatch(x.Name, sourceLayer.AttributeLabel));
            if (match == null) continue;

            layers.Add(new GawelaLayerAdminModel
            {
                IsActive = true,
                Key = sourceLayer.Key,
                Name = sourceLayer.Name,
                ProductVariantAttributeId = match.Id,
                BaseRal = NormalizeRal(sourceLayer.BaseRal, "7035"),
                DefaultRal = NormalizeRal(sourceLayer.DefaultRal, NormalizeRal(sourceLayer.BaseRal, "7035")),
                MaskMediaFileId = sourceLayer.MaskMediaFileId,
                HasExistingMask = _assetStore.HasLayerMaskReference(masterId, sourceLayer),
                HasLocalMask = _assetStore.Exists(masterId, sourceLayer.AssetKind)
            });
        }
        EnsureLayerSlots(layers);

        var additionalIds = (group?.ProductIds ?? new List<int> { masterId })
            .Where(x => x != masterId)
            .Distinct()
            .ToList();
        var assignedProducts = await LoadAssignedProductsAsync(additionalIds);

        return new GawelaAssetAdminModel
        {
            ActiveTab = "products",
            IsEditor = true,
            IsNew = false,
            OriginalMasterProductId = masterId,
            ProductId = masterId,
            ProductSku = product.Sku,
            ProductName = product.Name,
            ConfiguratorName = FirstValue(config.Name, group?.Name, SuggestedConfiguratorName(product)),
            ThumbnailLabel = string.IsNullOrWhiteSpace(config.ThumbnailLabel) ? "Farbe konfigurieren" : config.ThumbnailLabel,
            BaseMediaFileId = config.BaseMediaFileId,
            HasExistingBaseImage = _assetStore.HasBaseReference(masterId, config),
            HasLocalBaseImage = _assetStore.Exists(masterId, "base"),
            AdditionalProductIds = string.Join(',', additionalIds),
            AdditionalProductSkus = string.Empty,
            AdditionalProductIdStrings = additionalIds.Select(x => x.ToString()).ToArray(),
            AssignedProducts = assignedProducts,
            AvailableAttributes = attrs,
            Layers = layers
        };
    }

    private async Task<GawelaAssetAdminModel> RebuildPostedEditorAsync(GawelaAssetAdminModel model)
    {
        var product = await FindProductByIdAsync(model.ProductId);
        if (product != null)
        {
            model.ProductSku = product.Sku;
            model.ProductName = product.Name;
            model.AvailableAttributes = await GetAttributesAsync(product.Id);
            var config = _assetStore.LoadEffectiveConfig(product.Id);
            model.HasExistingBaseImage = _assetStore.HasBaseReference(product.Id, config);
            model.HasLocalBaseImage = _assetStore.Exists(product.Id, "base");

            foreach (var layer in model.Layers ?? new List<GawelaLayerAdminModel>())
            {
                if (!layer.IsActive || layer.ProductVariantAttributeId <= 0) continue;
                var kind = "layer-" + GawelaAssetStore.NormalizeKey(layer.Key);
                layer.HasLocalMask = _assetStore.Exists(product.Id, kind);
                layer.HasExistingMask = layer.MaskMediaFileId.GetValueOrDefault() > 0 || layer.HasLocalMask;
            }
        }

        model.Layers ??= new List<GawelaLayerAdminModel>();
        model.AdditionalProductIdStrings = ParseProductIds(model.AdditionalProductIds).Select(x => x.ToString()).ToArray();
        model.AssignedProducts = await LoadAssignedProductsAsync(ParseProductIds(model.AdditionalProductIds).ToList());
        model.Palette = BuildPaletteAdminModel();
        EnsureLayerSlots(model.Layers);
        return model;
    }

    private async Task<List<GawelaAssignedProductAdminModel>> LoadAssignedProductsAsync(IReadOnlyCollection<int> ids)
    {
        if (ids == null || ids.Count == 0) return new List<GawelaAssignedProductAdminModel>();
        var rows = await _db.Products.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new GawelaAssignedProductAdminModel { ProductId = x.Id, Sku = x.Sku, ProductName = x.Name })
            .ToListAsync();
        var order = ids.Select((id, index) => new { id, index }).ToDictionary(x => x.id, x => x.index);
        return rows.OrderBy(x => order.TryGetValue(x.ProductId, out var i) ? i : int.MaxValue).ToList();
    }

    private async Task<MediaFileInfo> ValidateMediaAsync(int? mediaFileId, string requiredExtension, string fieldName, string label)
    {
        if (mediaFileId.GetValueOrDefault() <= 0) return null;
        var file = await _mediaService.GetFileByIdAsync(mediaFileId.Value);
        if (file == null)
        {
            ModelState.AddModelError(fieldName, $"{label}: Die ausgewählte Datei wurde im Smartstore-Medienkatalog nicht gefunden.");
            return null;
        }

        if (!string.Equals(file.Extension, requiredExtension, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(fieldName, $"{label}: Erforderlich ist eine {requiredExtension.ToUpperInvariant()}-Datei. Ausgewählt wurde „{file.Name}“.");
            return null;
        }

        return file;
    }

    private GawelaPaletteAdminModel BuildPaletteAdminModel()
    {
        var model = new GawelaPaletteAdminModel();
        foreach (var c in _paletteStore.Load())
        {
            model.Colors.Add(new GawelaRalColorAdminModel
            {
                Ral = c.Ral,
                Name = c.Name,
                Hex = c.Hex,
                R = c.R,
                G = c.G,
                B = c.B
            });
        }
        return model;
    }

    private async Task<List<GawelaAttributeOptionModel>> GetAttributesAsync(int productId)
    {
        var rows = await _db.ProductVariantAttributes.AsNoTracking().Include(x => x.ProductAttribute)
            .Where(x => x.ProductId == productId)
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync();

        return rows.Select(x => new GawelaAttributeOptionModel
        {
            Id = x.Id,
            Name = (!string.IsNullOrWhiteSpace(x.TextPrompt) ? x.TextPrompt : x.ProductAttribute?.Name) ?? ($"Attribut {x.Id}")
        }).ToList();
    }

    private async Task<ProductLookupResult> FindProductByIdAsync(int productId)
    {
        if (productId <= 0) return null;
        return await _db.Products.AsNoTracking()
            .Where(x => x.Id == productId)
            .Select(x => new ProductLookupResult { Id = x.Id, Sku = x.Sku, Name = x.Name })
            .FirstOrDefaultAsync();
    }

    private static void EnsureLayerSlots(List<GawelaLayerAdminModel> layers)
    {
        layers ??= new List<GawelaLayerAdminModel>();
        while (layers.Count < MaxLayers)
            layers.Add(new GawelaLayerAdminModel { IsActive = false, BaseRal = "7035", DefaultRal = "7035" });
        if (layers.Count > MaxLayers) layers.RemoveRange(MaxLayers, layers.Count - MaxLayers);
    }

    private static IEnumerable<string> ParseProductSkus(string value)
    {
        return (value ?? string.Empty)
            .Split(new[] { '\r', '\n', '\t', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim().Trim('"', '\''))
            .Where(x => x.Length > 0);
    }

    private static string NormalizeSkuLookup(string value)
        => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static IEnumerable<int> ParseProductIds(string value)
    {
        return (value ?? string.Empty)
            .Split(new[] { ',', ';', '\r', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, out var id) ? id : 0)
            .Where(x => x > 0);
    }

    private static string NormalizeTab(string value)
        => value?.Equals("colors", StringComparison.OrdinalIgnoreCase) == true ? "colors" : "products";

    private static string NormalizeRal(string value, string fallback)
    {
        var s = (value ?? string.Empty).Trim();
        if (s.StartsWith("RAL ", StringComparison.OrdinalIgnoreCase)) s = s[4..].Trim();
        return s.Length == 4 && s.All(char.IsDigit) ? s : fallback;
    }

    private static string CreateLayerKey(string current, int attributeId, HashSet<string> used)
    {
        var baseKey = string.IsNullOrWhiteSpace(current) ? "a" + attributeId : GawelaAssetStore.NormalizeKey(current);
        if (string.IsNullOrWhiteSpace(baseKey) || baseKey == "layer") baseKey = "a" + attributeId;
        var key = baseKey;
        var suffix = 2;
        while (!used.Add(key)) key = baseKey + "-" + suffix++;
        return key;
    }

    private static bool LooksLikeColorAttribute(string name)
    {
        var n = (name ?? string.Empty).Trim().ToLowerInvariant();
        return n.Contains("farbe") || n.Contains("farb") || n.Contains(" ral") || n.StartsWith("ral") || n.Contains("color") || n.Contains("colour");
    }

    private static string FriendlyLayerName(string attributeName)
    {
        var n = (attributeName ?? string.Empty).Trim();
        if (n.Contains("Korpus", StringComparison.OrdinalIgnoreCase) || n.Contains("Gestell", StringComparison.OrdinalIgnoreCase)) return "Korpus / Gestell";
        if (n.Contains("Tür", StringComparison.OrdinalIgnoreCase) || n.Contains("Tuer", StringComparison.OrdinalIgnoreCase) || n.Contains("Schubl", StringComparison.OrdinalIgnoreCase)) return "Türen / Schubladen";
        return n;
    }

    private static string LegacyKind(string name)
    {
        var n = (name ?? string.Empty).ToLowerInvariant();
        if (n.Contains("korpus") || n.Contains("gestell")) return "corpus";
        if (n.Contains("tür") || n.Contains("tuer") || n.Contains("schubl")) return "doors";
        return null;
    }

    private static bool NamesMatch(string a, string b)
    {
        var x = (a ?? string.Empty).Trim().ToLowerInvariant();
        var y = (b ?? string.Empty).Trim().ToLowerInvariant();
        if (x.Length == 0 || y.Length == 0) return false;
        return x == y || x.Contains(y) || y.Contains(x);
    }

    private static string FirstValue(params string[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;

    private static string SuggestedConfiguratorName(ProductLookupResult product)
        => product == null ? "Neuer Farbkonfigurator" : $"{product.Sku} – {product.Name}";

    private sealed class ProductLookupResult
    {
        public int Id { get; set; }
        public string Sku { get; set; }
        public string Name { get; set; }
    }
}
