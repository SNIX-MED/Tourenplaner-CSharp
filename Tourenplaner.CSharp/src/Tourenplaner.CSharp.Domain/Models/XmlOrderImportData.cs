namespace Tourenplaner.CSharp.Domain.Models;

/// <summary>Normalized order data parsed from the ERP XML export.</summary>
public class XmlOrderImportData
{
    public string AuftragNr { get; set; } = string.Empty;
    public string Typ { get; set; } = string.Empty;
    public DateTime AuftragsDatum { get; set; }
    public bool Archiviert { get; set; }
    public bool Gesperrt { get; set; }
    public string KundeFirma { get; set; } = string.Empty;
    public string KundeNachname { get; set; } = string.Empty;
    public string KundeVorname { get; set; } = string.Empty;
    public string KundeStrasse { get; set; } = string.Empty;
    public string KundeHausnummer { get; set; } = string.Empty;
    public string KundePLZ { get; set; } = string.Empty;
    public string KundeOrt { get; set; } = string.Empty;
    public string KundeLand { get; set; } = string.Empty;
    public string KundeEmail { get; set; } = string.Empty;
    public string KundeTelefon { get; set; } = string.Empty;
    public string KundeKontaktperson { get; set; } = string.Empty;
    public string KundeAdressNummer { get; set; } = string.Empty;
    public string LieferFirma { get; set; } = string.Empty;
    public string LieferNachname { get; set; } = string.Empty;
    public string LieferVorname { get; set; } = string.Empty;
    public string LieferStrasse { get; set; } = string.Empty;
    public string LieferHausnummer { get; set; } = string.Empty;
    public string LieferPLZ { get; set; } = string.Empty;
    public string LieferOrt { get; set; } = string.Empty;
    public string LieferLand { get; set; } = string.Empty;
    public string LieferEmail { get; set; } = string.Empty;
    public string LieferTelefon { get; set; } = string.Empty;
    public string LieferKontaktperson { get; set; } = string.Empty;
    public string LieferAdressNummer { get; set; } = string.Empty;
    public string Lieferbedingung { get; set; } = "Selbstabholung";
    public List<XmlOrderProductData> Produkte { get; set; } = new();
    public decimal NettoTotal { get; set; }
    public decimal BruttoTotal { get; set; }
    public DateTime? Lieferdatum { get; set; }
    public bool LieferungKannFrueherErfolgen { get; set; }
    public string Lieferzeit { get; set; } = string.Empty;
    public string Notiz { get; set; } = string.Empty;
    public bool IstVorauszahlung { get; set; }
}

public class XmlOrderProductData
{
    public int PosNummer { get; set; }
    public string ArtikelNummer { get; set; } = string.Empty;
    public string Bezeichnung { get; set; } = string.Empty;
    public decimal Menge { get; set; }
    public decimal Gewicht { get; set; }
    public decimal Bruttogewicht { get; set; }
}
