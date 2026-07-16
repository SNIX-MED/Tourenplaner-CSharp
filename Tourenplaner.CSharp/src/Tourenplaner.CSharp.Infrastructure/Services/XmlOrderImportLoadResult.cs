using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Services;

public sealed class XmlOrderImportLoadResult
{
    public int TotalOrderElements { get; set; }
    public List<SqlOrderImportData> Orders { get; set; } = new();
    public List<string> Errors { get; set; } = new();

    public bool HasErrors => Errors.Count > 0;
}
