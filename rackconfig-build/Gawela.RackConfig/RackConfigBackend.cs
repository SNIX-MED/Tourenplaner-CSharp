using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Gawela.RackConfig.Models;
using Gawela.RackConfig.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Smartstore.Core.Configuration;
using Smartstore.Core.Data;
using Smartstore.Core.Seo;
using Smartstore.Core.Widgets;
using Smartstore.Engine;
using Smartstore.Engine.Builders;
using Smartstore.Engine.Modularity;
using Smartstore.Http;
using Smartstore.Web.Components;
using Smartstore.Web.Controllers;
using Smartstore.Web.Modelling;
using Smartstore.Web.Modelling.Settings;
using Smartstore.Web.Models.Catalog;

namespace Gawela.RackConfig.Settings
{
    public class RackConfigSettings : ISettings
    {
        public bool Enabled { get; set; } = true;
        public int CategoryId { get; set; }
        public string CategoryIds { get; set; } = string.Empty;
        public int MaxPalletWeight { get; set; } = 1000;
        public int DefaultDepth { get; set; } = 1100;
        public int MaxVariants { get; set; } = 2;
        public int MinLevelsLow { get; set; } = 2;
        public int MinLevelsHigh { get; set; } = 3;
        public string AccessoryMappingsJson { get; set; } = string.Empty;

        // Legacy fields retained for seamless update from 6.4.22 and as fallback.
        public int Spanplatte1825Id { get; set; }
        public int Spanplatte2700Id { get; set; }
        public int Spanplatte3600Id { get; set; }
        public int Gitterrost1825Id { get; set; }
        public int Gitterrost2700Id { get; set; }
        public int Gitterrost3600Id { get; set; }
        public int Stahlpanel1825Id { get; set; }
        public int Stahlpanel2700Id { get; set; }
        public int Stahlpanel3600Id { get; set; }
        public int Drahtgitter1825Id { get; set; }
        public int Drahtgitter2700Id { get; set; }
        public int Drahtgitter3600Id { get; set; }
        public int Durchschub1825Id { get; set; }
        public int Durchschub2700Id { get; set; }
        public int Durchschub3600Id { get; set; }
        public int EckRammschutzId { get; set; }
        public int MittelRammschutz76Id { get; set; }
        public int MittelRammschutz100Id { get; set; }
    }
}

namespace Gawela.RackConfig.Models
{
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

    public class ConfigurationModel : ModelBase
    {
        public bool Enabled { get; set; }
        public string CategoryIds { get; set; } = string.Empty;
        public int MaxPalletWeight { get; set; }
        public int DefaultDepth { get; set; }
        public int MaxVariants { get; set; }
        public int MinLevelsLow { get; set; }
        public int MinLevelsHigh { get; set; }
        public List<EntityDisplayModel> SelectedCategories { get; set; } = new();
        public List<AccessoryMappingModel> Accessories { get; set; } = new();

        public string[] CategorySelectedIds => RackConfigMapping.ParseIds(CategoryIds).Select(x => x.ToString()).ToArray();
    }

    internal static class RackConfigMapping
    {
        internal static readonly string[] AllowedAccessoryKeys =
        {
            "spanplatte", "gitterrost", "stahlpanel", "drahtgitter", "durchschub",
            "schutz_eck", "schutz_mittel76", "schutz_mittel100"
        };

        internal static int[] ParseIds(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<int>();
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => int.TryParse(x, out var id) ? id : 0)
                .Where(x => x > 0)
                .Distinct()
                .ToArray();
        }

        internal static string NormalizeIds(string raw) => string.Join(',', ParseIds(raw));

        internal static List<AccessoryMappingModel> ParseMappings(RackConfigSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.AccessoryMappingsJson))
            {
                try
                {
                    var data = JsonSerializer.Deserialize<List<AccessoryMappingData>>(settings.AccessoryMappingsJson) ?? new();
                    return data.Select(x => new AccessoryMappingModel
                    {
                        Key = x.Key ?? string.Empty,
                        Width = x.Width,
                        ProductId = x.ProductId
                    }).ToList();
                }
                catch
                {
                    // Continue with migration defaults below.
                }
            }

            var rows = new List<AccessoryMappingModel>();
            Add(rows, "spanplatte", 1825, settings.Spanplatte1825Id);
            Add(rows, "spanplatte", 2700, settings.Spanplatte2700Id);
            Add(rows, "spanplatte", 3600, settings.Spanplatte3600Id);
            Add(rows, "gitterrost", 1825, settings.Gitterrost1825Id);
            Add(rows, "gitterrost", 2700, settings.Gitterrost2700Id);
            Add(rows, "gitterrost", 3600, settings.Gitterrost3600Id);
            Add(rows, "stahlpanel", 1825, settings.Stahlpanel1825Id);
            Add(rows, "stahlpanel", 2700, settings.Stahlpanel2700Id);
            Add(rows, "stahlpanel", 3600, settings.Stahlpanel3600Id);
            Add(rows, "drahtgitter", 1825, settings.Drahtgitter1825Id);
            Add(rows, "drahtgitter", 2700, settings.Drahtgitter2700Id);
            Add(rows, "drahtgitter", 3600, settings.Drahtgitter3600Id);
            Add(rows, "durchschub", 1825, settings.Durchschub1825Id);
            Add(rows, "durchschub", 2700, settings.Durchschub2700Id);
            Add(rows, "durchschub", 3600, settings.Durchschub3600Id);
            Add(rows, "schutz_eck", 0, settings.EckRammschutzId);
            Add(rows, "schutz_mittel76", 0, settings.MittelRammschutz76Id);
            Add(rows, "schutz_mittel100", 0, settings.MittelRammschutz100Id);
            return rows;
        }

        private static void Add(List<AccessoryMappingModel> rows, string key, int width, int productId)
        {
            rows.Add(new AccessoryMappingModel { Key = key, Width = width, ProductId = productId });
        }

        internal static string LabelFor(string key) => key switch
        {
            "spanplatte" => "Spanplattenauflagen",
            "gitterrost" => "Gitterrostauflagen",
            "stahlpanel" => "Stahlpanelauflagen",
            "drahtgitter" => "Drahtgitterauflagen",
            "durchschub" => "Durchschubsicherungen",
            "schutz_eck" => "Eck-Rammschutz",
            "schutz_mittel76" => "Mittelstützen-Rammschutz 76 mm",
            "schutz_mittel100" => "Mittelstützen-Rammschutz 100 mm",
            _ => key
        };
    }
}

namespace Gawela.RackConfig
{
    internal class Module : ModuleBase, IConfigurable
    {
        public RouteInfo GetConfigurationRoute() => new("Configure", "RackConfigAdmin", new { area = "Admin" });

        public override async Task InstallAsync(ModuleInstallationContext context)
        {
            await TrySaveSettingsAsync<RackConfigSettings>();
            await base.InstallAsync(context);
        }

        public override async Task UninstallAsync()
        {
            await DeleteSettingsAsync<RackConfigSettings>();
            await base.UninstallAsync();
        }
    }

    internal class Startup : StarterBase
    {
        public override void ConfigureServices(IServiceCollection services, IApplicationContext appContext)
        {
            services.AddScoped<Filters.RackCategoryFilter>();
            services.Configure<MvcOptions>(options => options.Filters.AddService<Filters.RackCategoryFilter>());
        }
    }
}

namespace Gawela.RackConfig.Controllers
{
    [Area("Admin")]
    [Route("Admin/RackConfig/{action=Configure}")]
    public class RackConfigAdminController : AdminController
    {
        private readonly SmartDbContext _db;

        public RackConfigAdminController(SmartDbContext db)
        {
            _db = db;
        }

        [LoadSetting]
        public async Task<IActionResult> Configure(RackConfigSettings settings)
        {
            var categoryIds = RackConfigMapping.NormalizeIds(settings.CategoryIds);
            if (string.IsNullOrEmpty(categoryIds) && settings.CategoryId > 0)
            {
                categoryIds = settings.CategoryId.ToString();
            }

            var model = new ConfigurationModel
            {
                Enabled = settings.Enabled,
                CategoryIds = categoryIds,
                MaxPalletWeight = settings.MaxPalletWeight,
                DefaultDepth = settings.DefaultDepth,
                MaxVariants = settings.MaxVariants,
                MinLevelsLow = settings.MinLevelsLow,
                MinLevelsHigh = settings.MinLevelsHigh,
                Accessories = RackConfigMapping.ParseMappings(settings)
            };

            await EnrichAsync(model);
            return View(model);
        }

        [HttpPost, SaveSetting, ValidateAntiForgeryToken]
        public async Task<IActionResult> Configure(ConfigurationModel model, RackConfigSettings settings)
        {
            model.CategoryIds = RackConfigMapping.NormalizeIds(model.CategoryIds);
            model.Accessories ??= new List<AccessoryMappingModel>();

            if (RackConfigMapping.ParseIds(model.CategoryIds).Length == 0)
                ModelState.AddModelError(nameof(model.CategoryIds), "Bitte mindestens eine Warengruppe auswählen.");
            if (model.MaxPalletWeight <= 0)
                ModelState.AddModelError(nameof(model.MaxPalletWeight), "Die Traglast muss grösser als 0 sein.");
            if (model.DefaultDepth <= 0)
                ModelState.AddModelError(nameof(model.DefaultDepth), "Die Regaltiefe muss grösser als 0 sein.");
            if (model.MaxVariants < 1 || model.MaxVariants > 4)
                ModelState.AddModelError(nameof(model.MaxVariants), "Die Anzahl Varianten muss zwischen 1 und 4 liegen.");
            if (model.MinLevelsLow < 1 || model.MinLevelsHigh < model.MinLevelsLow)
                ModelState.AddModelError(nameof(model.MinLevelsHigh), "Die Mindestanzahl Ebenen ist ungültig.");

            foreach (var row in model.Accessories)
            {
                row.Key = (row.Key ?? string.Empty).Trim().ToLowerInvariant();
                if (!RackConfigMapping.AllowedAccessoryKeys.Contains(row.Key))
                    ModelState.AddModelError(nameof(model.Accessories), "Eine Zubehör-Zuordnung enthält eine unbekannte Zubehörart.");
                if (row.Key is "spanplatte" or "gitterrost" or "stahlpanel" or "drahtgitter" or "durchschub")
                {
                    if (row.Width is not (1825 or 2700 or 3600))
                        ModelState.AddModelError(nameof(model.Accessories), $"Für {RackConfigMapping.LabelFor(row.Key)} muss eine Breite von 1825, 2700 oder 3600 mm gewählt werden.");
                }
                else
                {
                    row.Width = 0;
                }
            }

            var duplicates = model.Accessories
                .GroupBy(x => new { x.Key, x.Width })
                .Where(g => g.Count() > 1)
                .ToList();
            if (duplicates.Count > 0)
                ModelState.AddModelError(nameof(model.Accessories), "Eine Zubehörart/Ausführung ist mehrfach vorhanden. Bitte jede Zuordnung nur einmal anlegen.");

            if (!ModelState.IsValid)
            {
                await EnrichAsync(model);
                return View(model);
            }

            var ids = RackConfigMapping.ParseIds(model.CategoryIds);
            settings.Enabled = model.Enabled;
            settings.CategoryIds = model.CategoryIds;
            settings.CategoryId = ids.FirstOrDefault();
            settings.MaxPalletWeight = model.MaxPalletWeight;
            settings.DefaultDepth = model.DefaultDepth;
            settings.MaxVariants = model.MaxVariants;
            settings.MinLevelsLow = model.MinLevelsLow;
            settings.MinLevelsHigh = model.MinLevelsHigh;

            var mappingData = model.Accessories.Select(x => new AccessoryMappingData
            {
                Key = x.Key,
                Width = x.Width,
                ProductId = x.ProductId
            }).ToList();
            settings.AccessoryMappingsJson = JsonSerializer.Serialize(mappingData);
            ApplyLegacyAccessoryFields(settings, mappingData);

            ModelState.Clear();
            NotifySuccess("Palettenregal-Konfigurator gespeichert.");
            return RedirectToAction(nameof(Configure));
        }

        private async Task EnrichAsync(ConfigurationModel model)
        {
            var categoryIds = RackConfigMapping.ParseIds(model.CategoryIds);
            if (categoryIds.Length > 0)
            {
                model.SelectedCategories = await _db.Categories.AsNoTracking()
                    .Where(x => categoryIds.Contains(x.Id))
                    .Select(x => new EntityDisplayModel { Id = x.Id, Name = x.Name })
                    .ToListAsync();
                model.SelectedCategories = categoryIds
                    .Select(id => model.SelectedCategories.FirstOrDefault(x => x.Id == id) ?? new EntityDisplayModel { Id = id, Name = $"Warengruppe #{id}" })
                    .ToList();
            }

            var productIds = model.Accessories.Where(x => x.ProductId > 0).Select(x => x.ProductId).Distinct().ToArray();
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
                }
            }

            foreach (var row in model.Accessories)
            {
                if (string.IsNullOrWhiteSpace(row.Index)) row.Index = Guid.NewGuid().ToString("N");
            }
        }

        private static void ApplyLegacyAccessoryFields(RackConfigSettings s, List<AccessoryMappingData> rows)
        {
            int Find(string key, int width = 0) => rows.LastOrDefault(x => x.Key == key && x.Width == width)?.ProductId ?? 0;
            s.Spanplatte1825Id = Find("spanplatte", 1825);
            s.Spanplatte2700Id = Find("spanplatte", 2700);
            s.Spanplatte3600Id = Find("spanplatte", 3600);
            s.Gitterrost1825Id = Find("gitterrost", 1825);
            s.Gitterrost2700Id = Find("gitterrost", 2700);
            s.Gitterrost3600Id = Find("gitterrost", 3600);
            s.Stahlpanel1825Id = Find("stahlpanel", 1825);
            s.Stahlpanel2700Id = Find("stahlpanel", 2700);
            s.Stahlpanel3600Id = Find("stahlpanel", 3600);
            s.Drahtgitter1825Id = Find("drahtgitter", 1825);
            s.Drahtgitter2700Id = Find("drahtgitter", 2700);
            s.Drahtgitter3600Id = Find("drahtgitter", 3600);
            s.Durchschub1825Id = Find("durchschub", 1825);
            s.Durchschub2700Id = Find("durchschub", 2700);
            s.Durchschub3600Id = Find("durchschub", 3600);
            s.EckRammschutzId = Find("schutz_eck");
            s.MittelRammschutz76Id = Find("schutz_mittel76");
            s.MittelRammschutz100Id = Find("schutz_mittel100");
        }
    }

    [Route("PalletRackX/{action=Config}")]
    public class PalletRackXController : PublicController
    {
        private readonly SmartDbContext _db;
        private readonly RackConfigSettings _settings;

        public PalletRackXController(SmartDbContext db, RackConfigSettings settings)
        {
            _db = db;
            _settings = settings;
        }

        public IActionResult Config() => View(_settings);

        [HttpGet]
        public async Task<IActionResult> ProductPage(int id)
        {
            if (id <= 0) return NotFound();
            var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (product == null || product.Deleted) return NotFound();
            return RedirectToRoute("Product", new { SeName = await product.GetActiveSlugAsync() });
        }
    }
}

namespace Gawela.RackConfig.Filters
{
    public class RackCategoryFilter : IAsyncResultFilter
    {
        private readonly RackConfigSettings _settings;
        private readonly IWidgetProvider _widgetProvider;

        public RackCategoryFilter(RackConfigSettings settings, IWidgetProvider widgetProvider)
        {
            _settings = settings;
            _widgetProvider = widgetProvider;
        }

        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (_settings.Enabled && context.Result is ViewResult viewResult && viewResult.Model is CategoryModel category)
            {
                var ids = RackConfigMapping.ParseIds(_settings.CategoryIds);
                if (ids.Length == 0 && _settings.CategoryId > 0) ids = new[] { _settings.CategoryId };
                var configured = ids.Contains(category.Id);
                var legacy = ids.Length == 0 && context.HttpContext.Request.Path.Value?.Contains("palettenregale-schnell-ab-lager-lieferbar-3", StringComparison.OrdinalIgnoreCase) == true;
                if (configured || legacy)
                    _widgetProvider.RegisterWidget("categorydetails_top", new ComponentWidget<Gawela.RackConfig.Components.RackConfiguratorViewComponent> { Key = "Gawela.RackConfig" });
            }
            await next();
        }
    }
}

namespace Gawela.RackConfig.Components
{
    public class RackConfiguratorViewComponent : SmartViewComponent
    {
        private readonly RackConfigSettings _settings;

        public RackConfiguratorViewComponent(RackConfigSettings settings)
        {
            _settings = settings;
        }

        public IViewComponentResult Invoke(object model)
        {
            if (!_settings.Enabled || model is not CategoryModel category) return Empty();
            var ids = RackConfigMapping.ParseIds(_settings.CategoryIds);
            if (ids.Length == 0 && _settings.CategoryId > 0) ids = new[] { _settings.CategoryId };
            if (ids.Length > 0 && !ids.Contains(category.Id)) return Empty();
            return View(_settings);
        }
    }
}
