using System.Text.Json;
using Gawela.RackConfig.Models;
using Gawela.RackConfig.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Data;
using Smartstore.Core.Security;
using Smartstore.Web.Controllers;

namespace Gawela.RackConfig.Controllers;

[Area("Admin")]
[AuthorizeAdmin]
public class RackConfigAdminController : AdminController
{
    private readonly SmartDbContext _db;
    private readonly RackConfigSettings _settings;

    public RackConfigAdminController(SmartDbContext db, RackConfigSettings settings)
    {
        _db = db;
        _settings = settings;
    }

    public async Task<IActionResult> Configure()
    {
        var categoryIds = RackConfigMapping.NormalizeIds(_settings.CategoryIds);
        if (string.IsNullOrEmpty(categoryIds) && _settings.CategoryId > 0)
            categoryIds = _settings.CategoryId.ToString();

        var model = new ConfigurationModel
        {
            Enabled = _settings.Enabled,
            CategoryIds = categoryIds,
            MaxPalletWeight = _settings.MaxPalletWeight,
            DefaultDepth = _settings.DefaultDepth,
            MaxVariants = _settings.MaxVariants,
            MinLevelsLow = _settings.MinLevelsLow,
            MinLevelsHigh = _settings.MinLevelsHigh,
            Accessories = RackConfigMapping.ParseMappings(_settings)
        };
        await EnrichAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Configure(ConfigurationModel model)
    {
        model.CategoryIds = RackConfigMapping.NormalizeIds(model.CategoryIds);
        model.Accessories ??= [];

        if (RackConfigMapping.ParseIds(model.CategoryIds).Length == 0)
            ModelState.AddModelError(nameof(model.CategoryIds), "Bitte mindestens eine Warengruppe auswählen.");
        if (model.MaxPalletWeight <= 0)
            ModelState.AddModelError(nameof(model.MaxPalletWeight), "Die Traglast muss grösser als 0 sein.");
        if (model.DefaultDepth <= 0)
            ModelState.AddModelError(nameof(model.DefaultDepth), "Die Regaltiefe muss grösser als 0 sein.");
        if (model.MaxVariants is < 1 or > 4)
            ModelState.AddModelError(nameof(model.MaxVariants), "Die Anzahl Varianten muss zwischen 1 und 4 liegen.");
        if (model.MinLevelsLow < 1 || model.MinLevelsHigh < model.MinLevelsLow)
            ModelState.AddModelError(nameof(model.MinLevelsHigh), "Die Mindestanzahl Ebenen ist ungültig.");

        foreach (var row in model.Accessories)
        {
            row.Key = (row.Key ?? string.Empty).Trim().ToLowerInvariant();
            if (!RackConfigMapping.KnownKeys.Contains(row.Key))
                ModelState.AddModelError(nameof(model.Accessories), "Eine Zubehör-Zuordnung enthält eine unbekannte Zubehörart.");

            var widthBased = row.Key is "spanplatte" or "gitterrost" or "stahlpanel" or "drahtgitter" or "durchschub";
            if (widthBased && row.Width is not (1825 or 2700 or 3600))
                ModelState.AddModelError(nameof(model.Accessories), $"Für {RackConfigMapping.LabelFor(row.Key)} muss eine Breite von 1825, 2700 oder 3600 mm gewählt werden.");
            if (!widthBased)
                row.Width = 0;
        }

        model.Accessories = model.Accessories
            .Where(x => RackConfigMapping.KnownKeys.Contains(x.Key))
            .GroupBy(x => new { x.Key, x.Width })
            .Select(g => g.Last())
            .ToList();

        if (!ModelState.IsValid)
        {
            await EnrichAsync(model);
            return View(model);
        }

        _settings.Enabled = model.Enabled;
        _settings.CategoryIds = model.CategoryIds;
        _settings.CategoryId = RackConfigMapping.ParseIds(model.CategoryIds).FirstOrDefault();
        _settings.MaxPalletWeight = model.MaxPalletWeight;
        _settings.DefaultDepth = model.DefaultDepth;
        _settings.MaxVariants = model.MaxVariants;
        _settings.MinLevelsLow = model.MinLevelsLow;
        _settings.MinLevelsHigh = model.MinLevelsHigh;
        _settings.AccessoryMappingsJson = JsonSerializer.Serialize(model.Accessories.Select(x => new AccessoryMappingData
        {
            Key = x.Key,
            Width = x.Width,
            ProductId = x.ProductId
        }).ToList());
        ApplyLegacyAccessoryFields(_settings, model.Accessories);

        await Services.SettingFactory.SaveSettingsAsync(_settings);
        NotifySuccess(T("Admin.Common.DataSuccessfullySaved"));
        return RedirectToAction(nameof(Configure));
    }

    private async Task EnrichAsync(ConfigurationModel model)
    {
        var ids = RackConfigMapping.ParseIds(model.CategoryIds);
        if (ids.Length > 0)
        {
            var categories = await _db.Categories.AsNoTracking()
                .Where(x => ids.Contains(x.Id))
                .Select(x => new EntityDisplayModel { Id = x.Id, Name = x.Name })
                .ToListAsync();
            model.SelectedCategories = ids
                .Select(id => categories.FirstOrDefault(x => x.Id == id) ?? new EntityDisplayModel { Id = id, Name = $"Warengruppe #{id}" })
                .ToList();
        }

        var productIds = model.Accessories.Select(x => x.ProductId).Where(x => x > 0).Distinct().ToArray();
        if (productIds.Length > 0)
        {
            var products = await _db.Products.AsNoTracking()
                .Where(x => productIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Name, x.Sku })
                .ToListAsync();
            foreach (var row in model.Accessories)
            {
                var product = products.FirstOrDefault(x => x.Id == row.ProductId);
                if (product != null)
                {
                    row.ProductName = product.Name ?? string.Empty;
                    row.ProductSku = product.Sku ?? string.Empty;
                }
                if (string.IsNullOrWhiteSpace(row.Index))
                    row.Index = Guid.NewGuid().ToString("N");
            }
        }
        else
        {
            foreach (var row in model.Accessories)
                if (string.IsNullOrWhiteSpace(row.Index)) row.Index = Guid.NewGuid().ToString("N");
        }
    }

    private static void ApplyLegacyAccessoryFields(RackConfigSettings s, IEnumerable<AccessoryMappingModel> rows)
    {
        int Find(string key, int width) => rows.LastOrDefault(x => x.Key == key && x.Width == width)?.ProductId ?? 0;
        s.Spanplatte1825Id = Find("spanplatte",1825); s.Spanplatte2700Id = Find("spanplatte",2700); s.Spanplatte3600Id = Find("spanplatte",3600);
        s.Gitterrost1825Id = Find("gitterrost",1825); s.Gitterrost2700Id = Find("gitterrost",2700); s.Gitterrost3600Id = Find("gitterrost",3600);
        s.Stahlpanel1825Id = Find("stahlpanel",1825); s.Stahlpanel2700Id = Find("stahlpanel",2700); s.Stahlpanel3600Id = Find("stahlpanel",3600);
        s.Drahtgitter1825Id = Find("drahtgitter",1825); s.Drahtgitter2700Id = Find("drahtgitter",2700); s.Drahtgitter3600Id = Find("drahtgitter",3600);
        s.Durchschub1825Id = Find("durchschub",1825); s.Durchschub2700Id = Find("durchschub",2700); s.Durchschub3600Id = Find("durchschub",3600);
        s.EckRammschutzId = Find("schutz_eck",0); s.MittelRammschutz76Id = Find("schutz_mittel76",0); s.MittelRammschutz100Id = Find("schutz_mittel100",0);
    }
}
