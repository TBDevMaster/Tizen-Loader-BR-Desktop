namespace TizenLoaderBRDesktop.Models;

public sealed class LogEntry
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    public string Category { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string Level { get; set; } = "INFO";
}
