namespace Tourenplaner.CSharp.Application.Services;

internal static class BackupPathMapper
{
    public static string ClassifyArchiveMember(string? member)
    {
        var path = (member ?? string.Empty).Replace('\\', '/');
        if (path == "config/settings.json") return "settings";
        if (path == "data_root/pins.json") return "orders";
        if (path == "data_root/tours.json") return "tours";
        if (path == "data_root/config.json") return "misc";
        if (path == "data/employees.json") return "employees";
        if (path == "data/vehicles.json") return "vehicles";
        if (path.StartsWith("data/", StringComparison.OrdinalIgnoreCase)) return "other_data";
        return "misc";
    }

    public static string? ResolveRestoreDestination(
        string archivePath,
        string targetDataDirectory,
        string targetConfigDirectory)
    {
        if (string.Equals(archivePath, "config/settings.json", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(targetConfigDirectory, "settings.json");
        }

        if (archivePath.StartsWith("data_root/", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(
                targetConfigDirectory,
                archivePath["data_root/".Length..].Replace('/', Path.DirectorySeparatorChar));
        }

        if (archivePath.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(
                targetDataDirectory,
                archivePath["data/".Length..].Replace('/', Path.DirectorySeparatorChar));
        }

        return null;
    }
}
