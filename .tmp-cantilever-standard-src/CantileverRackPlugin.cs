using System.Text.Json;
using Gawela.CantileverRackConfig.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Smartstore;
using Smartstore.Core;
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

namespace Gawela.CantileverRackConfig.Settings
{
    public class CantileverRackConfigSettings : ISettings
    {
        public bool Enabled { get; set; } = true;
        public int CategoryId { get; set; }
        public string CategoryIds { get; set; } = string.Empty;
        public int DefaultHeight { get; set; } = 2500;
        public int DefaultFieldWidth { get; set; } = 0;
        public int DefaultArmDepth { get; set; } = 600;
        public string DefaultSide { get; set; } = "single";
        public int MaxVariants { get; set; } = 2;
    }
}

namespace Gawela.CantileverRackConfig.Models
{
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
        public int DefaultFieldWidth { get; set; }
        public int DefaultArmDepth { get; set; }
        public string DefaultSide { get; set; } = "single";
        public int MaxVariants { get; set; }
        public List<EntityDisplayModel> SelectedCategories { get; set; } = [];
        public int[] CategorySelectedIds => CantileverRackMapping.ParseIds(CategoryIds);
    }

    public static class CantileverRackMapping
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

namespace Gawela.CantileverRackConfig.Components
{
    public class CantileverRackConfiguratorViewComponent : SmartViewComponent
    {
        private readonly CantileverRackConfigSettings _settings;
        public CantileverRackConfiguratorViewComponent(CantileverRackConfigSettings settings) => _settings = settings;
        public IViewComponentResult Invoke() => _settings.Enabled ? View(_settings) : Empty();
    }
}

namespace Gawela.CantileverRackConfig.Filters
{
    using Gawela.CantileverRackConfig.Components;
    using Gawela.CantileverRackConfig.Models;

    public sealed class CantileverRackCategoryFilter : IAsyncResultFilter
    {
        private const string SystemName = "Gawela.CantileverRackConfig";
        private readonly CantileverRackConfigSettings _settings;
        private readonly IWidgetProvider _widgetProvider;

        public CantileverRackCategoryFilter(CantileverRackConfigSettings settings, IWidgetProvider widgetProvider)
        {
            _settings = settings;
            _widgetProvider = widgetProvider;
        }

        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (ModularState.Instance.InstalledModules.Contains(SystemName) && _settings.Enabled && context.Result is ViewResult { Model: CategoryModel categoryModel })
            {
                var ids = CantileverRackMapping.ParseIds(_settings.CategoryIds);
                if (ids.Length == 0 && _settings.CategoryId > 0)
                    ids = [_settings.CategoryId];

                var path = context.HttpContext.Request.Path.Value ?? string.Empty;
                var active = ids.Contains(categoryModel.Id) || (ids.Length == 0 && path.Contains("kragarmregale-standard-ausfuehrung", StringComparison.OrdinalIgnoreCase));
                if (active)
                {
                    _widgetProvider.RegisterWidget("categorydetails_top", new ComponentWidget<CantileverRackConfiguratorViewComponent> { Key = SystemName });
                }
            }
            await next();
        }
    }
}

namespace Gawela.CantileverRackConfig.Controllers
{
    using Gawela.CantileverRackConfig.Models;

    [Area("Admin")]
    [AuthorizeAdmin]
    public class CantileverRackConfigAdminController : AdminController
    {
        private readonly SmartDbContext _db;
        private readonly CantileverRackConfigSettings _settings;

        public CantileverRackConfigAdminController(SmartDbContext db, CantileverRackConfigSettings settings)
        {
            _db = db;
            _settings = settings;
        }

        public async Task<IActionResult> Configure()
        {
            var categoryIds = CantileverRackMapping.NormalizeIds(_settings.CategoryIds);
            if (string.IsNullOrEmpty(categoryIds) && _settings.CategoryId > 0)
                categoryIds = _settings.CategoryId.ToString();

            var model = new ConfigurationModel
            {
                Enabled = _settings.Enabled,
                CategoryIds = categoryIds,
                DefaultHeight = _settings.DefaultHeight,
                DefaultFieldWidth = _settings.DefaultFieldWidth,
                DefaultArmDepth = _settings.DefaultArmDepth,
                DefaultSide = _settings.DefaultSide,
                MaxVariants = _settings.MaxVariants
            };
            await EnrichAsync(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            model.CategoryIds = CantileverRackMapping.NormalizeIds(model.CategoryIds);
            if (CantileverRackMapping.ParseIds(model.CategoryIds).Length == 0)
                ModelState.AddModelError(nameof(model.CategoryIds), "Bitte mindestens eine Warengruppe auswählen.");
            if (model.DefaultHeight is not (2000 or 2500 or 3000 or 3500 or 4000))
                ModelState.AddModelError(nameof(model.DefaultHeight), "Die Standardhöhe ist ungültig.");
            if (model.DefaultFieldWidth is not (0 or 1000 or 1280 or 1500))
                ModelState.AddModelError(nameof(model.DefaultFieldWidth), "Die Standard-Feldbreite ist ungültig.");
            if (model.DefaultArmDepth is not (400 or 500 or 600 or 800 or 1000 or 1200))
                ModelState.AddModelError(nameof(model.DefaultArmDepth), "Die Standard-Armtiefe ist ungültig.");
            if (model.DefaultSide is not ("single" or "double"))
                ModelState.AddModelError(nameof(model.DefaultSide), "Die Standard-Ausführung ist ungültig.");
            if (model.MaxVariants is < 1 or > 4)
                ModelState.AddModelError(nameof(model.MaxVariants), "Die Anzahl Varianten muss zwischen 1 und 4 liegen.");

            if (!ModelState.IsValid)
            {
                await EnrichAsync(model);
                return View(model);
            }

            _settings.Enabled = model.Enabled;
            _settings.CategoryIds = model.CategoryIds;
            _settings.CategoryId = CantileverRackMapping.ParseIds(model.CategoryIds).FirstOrDefault();
            _settings.DefaultHeight = model.DefaultHeight;
            _settings.DefaultFieldWidth = model.DefaultFieldWidth;
            _settings.DefaultArmDepth = model.DefaultArmDepth;
            _settings.DefaultSide = model.DefaultSide;
            _settings.MaxVariants = model.MaxVariants;
            await Services.SettingFactory.SaveSettingsAsync(_settings);
            NotifySuccess(T("Admin.Common.DataSuccessfullySaved"));
            return RedirectToAction(nameof(Configure));
        }

        private async Task EnrichAsync(ConfigurationModel model)
        {
            var ids = CantileverRackMapping.ParseIds(model.CategoryIds);
            if (ids.Length == 0) return;
            var categories = await _db.Categories.AsNoTracking()
                .Where(x => ids.Contains(x.Id))
                .Select(x => new EntityDisplayModel { Id = x.Id, Name = x.Name })
                .ToListAsync();
            model.SelectedCategories = ids
                .Select(id => categories.FirstOrDefault(x => x.Id == id) ?? new EntityDisplayModel { Id = id, Name = $"Warengruppe #{id}" })
                .ToList();
        }
    }

    public class CantileverRackXController : PublicController
    {
        private readonly SmartDbContext _db;
        private readonly CantileverRackConfigSettings _settings;
        private readonly IWorkContext _workContext;
        private readonly IShoppingCartService _shoppingCartService;

        public CantileverRackXController(SmartDbContext db, CantileverRackConfigSettings settings, IWorkContext workContext, IShoppingCartService shoppingCartService)
        {
            _db = db;
            _settings = settings;
            _workContext = workContext;
            _shoppingCartService = shoppingCartService;
        }

        public IActionResult Config() => View(_settings);

        public async Task<IActionResult> ProductPage(int id)
        {
            if (id <= 0) return NotFound();
            var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (product == null || product.Deleted) return NotFound();
            var slug = await product.GetActiveSlugAsync();
            return RedirectToRoute("Product", new { SeName = slug });
        }

        [HttpPost]
        public async Task<IActionResult> MarkSource(int productId, int cartType = 1, int lineIndex = -1, int quantity = 1, string? sku = null)
        {
            if (productId <= 0)
                return BadRequest(new { success = false, message = "Ungültige Produkt-ID." });

            var customer = _workContext.CurrentCustomer;
            if (customer == null || customer.Id <= 0)
                return BadRequest(new { success = false, message = "Kunde konnte nicht ermittelt werden." });

            var type = cartType == 2 ? ShoppingCartType.Wishlist : ShoppingCartType.ShoppingCart;
            var cart = await _shoppingCartService.GetCartAsync(customer, type, 0, null);
            var item = cart.Items.Select(x => x.Item)
                .Where(x => x.ParentItemId == null && x.ProductId == productId)
                .OrderByDescending(x => x.UpdatedOnUtc)
                .ThenByDescending(x => x.Id)
                .FirstOrDefault();

            if (item?.GenericAttributes == null)
                return NotFound(new { success = false, message = "Übertragene Warenkorbposition wurde nicht gefunden." });

            var qty = Math.Max(1, quantity);
            var line = lineIndex >= 0 ? lineIndex + 1 : 0;
            var attributes = item.GenericAttributes;
            attributes.Set(CantileverRackTracking.SourceKey, CantileverRackTracking.SourceValue);
            attributes.Set(CantileverRackTracking.TargetKey, type == ShoppingCartType.Wishlist ? "Offertanfrage" : "Warenkorb");
            attributes.Set(CantileverRackTracking.VersionKey, CantileverRackTracking.Version);
            attributes.Set(CantileverRackTracking.MarkedOnUtcKey, DateTime.UtcNow.ToString("O"));
            attributes.Set(CantileverRackTracking.QuantityKey, attributes.Get<int>(CantileverRackTracking.QuantityKey) + qty);
            if (line > 0) attributes.Set(CantileverRackTracking.LineKey, line.ToString());
            await attributes.SaveChangesAsync();

            if (type == ShoppingCartType.ShoppingCart && customer.GenericAttributes != null)
            {
                var pending = CantileverRackTracking.ReadPending(customer.GenericAttributes.Get<string>(CantileverRackTracking.PendingKey));
                pending.Add(new CantileverRackPendingMarker
                {
                    ProductId = item.ProductId,
                    Sku = string.IsNullOrWhiteSpace(sku) ? item.Product?.Sku : sku,
                    Quantity = qty,
                    Line = line,
                    StoreId = item.StoreId,
                    MarkedOnUtc = DateTime.UtcNow
                });
                if (pending.Count > 200) pending = pending.TakeLast(200).ToList();
                customer.GenericAttributes.Set(CantileverRackTracking.PendingKey, CantileverRackTracking.WritePending(pending));
                await customer.GenericAttributes.SaveChangesAsync();
            }

            return Json(new { success = true, source = CantileverRackTracking.SourceValue, cartItemId = item.Id, cartType = (int)type, line });
        }
    }
}

namespace Gawela.CantileverRackConfig
{
    using Gawela.CantileverRackConfig.Filters;

    public class Module : ModuleBase, IConfigurable
    {
        public RouteInfo GetConfigurationRoute() => new("Configure", "CantileverRackConfigAdmin", new { area = "Admin" });
        public override async Task InstallAsync(ModuleInstallationContext context)
        {
            await TrySaveSettingsAsync(new CantileverRackConfigSettings());
            await base.InstallAsync(context);
        }
        public override async Task UninstallAsync() => await base.UninstallAsync();
    }

    internal class Startup : StarterBase
    {
        public override void ConfigureServices(IServiceCollection services, IApplicationContext appContext)
        {
            services.Configure<MvcOptions>(o => o.Filters.Add<CantileverRackCategoryFilter>());
        }
    }

    internal static class CantileverRackTracking
    {
        public const string SourceKey = "Gawela.CantileverRackConfig.Source";
        public const string TargetKey = "Gawela.CantileverRackConfig.Target";
        public const string LineKey = "Gawela.CantileverRackConfig.Line";
        public const string QuantityKey = "Gawela.CantileverRackConfig.Quantity";
        public const string MarkedOnUtcKey = "Gawela.CantileverRackConfig.MarkedOnUtc";
        public const string VersionKey = "Gawela.CantileverRackConfig.Version";
        public const string PendingKey = "Gawela.CantileverRackConfig.PendingOrderMarkers";
        public const string SourceValue = "GAWELA Kragarmregal-Konfigurator Standard";
        public const string Version = "6.4.1";

        public static List<CantileverRackPendingMarker> ReadPending(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return [];
            try { return JsonSerializer.Deserialize<List<CantileverRackPendingMarker>>(json) ?? []; }
            catch { return []; }
        }

        public static string WritePending(IEnumerable<CantileverRackPendingMarker> markers) => JsonSerializer.Serialize(markers);
    }

    internal sealed class CantileverRackPendingMarker
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
            if (order == null || order.CustomerId <= 0) return;
            var customer = await db.Customers.FindByIdAsync(order.CustomerId, true, cancelToken);
            if (customer?.GenericAttributes == null) return;

            var pending = CantileverRackTracking.ReadPending(customer.GenericAttributes.Get<string>(CantileverRackTracking.PendingKey));
            if (pending.Count == 0) return;
            var earliest = order.CreatedOnUtc.AddHours(-2);
            var latest = DateTime.UtcNow.AddMinutes(5);
            var orderedProductIds = order.OrderItems.Select(x => x.ProductId).ToHashSet();
            var matched = pending.Where(x => x.StoreId == order.StoreId && orderedProductIds.Contains(x.ProductId) && x.MarkedOnUtc >= earliest && x.MarkedOnUtc <= latest).ToList();
            customer.GenericAttributes.Set(CantileverRackTracking.PendingKey, string.Empty);
            await customer.GenericAttributes.SaveChangesAsync(cancelToken);
            if (matched.Count == 0) return;

            var lines = matched.Where(x => x.Line > 0).Select(x => x.Line).Distinct().OrderBy(x => x).ToArray();
            var matchedIds = matched.Select(x => x.ProductId).ToHashSet();
            var itemSummary = order.OrderItems.Where(x => matchedIds.Contains(x.ProductId)).Select(x => $"{x.Sku} × {x.Quantity}").Distinct().ToArray();

            if (order.GenericAttributes != null)
            {
                order.GenericAttributes.Set(CantileverRackTracking.SourceKey, CantileverRackTracking.SourceValue);
                order.GenericAttributes.Set(CantileverRackTracking.VersionKey, CantileverRackTracking.Version);
                order.GenericAttributes.Set(CantileverRackTracking.LineKey, lines.Length > 0 ? string.Join(", ", lines) : "Konfigurator");
                order.GenericAttributes.Set(CantileverRackTracking.QuantityKey, matched.Sum(x => Math.Max(1, x.Quantity)));
                await order.GenericAttributes.SaveChangesAsync(cancelToken);
            }

            var lineText = lines.Length > 0 ? $"Regalzeile(n): {string.Join(", ", lines)}. " : string.Empty;
            var itemText = itemSummary.Length > 0 ? $"Artikel: {string.Join("; ", itemSummary)}." : string.Empty;
            db.OrderNotes.Add(order, $"Quelle: {CantileverRackTracking.SourceValue}. {lineText}{itemText} Tracking-Version {CantileverRackTracking.Version}.");
            await db.SaveChangesAsync(cancelToken);
        }
    }
}
