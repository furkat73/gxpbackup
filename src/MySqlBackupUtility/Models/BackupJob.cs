namespace MySqlBackupUtility.Models;

public enum ScheduleKind { Daily = 0, Weekly = 1, Monthly = 2, Interval = 3 }

public sealed class BackupJob
{
    public long Id { get; set; }
    public long ConnectionId { get; set; }
    public string Name { get; set; } = "";
    public ScheduleKind Kind { get; set; } = ScheduleKind.Daily;
    /// <summary>For Daily/Weekly/Monthly: "HH:mm".</summary>
    public string TimeOfDay { get; set; } = "03:00";
    /// <summary>Weekly: comma-separated DayOfWeek ints 0=Sunday..6=Saturday, e.g. "1,3,5".</summary>
    public string DaysOfWeek { get; set; } = "1,2,3,4,5,6,0";
    public int DayOfMonth { get; set; } = 1;
    /// <summary>Interval kind: minutes.</summary>
    public int IntervalMinutes { get; set; } = 360;
    public string OutputFolder { get; set; } = "";
    public int KeepCopies { get; set; } = 7;   // 0 = unlimited
    public int KeepDays { get; set; } = 7;     // 0 = unlimited
    public bool Enabled { get; set; } = true;
    public string? LastStatus { get; set; }
    public string? LastRunUtc { get; set; }
    public string? NextRunUtc { get; set; }

    // Denormalized for dashboard display
    public string ConnectionName { get; set; } = "";
    public string Database { get; set; } = "";
}
