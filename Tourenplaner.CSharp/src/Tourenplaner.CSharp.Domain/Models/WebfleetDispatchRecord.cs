namespace Tourenplaner.CSharp.Domain.Models;

public sealed class WebfleetDispatchRecord
{
    public string ObjectUid { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string State { get; set; } = "not_sent";
    public DateTimeOffset? SentAtUtc { get; set; }
    public DateTimeOffset? LastStatusCheckUtc { get; set; }
    public string LastMessage { get; set; } = string.Empty;
    public List<WebfleetStopDispatchRecord> Stops { get; set; } = new();
}

public sealed class WebfleetStopDispatchRecord
{
    public string StopId { get; set; } = string.Empty;
    public string WebfleetOrderId { get; set; } = string.Empty;
    public int StateCode { get; set; }
    public string StateLabel { get; set; } = "Nicht gesendet";
    public DateTimeOffset? StateChangedAtUtc { get; set; }
    public string LastMessage { get; set; } = string.Empty;
}
