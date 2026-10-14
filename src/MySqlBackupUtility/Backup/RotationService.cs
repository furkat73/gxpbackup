using MySqlBackupUtility.Data;

namespace MySqlBackupUtility.Backup;

/// <summary>Ротация: keep N копий / N дней (ТЗ п.2.2). Удаляет старые .gz + чистит записи в SQLite.</summary>
public static class RotationService
{
    public static void Apply(Database db, long jobId, string folder, int keepCopies, int keepDays)
    {
        try
        {
            var archives = db.GetArchives(jobId, 5000)
                .OrderByDescending(a => a.CreatedUtc).ToList();

            var toDelete = new HashSet<long>();

            if (keepDays > 0)
            {
                var cutoff = DateTime.UtcNow.AddDays(-keepDays);
                foreach (var a in archives)
                    if (DateTime.TryParse(a.CreatedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) && dt < cutoff)
                        toDelete.Add(a.Id);
            }
            if (keepCopies > 0)
            {
                foreach (var a in archives.Skip(keepCopies))
                    toDelete.Add(a.Id);
            }

            foreach (var a in archives.Where(a => toDelete.Contains(a.Id)))
            {
                try { if (File.Exists(a.FilePath)) File.Delete(a.FilePath); } catch { }
                db.DeleteArchive(a.Id);
            }

            // Подчистить файлы-сироты по маске, которых нет в БД (старше keepDays)
            if (keepDays > 0 && Directory.Exists(folder))
            {
                var cutoff = DateTime.Now.AddDays(-keepDays);
                foreach (var f in Directory.GetFiles(folder, "*.sql.gz"))
                {
                    try
                    {
                        if (File.GetCreationTime(f) < cutoff &&
                            !archives.Any(a => string.Equals(a.FilePath, f, StringComparison.OrdinalIgnoreCase)))
                            File.Delete(f);
                    }
                    catch { }
                }
            }
        }
        catch { /* ротация не должна ронять бэкап */ }
    }
}
