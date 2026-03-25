using System.Text;

namespace Tourenplaner.CSharp.Domain.Models;

public class SqlConnectionSettings
{
    public string Server { get; set; } = ".\\SQLEXPRESS";
    public string Database { get; set; } = "Business11";
    public bool UseWindowsAuthentication { get; set; } = true;
    public string UserId { get; set; } = "";
    public string Password { get; set; } = "";
    public int CommandTimeoutSeconds { get; set; } = 30;
    
    public string GetConnectionString()
    {
        var sb = new StringBuilder();
        sb.Append($"Server={Server};");
        sb.Append($"Database={Database};");

        if (UseWindowsAuthentication)
        {
            sb.Append("Integrated Security=true;");
        }
        else
        {
            sb.Append($"User Id={UserId};");
            sb.Append($"Password={Password};");
        }

        sb.Append("TrustServerCertificate=true;");
        sb.Append("Encrypt=false;");
        sb.Append($"Connection Timeout={CommandTimeoutSeconds};");

        return sb.ToString();
    }
}

// SQL Import Data Models
public class SqlOrderImportData
{
    public string AuftragNr { get; set; } = string.Empty;
    public string Typ { get; set; } = string.Empty;
    public DateTime AuftragsDatum { get; set; }
    public bool Archiviert { get; set; }
    public bool Gesperrt { get; set; }
    
    // Kundenadresse
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
    
    // Lieferadresse
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
    
    // Liefermethode
    public string Lieferbedingung { get; set; } = "Selbstabholung";
    
    // Positionen (Produkte)
    public List<SqlOrderProductData> Produkte { get; set; } = new();
    
    // Zusätzlich
    public decimal NettoTotal { get; set; }
    public decimal BruttoTotal { get; set; }
    public DateTime? Lieferdatum { get; set; }
    public string Notiz { get; set; } = string.Empty;
}

public class SqlOrderProductData
{
    public int PosNummer { get; set; }
    public string Bezeichnung { get; set; } = string.Empty;
    public decimal Menge { get; set; }
    public decimal Gewicht { get; set; }
    public decimal Bruttogewicht { get; set; }
}
