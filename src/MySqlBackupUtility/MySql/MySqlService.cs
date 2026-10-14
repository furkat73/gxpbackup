using MySqlConnector;
using MySqlBackupUtility.Models;
using MySqlBackupUtility.Security;

namespace MySqlBackupUtility.MySql;

public static class MySqlService
{
    public static string BuildConnectionString(ConnectionProfile p, string? databaseOverride = null, int timeoutSec = 10)
    {
        var b = new MySqlConnectionStringBuilder
        {
            Server = p.Host,
            Port = (uint)p.Port,
            UserID = p.Username,
            Password = CryptoService.Decrypt(p.EncryptedPassword),
            ConnectionTimeout = (uint)timeoutSec,
            SslMode = p.UseSsl ? MySqlSslMode.Required : MySqlSslMode.Preferred,
        };
        string db = databaseOverride ?? p.Database;
        if (!string.IsNullOrWhiteSpace(db) && db != "*")
            b.Database = db;
        return b.ConnectionString;
    }

    public static async Task<(bool ok, string message)> TestConnectionAsync(ConnectionProfile p, CancellationToken ct = default)
    {
        try
        {
            using var conn = new MySqlConnection(BuildConnectionString(p, "mysql", 8));
            await conn.OpenAsync(ct);
            using var cmd = new MySqlCommand("SELECT VERSION()", conn);
            var v = (await cmd.ExecuteScalarAsync(ct))?.ToString();
            return (true, $"OK. Сервер: {p.Host}:{p.Port}, версия {v}");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка: {ex.Message}");
        }
    }

    public static async Task<List<string>> GetDatabasesAsync(ConnectionProfile p, CancellationToken ct = default)
    {
        var list = new List<string>();
        using var conn = new MySqlConnection(BuildConnectionString(p, "mysql", 10));
        await conn.OpenAsync(ct);
        using var cmd = new MySqlCommand("SHOW DATABASES", conn);
        using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            string db = r.GetString(0);
            if (db is "information_schema" or "performance_schema" or "sys") continue;
            list.Add(db);
        }
        return list;
    }
}
