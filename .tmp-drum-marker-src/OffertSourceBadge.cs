using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Smartstore;
using Smartstore.Core.Checkout.Cart;
using Smartstore.Core.Data;
using Smartstore.Core.Widgets;
using Smartstore.Engine;
using Smartstore.Engine.Builders;
using Smartstore.Engine.Modularity;
using Smartstore.Web.Components;
using Smartstore.Web.Models.Cart;

namespace Gawela.DrumRackConfig.Components
{
    public sealed class DrumRackSourceBadgeModel
    {
        public int MarkedPositions { get; set; }
        public int MarkedQuantity { get; set; }
        public string[] Lines { get; set; } = [];
        public string[] Skus { get; set; } = [];
    }

    public class DrumRackSourceBadgeViewComponent : SmartViewComponent
    {
        private readonly SmartDbContext _db;
        private readonly IShoppingCartService _shoppingCartService;

        public DrumRackSourceBadgeViewComponent(
            SmartDbContext db,
            IShoppingCartService shoppingCartService)
        {
            _db = db;
            _shoppingCartService = shoppingCartService;
        }

        public async Task<IViewComponentResult> InvokeAsync(WishlistModel model)
        {
            if (model == null || model.CustomerGuid == Guid.Empty || !model.Items.Any())
                return Empty();

            var customer = await _db.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CustomerGuid == model.CustomerGuid);

            if (customer == null)
                return Empty();

            var cart = await _shoppingCartService.GetCartAsync(customer, ShoppingCartType.Wishlist, 0, null);
            var visibleItemIds = model.Items.Select(x => x.Id).ToHashSet();

            var marked = cart.Items
                .Select(x => x.Item)
                .Where(x =>
                    x.ParentItemId == null &&
                    visibleItemIds.Contains(x.Id) &&
                    x.GenericAttributes != null &&
                    string.Equals(
                        x.GenericAttributes.Get<string>(DrumRackTracking.SourceKey),
                        DrumRackTracking.SourceValue,
                        StringComparison.Ordinal))
                .ToList();

            if (marked.Count == 0)
                return Empty();

            var lines = marked
                .Select(x => x.GenericAttributes?.Get<string>(DrumRackTracking.LineKey))
                .Where(x => !string.IsNullOrWhiteSpace(x) && x != "0")
                .Select(x => x!)
                .Distinct()
                .OrderBy(x => x)
                .ToArray();

            var skus = marked
                .Select(x => x.Product?.Sku)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return View(new DrumRackSourceBadgeModel
            {
                MarkedPositions = marked.Count,
                MarkedQuantity = marked.Sum(x => Math.Max(1, x.GenericAttributes?.Get<int>(DrumRackTracking.QuantityKey) ?? x.Quantity)),
                Lines = lines,
                Skus = skus
            });
        }
    }
}

namespace Gawela.DrumRackConfig.Filters
{
    using Gawela.DrumRackConfig.Components;

    public sealed class DrumRackWishlistSourceFilter : IAsyncResultFilter
    {
        private const string SystemName = "Gawela.DrumRackConfig";
        private readonly IWidgetProvider _widgetProvider;

        public DrumRackWishlistSourceFilter(IWidgetProvider widgetProvider)
            => _widgetProvider = widgetProvider;

        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (ModularState.Instance.InstalledModules.Contains(SystemName) &&
                context.Result is ViewResult { Model: WishlistModel })
            {
                _widgetProvider.RegisterWidget(
                    "wishlist_items_top",
                    new ComponentWidget<DrumRackSourceBadgeViewComponent>
                    {
                        Key = SystemName + ".SourceBadge",
                        Order = -1000,
                        Prepend = true
                    });
            }

            await next();
        }
    }
}

namespace Gawela.DrumRackConfig
{
    using Gawela.DrumRackConfig.Filters;

    internal sealed class WishlistTrackingStartup : StarterBase
    {
        public override void ConfigureServices(IServiceCollection services, IApplicationContext appContext)
        {
            services.Configure<MvcOptions>(o => o.Filters.Add<DrumRackWishlistSourceFilter>());
        }
    }
}
