namespace Tourenplaner.CSharp.Domain.Models;

public sealed class PinDataRecord
{
    public string Typ { get; set; } = string.Empty;
    public string ImportID { get; set; } = string.Empty;
    public string Auftragsnummer { get; set; } = string.Empty;
    public string Bestelldatum { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Strasse { get; set; } = string.Empty;
    public string PLZ { get; set; } = string.Empty;
    public string Ort { get; set; } = string.Empty;
    public string Land { get; set; } = string.Empty;
    public string AuftragName { get; set; } = string.Empty;
    public string AuftragPLZ { get; set; } = string.Empty;
    public string AuftragOrt { get; set; } = string.Empty;
    public string AuftragsadresseName { get; set; } = string.Empty;
    public string AuftragsadressePLZ { get; set; } = string.Empty;
    public string AuftragsadresseOrt { get; set; } = string.Empty;
    public string LieferadresseName { get; set; } = string.Empty;
    public string LieferadresseStrasse { get; set; } = string.Empty;
    public string LieferadressePLZ { get; set; } = string.Empty;
    public string LieferadresseOrt { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Telefon { get; set; } = string.Empty;
    public string Gewicht { get; set; } = string.Empty;
    public string Auftragsgewicht { get; set; } = string.Empty;
    public string Produkte { get; set; } = string.Empty;
    public string ProduktgewichtTotal { get; set; } = string.Empty;
    public string Notizen { get; set; } = string.Empty;
    public string Liefercode { get; set; } = string.Empty;
    public string Lieferart { get; set; } = string.Empty;
    public string NichtKarteKategorie { get; set; } = string.Empty;
    public string Status { get; set; } = "nicht festgelegt";
    public double? Lat { get; set; }
    public double? Lng { get; set; }
}
