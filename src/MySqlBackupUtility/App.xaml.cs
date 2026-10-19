namespace MySqlBackupUtility;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ReportFatal(args.ExceptionObject as Exception, "AppDomain");
        DispatcherUnhandledException += (_, args) =>
        {
            ReportFatal(args.Exception, "Dispatcher");
            args.Handled = true;
        };

        try
        {
            base.OnStartup(e);
            Services.AppState.Init();
        }
        catch (Exception ex)
        {
            ReportFatal(ex, "Startup");
            Shutdown(1);
        }
    }

    /// <summary>
    /// Показывает понятное сообщение и пишет лог, если запуск не удался.
    /// Без этого окно просто не появляется и причину не видно.
    /// </summary>
    private static void ReportFatal(Exception? ex, string source)
    {
        string text = ex?.ToString() ?? "(нет сведений)";
        string logPath = "";
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MySqlBackupUtility", "logs");
            if (string.IsNullOrWhiteSpace(dir) || dir.StartsWith("MySqlBackupUtility"))
                dir = Path.Combine(Path.GetTempPath(), "MySqlBackupUtility", "logs");
            Directory.CreateDirectory(dir);
            logPath = Path.Combine(dir, "crash-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            File.WriteAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{text}{Environment.NewLine}");
        }
        catch { }

        try
        {
            System.Windows.MessageBox.Show(
                "Не удалось запустить MySQL Backup Utility." + Environment.NewLine + Environment.NewLine +
                ex?.GetType().Name + ": " + ex?.Message + Environment.NewLine + Environment.NewLine +
                (logPath.Length > 0 ? "Подробности записаны в файл:" + Environment.NewLine + logPath
                                    : "Подробности: " + text),
                "MySQL Backup Utility — ошибка запуска",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        catch { }
    }
}
