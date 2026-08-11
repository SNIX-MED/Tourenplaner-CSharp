namespace Gawela.ColorConfigurator.Models;

public sealed class GawelaProductGroupDocument
{
    public List<GawelaProductGroup> Groups { get; set; } = new();
}

public sealed class GawelaProductGroup
{
    public string Key { get; set; }
    public string Name { get; set; }
    public int MasterProductId { get; set; }
    public List<int> ProductIds { get; set; } = new();
}

public sealed class GawelaProductGroupAdminModel
{
    public string Key { get; set; }
    public string Name { get; set; }
    public int MasterProductId { get; set; }
    public string MasterSku { get; set; }
    public string MasterName { get; set; }
    public List<GawelaProductGroupMemberAdminModel> Members { get; set; } = new();
}

public sealed class GawelaProductGroupMemberAdminModel
{
    public int ProductId { get; set; }
    public string Sku { get; set; }
    public string ProductName { get; set; }
}
