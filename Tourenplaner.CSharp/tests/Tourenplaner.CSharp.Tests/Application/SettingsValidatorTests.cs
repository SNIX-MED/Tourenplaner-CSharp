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
            BaseDataPath = "",
            BackupPath = "",
            SqlConnectionString = "Server=.\\SQLEXPRESS;Trusted_Connection=True;",
            AutoBackupRetentionDays = 0
        };

        var result = validator.Validate(settings);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("BaseDataPath"));
        Assert.Contains(result.Errors, e => e.Contains("BackupPath"));
        Assert.Contains(result.Errors, e => e.Contains("AutoBackupRetentionDays"));
        Assert.Contains(result.Errors, e => e.Contains("SqlConnectionString"));
    }

    [Fact]
    public void Validate_ReturnsValid_ForWellFormedSettings()
    {
        var validator = new SettingsValidator();
        var settings = new AppSettings
        {
            BaseDataPath = @"C:\data\tourenplaner",
            BackupPath = @"C:\data\tourenplaner\backups",
            SqlConnectionString = "Server=.\\SQLEXPRESS;Database=TourenplanerDb;Trusted_Connection=True;",
            AutoBackupRetentionDays = 30
        };

        var result = validator.Validate(settings);

        Assert.True(result.IsValid);
    }
}
