using System.Diagnostics;
using System.IO.Compression;
using MySqlBackupUtility.Models;
using MySqlBackupUtility.MySql;
using MySqlBackupUtility.Security;

namespace MySqlBackupUtility.Backup;

public sealed record BackupResult(bool Ok, string FilePath, long SizeBytes, double DurationSec, string Message);

/// <summary>Экспорт через mysqldump + GZip. Имя [ИмяБД]_[ГГГГММДД]_[ЧЧММСС].sql.gz (ТЗ п.2.2).</summary>
public static class BackupService
{
    public static string BuildFileName(string dbName, DateTime now) =>
        $"{Sanitize(dbName)}_{now:yyyyMMdd}_{now:HHmmss}.sql.gz";

    /// <summary>
    /// Разбор выбора БД: "*" или пусто → null (= все базы с сервера),
    /// иначе список имён (через запятую/точку с запятой).
    /// </summary>
    public static List<string>? ParseDatabaseList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == "*")
            return null;
        var list = raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().Trim('`', '"'))
            .Where(s => s.Length > 0 && s != "*")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return list.Count == 0 ? null : list;
    }

    private static string Sanitize(string s)
    {
        foreach (char ch in Path.GetInvalidFileNameChars())
            s = s.Replace(ch, '_');
        return string.IsNullOrWhiteSpace(s) ? "all" : s;
    }

    public static async Task<BackupResult> RunBackupAsync(
        ConnectionProfile profile, string database, string outputFolder,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        Directory.CreateDirectory(outputFolder);
        string fileName = BuildFileName(database, DateTime.Now);
        string gzPath = Path.Combine(outputFolder, fileName);
        string tmpSql = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(fileName) + ".tmp.sql");

        string dumpExe = ToolLocator.MysqldumpPath;
        string password = CryptoService.Decrypt(profile.EncryptedPassword);

        // mysqldump args; пароль передаём через env MYSQL_PWD чтобы не светить в командной строке
        string args = $"--host=\"{profile.Host}\" --port={profile.Port} --user=\"{profile.Username}\" " +
                      $"--single-transaction --routines --triggers --events --default-character-set=utf8mb4 " +
                      (profile.UseSsl ? "--ssl-mode=REQUIRED " : "") +
                      $"\"{database}\"";

        progress?.Report("Запуск mysqldump...");
        var psi = new ProcessStartInfo
        {
            FileName = dumpExe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        psi.Environment["MYSQL_PWD"] = password;

        try
        {
            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.Start();

            // Стримим stdout сразу в tmp .sql файл (важно для БД 5+ ГБ — не держим всё в памяти)
            await using (var fs = new FileStream(tmpSql, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true))
            await using (var stdout = proc.StandardOutput.BaseStream)
            {
                await stdout.CopyToAsync(fs, ct);
            }

            string stderr = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            if (proc.ExitCode != 0)
            {
                try { File.Delete(tmpSql); } catch { }
                return new BackupResult(false, "", 0, sw.Elapsed.TotalSeconds, $"mysqldump exit={proc.ExitCode}: {stderr.Trim()}");
            }

            progress?.Report("Сжатие (gzip)...");
            await using (var src = new FileStream(tmpSql, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true))
            await using (var dst = new FileStream(gzPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true))
            await using (var gz = new GZipStream(dst, CompressionLevel.Optimal))
            {
                await src.CopyToAsync(gz, ct);
            }
            try { File.Delete(tmpSql); } catch { }

            long size = new FileInfo(gzPath).Length;
            sw.Stop();
            return new BackupResult(true, gzPath, size, sw.Elapsed.TotalSeconds, "OK");
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tmpSql)) File.Delete(tmpSql); } catch { }
            try { if (File.Exists(gzPath) && new FileInfo(gzPath).Length == 0) File.Delete(gzPath); } catch { }
            sw.Stop();
            return new BackupResult(false, "", 0, sw.Elapsed.TotalSeconds, ex.Message);
        }
    }
}
