namespace Tourenplaner.CSharp.App.Services;

public static class AvisoStatusVisualService
{
    public static string ResolveBadgeColor(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "informiert" => "#F59E0B",
        "bestätigt" => "#16A34A",
        _ => "#64748B"
    };
}
