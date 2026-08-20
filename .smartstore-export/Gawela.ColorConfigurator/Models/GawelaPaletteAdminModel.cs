namespace Gawela.ColorConfigurator.Models;

public sealed class GawelaPaletteAdminModel
{
    public List<GawelaRalColorAdminModel> Colors { get; set; } = new();
}

public sealed class GawelaRalColorAdminModel
{
    public string Ral { get; set; }
    public string Name { get; set; }
    public string Hex { get; set; }
    public int R { get; set; }
    public int G { get; set; }
    public int B { get; set; }
}
