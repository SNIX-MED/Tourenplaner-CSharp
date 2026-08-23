using Smartstore.Core.Configuration;

namespace Gawela.RackConfig.Settings;

public class RackConfigSettings : ISettings
{
    public bool Enabled { get; set; } = true;
    public int CategoryId { get; set; }
    public string CategoryIds { get; set; } = string.Empty;
    public int MaxPalletWeight { get; set; } = 1000;
    public int DefaultDepth { get; set; } = 1100;
    public int MaxVariants { get; set; } = 2;
    public int MinLevelsLow { get; set; } = 2;
    public int MinLevelsHigh { get; set; } = 3;
    public string AccessoryMappingsJson { get; set; } = string.Empty;

    public int Spanplatte1825Id { get; set; }
    public int Spanplatte2700Id { get; set; }
    public int Spanplatte3600Id { get; set; }
    public int Gitterrost1825Id { get; set; }
    public int Gitterrost2700Id { get; set; }
    public int Gitterrost3600Id { get; set; }
    public int Stahlpanel1825Id { get; set; }
    public int Stahlpanel2700Id { get; set; }
    public int Stahlpanel3600Id { get; set; }
    public int Drahtgitter1825Id { get; set; }
    public int Drahtgitter2700Id { get; set; }
    public int Drahtgitter3600Id { get; set; }
    public int Durchschub1825Id { get; set; }
    public int Durchschub2700Id { get; set; }
    public int Durchschub3600Id { get; set; }
    public int EckRammschutzId { get; set; }
    public int MittelRammschutz76Id { get; set; }
    public int MittelRammschutz100Id { get; set; }
}
