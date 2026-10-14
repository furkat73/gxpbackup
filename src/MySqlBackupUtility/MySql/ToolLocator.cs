namespace MySqlBackupUtility.MySql;

/// <summary>
/// Поиск mysqldump/mysql: явный путь из настроек → PATH → авто-поиск в Program Files.
/// ТЗ п.4.3: комплектный вариант — пути задаются в «Настройках» и переживают перезапуск.
/// </summary>
public static class ToolLocator
{
    /// <summary>Явные пути из настроек (AppState). Приоритет выше PATH.</summary>
    public static string? OverrideDumpPath { get; set; }
    public static string? OverrideMysqlPath { get; set; }

    public static string? FindOnPath(string exeName)
    {
        string file = exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exeName : exeName + ".exe";
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv is null) return null;
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            try
            {
                string candidate = Path.Combine(dir.Trim().Trim('"'), file);
                if (File.Exists(candidate)) return candidate;
                string noExt = Path.Combine(dir.Trim().Trim('"'), Path.GetFileNameWithoutExtension(file));
                if (File.Exists(noExt)) return noExt;
            }
            catch { }
        }
        return null;
    }

    private static string? ValidFile(string? p) =>
        !string.IsNullOrWhiteSpace(p) && File.Exists(p.Trim().Trim('"')) ? p.Trim().Trim('"') : null;

    /// <summary>Полный путь или bare-имя (пусть резолвит ОС). Никогда не null.</summary>
    public static string MysqldumpPath =>
        ValidFile(OverrideDumpPath) ?? FindOnPath("mysqldump") ?? "mysqldump";

    public static string MysqlPath =>
        ValidFile(OverrideMysqlPath) ?? FindOnPath("mysql") ?? "mysql";

    /// <summary>Полный путь или null, если утилита реально не найдена.</summary>
    public static string? TryResolveDump() => ValidFile(OverrideDumpPath) ?? FindOnPath("mysqldump");
    public static string? TryResolveMysql() => ValidFile(OverrideMysqlPath) ?? FindOnPath("mysql");

    public static bool AreToolsAvailable(out string missing)
    {
        var miss = new List<string>();
        if (TryResolveDump() is null) miss.Add("mysqldump");
        if (TryResolveMysql() is null) miss.Add("mysql");
        missing = string.Join(", ", miss);
        return miss.Count == 0;
    }

    /// <summary>
    /// Авто-поиск: Program Files*\MariaDB*\bin, *\MySQL*\bin (включая вложенные
    /// "MySQL Server X.Y"), XAMPP. Возвращает найденные полные пути (null, если нет).
    /// </summary>
    public static (string? dump, string? mysql) AutoDetect()
    {
        string? dump = FindOnPath("mysqldump");
        string? mysql = FindOnPath("mysql");
        if (dump != null && mysql != null) return (dump, mysql);

        var binDirs = new List<string>();
        foreach (var root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        })
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            foreach (var vendor in new[] { "MariaDB*", "MySQL*" })
            {
                string[] level1;
                try { level1 = Directory.GetDirectories(root, vendor); }
                catch { continue; }
                foreach (var d in level1)
                {
                    binDirs.Add(Path.Combine(d, "bin"));
                    // вложенность вида MySQL\MySQL Server 8.0\bin
                    string[] level2;
                    try { level2 = Directory.GetDirectories(d); }
                    catch { continue; }
                    foreach (var s in level2) binDirs.Add(Path.Combine(s, "bin"));
                }
            }
        }
        binDirs.Add(@"C:\xampp\mysql\bin");

        foreach (var bin in binDirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (dump is null && File.Exists(Path.Combine(bin, "mysqldump.exe")))
                    dump = Path.Combine(bin, "mysqldump.exe");
                if (mysql is null && File.Exists(Path.Combine(bin, "mysql.exe")))
                    mysql = Path.Combine(bin, "mysql.exe");
                if (dump != null && mysql != null) break;
            }
            catch { }
        }
        return (dump, mysql);
    }
}
