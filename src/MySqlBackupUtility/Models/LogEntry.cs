namespace MySqlBackupUtility.Models;

public sealed class LogEntry
{
    public long Id { get; set; }
    public string TimestampUtc { get; set; } = "";
    public string Level { get; set; } = "INFO"; // INFO, OK, ERROR
    public string Message { get; set; } = "";
    public string? JobName { get; set; }
    public long? SizeBytes { get; set; }
    public double? DurationSec { get; set; }
}
