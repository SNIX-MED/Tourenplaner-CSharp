using Smartstore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Smartstore.Engine;
using Smartstore.Engine.Builders;
using Smartstore.Web.Controllers;
using Gawela.ColorConfigurator.Filters;
using Gawela.ColorConfigurator.Services;

namespace Gawela.ColorConfigurator;

internal class Startup : StarterBase
{
    public override bool Matches(IApplicationContext appContext)
        => appContext.IsInstalled;

    public override void ConfigureServices(IServiceCollection services, IApplicationContext appContext)
    {
        services.Configure<MvcOptions>(o =>
        {
            o.Filters.AddEndpointFilter<GawelaProductDetailFilter, ProductController>()
                .ForAction(x => x.ProductDetails(0, null))
                .WhenNonAjax();
        });

        services.AddSingleton<GawelaAssetStore>();
        services.AddSingleton<GawelaPaletteStore>();
        services.AddSingleton<GawelaProductGroupStore>();
    }
}
