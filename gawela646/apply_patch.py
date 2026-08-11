from pathlib import Path
import sys

root = Path(sys.argv[1])
controller = root / 'Controllers' / 'GawelaColorAdminController.cs'
model = root / 'Models' / 'GawelaAssetAdminModel.cs'
view = root / 'Views' / 'GawelaColorAdmin' / 'Configure.cshtml'
module = root / 'module.json'

# ---------------- Controller ----------------
s = controller.read_text(encoding='utf-8')
s = s.replace(
    'public async Task<IActionResult> Configure(string productReference = null, int? productId = null, string tab = "products")',
    'public async Task<IActionResult> Configure(string productReference = null, int? productId = null, int? copyFromProductId = null, string tab = "products")'
)
s = s.replace(
    'var model = await BuildModelAsync(new GawelaAssetAdminModel(), product);\n        model.ActiveTab = NormalizeTab(tab);',
    'var model = await BuildModelAsync(new GawelaAssetAdminModel(), product);\n        if (product != null && copyFromProductId.GetValueOrDefault() > 0) await ApplyTemplateAsync(model, product, copyFromProductId.Value);\n        model.ActiveTab = NormalizeTab(tab);'
)
s = s.replace(
    '        if (model.Layers.Select(x => x.ProductVariantAttributeId).Distinct().Count() != model.Layers.Count) ModelState.AddModelError(nameof(model.Layers), "Jedes Smartstore-Attribut darf nur einmal verwendet werden.");\n\n        var allowedRals',
    '        // Seit 6.4.6 darf dasselbe Smartstore-Attribut bewusst mehrere Visualisierungsebenen steuern.\n        // Die Zuordnung ist ausserdem immer produktbezogen; gleiche Attribute auf anderen Produkten sind ausdrücklich zulässig.\n        var usedLayerKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);\n\n        var allowedRals'
)
s = s.replace(
    '            layer.Key = "a" + layer.ProductVariantAttributeId;\n            layer.Name = string.IsNullOrWhiteSpace(layer.Name) ? a.Name : layer.Name.Trim();',
    '            layer.Key = CreateLayerKey(layer.Key, layer.ProductVariantAttributeId, usedLayerKeys);\n            layer.Name = string.IsNullOrWhiteSpace(layer.Name) ? a.Name : layer.Name.Trim();'
)
s = s.replace(
    '                                Key = "a" + match.Id,\n                                Name = l.Name,',
    '                                Key = !string.IsNullOrWhiteSpace(l.Key) ? l.Key : "a" + match.Id,\n                                Name = l.Name,'
)
needle = '''                    model.ThumbnailLabel = cfg.ThumbnailLabel;
                }
            }
        }

        var ids = _assetStore.GetConfiguredProductIds()'''
replacement = '''                    model.ThumbnailLabel = cfg.ThumbnailLabel;
                }

                // Bei neuen Produkten automatisch alle erkennbaren Farbattribute einmal vorschlagen.
                // Dadurch wird nicht mehr versehentlich zweimal das erste Dropdown-Attribut angelegt.
                if (model.Layers.Count == 0)
                {
                    foreach (var a in model.AvailableAttributes.Where(x => LooksLikeColorAttribute(x.Name)).Take(8))
                    {
                        model.Layers.Add(new GawelaLayerAdminModel
                        {
                            Key = "a" + a.Id,
                            Name = a.Name,
                            ProductVariantAttributeId = a.Id,
                            BaseRal = "7035",
                            DefaultRal = "7035",
                            HasExistingMask = false
                        });
                    }
                }
            }
        }

        var ids = _assetStore.GetConfiguredProductIds()'''
if needle not in s:
    raise SystemExit('BuildModel insertion point not found')
s = s.replace(needle, replacement, 1)

needle = '''    private GawelaPaletteAdminModel BuildPaletteAdminModel()
    {'''
insert = '''    private async Task ApplyTemplateAsync(GawelaAssetAdminModel model, ProductLookupResult targetProduct, int sourceProductId)
    {
        if (sourceProductId <= 0 || sourceProductId == targetProduct.Id) return;
        var source = _assetStore.LoadEffectiveConfig(sourceProductId);
        if (source?.Layers?.Count == 0)
        {
            model.TemplateMessage = "Die gewählte Vorlage enthält keine Visualisierungsebenen.";
            return;
        }

        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var copied = new List<GawelaLayerAdminModel>();
        var missing = new List<string>();

        foreach (var sourceLayer in source.Layers)
        {
            var matches = model.AvailableAttributes
                .Where(x => NamesMatch(x.Name, sourceLayer.AttributeLabel) || NamesMatch(x.Name, sourceLayer.Name))
                .ToList();
            var match = matches.FirstOrDefault();
            if (match == null)
            {
                missing.Add(sourceLayer.AttributeLabel ?? sourceLayer.Name ?? "unbekannt");
                continue;
            }

            copied.Add(new GawelaLayerAdminModel
            {
                Key = CreateLayerKey(null, match.Id, usedKeys),
                Name = string.IsNullOrWhiteSpace(sourceLayer.Name) ? match.Name : sourceLayer.Name,
                ProductVariantAttributeId = match.Id,
                BaseRal = NormalizeRal(sourceLayer.BaseRal, "7035"),
                DefaultRal = NormalizeRal(sourceLayer.DefaultRal, NormalizeRal(sourceLayer.BaseRal, "7035")),
                HasExistingMask = false
            });
        }

        if (copied.Count > 0)
        {
            model.Layers = copied;
            model.ThumbnailLabel = string.IsNullOrWhiteSpace(source.ThumbnailLabel) ? "Farbe konfigurieren" : source.ThumbnailLabel;
            model.TemplateMessage = $"Zuordnung von Produkt-ID {sourceProductId} übernommen: {copied.Count} Ebene(n). Bitte für dieses Produkt Basisbild und Masken hochladen und anschliessend speichern.";
            if (missing.Count > 0) model.TemplateMessage += " Nicht gefundene Attribute: " + string.Join(", ", missing.Distinct()) + ".";
        }
        else
        {
            model.TemplateMessage = "Die Vorlage konnte nicht übernommen werden, weil keine passenden Attribute auf dem Zielprodukt gefunden wurden.";
        }

        await Task.CompletedTask;
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

    private GawelaPaletteAdminModel BuildPaletteAdminModel()
    {'''
if needle not in s:
    raise SystemExit('Helper insertion point not found')
s = s.replace(needle, insert, 1)
controller.write_text(s, encoding='utf-8')

# ---------------- Model ----------------
s = model.read_text(encoding='utf-8')
s = s.replace(
    '    public string ProductName { get; set; }\n    public string ThumbnailLabel',
    '    public string ProductName { get; set; }\n    public string TemplateMessage { get; set; }\n    public string ThumbnailLabel'
)
model.write_text(s, encoding='utf-8')

# ---------------- View ----------------
s = view.read_text(encoding='utf-8')
s = s.replace(
    '<div class="alert alert-info"><strong>Universeller GAWELA Produktkonfigurator:</strong> Produkt laden, relevante Smartstore-Attribute als Farbebenen auswählen und pro Ebene eine pixelgenaue PNG-Maske hinterlegen. Die verwendeten RAL-Bildschirmwerte können im Reiter <strong>Farben / RAL</strong> zentral gepflegt werden.</div>',
    '<div class="alert alert-info"><strong>Universeller GAWELA Produktkonfigurator:</strong> Die Zuordnung ist produktbezogen. Dieselben Smartstore-Attribute können deshalb auf beliebig vielen Produkten verwendet werden. Ein Attribut darf bei Bedarf sogar mehrere Masken desselben Produkts steuern. Die RAL-Bildschirmwerte werden im Reiter <strong>Farben / RAL</strong> zentral gepflegt.</div>'
)
old = '''  @if(Model.ProductId>0)
  {
  <form id="gawela-config-form" asp-action="Configure" method="post" enctype="multipart/form-data">'''
new = '''  @if(Model.ProductId>0)
  {
  @if(Model.ConfiguredProducts.Any(x => x.ProductId != Model.ProductId))
  {
    <div class="card mb-3"><div class="card-body py-3">
      <form asp-action="Configure" method="get" class="row align-items-end">
        <input type="hidden" name="productId" value="@Model.ProductId"/>
        <input type="hidden" name="tab" value="products"/>
        <div class="col-md-8"><label>Zuordnung von bestehendem Produkt übernehmen</label><select name="copyFromProductId" class="form-control"><option value="">– Vorlage wählen –</option>@foreach(var p in Model.ConfiguredProducts.Where(x => x.ProductId != Model.ProductId)){<option value="@p.ProductId">@p.Sku – @p.ProductName (ID @p.ProductId)</option>}</select><small class="text-muted">Übernommen werden Ebenennamen, Attribut-Zuordnung sowie Basis-/Fallback-RAL. Bilder und Masken werden nie kopiert.</small></div>
        <div class="col-md-4"><button type="submit" class="btn btn-outline-primary"><i class="fa fa-copy"></i> Zuordnung übernehmen</button></div>
      </form>
    </div></div>
  }
  @if(!string.IsNullOrWhiteSpace(Model.TemplateMessage)){<div class="alert alert-info">@Model.TemplateMessage</div>}
  <form id="gawela-config-form" asp-action="Configure" method="post" enctype="multipart/form-data">'''
if old not in s:
    raise SystemExit('Product form insertion point not found')
s = s.replace(old, new, 1)

s = s.replace(
    '<p class="text-muted">Für jeden unabhängig einfärbbaren Bereich eine Ebene anlegen. Beispiel: Gestell, Sitz, Rückenlehne. Maximal 8 Ebenen empfohlen.</p>',
    '<p class="text-muted">Für jeden einfärbbaren Bereich eine Ebene anlegen. Gleiche Attributnamen auf verschiedenen Produkten sind ausdrücklich erlaubt. Soll ein Attribut mehrere Bildbereiche gleichzeitig steuern, kann dasselbe Attribut auch in mehreren Ebenen verwendet werden. Maximal 8 Ebenen empfohlen.</p>'
)
s = s.replace(
    '<div class="card mb-3 gawela-layer" data-index="@i"><div class="card-body">',
    '<div class="card mb-3 gawela-layer" data-index="@i"><div class="card-body"><input type="hidden" name="Layers[@i].Key" value="@l.Key"/>'
)
s = s.replace(
    '<option value="@a.Id" selected="@(a.Id==l.ProductVariantAttributeId)">@a.Name</option>',
    '<option value="@a.Id" selected="@(a.Id==l.ProductVariantAttributeId)">@a.Name (ID @a.Id)</option>'
)
s = s.replace(
    "const opts=attrs.map(a=>'<option value=\"'+a.Id+'\">'+a.Name.replace(/</g,'&lt;')+'</option>').join('');",
    "const opts=attrs.map(a=>'<option value=\"'+a.Id+'\">'+a.Name.replace(/</g,'&lt;')+' (ID '+a.Id+')</option>').join('');"
)
s = s.replace(
    "d.innerHTML='<div class=\"card-body\"><div class=\"row\">",
    "d.innerHTML='<div class=\"card-body\"><input type=\"hidden\" name=\"Layers['+i+'].Key\" value=\"\"/><div class=\"row\">"
)
view.write_text(s, encoding='utf-8')

# ---------------- Version ----------------
s = module.read_text(encoding='utf-8')
s = s.replace('"Version": "6.4.5"', '"Version": "6.4.6"')
module.write_text(s, encoding='utf-8')
