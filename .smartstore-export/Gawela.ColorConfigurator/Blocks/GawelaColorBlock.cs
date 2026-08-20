using System.ComponentModel.DataAnnotations;
using Smartstore.Core.Content.Blocks;

namespace Gawela.ColorConfigurator.Blocks;

[Block(
    "gawelacolor",
    Icon = "fas fa-palette",
    FriendlyName = "GAWELA Farbkonfigurator",
    DisplayOrder = 20)]
public class GawelaColorBlockHandler : BlockHandlerBase<GawelaColorBlock>
{
}

public class GawelaColorBlock : IBlock
{
    [Display(Name = "Bezeichnung Korpus-Attribut")]
    public string CorpusAttributeLabel { get; set; } = "Farben Korpus/Gestell ML";

    [Display(Name = "Bezeichnung Tür-Attribut")]
    public string DoorsAttributeLabel { get; set; } = "Farben Türen/Schubladen ML";

    [Display(Name = "Basisfarbe Korpus (RAL)")]
    public string BaseCorpusRal { get; set; } = "7035";

    [Display(Name = "Basisfarbe Türen (RAL)")]
    public string BaseDoorsRal { get; set; } = "7035";

    [Display(Name = "Fallback Korpus (RAL)")]
    public string DefaultCorpusRal { get; set; } = "7035";

    [Display(Name = "Fallback Türen (RAL)")]
    public string DefaultDoorsRal { get; set; } = "7035";

    [Display(Name = "Normale Smartstore-Galerie ersetzen")]
    public bool ReplaceGallery { get; set; }

    [Display(Name = "RAL / RGB / HEX / NCS anzeigen")]
    public bool ShowColorData { get; set; } = true;

    [Display(Name = "Hinweis zur Bildschirmdarstellung anzeigen")]
    public bool ShowDisclaimer { get; set; } = true;
}
