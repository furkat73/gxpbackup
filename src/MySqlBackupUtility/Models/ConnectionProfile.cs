namespace MySqlBackupUtility.Models;

public sealed class ConnectionProfile
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3306;
    public string Username { get; set; } = "root";
    /// <summary>DPAPI-encrypted password (base64 of ProtectedData blob). Never plain text.</summary>
    public string EncryptedPassword { get; set; } = "";
    public string Database { get; set; } = ""; // empty or "*" = all
    public bool UseSsl { get; set; } = false;
}
