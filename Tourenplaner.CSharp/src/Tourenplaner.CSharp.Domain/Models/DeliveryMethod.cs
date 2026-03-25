namespace Tourenplaner.CSharp.Domain.Models;

/// <summary>
/// Liefermethoden für die Klassifizierung von Aufträgen in Map/NonMap
/// </summary>
public enum DeliveryMethodType
{
    // Karte (Map Orders) - erhalten Pin auf der Karte
    Fracht_o_vert,           // Frei Bordsteinkante
    Fracht_m_vert,           // Mit Verteilung
    Fracht_m_vert_mont,      // Mit Verteilung & Montage
    
    // Keine Karte (Non-Map Orders)
    Selbstabholung,          // Selbstabholung
    Fracht_mit_Spediteur,    // Fracht mit Spediteur
    Fracht_Tresor_Bordstein, // Fracht-Tresor-Bordstein
    Fracht_Tresor_Verwendung // Fracht-Tresor-Verwendung
}

public static class DeliveryMethodExtensions
{
    public const string FreiBordsteinkante = "Frei Bordsteinkante";
    public const string MitVerteilung = "Mit Verteilung";
    public const string MitVerteilungMontage = "Mit Verteilung & Montage";
    public const string Spediteur = "Spediteur";
    public const string Post = "Post";
    public const string TresorBordstein = "Tresor-Bordstein";
    public const string TresorVerwendung = "Tresor-Verwendung";
    public const string SelbstabholungLabel = "Selbstabholung";

    public static readonly IReadOnlyList<string> MapDeliveryTypeOptions =
    [
        FreiBordsteinkante,
        MitVerteilung,
        MitVerteilungMontage
    ];

    public static readonly IReadOnlyList<string> NonMapDeliveryTypeOptions =
    [
        Spediteur,
        Post,
        TresorBordstein,
        TresorVerwendung,
        SelbstabholungLabel
    ];

    /// <summary>
    /// Gibt an, ob die Liefermethode einen Map-Pin erhalten soll
    /// </summary>
    public static bool IsMapOrder(this DeliveryMethodType method) => method switch
    {
        DeliveryMethodType.Fracht_o_vert or
        DeliveryMethodType.Fracht_m_vert or
        DeliveryMethodType.Fracht_m_vert_mont => true,
        _ => false
    };

    /// <summary>
    /// Konvertiert einen String in DeliveryMethodType
    /// </summary>
    public static DeliveryMethodType ParseDeliveryMethod(string? method)
    {
        if (string.IsNullOrWhiteSpace(method))
            return DeliveryMethodType.Selbstabholung;

        var normalized = method
            .Replace("&", "und")
            .Replace("-", "_")
            .Replace(" ", "_")
            .ToLowerInvariant()
            .Trim();

        return normalized switch
        {
            "frei_bordsteinkante" => DeliveryMethodType.Fracht_o_vert,
            "mit_verteilung" => DeliveryMethodType.Fracht_m_vert,
            "mit_verteilung_und_montage" => DeliveryMethodType.Fracht_m_vert_mont,
            "fracht_o_vert" => DeliveryMethodType.Fracht_o_vert,
            "fracht_m_vert" => DeliveryMethodType.Fracht_m_vert,
            "fracht_m_vert_mont" => DeliveryMethodType.Fracht_m_vert_mont,
            "spediteur" => DeliveryMethodType.Fracht_mit_Spediteur,
            "post" => DeliveryMethodType.Fracht_mit_Spediteur,
            "tresor_bordstein" => DeliveryMethodType.Fracht_Tresor_Bordstein,
            "tresor_verwendung" => DeliveryMethodType.Fracht_Tresor_Verwendung,
            "selbstabholung" => DeliveryMethodType.Selbstabholung,
            "fracht_mit_spediteur" => DeliveryMethodType.Fracht_mit_Spediteur,
            "fracht_tresor_bordstein" => DeliveryMethodType.Fracht_Tresor_Bordstein,
            "fracht_tresor_verwendung" => DeliveryMethodType.Fracht_Tresor_Verwendung,
            _ => DeliveryMethodType.Selbstabholung
        };
    }

    public static string NormalizeDeliveryTypeLabel(string? value)
    {
        var normalized = ParseDeliveryMethod(value);
        var raw = value?.Trim() ?? string.Empty;
        return normalized switch
        {
            DeliveryMethodType.Fracht_o_vert => FreiBordsteinkante,
            DeliveryMethodType.Fracht_m_vert => MitVerteilung,
            DeliveryMethodType.Fracht_m_vert_mont => MitVerteilungMontage,
            DeliveryMethodType.Fracht_mit_Spediteur =>
                raw.Equals(Post, StringComparison.OrdinalIgnoreCase) ? Post : Spediteur,
            DeliveryMethodType.Fracht_Tresor_Bordstein => TresorBordstein,
            DeliveryMethodType.Fracht_Tresor_Verwendung => TresorVerwendung,
            _ => SelbstabholungLabel
        };
    }
}
