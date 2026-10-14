using MySqlBackupUtility.Data;
using MySqlBackupUtility.MySql;

namespace MySqlBackupUtility.Services;

/// <summary>Global state: paths, database, default settings.</summary>
public static class AppState
{
    public static readonly string AppDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MySqlBackupUtility");

    public static readonly string DbPath = Path.Combine(AppDir, "app.db");

    public static string DefaultBackupDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MySqlBackups");

    private static Database? _db;

    public static Database Db
    {
        get { return _db!; }
    }

    public static void Init()
    {
        Directory.CreateDirectory(AppDir);
        Directory.CreateDirectory(DefaultBackupDir);
        _db = new Database(DbPath);
        string cfg = Path.Combine(AppDir, "default_folder.txt");
        try
        {
            if (File.Exists(cfg))
            {
                string v = File.ReadAllText(cfg).Trim();
                if (!string.IsNullOrWhiteSpace(v)) DefaultBackupDir = v;
            }
        }
        catch { }
        // Переопределения путей к mysqldump/mysql (если их нет в PATH)
        ToolLocator.OverrideDumpPath = LoadText("mysqldump_path.txt");
        ToolLocator.OverrideMysqlPath = LoadText("mysql_path.txt");
    }

    public static void SaveDefaultFolder(string folder)
    {
        DefaultBackupDir = folder;
        try { File.WriteAllText(Path.Combine(AppDir, "default_folder.txt"), folder); } catch { }
    }

    public static void SaveToolPaths(string dumpPath, string mysqlPath)
    {
        SaveText("mysqldump_path.txt", dumpPath.Trim().Trim('"'));
        SaveText("mysql_path.txt", mysqlPath.Trim().Trim('"'));
        ToolLocator.OverrideDumpPath = LoadText("mysqldump_path.txt");
        ToolLocator.OverrideMysqlPath = LoadText("mysql_path.txt");
    }

    private static string LoadText(string fileName)
    {
        try
        {
            string p = Path.Combine(AppDir, fileName);
            return File.Exists(p) ? File.ReadAllText(p).Trim().Trim('"') : "";
        }
        catch { return ""; }
    }

    private static void SaveText(string fileName, string value)
    {
        try
        {
            string p = Path.Combine(AppDir, fileName);
            if (string.IsNullOrWhiteSpace(value)) { if (File.Exists(p)) File.Delete(p); }
            else File.WriteAllText(p, value);
        }
        catch { }
    }
}
