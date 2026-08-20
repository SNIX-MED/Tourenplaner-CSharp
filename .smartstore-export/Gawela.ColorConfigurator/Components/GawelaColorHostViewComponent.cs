using Microsoft.AspNetCore.Mvc;

namespace Gawela.ColorConfigurator.Components;

public sealed class GawelaColorHostViewComponent : ViewComponent
{
    public IViewComponentResult Invoke() => View();
}
