using Gawela.RackConfig.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartstore.Core;
using Smartstore.Core.Checkout.Cart;
using Smartstore.Core.Data;
using Smartstore.Core.Seo;
using Smartstore.Web.Controllers;

namespace Gawela.RackConfig.Controllers;

public class PalletRackXController : PublicController
{
    private readonly SmartDbContext _db;
    private readonly RackConfigSettings _settings;
    private readonly IWorkContext _workContext;
    private readonly IShoppingCartService _shoppingCartService;

    public PalletRackXController(
        SmartDbContext db,
        RackConfigSettings settings,
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

        attributes.Set(RackConfigTracking.SourceKey, RackConfigTracking.SourceValue);
        attributes.Set(RackConfigTracking.TargetKey, type == ShoppingCartType.Wishlist ? "Offertanfrage" : "Warenkorb");
        attributes.Set(RackConfigTracking.VersionKey, RackConfigTracking.Version);
        attributes.Set(RackConfigTracking.MarkedOnUtcKey, DateTime.UtcNow.ToString("O"));
        attributes.Set(RackConfigTracking.QuantityKey, attributes.Get<int>(RackConfigTracking.QuantityKey) + qty);
        if (line > 0)
            attributes.Set(RackConfigTracking.LineKey, line.ToString());

        await attributes.SaveChangesAsync();

        if (type == ShoppingCartType.ShoppingCart && customer.GenericAttributes != null)
        {
            var pending = RackConfigTracking.ReadPending(customer.GenericAttributes.Get<string>(RackConfigTracking.PendingKey));
            pending.Add(new RackConfigPendingMarker
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

            customer.GenericAttributes.Set(RackConfigTracking.PendingKey, RackConfigTracking.WritePending(pending));
            await customer.GenericAttributes.SaveChangesAsync();
        }

        return Json(new
        {
            success = true,
            source = RackConfigTracking.SourceValue,
            cartItemId = item.Id,
            cartType = (int)type,
            line
        });
    }
}
