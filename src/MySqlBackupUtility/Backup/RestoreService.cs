using System.Diagnostics;
using System.IO.Compression;
using MySqlBackupUtility.Models;
using MySqlBackupUtility.MySql;
using MySqlBackupUtility.Security;

namespace MySqlBackupUtility.Backup;

public sealed record RestoreResult(bool Ok, double DurationSec, string Message);

/// <summary>Импорт .sql / .gz / .zip через клиент mysql (ТЗ п.2.4). Потоково, без загрузки всего файла в память.</summary>
public static class RestoreService
{
    public static async Task<RestoreResult> RunRestoreAsync(
        ConnectionProfile profile, string targetDatabase, string archivePath,
        IProgress<int>? percent = null, IProgress<string>? log = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        string ext = Path.GetExtension(archivePath).ToLowerInvariant();
        string tmpSql = Path.Combine(Path.GetTempPath(), $"mbu_restore_{Guid.NewGuid():N}.sql");

        try
        {
            log?.Report("Подготовка файла...");
            if (ext == ".gz")
            {
                await using var src = File.OpenRead(archivePath);
                await using var gz = new GZipStream(src, CompressionMode.Decompress, leaveOpen: false);
                await using var dst = new FileStream(tmpSql, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true);
                await gz.CopyToAsync(dst, ct);
            }
            else if (ext == ".zip")
            {
                using var zip = ZipFile.OpenRead(archivePath);
                var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)) ?? zip.Entries.FirstOrDefault()
                    ?? throw new InvalidOperationException("ZIP-архив пуст");
                percent?.Report(30);
                await using var s = entry.Open();
                await using var dst = new FileStream(tmpSql, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true);
                await s.CopyToAsync(dst, ct);
            }
            else // .sql
            {
                tmpSql = archivePath; // читаем напрямую
            }

            long total = tmpSql == archivePath ? new FileInfo(archivePath).Length : new FileInfo(tmpSql).Length;
            log?.Report($"Импорт в `{targetDatabase}` ({total / 1048576.0:F1} МБ)...");

            string mysqlExe = ToolLocator.MysqlPath;
            string password = CryptoService.Decrypt(profile.EncryptedPassword);
            string args = $"--host=\"{profile.Host}\" --port={profile.Port} --user=\"{profile.Username}\" " +
                          (profile.UseSsl ? "--ssl-mode=REQUIRED " : "") +
                          $"--default-character-set=utf8mb4 \"{targetDatabase}\"";

            var psi = new ProcessStartInfo
            {
                FileName = mysqlExe,
                Arguments = args,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.Environment["MYSQL_PWD"] = password;

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.Start();

            // Стримим SQL-файл в stdin процесса mysql
            await using (var fs = new FileStream(tmpSql, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true))
            {
                await fs.CopyToAsync(proc.StandardInput.BaseStream, ct);
            }
            proc.StandardInput.Close();

            string stderr = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            percent?.Report(100);

            if (proc.ExitCode != 0)
                return new RestoreResult(false, sw.Elapsed.TotalSeconds, $"mysql exit={proc.ExitCode}: {stderr.Trim()}");
            return new RestoreResult(true, sw.Elapsed.TotalSeconds, "OK");
        }
        catch (Exception ex)
        {
            return new RestoreResult(false, sw.Elapsed.TotalSeconds, ex.Message);
        }
        finally
        {
            sw.Stop();
            try { if (tmpSql.StartsWith(Path.GetTempPath()) && File.Exists(tmpSql)) File.Delete(tmpSql); } catch { }
        }
    }
}
