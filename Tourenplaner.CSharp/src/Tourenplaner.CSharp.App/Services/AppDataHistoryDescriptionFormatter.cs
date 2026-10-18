namespace Tourenplaner.CSharp.App.Services;

internal enum AppDataHistoryDescriptionProfile
{
    LocalFile,
    PostgreSql
}

internal static class AppDataHistoryDescriptionFormatter
{
    public static string Format(
        AppDataChangedEventArgs args,
        AppDataHistoryDescriptionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Kinds == AppDataKind.Orders)
        {
            return FormatEntity("Auftrag", "Aufträge", args.PreviousId, args.CurrentId);
        }

        if (args.Kinds == AppDataKind.Tours)
        {
            return FormatEntity("Tour", "Touren", args.PreviousId, args.CurrentId);
        }

        if (args.Kinds == AppDataKind.Employees)
        {
            return FormatEntity("Mitarbeiter", "Mitarbeiter", args.PreviousId, args.CurrentId);
        }

        if (args.Kinds == AppDataKind.Vehicles)
        {
            return FormatEntity("Fahrzeug", "Fahrzeuge", args.PreviousId, args.CurrentId);
        }

        if (args.Kinds == AppDataKind.Settings)
        {
            return "Einstellungen ändern";
        }

        if (args.Kinds == AppDataKind.CalendarEntries)
        {
            return FormatEntity("Kalendereintrag", "Kalendereinträge", args.PreviousId, args.CurrentId);
        }

        return profile == AppDataHistoryDescriptionProfile.PostgreSql
            ? "Daten ändern"
            : DescribeKinds(args.Kinds);
    }

    public static bool IsGeneric(string? description)
    {
        var normalized = (description ?? string.Empty).Trim();
        return normalized.Length == 0 ||
               string.Equals(normalized, "Änderung", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "Dateiänderung", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "Dateiumbenennung", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatEntity(
        string singular,
        string plural,
        string? previousId,
        string? currentId)
    {
        var hasPrevious = !string.IsNullOrWhiteSpace(previousId);
        var hasCurrent = !string.IsNullOrWhiteSpace(currentId);
        if (!hasPrevious && hasCurrent)
        {
            return $"{singular} erstellen";
        }

        if (hasPrevious && !hasCurrent)
        {
            return $"{singular} löschen";
        }

        if (hasPrevious && hasCurrent)
        {
            return string.Equals(previousId, currentId, StringComparison.OrdinalIgnoreCase)
                ? $"{singular} aktualisieren"
                : $"{singular} umbenennen";
        }

        return $"{plural} ändern";
    }

    private static string DescribeKinds(AppDataKind kinds)
    {
        if (kinds == AppDataKind.None)
        {
            return "Änderung";
        }

        var labels = new List<string>();
        AddLabelIfPresent(labels, kinds, AppDataKind.Orders, "Aufträge");
        AddLabelIfPresent(labels, kinds, AppDataKind.Tours, "Touren");
        AddLabelIfPresent(labels, kinds, AppDataKind.Employees, "Mitarbeiter");
        AddLabelIfPresent(labels, kinds, AppDataKind.Vehicles, "Fahrzeuge");
        AddLabelIfPresent(labels, kinds, AppDataKind.Settings, "Einstellungen");
        AddLabelIfPresent(labels, kinds, AppDataKind.CalendarEntries, "Kalendereinträge");
        return labels.Count == 0 ? "Änderung" : string.Join(", ", labels);
    }

    private static void AddLabelIfPresent(
        ICollection<string> labels,
        AppDataKind kinds,
        AppDataKind expectedKind,
        string label)
    {
        if (kinds.HasFlag(expectedKind))
        {
            labels.Add(label);
        }
    }
}
