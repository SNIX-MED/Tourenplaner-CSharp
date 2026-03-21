using System.Data.Common;
using System.Text.RegularExpressions;

namespace Tourenplaner.CSharp.Application.Services;

public static class SqlDatabaseNameInference
{
    private static readonly Regex AllowedNameCharacters = new("[^A-Za-z0-9_]", RegexOptions.Compiled);

    public static string InferDatabaseName(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "unknown_db";
        }

        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = connectionString
        };

        if (TryGetValue(builder, "Initial Catalog", out var initialCatalog))
        {
            return Sanitize(initialCatalog);
        }

        if (TryGetValue(builder, "Database", out var database))
        {
            return Sanitize(database);
        }

        return "unknown_db";
    }

    private static bool TryGetValue(DbConnectionStringBuilder builder, string key, out string value)
    {
        if (builder.TryGetValue(key, out var raw) && raw is not null)
        {
            value = raw.ToString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        value = string.Empty;
        return false;
    }

    private static string Sanitize(string candidate)
    {
        var clean = AllowedNameCharacters.Replace(candidate.Trim(), "_");
        return string.IsNullOrWhiteSpace(clean) ? "unknown_db" : clean;
    }
}
