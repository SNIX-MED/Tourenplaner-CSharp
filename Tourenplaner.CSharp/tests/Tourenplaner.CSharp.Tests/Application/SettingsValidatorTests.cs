using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class SettingsValidatorTests
{
    [Fact]
    public void Validate_ReturnsErrors_ForInvalidSettings()
    {
        var validator = new SettingsValidator();
        var settings = new AppSettings
        {
            SqlServerInstance = "",
            SqlDataDir = "",
            SqlDatabase = "",
            AppearanceMode = "Blue",
            BackupsEnabled = true,
            BackupDir = "",
            BackupModeDefault = "delta",
            BackupRetentionDays = 0,
            AutoBackupEnabled = true,
            AutoBackupIntervalDays = 0,
            QuickAccessItems = Enumerable.Range(0, 9).Select(i => $"action:{i}").ToList()
        };

        var result = validator.Validate(settings);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SqlServerInstance"));
        Assert.Contains(result.Errors, e => e.Contains("SqlDataDir"));
        Assert.Contains(result.Errors, e => e.Contains("SqlDatabase"));
        Assert.Contains(result.Errors, e => e.Contains("AppearanceMode"));
        Assert.Contains(result.Errors, e => e.Contains("BackupModeDefault"));
        Assert.Contains(result.Errors, e => e.Contains("BackupRetentionDays"));
        Assert.Contains(result.Errors, e => e.Contains("AutoBackupIntervalDays"));
        Assert.Contains(result.Errors, e => e.Contains("BackupDir"));
        Assert.Contains(result.Errors, e => e.Contains("QuickAccessItems"));
    }

    [Fact]
    public void Validate_ReturnsValid_ForWellFormedSettings()
    {
        var validator = new SettingsValidator();
        var settings = new AppSettings
        {
            SqlServerInstance = @".\SQLEXPRESS",
            SqlDataDir = @"C:\Program Files\Microsoft SQL Server\MSSQL15.SQLEXPRESS\MSSQL\DATA",
            SqlDatabase = "GAWELA_TP",
            AppearanceMode = "System",
            BackupsEnabled = true,
            BackupDir = @"C:\data\tourenplaner\backups",
            BackupModeDefault = "full",
            BackupRetentionDays = 30,
            AutoBackupEnabled = true,
            AutoBackupIntervalDays = 7,
            QuickAccessItems = new List<string> { "action:import_sql", "action:save_route" }
        };

        var result = validator.Validate(settings);

        Assert.True(result.IsValid);
    }
}
