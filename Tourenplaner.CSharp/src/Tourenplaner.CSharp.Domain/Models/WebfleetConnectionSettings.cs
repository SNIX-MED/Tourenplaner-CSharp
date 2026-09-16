namespace Tourenplaner.CSharp.Domain.Models;

/// <summary>Configuration for the WEBFLEET.connect account used by this installation.</summary>
public sealed class WebfleetConnectionSettings
{
    public const string DefaultCsvEndpoint = "https://csv.webfleet.com/extern";

    public bool IsEnabled { get; set; }
    public string AccountName { get; set; } = "gawela";
    public string UserName { get; set; } = "Janine Fäsi";
    public string ApiKey { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int PositionRefreshSeconds { get; set; } = 60;
    public string CsvEndpoint { get; set; } = DefaultCsvEndpoint;

    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(AccountName) &&
        !string.IsNullOrWhiteSpace(UserName) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Password);
}
