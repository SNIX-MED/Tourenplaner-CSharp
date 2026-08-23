using Gawela.RackConfig.Components;
using Gawela.RackConfig.Models;
using Gawela.RackConfig.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Smartstore.Core.Widgets;
using Smartstore.Engine.Modularity;
using Smartstore.Web.Models.Catalog;

namespace Gawela.RackConfig.Filters;

public sealed class RackCategoryFilter : IAsyncResultFilter
{
    private const string SystemName = "Gawela.RackConfig";
    private readonly RackConfigSettings _settings;
    private readonly IWidgetProvider _widgetProvider;

    public RackCategoryFilter(RackConfigSettings settings, IWidgetProvider widgetProvider)
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
            var ids = RackConfigMapping.ParseIds(_settings.CategoryIds);
            if (ids.Length == 0 && _settings.CategoryId > 0)
                ids = [_settings.CategoryId];

            var path = context.HttpContext.Request.Path.Value ?? string.Empty;
            var active = ids.Contains(categoryModel.Id) ||
                (ids.Length == 0 && path.Contains("palettenregale-schnell-ab-lager-lieferbar-3", StringComparison.OrdinalIgnoreCase));

            if (active)
            {
                _widgetProvider.RegisterWidget(
                    "categorydetails_top",
                    new ComponentWidget<RackConfiguratorViewComponent>() { Key = SystemName });
            }
        }

        await next();
    }
}
