using Microsoft.Data.Sqlite;
using MySqlBackupUtility.Models;

namespace MySqlBackupUtility.Data;

/// <summary>SQLite-хранилище настроек: профили, задачи, архивы, логи. ТЗ п.4.2.</summary>
public sealed class Database
{
    private readonly string _path;
    private string ConnectionString => $"Data Source={_path}";

    public Database(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Init();
    }

    private void Init()
    {
        using var c = new SqliteConnection(ConnectionString);
        c.Open();
        string[] ddl =
        {
            @"CREATE TABLE IF NOT EXISTS connections(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL, host TEXT NOT NULL, port INTEGER NOT NULL,
                username TEXT NOT NULL, encrypted_password TEXT NOT NULL DEFAULT '',
                database_name TEXT NOT NULL DEFAULT '', use_ssl INTEGER NOT NULL DEFAULT 0)",
            @"CREATE TABLE IF NOT EXISTS jobs(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                connection_id INTEGER NOT NULL REFERENCES connections(id) ON DELETE CASCADE,
                name TEXT NOT NULL, kind INTEGER NOT NULL DEFAULT 0,
                time_of_day TEXT NOT NULL DEFAULT '03:00',
                days_of_week TEXT NOT NULL DEFAULT '1,2,3,4,5,6,0',
                day_of_month INTEGER NOT NULL DEFAULT 1,
                interval_minutes INTEGER NOT NULL DEFAULT 360,
                output_folder TEXT NOT NULL DEFAULT '',
                keep_copies INTEGER NOT NULL DEFAULT 7,
                keep_days INTEGER NOT NULL DEFAULT 7,
                enabled INTEGER NOT NULL DEFAULT 1,
                last_status TEXT, last_run_utc TEXT, next_run_utc TEXT)",
            @"CREATE TABLE IF NOT EXISTS archives(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                job_id INTEGER NOT NULL REFERENCES jobs(id) ON DELETE CASCADE,
                file_path TEXT NOT NULL, size_bytes INTEGER NOT NULL DEFAULT 0,
                created_utc TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'OK')",
            @"CREATE TABLE IF NOT EXISTS logs(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp_utc TEXT NOT NULL, level TEXT NOT NULL,
                message TEXT NOT NULL, job_name TEXT,
                size_bytes INTEGER, duration_sec REAL)",
        };
        foreach (var sql in ddl)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
        using (var pragma = c.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys=ON";
            pragma.ExecuteNonQuery();
        }
    }

    // ---- Connections ----
    public List<ConnectionProfile> GetConnections()
    {
        var list = new List<ConnectionProfile>();
        using var c = new SqliteConnection(ConnectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,name,host,port,username,encrypted_password,database_name,use_ssl FROM connections ORDER BY name";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ConnectionProfile
            {
                Id = r.GetInt64(0), Name = r.GetString(1), Host = r.GetString(2),
                Port = r.GetInt32(3), Username = r.GetString(4), EncryptedPassword = r.GetString(5),
                Database = r.GetString(6), UseSsl = r.GetInt32(7) == 1
            });
        }
        return list;
    }

    public long UpsertConnection(ConnectionProfile p)
    {
        using var c = new SqliteConnection(ConnectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        if (p.Id == 0)
        {
            cmd.CommandText = "INSERT INTO connections(name,host,port,username,encrypted_password,database_name,use_ssl) VALUES($n,$h,$p,$u,$e,$d,$s); SELECT last_insert_rowid();";
        }
        else
        {
            cmd.CommandText = "UPDATE connections SET name=$n,host=$h,port=$p,username=$u,encrypted_password=$e,database_name=$d,use_ssl=$s WHERE id=$id; SELECT $id;";
            cmd.Parameters.AddWithValue("$id", p.Id);
        }
        cmd.Parameters.AddWithValue("$n", p.Name);
        cmd.Parameters.AddWithValue("$h", p.Host);
        cmd.Parameters.AddWithValue("$p", p.Port);
        cmd.Parameters.AddWithValue("$u", p.Username);
        cmd.Parameters.AddWithValue("$e", p.EncryptedPassword);
        cmd.Parameters.AddWithValue("$d", p.Database);
        cmd.Parameters.AddWithValue("$s", p.UseSsl ? 1 : 0);
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public void DeleteConnection(long id)
    {
        using var c = new SqliteConnection(ConnectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM connections WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ---- Jobs ----
    public List<BackupJob> GetJobs()
    {
        var list = new List<BackupJob>();
        using var c = new SqliteConnection(ConnectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT j.id,j.connection_id,j.name,j.kind,j.time_of_day,j.days_of_week,
            j.day_of_month,j.interval_minutes,j.output_folder,j.keep_copies,j.keep_days,j.enabled,
            j.last_status,j.last_run_utc,j.next_run_utc,
            COALESCE(cx.name,''), COALESCE(cx.database_name,'')
            FROM jobs j LEFT JOIN connections cx ON cx.id=j.connection_id ORDER BY j.name";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new BackupJob
            {
                Id = r.GetInt64(0), ConnectionId = r.GetInt64(1), Name = r.GetString(2),
                Kind = (ScheduleKind)r.GetInt32(3), TimeOfDay = r.GetString(4), DaysOfWeek = r.GetString(5),
                DayOfMonth = r.GetInt32(6), IntervalMinutes = r.GetInt32(7), OutputFolder = r.GetString(8),
                KeepCopies = r.GetInt32(9), KeepDays = r.GetInt32(10), Enabled = r.GetInt32(11) == 1,
                LastStatus = r.IsDBNull(12) ? null : r.GetString(12),
                LastRunUtc = r.IsDBNull(13) ? null : r.GetString(13),
                NextRunUtc = r.IsDBNull(14) ? null : r.GetString(14),
                ConnectionName = r.GetString(15), Database = r.GetString(16),
            });
        }
        return list;
    }

    public long UpsertJob(BackupJob j)
    {
        using var c = new SqliteConnection(ConnectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        if (j.Id == 0)
            cmd.CommandText = @"INSERT INTO jobs(connection_id,name,kind,time_of_day,days_of_week,day_of_month,interval_minutes,output_folder,keep_copies,keep_days,enabled,last_status,last_run_utc,next_run_utc)
                VALUES($cx,$n,$k,$t,$d,$dm,$iv,$o,$kc,$kd,$e,$ls,$lr,$nr); SELECT last_insert_rowid();";
        else
        {
            cmd.CommandText = @"UPDATE jobs SET connection_id=$cx,name=$n,kind=$k,time_of_day=$t,days_of_week=$d,day_of_month=$dm,interval_minutes=$iv,output_folder=$o,keep_copies=$kc,keep_days=$kd,enabled=$e,last_status=$ls,last_run_utc=$lr,next_run_utc=$nr WHERE id=$id; SELECT $id;";
            cmd.Parameters.AddWithValue("$id", j.Id);
        }
        cmd.Parameters.AddWithValue("$cx", j.ConnectionId);
        cmd.Parameters.AddWithValue("$n", j.Name);
        cmd.Parameters.AddWithValue("$k", (int)j.Kind);
        cmd.Parameters.AddWithValue("$t", j.TimeOfDay);
        cmd.Parameters.AddWithValue("$d", j.DaysOfWeek);
        cmd.Parameters.AddWithValue("$dm", j.DayOfMonth);
        cmd.Parameters.AddWithValue("$iv", j.IntervalMinutes);
        cmd.Parameters.AddWithValue("$o", j.OutputFolder);
        cmd.Parameters.AddWithValue("$kc", j.KeepCopies);
        cmd.Parameters.AddWithValue("$kd", j.KeepDays);
        cmd.Parameters.AddWithValue("$e", j.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$ls", (object?)j.LastStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lr", (object?)j.LastRunUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$nr", (object?)j.NextRunUtc ?? DBNull.Value);
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public void DeleteJob(long id)
    {
        using var c = new SqliteConnection(ConnectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM jobs WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void UpdateJobStatus(long id, string status, string lastRunUtc, string? nextRunUtc)
    {
        using var c = new SqliteConnection(ConnectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE jobs SET last_status=$s, last_run_utc=$l, next_run_utc=$n WHERE id=$id";
        cmd.Parameters.AddWithValue("$s", status);
        cmd.Parameters.AddWithValue("$l", lastRunUtc);
        cmd.Parameters.AddWithValue("$n", (object?)nextRunUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ---- Archives ----
    public void AddArchive(long jobId, string filePath, long size, string createdUtc, string status)
    {
        using var c = new SqliteConnection(ConnectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO archives(job_id,file_path,size_bytes,created_utc,status) VALUES($j,$f,$s,$c,$st)";
        cmd.Parameters.AddWithValue("$j", jobId);
        cmd.Parameters.AddWithValue("$f", filePath);
        cmd.Parameters.AddWithValue("$s", size);
        cmd.Parameters.AddWithValue("$c", createdUtc);
        cmd.Parameters.AddWithValue("$st", status);
        cmd.ExecuteNonQuery();
    }

    public List<BackupArchive> GetArchives(long? jobId = null, int limit = 500)
    {
        var list = new List<BackupArchive>();
        using var c = new SqliteConnection(ConnectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = jobId is null
            ? "SELECT id,job_id,file_path,size_bytes,created_utc,status FROM archives ORDER BY id DESC LIMIT $l"
            : "SELECT id,job_id,file_path,size_bytes,created_utc,status FROM archives WHERE job_id=$j ORDER BY id DESC LIMIT $l";
        if (jobId is not null) cmd.Parameters.AddWithValue("$j", jobId.Value);
        cmd.Parameters.AddWithValue("$l", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new BackupArchive { Id = r.GetInt64(0), JobId = r.GetInt64(1), FilePath = r.GetString(2), SizeBytes = r.GetInt64(3), CreatedUtc = r.GetString(4), Status = r.GetString(5) });
        return list;
    }

    public void DeleteArchive(long id)
    {
        using var c = new SqliteConnection(ConnectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM archives WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ---- Logs ----
    public void AddLog(string level, string message, string? jobName = null, long? size = null, double? durSec = null)
    {
        using var c = new SqliteConnection(ConnectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO logs(timestamp_utc,level,message,job_name,size_bytes,duration_sec) VALUES($t,$l,$m,$j,$s,$d)";
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$l", level);
        cmd.Parameters.AddWithValue("$m", message);
        cmd.Parameters.AddWithValue("$j", (object?)jobName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$s", (object?)size ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$d", (object?)durSec ?? DBNull.Value);
        cmd.ExecuteNonQuery();

        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MySqlBackupUtility", "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, $"app-{DateTime.Now:yyyyMMdd}.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {(jobName is null ? "" : $"({jobName}) ")}{message}{Environment.NewLine}");
        }
        catch { /* logging must not crash app */ }
    }

    public List<LogEntry> GetLogs(int limit = 500)
    {
        var list = new List<LogEntry>();
        using var c = new SqliteConnection(ConnectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,timestamp_utc,level,message,job_name,size_bytes,duration_sec FROM logs ORDER BY id DESC LIMIT $l";
        cmd.Parameters.AddWithValue("$l", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new LogEntry
            {
                Id = r.GetInt64(0), TimestampUtc = r.GetString(1), Level = r.GetString(2),
                Message = r.GetString(3), JobName = r.IsDBNull(4) ? null : r.GetString(4),
                SizeBytes = r.IsDBNull(5) ? null : r.GetInt64(5),
                DurationSec = r.IsDBNull(6) ? null : r.GetDouble(6),
            });
        return list;
    }

    public ConnectionProfile? GetConnection(long id)
    {
        using var c = new SqliteConnection(ConnectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,name,host,port,username,encrypted_password,database_name,use_ssl FROM connections WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new ConnectionProfile
        {
            Id = r.GetInt64(0), Name = r.GetString(1), Host = r.GetString(2),
            Port = r.GetInt32(3), Username = r.GetString(4), EncryptedPassword = r.GetString(5),
            Database = r.GetString(6), UseSsl = r.GetInt32(7) == 1
        };
    }
}
