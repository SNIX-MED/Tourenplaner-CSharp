using Gawela.RackConfig.Settings;
using Microsoft.AspNetCore.Mvc;
using Smartstore.Web.Components;

namespace Gawela.RackConfig.Components;

public class RackConfiguratorViewComponent : SmartViewComponent
{
    private readonly RackConfigSettings _settings;
    public RackConfiguratorViewComponent(RackConfigSettings settings) => _settings = settings;

    // IMPORTANT: parameterless by design. Smartstore OutputCache serializes ViewComponent
    // invocation arguments. Passing CategoryModel here would include CatalogSearchResult and
    // break cache deserialization on the next request.
    public IViewComponentResult Invoke()
    {
        if (!_settings.Enabled)
            return Empty();
        return View(_settings);
    }
}
