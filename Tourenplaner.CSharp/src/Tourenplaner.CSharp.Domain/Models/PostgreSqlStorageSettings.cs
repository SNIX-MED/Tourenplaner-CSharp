namespace Tourenplaner.CSharp.Domain.Models;

public sealed class PostgreSqlStorageSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "tourenplaner";
    public string Schema { get; set; } = "app";
    public bool UseSsl { get; set; }
    public string Username { get; set; } = "postgres";
    public string Password { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 10;

    public bool IsConfigured()
    {
        return !string.IsNullOrWhiteSpace((Host ?? string.Empty).Trim()) &&
               Port > 0 &&
               !string.IsNullOrWhiteSpace((Database ?? string.Empty).Trim()) &&
               !string.IsNullOrWhiteSpace((Username ?? string.Empty).Trim());
    }
}
