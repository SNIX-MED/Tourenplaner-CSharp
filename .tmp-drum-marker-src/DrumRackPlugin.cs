using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Smartstore;
using Smartstore.Core;
using Smartstore.Core.Catalog.Categories;
using Smartstore.Core.Checkout.Cart;
using Smartstore.Core.Checkout.Orders.Events;
using Smartstore.Core.Configuration;
using Smartstore.Core.Data;
using Smartstore.Core.Security;
using Smartstore.Core.Seo;
using Smartstore.Core.Widgets;
using Smartstore.Engine;
using Smartstore.Engine.Builders;
using Smartstore.Engine.Modularity;
using Smartstore.Events;
using Smartstore.Http;
using Smartstore.Web.Components;
using Smartstore.Web.Controllers;
using Smartstore.Web.Models.Catalog;

namespace Gawela.DrumRackConfig.Settings
{
    public class DrumRackConfigSettings : ISettings
    {
        public bool Enabled { get; set; } = true;
        public int CategoryId { get; set; }
        public string CategoryIds { get; set; } = string.Empty;
        public int DefaultHeight { get; set; } = 2000;
        public int DefaultDepth { get; set; } = 400;
        public int DefaultShelfLoad { get; set; } = 500;
        public string DefaultMaterial { get; set; } = "wood";
        public int DefaultLevels2000 { get; set; } = 2;
        public int DefaultLevels2500 { get; set; } = 2;
        public int DefaultLevels3000 { get; set; } = 2;
        public int MaxVariants { get; set; } = 2;
    }
}

namespace Gawela.DrumRackConfig.Models
{
    using Gawela.DrumRackConfig.Settings;

    public class EntityDisplayModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class ConfigurationModel
    {
        public bool Enabled { get; set; }
        public string CategoryIds { get; set; } = string.Empty;
        public int DefaultHeight { get; set; }
        public int DefaultDepth { get; set; }
        public int DefaultShelfLoad { get; set; }
        public string DefaultMaterial { get; set; } = "wood";
        public int DefaultLevels2000 { get; set; }
        public int DefaultLevels2500 { get; set; }
        public int DefaultLevels3000 { get; set; }
        public int MaxVariants { get; set; }
        public List<EntityDisplayModel> SelectedCategories { get; set; } = [];
        public int[] CategorySelectedIds => DrumRackMapping.ParseIds(CategoryIds);
    }

    public static class DrumRackMapping
    {
        public static int[] ParseIds(string? value)
            => (value ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => int.TryParse(x, out var id) ? id : 0)
                .Where(x => x > 0)
                .Distinct()
                .ToArray();

        public static string NormalizeIds(string? value) => string.Join(',', ParseIds(value));
    }
}

namespace Gawela.DrumRackConfig.Components
{
    using Gawela.DrumRackConfig.Settings;

    public class DrumRackConfiguratorViewComponent : SmartViewComponent
    {
        private readonly DrumRackConfigSettings _settings;

        public DrumRackConfiguratorViewComponent(DrumRackConfigSettings settings)
            => _settings = settings;

        public IViewComponentResult Invoke()
        {
            if (!_settings.Enabled)
                return Empty();

            return View(_settings);
        }
    }
}

namespace Gawela.DrumRackConfig.Filters
{
    using Gawela.DrumRackConfig.Components;
    using Gawela.DrumRackConfig.Models;
    using Gawela.DrumRackConfig.Settings;

    public sealed class DrumRackCategoryFilter : IAsyncResultFilter
    {
        private const string SystemName = "Gawela.DrumRackConfig";
        private readonly DrumRackConfigSettings _settings;
        private readonly IWidgetProvider _widgetProvider;

        public DrumRackCategoryFilter(DrumRackConfigSettings settings, IWidgetProvider widgetProvider)
        {
            _settings = settings;
            _widgetProvider = widgetProvider;
        }

        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (ModularState.Instance.InstalledModules.Contains(SystemName) &&
                _settings.Enabled &&
                context.Result is ViewResult { Model: CategoryModel categoryModel })
            {
                var ids = DrumRackMapping.ParseIds(_settings.CategoryIds);
                if (ids.Length == 0 && _settings.CategoryId > 0)
                    ids = [_settings.CategoryId];

                var path = context.HttpContext.Request.Path.Value ?? string.Empty;
                var active = ids.Contains(categoryModel.Id) ||
                    (ids.Length == 0 && path.Contains("fassregal", StringComparison.OrdinalIgnoreCase));

                if (active)
                {
                    _widgetProvider.RegisterWidget(
                        "categorydetails_top",
                        new ComponentWidget<DrumRackConfiguratorViewComponent>() { Key = SystemName });
                }
            }

            await next();
        }
    }
}

namespace Gawela.DrumRackConfig.Controllers
{
    using Gawela.DrumRackConfig.Models;
    using Gawela.DrumRackConfig.Settings;

    [Area("Admin")]
    [AuthorizeAdmin]
    public class DrumRackConfigAdminController : AdminController
    {
        private readonly SmartDbContext _db;
        private readonly DrumRackConfigSettings _settings;

        public DrumRackConfigAdminController(SmartDbContext db, DrumRackConfigSettings settings)
        {
            _db = db;
            _settings = settings;
        }

        public async Task<IActionResult> Configure()
        {
            var categoryIds = DrumRackMapping.NormalizeIds(_settings.CategoryIds);
            if (string.IsNullOrEmpty(categoryIds) && _settings.CategoryId > 0)
                categoryIds = _settings.CategoryId.ToString();

            var model = new ConfigurationModel
            {
                Enabled = _settings.Enabled,
                CategoryIds = categoryIds,
                DefaultHeight = _settings.DefaultHeight,
                DefaultDepth = _settings.DefaultDepth,
                DefaultShelfLoad = _settings.DefaultShelfLoad,
                DefaultMaterial = _settings.DefaultMaterial,
                DefaultLevels2000 = _settings.DefaultLevels2000,
                DefaultLevels2500 = _settings.DefaultLevels2500,
                DefaultLevels3000 = _settings.DefaultLevels3000,
                MaxVariants = _settings.MaxVariants
            };

            await EnrichAsync(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            model.CategoryIds = DrumRackMapping.NormalizeIds(model.CategoryIds);

            if (DrumRackMapping.ParseIds(model.CategoryIds).Length == 0)
                ModelState.AddModelError(nameof(model.CategoryIds), "Bitte mindestens eine Warengruppe auswählen.");
            if (model.DefaultHeight is not (2000 or 2500 or 3000))
                ModelState.AddModelError(nameof(model.DefaultHeight), "Die Standardhöhe ist ungültig.");
            if (model.DefaultDepth <= 0)
                ModelState.AddModelError(nameof(model.DefaultDepth), "Die Regaltiefe muss grösser als 0 sein.");
            if (model.DefaultShelfLoad <= 0)
                ModelState.AddModelError(nameof(model.DefaultShelfLoad), "Die Fachlast muss grösser als 0 sein.");
            if (string.IsNullOrWhiteSpace(model.DefaultMaterial))
                ModelState.AddModelError(nameof(model.DefaultMaterial), "Das Standardmaterial ist ungültig.");
            if (model.DefaultLevels2000 <= 0 || model.DefaultLevels2500 <= 0 || model.DefaultLevels3000 <= 0)
                ModelState.AddModelError(nameof(model.DefaultLevels2000), "Die Standardanzahl Ebenen ist ungültig.");
            if (model.MaxVariants is < 1 or > 4)
                ModelState.AddModelError(nameof(model.MaxVariants), "Die Anzahl Varianten muss zwischen 1 und 4 liegen.");

            if (!ModelState.IsValid)
            {
                await EnrichAsync(model);
                return View(model);
            }

            _settings.Enabled = model.Enabled;
            _settings.CategoryIds = model.CategoryIds;
            _settings.CategoryId = DrumRackMapping.ParseIds(model.CategoryIds).FirstOrDefault();
            _settings.DefaultHeight = model.DefaultHeight;
            _settings.DefaultDepth = model.DefaultDepth;
            _settings.DefaultShelfLoad = model.DefaultShelfLoad;
            _settings.DefaultMaterial = model.DefaultMaterial;
            _settings.DefaultLevels2000 = model.DefaultLevels2000;
            _settings.DefaultLevels2500 = model.DefaultLevels2500;
            _settings.DefaultLevels3000 = model.DefaultLevels3000;
            _settings.MaxVariants = model.MaxVariants;

            await Services.SettingFactory.SaveSettingsAsync(_settings);
            NotifySuccess(T("Admin.Common.DataSuccessfullySaved"));
            return RedirectToAction(nameof(Configure));
        }

        private async Task EnrichAsync(ConfigurationModel model)
        {
            var ids = DrumRackMapping.ParseIds(model.CategoryIds);
            if (ids.Length == 0)
                return;

            var categories = await _db.Categories.AsNoTracking()
                .Where(x => ids.Contains(x.Id))
                .Select(x => new EntityDisplayModel { Id = x.Id, Name = x.Name })
                .ToListAsync();

            model.SelectedCategories = ids
                .Select(id => categories.FirstOrDefault(x => x.Id == id) ?? new EntityDisplayModel { Id = id, Name = $"Warengruppe #{id}" })
                .ToList();
        }
    }

    public class DrumRackXController : PublicController
    {
        private readonly SmartDbContext _db;
        private readonly DrumRackConfigSettings _settings;
        private readonly IWorkContext _workContext;
        private readonly IShoppingCartService _shoppingCartService;

        public DrumRackXController(
            SmartDbContext db,
            DrumRackConfigSettings settings,
            IWorkContext workContext,
            IShoppingCartService shoppingCartService)
        {
            _db = db;
            _settings = settings;
            _workContext = workContext;
            _shoppingCartService = shoppingCartService;
        }

        public IActionResult Config() => View(_settings);

        public async Task<IActionResult> ProductPage(int id)
        {
            if (id <= 0)
                return NotFound();

            var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (product == null || product.Deleted)
                return NotFound();

            var slug = await product.GetActiveSlugAsync();
            return RedirectToRoute("Product", new { SeName = slug });
        }

        [HttpPost]
        public async Task<IActionResult> MarkSource(
            int productId,
            int cartType = 1,
            int lineIndex = -1,
            int quantity = 1,
            string? sku = null)
        {
            if (productId <= 0)
                return BadRequest(new { success = false, message = "Ungültige Produkt-ID." });

            var customer = _workContext.CurrentCustomer;
            if (customer == null || customer.Id <= 0)
                return BadRequest(new { success = false, message = "Kunde konnte nicht ermittelt werden." });

            var type = cartType == 2 ? ShoppingCartType.Wishlist : ShoppingCartType.ShoppingCart;
            var cart = await _shoppingCartService.GetCartAsync(customer, type, 0, null);
            var item = cart.Items
                .Select(x => x.Item)
                .Where(x => x.ParentItemId == null && x.ProductId == productId)
                .OrderByDescending(x => x.UpdatedOnUtc)
                .ThenByDescending(x => x.Id)
                .FirstOrDefault();

            if (item?.GenericAttributes == null)
                return NotFound(new { success = false, message = "Übertragene Warenkorbposition wurde nicht gefunden." });

            var qty = Math.Max(1, quantity);
            var line = lineIndex >= 0 ? lineIndex + 1 : 0;
            var attributes = item.GenericAttributes;

            attributes.Set(DrumRackTracking.SourceKey, DrumRackTracking.SourceValue);
            attributes.Set(DrumRackTracking.TargetKey, type == ShoppingCartType.Wishlist ? "Offertanfrage" : "Warenkorb");
            attributes.Set(DrumRackTracking.VersionKey, DrumRackTracking.Version);
            attributes.Set(DrumRackTracking.MarkedOnUtcKey, DateTime.UtcNow.ToString("O"));
            attributes.Set(DrumRackTracking.QuantityKey, attributes.Get<int>(DrumRackTracking.QuantityKey) + qty);
            if (line > 0)
                attributes.Set(DrumRackTracking.LineKey, line.ToString());

            await attributes.SaveChangesAsync();

            if (type == ShoppingCartType.ShoppingCart && customer.GenericAttributes != null)
            {
                var pending = DrumRackTracking.ReadPending(customer.GenericAttributes.Get<string>(DrumRackTracking.PendingKey));
                pending.Add(new DrumRackPendingMarker
                {
                    ProductId = item.ProductId,
                    Sku = string.IsNullOrWhiteSpace(sku) ? item.Product?.Sku : sku,
                    Quantity = qty,
                    Line = line,
                    StoreId = item.StoreId,
                    MarkedOnUtc = DateTime.UtcNow
                });

                if (pending.Count > 200)
                    pending = pending.TakeLast(200).ToList();

                customer.GenericAttributes.Set(DrumRackTracking.PendingKey, DrumRackTracking.WritePending(pending));
                await customer.GenericAttributes.SaveChangesAsync();
            }

            return Json(new
            {
                success = true,
                source = DrumRackTracking.SourceValue,
                cartItemId = item.Id,
                cartType = (int)type,
                line
            });
        }
    }
}

namespace Gawela.DrumRackConfig
{
    using Gawela.DrumRackConfig.Controllers;
    using Gawela.DrumRackConfig.Filters;
    using Gawela.DrumRackConfig.Settings;

    internal static class DrumRackTracking
    {
        public const string SourceKey = "Gawela.DrumRackConfig.Source";
        public const string TargetKey = "Gawela.DrumRackConfig.Target";
        public const string LineKey = "Gawela.DrumRackConfig.Line";
        public const string QuantityKey = "Gawela.DrumRackConfig.Quantity";
        public const string MarkedOnUtcKey = "Gawela.DrumRackConfig.MarkedOnUtc";
        public const string VersionKey = "Gawela.DrumRackConfig.Version";
        public const string PendingKey = "Gawela.DrumRackConfig.PendingOrderMarkers";
        public const string SourceValue = "GAWELA Fassregal-Konfigurator";
        public const string Version = "6.4.4";

        public static List<DrumRackPendingMarker> ReadPending(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return [];

            try
            {
                return JsonSerializer.Deserialize<List<DrumRackPendingMarker>>(json) ?? [];
            }
            catch
            {
                return [];
            }
        }

        public static string WritePending(IEnumerable<DrumRackPendingMarker> markers)
            => JsonSerializer.Serialize(markers);
    }

    internal sealed class DrumRackPendingMarker
    {
        public int ProductId { get; set; }
        public string? Sku { get; set; }
        public int Quantity { get; set; }
        public int Line { get; set; }
        public int StoreId { get; set; }
        public DateTime MarkedOnUtc { get; set; }
    }

    internal class Events : IConsumer
    {
        public async Task HandleEventAsync(OrderPlacedEvent message, SmartDbContext db, CancellationToken cancelToken)
        {
            var order = message.Order;
            if (order == null || order.CustomerId <= 0)
                return;

            var customer = await db.Customers.FindByIdAsync(order.CustomerId, true, cancelToken);
            if (customer?.GenericAttributes == null)
                return;

            var pending = DrumRackTracking.ReadPending(customer.GenericAttributes.Get<string>(DrumRackTracking.PendingKey));
            if (pending.Count == 0)
                return;

            var orderedProductIds = order.OrderItems.Select(x => x.ProductId).ToHashSet();
            var matched = pending
                .Where(x => x.StoreId == order.StoreId && orderedProductIds.Contains(x.ProductId))
                .ToList();

            customer.GenericAttributes.Set(DrumRackTracking.PendingKey, string.Empty);
            await customer.GenericAttributes.SaveChangesAsync(cancelToken);

            if (matched.Count == 0)
                return;

            var lines = matched.Where(x => x.Line > 0).Select(x => x.Line).Distinct().OrderBy(x => x).ToArray();
            var matchedIds = matched.Select(x => x.ProductId).ToHashSet();
            var itemSummary = order.OrderItems
                .Where(x => matchedIds.Contains(x.ProductId))
                .Select(x => $"{x.Sku} × {x.Quantity}")
                .Distinct()
                .ToArray();

            if (order.GenericAttributes != null)
            {
                order.GenericAttributes.Set(DrumRackTracking.SourceKey, DrumRackTracking.SourceValue);
                order.GenericAttributes.Set(DrumRackTracking.VersionKey, DrumRackTracking.Version);
                order.GenericAttributes.Set(DrumRackTracking.LineKey, lines.Length > 0 ? string.Join(", ", lines) : "Konfigurator");
                order.GenericAttributes.Set(DrumRackTracking.QuantityKey, matched.Sum(x => Math.Max(1, x.Quantity)));
                await order.GenericAttributes.SaveChangesAsync(cancelToken);
            }

            var lineText = lines.Length > 0 ? $"Regalzeile(n): {string.Join(", ", lines)}. " : string.Empty;
            var itemText = itemSummary.Length > 0 ? $"Artikel: {string.Join("; ", itemSummary)}." : string.Empty;
            db.OrderNotes.Add(order, $"Quelle: {DrumRackTracking.SourceValue}. {lineText}{itemText} Tracking-Version {DrumRackTracking.Version}.");
            await db.SaveChangesAsync(cancelToken);
        }
    }

    public class Module : ModuleBase, IConfigurable
    {
        public RouteInfo GetConfigurationRoute()
            => new("Configure", "DrumRackConfigAdmin", new { area = "Admin" });

        public override async Task InstallAsync(ModuleInstallationContext context)
        {
            await TrySaveSettingsAsync(new DrumRackConfigSettings());
            await base.InstallAsync(context);
        }

        public override async Task UninstallAsync()
        {
            await base.UninstallAsync();
        }
    }

    internal class Startup : StarterBase
    {
        public override void ConfigureServices(IServiceCollection services, IApplicationContext appContext)
        {
            services.Configure<MvcOptions>(o => o.Filters.Add<DrumRackCategoryFilter>());
        }
    }
}
