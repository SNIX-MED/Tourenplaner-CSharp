namespace Tourenplaner.CSharp.Domain.Models;

public sealed class CalendarManualEntry
{
    public string Id { get; set; } = string.Empty;

    public string Date { get; set; } = string.Empty;

    public string Time { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string ColorHex { get; set; } = "#0EA5E9";
}
