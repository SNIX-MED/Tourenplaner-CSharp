using Smartstore.Engine.Modularity;
using Smartstore.Http;

namespace Gawela.ColorConfigurator;

internal class Module : ModuleBase, IConfigurable
{
    public RouteInfo GetConfigurationRoute()
        => new("Configure", "GawelaColorAdmin", new { area = "Admin" });
}
