using Gawela.RackConfig.Settings;
using Smartstore.Engine.Modularity;
using Smartstore.Http;

namespace Gawela.RackConfig;

public class Module : ModuleBase, IConfigurable
{
    public RouteInfo GetConfigurationRoute()
        => new("Configure", "RackConfigAdmin", new { area = "Admin" });

    public override async Task InstallAsync(ModuleInstallationContext context)
    {
        await TrySaveSettingsAsync(new RackConfigSettings());
        await base.InstallAsync(context);
    }

    // Settings are intentionally preserved on uninstall/update.
    public override async Task UninstallAsync()
    {
        await base.UninstallAsync();
    }
}
