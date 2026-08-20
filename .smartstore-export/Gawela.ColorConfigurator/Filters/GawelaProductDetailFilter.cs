using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Smartstore.Core.Widgets;
using Smartstore.Web.Models.Catalog;
using Gawela.ColorConfigurator.Components;

namespace Gawela.ColorConfigurator.Filters;

public sealed class GawelaProductDetailFilter : IAsyncResultFilter
{
    private readonly IWidgetProvider _widgetProvider;

    public GawelaProductDetailFilter(IWidgetProvider widgetProvider)
    {
        _widgetProvider = widgetProvider;
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ViewResult { Model: ProductDetailsModel productModel })
        {
            context.HttpContext.Items[GawelaColorSeoViewComponent.ProductModelItemKey] = productModel;
        }

        _widgetProvider.RegisterViewComponent<GawelaColorHostViewComponent>(
            "productdetails_pictures_top",
            order: -1000);

        _widgetProvider.RegisterViewComponent<GawelaColorSeoViewComponent>(
            "productdetails_pictures_bottom",
            order: 1000);

        await next();
    }
}
