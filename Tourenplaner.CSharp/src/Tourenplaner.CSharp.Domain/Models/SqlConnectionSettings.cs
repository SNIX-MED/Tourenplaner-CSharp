using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace Tourenplaner.CSharp.Domain.Models;

public class SqlConnectionSettings
{
    public const string DefaultServer = @"DESKTOP-K4DH1NL\PROFITEX";
    public const string DefaultBusiness11DatabasePath = @"L:\Business11\Custom\Database\Business11";
    public const string DefaultBusiness11DatabaseFileName = "Business11.mdf";

    public string Server { get; set; } = DefaultServer;
    public string DatabasePath { get; set; } = DefaultBusiness11DatabasePath;
    public string Database { get; set; } = "Business11";
    public bool UseWindowsAuthentication { get; set; } = true;
    public string UserId { get; set; } = "";
    public string Password { get; set; } = "";
    public int CommandTimeoutSeconds { get; set; } = 30;
    public int ConnectionTimeoutSeconds { get; set; } = 4;
    
    public string GetConnectionString()
    {
        var server = string.IsNullOrWhiteSpace(Server) ? DefaultServer : Server.Trim();
        return BuildConnectionString(server, attachDbFile: ShouldAttachDatabaseFile(), connectionTimeoutSeconds: ConnectionTimeoutSeconds);
    }

    public string BuildConnectionString(string server, bool attachDbFile, int? connectionTimeoutSeconds = null)
    {
        var sb = new StringBuilder();
        sb.Append($"Server={server};");
        sb.Append($"Database={GetDatabaseName()};");

        if (attachDbFile)
        {
            sb.Append($"AttachDbFilename={GetDatabaseFilePath()};");
        }

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
        var timeout = connectionTimeoutSeconds ?? ConnectionTimeoutSeconds;
        sb.Append($"Connection Timeout={timeout};");

        return sb.ToString();
    }

    public string GetDatabaseName()
    {
        return string.IsNullOrWhiteSpace(Database) ? "Business11" : Database.Trim();
    }

    public string GetDatabaseFilePath()
    {
        var configuredPath = (DatabasePath ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = DefaultBusiness11DatabasePath;
        }

        return configuredPath.EndsWith(".mdf", StringComparison.OrdinalIgnoreCase)
            ? configuredPath
            : Path.Combine(configuredPath, DefaultBusiness11DatabaseFileName);
    }

    public bool HasDatabasePath()
    {
        return !string.IsNullOrWhiteSpace((DatabasePath ?? string.Empty).Trim());
    }

    public bool ShouldAttachDatabaseFile()
    {
        if (!HasDatabasePath())
        {
            return false;
        }

        var path = GetDatabaseFilePath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
        {
            return true;
        }

        try
        {
            var drive = new DriveInfo(root);
            return drive.DriveType == DriveType.Fixed;
        }
        catch
        {
            return false;
        }
    }

    public IEnumerable<string> GetConnectionAttemptDescriptions()
    {
        foreach (var server in GetServerCandidates().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return $"{server} / DB={GetDatabaseName()}";
            if (ShouldAttachDatabaseFile())
            {
                yield return $"{server} / DB={GetDatabaseName()} / Attach={GetDatabaseFilePath()}";
            }
        }
    }

    public IEnumerable<string> GetServerCandidates()
    {
        var explicitServer = (Server ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(explicitServer))
        {
            yield return explicitServer;
        }

        if (!string.Equals(explicitServer, @".\SQLEXPRESS", StringComparison.OrdinalIgnoreCase))
        {
            yield return @".\SQLEXPRESS";
        }

        if (!string.Equals(explicitServer, @"(LocalDB)\MSSQLLocalDB", StringComparison.OrdinalIgnoreCase))
        {
            yield return @"(LocalDB)\MSSQLLocalDB";
        }

        if (!string.Equals(explicitServer, @".", StringComparison.OrdinalIgnoreCase))
        {
            yield return @".";
        }
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
