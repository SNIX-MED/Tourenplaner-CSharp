using Microsoft.Data.SqlClient;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Services;

public interface ISqlServerOrderService
{
    Task<List<SqlOrderImportData>> GetNewAndUpdatedOrdersAsync(DateTime? lastImportDate);
    Task TestConnectionAsync();
}

public class SqlServerOrderService : ISqlServerOrderService
{
    private readonly SqlConnectionSettings _settings;

    private sealed record DeliveryRule(string Label, DeliveryMethodType Type, int Priority);

    // Hidden/internal mapping from WW_Pos.ArtikelID to delivery type.
    private static readonly Dictionary<string, DeliveryRule> DeliveryRuleByArticleId =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Map orders
            ["9314dcad-f0ed-11eb-8a5a-40b076de1f8f"] = new(DeliveryMethodExtensions.FreiBordsteinkante, DeliveryMethodType.Fracht_o_vert, 400),
            ["1be04ec6-080a-11ec-8a64-40b076de1f8f"] = new(DeliveryMethodExtensions.MitVerteilung, DeliveryMethodType.Fracht_m_vert, 500),
            ["57E8F414-080C-11EC-8A64-40B076DE1F8F"] = new(DeliveryMethodExtensions.MitVerteilungMontage, DeliveryMethodType.Fracht_m_vert_mont, 900),
            ["f874e760-16f4-11ec-8a67-40b076de1f8f"] = new(DeliveryMethodExtensions.MitVerteilungMontage, DeliveryMethodType.Fracht_m_vert_mont, 900),

            // Force-upgrade to montage
            ["973d1a2f-155b-11ec-8a65-40b076de1f8f"] = new(DeliveryMethodExtensions.MitVerteilungMontage, DeliveryMethodType.Fracht_m_vert_mont, 1000),

            // Non-map orders
            ["55db6564-16f1-11ec-8a67-40b076de1f8f"] = new(DeliveryMethodExtensions.Spediteur, DeliveryMethodType.Fracht_mit_Spediteur, 700),
            ["3259a7b5-0810-11ec-8a64-40b076de1f8f"] = new(DeliveryMethodExtensions.Post, DeliveryMethodType.Fracht_mit_Spediteur, 700),
            ["EE37146F-0810-11EC-8A64-40B076DE1F8F"] = new(DeliveryMethodExtensions.TresorBordstein, DeliveryMethodType.Fracht_Tresor_Bordstein, 700),
            ["BAE3AFC3-0813-11EC-8A64-40B076DE1F8F"] = new(DeliveryMethodExtensions.TresorVerwendung, DeliveryMethodType.Fracht_Tresor_Verwendung, 700),
            ["D06B905E-16F7-11EC-8A67-40B076DE1F8F"] = new(DeliveryMethodExtensions.SelbstabholungLabel, DeliveryMethodType.Selbstabholung, 300)
        };

    private static readonly string[] ArticleIdColumnCandidates =
    [
        "ArtikelID",
        "ArtikelId",
        "ArtikelIdent",
        "ArticleID",
        "ArticleId"
    ];

    private static readonly string[] ContactNumberColumnCandidates =
    [
        "Nummer",
        "Number",
        "Kontakt",
        "Kontaktwert"
    ];

    private static readonly string[] StammKontaktAddressKeyCandidates =
    [
        "AdressID",
        "AdresseID",
        "AddressID",
        "StammID",
        "StammIdent",
        "AdressIdent",
        "AdresseIdent"
    ];

    public SqlServerOrderService(SqlConnectionSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task TestConnectionAsync()
    {
        using var connection = new SqlConnection(_settings.GetConnectionString());
        try
        {
            await connection.OpenAsync();
        }
        catch (SqlException ex)
        {
            throw new InvalidOperationException(
                $"SQL Server Verbindung fehlgeschlagen: {ex.Message}", ex);
        }
    }

    public async Task<List<SqlOrderImportData>> GetNewAndUpdatedOrdersAsync(DateTime? lastImportDate)
    {
        var orders = new List<SqlOrderImportData>();
        var whereClause = lastImportDate.HasValue
            ? """
              AND (
                    COALESCE(
                        TRY_CONVERT(date, K.Datum, 104), -- dd.MM.yyyy
                        TRY_CONVERT(date, K.Datum, 120), -- yyyy-MM-dd HH:mi:ss
                        TRY_CONVERT(date, K.Datum, 23),  -- yyyy-MM-dd
                        TRY_CONVERT(date, K.Datum)       -- fallback / already date
                    ) >= @LastImportDate
                  )
              """
            : string.Empty;

        using var connection = new SqlConnection(_settings.GetConnectionString());
        await connection.OpenAsync();

        var availableArticleIdColumns = await ResolveAvailableArticleIdColumnsAsync(connection);
        if (availableArticleIdColumns.Count == 0)
        {
            throw new InvalidOperationException(
                "SQL Import abgebrochen: In Tabelle WW_Pos wurde keine ArtikelID-Spalte gefunden " +
                "(erwartet z.B. 'ArtikelID').");
        }

        var articleIdSelect = BuildArticleIdSelectExpression(availableArticleIdColumns);
        var stammKontaktAddressColumn = await ResolveStammKontaktAddressReferenceColumnAsync(connection);
        var kontaktNummerColumn = await ResolveKontaktNumberColumnAsync(connection);
        var customerEmailSelect = BuildStammKontaktSelectExpression(
            "COALESCE(A.Ident, K.AdressID)",
            stammKontaktAddressColumn,
            kontaktNummerColumn,
            KontaktValueType.Email,
            "KundeEmail");
        var customerPhoneSelect = BuildStammKontaktSelectExpression(
            "COALESCE(A.Ident, K.AdressID)",
            stammKontaktAddressColumn,
            kontaktNummerColumn,
            KontaktValueType.Phone,
            "KundeTelefon");
        var deliveryEmailSelect = BuildStammKontaktSelectExpression(
            "COALESCE(LA.Ident, K.LieferadressID)",
            stammKontaktAddressColumn,
            kontaktNummerColumn,
            KontaktValueType.Email,
            "LieferEmail");
        var deliveryPhoneSelect = BuildStammKontaktSelectExpression(
            "COALESCE(LA.Ident, K.LieferadressID)",
            stammKontaktAddressColumn,
            kontaktNummerColumn,
            KontaktValueType.Phone,
            "LieferTelefon");
        var personNameSelects = await BuildPersonNameSelectExpressionsAsync(connection);

        var query = $@"
            SELECT
                K.Kopf, K.Typ, K.Datum, K.Archiv, K.Sperre,
                K.Lieferdatum, K.Nettototal, K.Bruttototal, K.Notiz,
                A.Firma, A.Nachname, A.Vorname, A.Strasse, A.Hausnummer,
                A.PLZ, A.Ort, A.Land,
                LA.Firma AS LieferFirma, LA.Nachname AS LieferNachname,
                LA.Vorname AS LieferVorname, LA.Strasse AS LieferStrasse,
                LA.Hausnummer AS LieferHausnummer, LA.PLZ AS LieferPLZ,
                LA.Ort AS LieferOrt, LA.Land AS LieferLand,
                P.Position, P.Bezeichnung, P.Menge, P.Gewicht, P.Bruttogewicht,
                {articleIdSelect},
                {customerEmailSelect},
                {customerPhoneSelect},
                {deliveryEmailSelect},
                {deliveryPhoneSelect},
                {personNameSelects.CustomerExpression},
                {personNameSelects.DeliveryExpression}
            FROM WW_Kopf K
            LEFT JOIN AVE_Stamm A ON K.AdressID = A.Ident
            LEFT JOIN AVE_Stamm LA ON K.LieferadressID = LA.Ident
            LEFT JOIN WW_Pos P ON K.Ident = P.KopfID
            WHERE K.Typ = 'SALES'
              AND K.Archiv = 0
              AND K.Sperre = 0
              {whereClause}
            ORDER BY K.Kopf, P.Position";

        using var command = new SqlCommand(query, connection)
        {
            CommandTimeout = _settings.CommandTimeoutSeconds
        };
        if (lastImportDate.HasValue)
        {
            command.Parameters.AddWithValue("@LastImportDate", lastImportDate.Value.Date);
        }

        using var reader = await command.ExecuteReaderAsync();
        SqlOrderImportData? currentOrder = null;
        var currentDeliverySignals = new List<(string? ArticleId, string Description)>();
        var currentPositions = new List<(int Position, string Bezeichnung, decimal Menge, decimal Gewicht, decimal Bruttogewicht, string? ArticleId)>();

        while (await reader.ReadAsync())
        {
            var auftragNr = ReadString(reader, 0);

            if (currentOrder != null && !string.Equals(currentOrder.AuftragNr, auftragNr, StringComparison.Ordinal))
            {
                FinalizeOrder(currentOrder, currentDeliverySignals, currentPositions);
                currentDeliverySignals.Clear();
                currentPositions.Clear();
            }

            if (currentOrder == null || !string.Equals(currentOrder.AuftragNr, auftragNr, StringComparison.Ordinal))
            {
                currentOrder = new SqlOrderImportData
                {
                    AuftragNr = auftragNr,
                    Typ = ReadString(reader, 1),
                    AuftragsDatum = ReadDateTime(reader, 2),
                    Archiviert = reader.GetBoolean(3),
                    Gesperrt = reader.GetBoolean(4),
                    Lieferdatum = reader.IsDBNull(5) ? null : ReadDateTime(reader, 5),
                    NettoTotal = ReadDecimal(reader, 6),
                    BruttoTotal = ReadDecimal(reader, 7),
                    Notiz = ReadString(reader, 8),
                    KundeFirma = ReadString(reader, 9),
                    KundeNachname = ReadString(reader, 10),
                    KundeVorname = ReadString(reader, 11),
                    KundeStrasse = ReadString(reader, 12),
                    KundeHausnummer = ReadString(reader, 13),
                    KundePLZ = ReadString(reader, 14),
                    KundeOrt = ReadString(reader, 15),
                    KundeLand = ReadString(reader, 16),
                    LieferFirma = ReadString(reader, 17),
                    LieferNachname = ReadString(reader, 18),
                    LieferVorname = ReadString(reader, 19),
                    LieferStrasse = ReadString(reader, 20),
                    LieferHausnummer = ReadString(reader, 21),
                    LieferPLZ = ReadString(reader, 22),
                    LieferOrt = ReadString(reader, 23),
                    LieferLand = ReadString(reader, 24),
                    Lieferbedingung = DeliveryMethodExtensions.SelbstabholungLabel,
                    KundeEmail = ReadString(reader, 31),
                    KundeTelefon = ReadString(reader, 32),
                    LieferEmail = ReadString(reader, 33),
                    LieferTelefon = ReadString(reader, 34),
                    KundeKontaktperson = ReadString(reader, 35),
                    LieferKontaktperson = ReadString(reader, 36)
                };
                orders.Add(currentOrder);
            }

            if (!reader.IsDBNull(25))
            {
                var position = ReadInt(reader, 25);
                var bezeichnung = ReadString(reader, 26);
                var menge = ReadDecimal(reader, 27);
                var gewicht = ReadDecimal(reader, 28);
                var bruttogewicht = ReadDecimal(reader, 29);
                var articleId = ReadNullableString(reader, 30);

                currentDeliverySignals.Add((articleId, bezeichnung));
                currentPositions.Add((position, bezeichnung, menge, gewicht, bruttogewicht, articleId));
            }
        }

        if (currentOrder != null)
        {
            FinalizeOrder(currentOrder, currentDeliverySignals, currentPositions);
        }

        return orders;
    }

    private static async Task<List<string>> ResolveAvailableArticleIdColumnsAsync(SqlConnection connection)
    {
        const string sql = """
                           SELECT TOP 1 1
                           FROM sys.columns c
                           INNER JOIN sys.objects o ON c.object_id = o.object_id
                           WHERE o.name = 'WW_Pos' AND c.name = @ColumnName
                           """;

        var result = new List<string>();
        foreach (var candidate in ArticleIdColumnCandidates)
        {
            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@ColumnName", candidate);
            var exists = await command.ExecuteScalarAsync();
            if (exists is not null)
            {
                result.Add(candidate);
            }
        }

        return result;
    }

    private static async Task<string?> ResolveStammKontaktAddressReferenceColumnAsync(SqlConnection connection)
    {
        const string fkSql = """
                             SELECT TOP 1 parentCol.name
                             FROM sys.foreign_keys fk
                             INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
                             INNER JOIN sys.tables parentTbl ON fk.parent_object_id = parentTbl.object_id
                             INNER JOIN sys.tables refTbl ON fk.referenced_object_id = refTbl.object_id
                             INNER JOIN sys.columns parentCol ON parentCol.object_id = fkc.parent_object_id AND parentCol.column_id = fkc.parent_column_id
                             WHERE parentTbl.name = 'AVE_StammKontakt'
                               AND refTbl.name = 'AVE_Stamm'
                             ORDER BY parentCol.name
                             """;

        using (var fkCommand = new SqlCommand(fkSql, connection))
        {
            var fkColumn = await fkCommand.ExecuteScalarAsync();
            if (fkColumn is string fkColumnName && !string.IsNullOrWhiteSpace(fkColumnName))
            {
                return fkColumnName;
            }
        }

        const string colSql = """
                              SELECT TOP 1 1
                              FROM sys.columns c
                              INNER JOIN sys.objects o ON c.object_id = o.object_id
                              WHERE o.name = 'AVE_StammKontakt' AND c.name = @ColumnName
                              """;

        foreach (var candidate in StammKontaktAddressKeyCandidates)
        {
            using var command = new SqlCommand(colSql, connection);
            command.Parameters.AddWithValue("@ColumnName", candidate);
            var exists = await command.ExecuteScalarAsync();
            if (exists is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task<string?> ResolveKontaktNumberColumnAsync(SqlConnection connection)
    {
        const string sql = """
                           SELECT TOP 1 1
                           FROM sys.columns c
                           INNER JOIN sys.objects o ON c.object_id = o.object_id
                           WHERE o.name = 'AVE_StammKontakt' AND c.name = @ColumnName
                           """;

        foreach (var candidate in ContactNumberColumnCandidates)
        {
            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@ColumnName", candidate);
            var exists = await command.ExecuteScalarAsync();
            if (exists is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task<(string CustomerExpression, string DeliveryExpression)> BuildPersonNameSelectExpressionsAsync(SqlConnection connection)
    {
        var hasPersonLinkTable = await TableExistsAsync(connection, "AVE_PersonLink");
        var hasPersonTable = await TableExistsAsync(connection, "AVE_Person");
        if (!hasPersonLinkTable || !hasPersonTable)
        {
            return (
                "CAST(NULL AS nvarchar(256)) AS KundeKontaktperson",
                "CAST(NULL AS nvarchar(256)) AS LieferKontaktperson");
        }

        var hasLinkAddressId = await ColumnExistsAsync(connection, "AVE_PersonLink", "AdressID");
        var hasLinkPersonId = await ColumnExistsAsync(connection, "AVE_PersonLink", "PersonID");
        var hasPersonIdent = await ColumnExistsAsync(connection, "AVE_Person", "Ident");
        var hasPersonName = await ColumnExistsAsync(connection, "AVE_Person", "Name");
        var hasPersonArchiv = await ColumnExistsAsync(connection, "AVE_Person", "Archiv");
        if (!hasLinkAddressId || !hasLinkPersonId || !hasPersonIdent || !hasPersonName)
        {
            return (
                "CAST(NULL AS nvarchar(256)) AS KundeKontaktperson",
                "CAST(NULL AS nvarchar(256)) AS LieferKontaktperson");
        }

        var customerExpr = BuildPersonNameSelectExpression(
            "COALESCE(A.Ident, K.AdressID)",
            hasPersonArchiv,
            "KundeKontaktperson");
        var deliveryExpr = BuildPersonNameSelectExpression(
            "COALESCE(LA.Ident, K.LieferadressID)",
            hasPersonArchiv,
            "LieferKontaktperson");

        return (customerExpr, deliveryExpr);
    }

    private static async Task<bool> TableExistsAsync(SqlConnection connection, string tableName)
    {
        const string sql = """
                           SELECT TOP 1 1
                           FROM sys.objects
                           WHERE type = 'U' AND name = @TableName
                           """;

        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TableName", tableName);
        return await command.ExecuteScalarAsync() is not null;
    }

    private static async Task<bool> ColumnExistsAsync(SqlConnection connection, string tableName, string columnName)
    {
        const string sql = """
                           SELECT TOP 1 1
                           FROM sys.columns c
                           INNER JOIN sys.objects o ON c.object_id = o.object_id
                           WHERE o.name = @TableName AND c.name = @ColumnName
                           """;

        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@ColumnName", columnName);
        return await command.ExecuteScalarAsync() is not null;
    }

    private static string BuildArticleIdSelectExpression(IReadOnlyList<string> columns)
    {
        var coalesce = string.Join(", ", columns.Select(x => $"CONVERT(nvarchar(128), P.[{x}])"));
        return $"COALESCE({coalesce}) AS PositionArticleId";
    }

    private enum KontaktValueType
    {
        Email,
        Phone
    }

    private static string BuildStammKontaktSelectExpression(
        string addressIdExpression,
        string? stammKontaktAddressColumn,
        string? kontaktNummerColumn,
        KontaktValueType valueType,
        string outputAlias)
    {
        if (string.IsNullOrWhiteSpace(stammKontaktAddressColumn) || string.IsNullOrWhiteSpace(kontaktNummerColumn))
        {
            return $"CAST(NULL AS nvarchar(256)) AS {outputAlias}";
        }

        var rawValue = $"CONVERT(nvarchar(256), SK.[{kontaktNummerColumn}])";
        var trimmedValue = $"NULLIF(LTRIM(RTRIM({rawValue})), '')";
        var cleanedDigits = BuildDigitCleanSql(rawValue);

        var whereCondition = valueType == KontaktValueType.Email
            ? $"CHARINDEX('@', {rawValue}) > 0"
            : $"CHARINDEX('@', {rawValue}) = 0 AND {cleanedDigits} <> '' AND {cleanedDigits} NOT LIKE '%[^0-9]%'";

        return $"""
                (
                    SELECT TOP 1 {trimmedValue}
                    FROM AVE_StammKontakt SK
                    WHERE SK.[{stammKontaktAddressColumn}] = {addressIdExpression}
                      AND {whereCondition}
                    ORDER BY ISNULL(SK.Sortierung, 0), SK.Ident
                ) AS {outputAlias}
                """;
    }

    private static string BuildDigitCleanSql(string sqlValueExpression)
    {
        return $"REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE({sqlValueExpression}, ' ', ''), '+', ''), '-', ''), '/', ''), '(', ''), ')', ''), '.', ''), CHAR(9), ''), CHAR(160), '')";
    }

    private static string BuildPersonNameSelectExpression(
        string addressIdExpression,
        bool personHasArchivColumn,
        string outputAlias)
    {
        var personName = "NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(256), P.Name))), '')";
        var archivFilter = personHasArchivColumn ? "AND ISNULL(P.Archiv, 0) = 0" : string.Empty;

        return $"""
                (
                    SELECT TOP 1 {personName}
                    FROM AVE_PersonLink L
                    INNER JOIN AVE_Person P ON L.PersonID = P.Ident
                    WHERE L.AdressID = {addressIdExpression}
                      {archivFilter}
                    ORDER BY ISNULL(L.Standard, 0) DESC, L.Ident
                ) AS {outputAlias}
                """;
    }

    private static bool IsDeliveryMethodArticle(string? articleId)
    {
        var normalizedArticleId = NormalizeArticleId(articleId);
        return !string.IsNullOrWhiteSpace(normalizedArticleId) &&
               DeliveryRuleByArticleId.ContainsKey(normalizedArticleId);
    }

    private static DeliveryRule? ResolveDeliveryRule(string? articleId)
    {
        var normalizedArticleId = NormalizeArticleId(articleId);
        if (!string.IsNullOrWhiteSpace(normalizedArticleId) &&
            DeliveryRuleByArticleId.TryGetValue(normalizedArticleId, out var rule))
        {
            return rule;
        }

        return null;
    }

    private static DeliveryRule DetermineDeliveryRule(
        List<(string? ArticleId, string Description)> positionSignals)
    {
        if (positionSignals.Count == 0)
        {
            return new DeliveryRule(DeliveryMethodExtensions.SelbstabholungLabel, DeliveryMethodType.Selbstabholung, 0);
        }

        var matchedRules = new List<DeliveryRule>();
        foreach (var (articleId, _) in positionSignals)
        {
            var rule = ResolveDeliveryRule(articleId);
            if (rule is not null)
            {
                matchedRules.Add(rule);
            }
        }

        if (matchedRules.Count == 0)
        {
            return new DeliveryRule(DeliveryMethodExtensions.SelbstabholungLabel, DeliveryMethodType.Selbstabholung, 0);
        }

        return matchedRules
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static void FinalizeOrder(
        SqlOrderImportData order,
        List<(string? ArticleId, string Description)> deliverySignals,
        List<(int Position, string Bezeichnung, decimal Menge, decimal Gewicht, decimal Bruttogewicht, string? ArticleId)> positions)
    {
        var rule = DetermineDeliveryRule(deliverySignals);
        order.Lieferbedingung = rule.Label;

        foreach (var (position, bezeichnung, menge, gewicht, bruttogewicht, articleId) in positions)
        {
            if (IsMissingArticleId(articleId))
            {
                continue;
            }

            if (IsDeliveryMethodArticle(articleId))
            {
                continue;
            }

            order.Produkte.Add(new SqlOrderProductData
            {
                PosNummer = position,
                Bezeichnung = bezeichnung,
                Menge = menge,
                Gewicht = gewicht,
                Bruttogewicht = bruttogewicht
            });
        }
    }

    private static string NormalizeArticleId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        return normalized.Equals("NULL", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : normalized;
    }

    private static bool IsMissingArticleId(string? value)
    {
        return string.IsNullOrWhiteSpace(NormalizeArticleId(value));
    }

    private static string ReadString(SqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetValue(ordinal).ToString() ?? string.Empty;
    }

    private static string? ReadNullableString(SqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal).ToString();
    }

    private static int ReadInt(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return 0;
        }

        var raw = reader.GetValue(ordinal);
        return raw switch
        {
            int i => i,
            long l => (int)l,
            short s => s,
            decimal d => (int)d,
            double db => (int)db,
            float f => (int)f,
            _ => int.TryParse(raw.ToString(), out var parsed) ? parsed : 0
        };
    }

    private static decimal ReadDecimal(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return 0m;
        }

        var raw = reader.GetValue(ordinal);
        return raw switch
        {
            decimal d => d,
            double db => (decimal)db,
            float f => (decimal)f,
            int i => i,
            long l => l,
            _ => decimal.TryParse(raw.ToString(), out var parsed) ? parsed : 0m
        };
    }

    private static DateTime ReadDateTime(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return DateTime.MinValue;
        }

        var raw = reader.GetValue(ordinal);
        return raw switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.DateTime,
            _ => TryParseDateTime(raw.ToString(), out var parsed)
                ? parsed
                : DateTime.MinValue
        };
    }

    private static bool TryParseDateTime(string? value, out DateTime parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return DateTime.TryParseExact(
                   value.Trim(),
                   ["dd.MM.yyyy", "dd.MM.yyyy HH:mm:ss", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss"],
                   System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.AssumeLocal,
                   out parsed)
               || DateTime.TryParse(value, out parsed);
    }
}
