namespace MySqlBackupUtility.Models;

public sealed class BackupArchive
{
    public long Id { get; set; }
    public long JobId { get; set; }
    public string FilePath { get; set; } = "";
    public long SizeBytes { get; set; }
    public string CreatedUtc { get; set; } = "";
    public string Status { get; set; } = "OK";
}
