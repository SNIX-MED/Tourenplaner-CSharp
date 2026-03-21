using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Services;

public sealed class SettingsValidator
{
    public ValidationResult Validate(AppSettings settings)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.BaseDataPath))
        {
            errors.Add("BaseDataPath is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.BackupPath))
        {
            errors.Add("BackupPath is required.");
        }

        if (settings.AutoBackupRetentionDays < 1)
        {
            errors.Add("AutoBackupRetentionDays must be greater than zero.");
        }

        if (!string.IsNullOrWhiteSpace(settings.SqlConnectionString) &&
            SqlDatabaseNameInference.InferDatabaseName(settings.SqlConnectionString) == "unknown_db")
        {
            errors.Add("SqlConnectionString must contain Database or Initial Catalog.");
        }

        return new ValidationResult(errors);
    }
}
