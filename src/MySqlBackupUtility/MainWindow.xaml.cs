using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MySqlBackupUtility.Backup;
using MySqlBackupUtility.Data;
using MySqlBackupUtility.Models;
using MySqlBackupUtility.MySql;
using MySqlBackupUtility.Scheduling;
using MySqlBackupUtility.Security;
using MySqlBackupUtility.Services;
using WpfApp = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxResult = System.Windows.MessageBoxResult;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfOrientation = System.Windows.Controls.Orientation;
using Forms = System.Windows.Forms;

namespace MySqlBackupUtility;

public partial class MainWindow : Window
{
    private readonly Database _db = AppState.Db;
    private List<ConnectionProfile> _conns = new();
    private List<BackupJob> _jobs = new();
    private long _selectedConnId;
    private long _selectedJobId;
    private bool _allowClose;
    private bool _busy;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _pauseItem;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => InitAsync();
        Closing += OnClosing;
        _timer.Tick += async (_, _) => await TimerTickAsync();
    }

    private void InitAsync()
    {
        JobKind.ItemsSource = new[] { ScheduleKind.Daily, ScheduleKind.Weekly, ScheduleKind.Monthly, ScheduleKind.Interval };
        CfgDefaultFolder.Text = AppState.DefaultBackupDir;
        CfgDumpPath.Text = ToolLocator.TryResolveDump() ?? "";
        CfgMysqlPath.Text = ToolLocator.TryResolveMysql() ?? "";
        RefreshToolsStatus();
        RefreshConnections();
        RefreshJobs();
        RefreshArchives();
        RefreshLogs();
        SetupTray();
        RecalcAllNextRuns();
        _timer.Start();
        // первый тик сразу
        _ = TimerTickAsync();
    }

    // ---------- Tray ----------
    private void SetupTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Text = "MySQL Backup Utility",
            Visible = true,
        };
        try
        {
            using var s = typeof(MainWindow).Assembly.GetManifestResourceStream("MySqlBackupUtility.app.ico");
            _tray.Icon = s is null ? System.Drawing.SystemIcons.Application : new System.Drawing.Icon(s);
        }
        catch { try { _tray.Icon = System.Drawing.SystemIcons.Application; } catch { } }
        var menu = new Forms.ContextMenuStrip();
        var open = new Forms.ToolStripMenuItem("Открыть", null, (_, _) => { Show(); WindowState = WindowState.Normal; Activate(); });
        _pauseItem = new Forms.ToolStripMenuItem("Поставить на паузу", null, (_, _) =>
        {
            ChkSchedulerEnabled.IsChecked = !ChkSchedulerEnabled.IsChecked;
            _pauseItem!.Text = ChkSchedulerEnabled.IsChecked == true ? "Поставить на паузу" : "Продолжить";
        });
        var exit = new Forms.ToolStripMenuItem("Выход", null, (_, _) => { _allowClose = true; _tray!.Visible = false; Close(); });
        menu.Items.Add(open); menu.Items.Add(_pauseItem); menu.Items.Add(exit);
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => { Show(); WindowState = WindowState.Normal; Activate(); };
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose && CfgTray.IsChecked == true)
        {
            e.Cancel = true;
            Hide();
            _tray?.ShowBalloonTip(1500, "MySQL Backup Utility", "Программа свернута в трей. Планировщик продолжает работать.", Forms.ToolTipIcon.Info);
        }
        else
        {
            _timer.Stop();
            if (_tray != null) _tray.Visible = false;
        }
    }

    // ---------- Status ----------
    private void SetStatus(bool? ok, string text)
    {
        StatusText.Text = text;
        StatusDot.Fill = ok is null ? System.Windows.Media.Brushes.Gray : ok.Value ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;
    }

    // ---------- Connections tab ----------
    private void RefreshConnections()
    {
        _conns = _db.GetConnections();
        ConnList.ItemsSource = null;
        ConnList.ItemsSource = _conns;
        JobConnection.ItemsSource = null;
        JobConnection.ItemsSource = _conns;
        if (_conns.Count > 0 && _selectedConnId == 0)
        {
            ConnList.SelectedIndex = 0;
        }
    }

    private void ConnList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConnList.SelectedItem is not ConnectionProfile p) return;
        _selectedConnId = p.Id;
        ConnName.Text = p.Name; ConnHost.Text = p.Host; ConnPort.Text = p.Port.ToString();
        ConnUser.Text = p.Username; ConnPass.Password = CryptoService.Decrypt(p.EncryptedPassword);
        ConnDb.Text = p.Database; ConnSsl.IsChecked = p.UseSsl;
        ConnTestResult.Text = "";
        ConnDbCheckList.ItemsSource = null; // список относится к серверу — обновите кнопкой ниже
    }

    private ConnectionProfile ReadConnForm(long id)
    {
        int.TryParse(ConnPort.Text.Trim(), out int port);
        if (port <= 0) port = 3306;
        return new ConnectionProfile
        {
            Id = id,
            Name = ConnName.Text.Trim().Length == 0 ? "Новое соединение" : ConnName.Text.Trim(),
            Host = ConnHost.Text.Trim().Length == 0 ? "localhost" : ConnHost.Text.Trim(),
            Port = port,
            Username = ConnUser.Text.Trim(),
            EncryptedPassword = CryptoService.Encrypt(ConnPass.Password),
            Database = ConnDb.Text.Trim(),
            UseSsl = ConnSsl.IsChecked == true,
        };
    }

    private void BtnConnNew_Click(object sender, RoutedEventArgs e)
    {
        _selectedConnId = 0;
        ConnList.SelectedItem = null;
        ConnName.Text = "Новый сервер"; ConnHost.Text = "localhost"; ConnPort.Text = "3306";
        ConnUser.Text = "root"; ConnPass.Password = ""; ConnDb.Text = ""; ConnSsl.IsChecked = false;
        ConnDbCheckList.ItemsSource = null;
    }

    private void BtnConnSave_Click(object sender, RoutedEventArgs e)
    {
        var p = ReadConnForm(_selectedConnId);
        if (string.IsNullOrWhiteSpace(p.Username)) { WpfMessageBox.Show("Укажите пользователя."); return; }
        _selectedConnId = _db.UpsertConnection(p);
        _db.AddLog("INFO", $"Сохранено подключение '{p.Name}'");
        RefreshConnections();
        RefreshLogs();
    }

    private void BtnConnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConnId == 0) return;
        if (WpfMessageBox.Show("Удалить подключение и его задачи?", "Подтверждение", WpfMessageBoxButton.YesNo) != WpfMessageBoxResult.Yes) return;
        _db.DeleteConnection(_selectedConnId);
        _selectedConnId = 0;
        RefreshConnections();
    }

    private async void BtnConnTest_Click(object sender, RoutedEventArgs e)
    {
        var p = ReadConnForm(_selectedConnId);
        ConnTestResult.Text = "Проверка...";
        var (ok, msg) = await MySqlService.TestConnectionAsync(p);
        ConnTestResult.Text = msg;
        SetStatus(ok, ok ? "Соединение OK" : "Ошибка соединения");
    }

    private async void BtnConnLoadDb_Click(object sender, RoutedEventArgs e)
    {
        var p = ReadConnForm(_selectedConnId);
        try
        {
            var dbs = await MySqlService.GetDatabasesAsync(p);
            var selected = Backup.BackupService.ParseDatabaseList(ConnDb.Text);
            ConnDbCheckList.ItemsSource = dbs
                .Select(d => new Models.DbCheckItem
                {
                    Name = d,
                    IsChecked = selected is null // "*" = все отмечены
                        || selected.Contains(d, StringComparer.OrdinalIgnoreCase)
                }).ToList();
            ConnTestResult.Text = $"Найдено БД: {dbs.Count}. Отметьте галочками нужные для бэкапа.";
        }
        catch (Exception ex) { ConnTestResult.Text = "Ошибка: " + ex.Message; }
    }

    private void ConnDbCheck_Changed(object sender, RoutedEventArgs e) => SyncConnDbText();

    private void BtnConnDbAll_Click(object sender, RoutedEventArgs e)
    {
        if (ConnDbCheckList.ItemsSource is IEnumerable<Models.DbCheckItem> items)
            foreach (var i in items) i.IsChecked = true;
        SyncConnDbText();
    }

    private void BtnConnDbNone_Click(object sender, RoutedEventArgs e)
    {
        if (ConnDbCheckList.ItemsSource is IEnumerable<Models.DbCheckItem> items)
            foreach (var i in items) i.IsChecked = false;
        SyncConnDbText();
    }

    /// <summary>Собирает отмеченные базы в ConnDb.Text: все отмечены → "*", иначе через запятую.</summary>
    private void SyncConnDbText()
    {
        if (ConnDbCheckList.ItemsSource is not IEnumerable<Models.DbCheckItem> items) return;
        var list = items.ToList();
        var checkedNames = list.Where(i => i.IsChecked).Select(i => i.Name).ToList();
        ConnDb.Text = checkedNames.Count == 0 ? "" : checkedNames.Count == list.Count ? "*" : string.Join(",", checkedNames);
    }

    // ---------- Jobs / Dashboard ----------
    private void RefreshJobs()
    {
        _jobs = _db.GetJobs();
        JobsGrid.ItemsSource = null;
        JobsGrid.ItemsSource = _jobs;
        if (_selectedJobId != 0)
            JobsGrid.SelectedItem = _jobs.FirstOrDefault(j => j.Id == _selectedJobId);
    }

    private void JobsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (JobsGrid.SelectedItem is not BackupJob j) return;
        _selectedJobId = j.Id;
        JobName.Text = j.Name;
        JobConnection.SelectedItem = _conns.FirstOrDefault(c => c.Id == j.ConnectionId);
        JobDatabase.Text = j.Database;
        JobKind.SelectedItem = j.Kind;
        JobTime.Text = j.TimeOfDay;
        JobDays.Text = j.DaysOfWeek;
        JobDom.Text = j.DayOfMonth.ToString();
        JobInterval.Text = j.IntervalMinutes.ToString();
        JobFolder.Text = j.OutputFolder;
        JobKeepN.Text = j.KeepCopies.ToString();
        JobKeepD.Text = j.KeepDays.ToString();
        JobEnabled.IsChecked = j.Enabled;
    }

    private void BtnRefreshJobs_Click(object sender, RoutedEventArgs e) { RefreshJobs(); RefreshArchives(); RefreshLogs(); }

    private void BtnJobNew_Click(object sender, RoutedEventArgs e)
    {
        _selectedJobId = 0;
        JobsGrid.SelectedItem = null;
        JobName.Text = "Новая задача";
        JobDatabase.Text = "";
        JobFolder.Text = AppState.DefaultBackupDir;
    }

    private void BtnJobBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Forms.FolderBrowserDialog { SelectedPath = JobFolder.Text };
        if (dlg.ShowDialog() == Forms.DialogResult.OK) JobFolder.Text = dlg.SelectedPath;
    }

    private void BtnJobSave_Click(object sender, RoutedEventArgs e)
    {
        if (JobConnection.SelectedItem is not ConnectionProfile cp) { WpfMessageBox.Show("Выберите подключение."); return; }
        // Пустое поле БД задачи = взять выбор из подключения (чек-лист на вкладке «Подключения»)
        string jobDb = JobDatabase.Text.Trim();
        if (string.IsNullOrWhiteSpace(jobDb))
        {
            jobDb = (_db.GetConnection(cp.Id)?.Database ?? "").Trim();
            if (string.IsNullOrWhiteSpace(jobDb)) { WpfMessageBox.Show("Укажите базу данных (или * для всех)."); return; }
            JobDatabase.Text = jobDb;
        }
        if (JobKind.SelectedItem is not ScheduleKind kind) kind = ScheduleKind.Daily;
        int.TryParse(JobDom.Text, out int dom); int.TryParse(JobInterval.Text, out int iv);
        int.TryParse(JobKeepN.Text, out int kn); int.TryParse(JobKeepD.Text, out int kd);
        var j = new BackupJob
        {
            Id = _selectedJobId,
            ConnectionId = cp.Id,
            Name = string.IsNullOrWhiteSpace(JobName.Text) ? $"Бэкап {jobDb}" : JobName.Text.Trim(),
            Database = jobDb,
            Kind = kind,
            TimeOfDay = JobTime.Text.Trim(),
            DaysOfWeek = JobDays.Text.Trim(),
            DayOfMonth = dom <= 0 ? 1 : dom,
            IntervalMinutes = iv <= 0 ? 60 : iv,
            OutputFolder = string.IsNullOrWhiteSpace(JobFolder.Text) ? AppState.DefaultBackupDir : JobFolder.Text.Trim(),
            KeepCopies = kn, KeepDays = kd,
            Enabled = JobEnabled.IsChecked == true,
        };
        j.NextRunUtc = SchedulerService.ComputeNextRun(j, DateTime.Now).ToUniversalTime().ToString("o");
        _selectedJobId = _db.UpsertJob(j);
        _db.AddLog("INFO", $"Сохранена задача '{j.Name}'", j.Name);
        RefreshJobs(); RefreshLogs();
    }

    private void BtnJobDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedJobId == 0) return;
        if (WpfMessageBox.Show("Удалить задачу?", "Подтверждение", WpfMessageBoxButton.YesNo) != WpfMessageBoxResult.Yes) return;
        _db.DeleteJob(_selectedJobId);
        _selectedJobId = 0;
        RefreshJobs();
    }

    private async void BtnBackupNow_Click(object sender, RoutedEventArgs e)
    {
        if (JobsGrid.SelectedItem is not BackupJob j) { WpfMessageBox.Show("Выберите задачу в списке."); return; }
        await RunJobAsync(j.Id);
    }

    // ---------- Backup execution (фон, UI не виснет — ТЗ п.6) ----------
    private async Task RunJobAsync(long jobId)
    {
        if (_busy) { WpfMessageBox.Show("Другая операция уже выполняется. Дождитесь завершения."); return; }
        _busy = true;
        WorkProgress.IsIndeterminate = true;
        try
        {
            var job = _db.GetJobs().FirstOrDefault(x => x.Id == jobId);
            if (job is null) return;
            var profile = _db.GetConnection(job.ConnectionId);
            if (profile is null) { WpfMessageBox.Show("Подключение задачи не найдено."); return; }
            if (!ToolLocator.AreToolsAvailable(out string missingTools))
            {
                string msg = $"Не найдены утилиты: {missingTools}. Откройте «Настройки» и нажмите «Авто-поиск».";
                _db.AddLog("ERROR", $"Бэкап {job.Name} FAILED: {msg}", job.Name);
                _db.UpdateJobStatus(job.Id, "ERROR", DateTime.UtcNow.ToString("o"),
                    SchedulerService.ComputeNextRun(job, DateTime.Now).ToUniversalTime().ToString("o"));
                SetStatus(false, msg);
                return;
            }

            // null = "*" или пусто → все БД кроме системных; иначе выбранный список
            List<string> databases = BackupService.ParseDatabaseList(job.Database)
                ?? await MySqlService.GetDatabasesAsync(profile);
            if (databases.Count == 0) { WpfMessageBox.Show("Не выбрана ни одна база данных."); return; }

            WorkLabel.Text = $"Бэкап: {job.Name}";
            var prog = new Progress<string>(s => WorkLabel.Text = s);
            bool allOk = true;
            foreach (string dbName in databases)
            {
                SetStatus(null, $"Бэкап {dbName}...");
                var res = await Task.Run(() => BackupService.RunBackupAsync(profile, dbName, job.OutputFolder, prog));
                if (res.Ok)
                {
                    _db.AddArchive(job.Id, res.FilePath, res.SizeBytes, DateTime.UtcNow.ToString("o"), "OK");
                    _db.AddLog("OK", $"Бэкап {dbName}: {res.SizeBytes / 1048576.0:F1} МБ за {res.DurationSec:F1} с → {res.FilePath}", job.Name, res.SizeBytes, res.DurationSec);
                }
                else
                {
                    allOk = false;
                    _db.AddLog("ERROR", $"Бэкап {dbName} FAILED: {res.Message}", job.Name);
                }
            }
            RotationService.Apply(_db, job.Id, job.OutputFolder, job.KeepCopies, job.KeepDays);
            var next = SchedulerService.ComputeNextRun(job, DateTime.Now);
            _db.UpdateJobStatus(job.Id, allOk ? "OK" : "ERROR", DateTime.UtcNow.ToString("o"), next.ToUniversalTime().ToString("o"));
            SetStatus(allOk, allOk ? $"Готово: {job.Name}" : $"Ошибка: {job.Name} — см. Логи");
        }
        catch (Exception ex)
        {
            _db.AddLog("ERROR", "RunJob: " + ex.Message);
            SetStatus(false, "Ошибка: " + ex.Message);
        }
        finally
        {
            _busy = false;
            WorkProgress.IsIndeterminate = false;
            WorkLabel.Text = "";
            RefreshJobs(); RefreshArchives(); RefreshLogs();
        }
    }

    // ---------- Scheduler tick ----------
    private void RecalcAllNextRuns()
    {
        foreach (var j in _db.GetJobs())
        {
            if (string.IsNullOrEmpty(j.NextRunUtc))
            {
                var n = SchedulerService.ComputeNextRun(j, DateTime.Now);
                _db.UpdateJobStatus(j.Id, j.LastStatus ?? "-", j.LastRunUtc ?? DateTime.UtcNow.ToString("o"), n.ToUniversalTime().ToString("o"));
            }
        }
        RefreshJobs();
    }

    private async Task TimerTickAsync()
    {
        if (ChkSchedulerEnabled.IsChecked != true || _busy) return;
        var jobs = _db.GetJobs().Where(j => j.Enabled).ToList();
        foreach (var j in jobs)
        {
            if (SchedulerService.IsDue(j, DateTime.Now))
            {
                // сдвигаем NextRun заранее, чтобы не задвоить запуск
                var next = SchedulerService.ComputeNextRun(j, DateTime.Now.AddSeconds(5));
                _db.UpdateJobStatus(j.Id, j.LastStatus ?? "-", j.LastRunUtc ?? DateTime.UtcNow.ToString("o"), next.ToUniversalTime().ToString("o"));
                await RunJobAsync(j.Id);
                break; // по одной задаче за тик
            }
        }
        RefreshJobs();
    }

    // ---------- Archives ----------
    private void RefreshArchives()
    {
        var list = _db.GetArchives(null, 500)
            .Select(a => new { a.Id, a.JobId, a.FilePath, SizeMB = $"{a.SizeBytes / 1048576.0:F2}", a.SizeBytes, a.CreatedUtc, a.Status })
            .ToList();
        ArchGrid.ItemsSource = list;
    }

    private void BtnArchRefresh_Click(object sender, RoutedEventArgs e) => RefreshArchives();

    private dynamic? SelectedArchive() => ArchGrid.SelectedItem;

    private async void BtnArchRestore_Click(object sender, RoutedEventArgs e)
    {
        dynamic? a = SelectedArchive();
        if (a is null) { WpfMessageBox.Show("Выберите архив."); return; }
        string file = (string)a.FilePath;
        if (!File.Exists(file)) { WpfMessageBox.Show("Файл не найден: " + file); return; }
        long jobId = (long)a.JobId;
        var job = _db.GetJobs().FirstOrDefault(x => x.Id == jobId);
        var profile = job is null ? _conns.FirstOrDefault() : _db.GetConnection(job.ConnectionId);
        if (profile is null) { WpfMessageBox.Show("Нет подключений для восстановления."); return; }
        string targetDb = job?.Database ?? "";
        var candidates = BackupService.ParseDatabaseList(targetDb);
        if (candidates is null || candidates.Count != 1)
        {
            // несколько баз / все: уточнить конкретную целевую БД (по умолчанию — первая из списка)
            targetDb = Prompt("Целевая база данных", "Введите имя БД для восстановления:",
                candidates?.FirstOrDefault() ?? "") ?? "";
            if (string.IsNullOrWhiteSpace(targetDb)) return;
        }
        if (WpfMessageBox.Show($"Восстановить `{targetDb}` на {profile.Host} из:{Environment.NewLine}{file}{Environment.NewLine}Данные в целевой БД будут перезаписаны!",
                "Подтверждение восстановления", WpfMessageBoxButton.YesNo, WpfMessageBoxImage.Warning) != WpfMessageBoxResult.Yes) return;
        await DoRestoreAsync(profile, targetDb, file, job?.Name);
    }

    private async void BtnArchFromFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Бэкапы (*.sql;*.gz;*.zip)|*.sql;*.gz;*.zip|Все файлы (*.*)|*.*" };
        if (dlg.ShowDialog() != true) return;
        if (_conns.Count == 0) { WpfMessageBox.Show("Сначала создайте подключение."); return; }
        // используем первое подключение; целевую БД спрашиваем
        var profile = _conns[0];
        string? targetDb = Prompt("Целевая база данных", "Введите имя БД для восстановления:");
        if (string.IsNullOrWhiteSpace(targetDb)) return;
        if (WpfMessageBox.Show($"Восстановить `{targetDb}` из {dlg.FileName}?", "Подтверждение", WpfMessageBoxButton.YesNo, WpfMessageBoxImage.Warning) != WpfMessageBoxResult.Yes) return;
        await DoRestoreAsync(profile, targetDb, dlg.FileName, null);
    }

    private async Task DoRestoreAsync(ConnectionProfile profile, string targetDb, string file, string? jobName)
    {
        if (_busy) { WpfMessageBox.Show("Дождитесь завершения текущей операции."); return; }
        _busy = true;
        WorkProgress.IsIndeterminate = false;
        WorkProgress.Value = 10;
        try
        {
            var pp = new Progress<int>(v => WorkProgress.Value = v);
            var pl = new Progress<string>(s => WorkLabel.Text = s);
            SetStatus(null, $"Восстановление {targetDb}...");
            var res = await Task.Run(() => RestoreService.RunRestoreAsync(profile, targetDb, file, pp, pl));
            _db.AddLog(res.Ok ? "OK" : "ERROR", $"Восстановление {targetDb} из {Path.GetFileName(file)}: {res.Message} ({res.DurationSec:F1} с)", jobName);
            SetStatus(res.Ok, res.Ok ? "Восстановление завершено" : "Ошибка восстановления: " + res.Message);
            WpfMessageBox.Show(res.Ok ? "Восстановление завершено успешно." : "Ошибка: " + res.Message);
        }
        finally { _busy = false; WorkProgress.Value = 0; WorkLabel.Text = ""; RefreshLogs(); }
    }

    private void BtnArchDelete_Click(object sender, RoutedEventArgs e)
    {
        dynamic? a = SelectedArchive();
        if (a is null) return;
        if (WpfMessageBox.Show("Удалить архив (файл + запись)?", "Подтверждение", WpfMessageBoxButton.YesNo) != WpfMessageBoxResult.Yes) return;
        try { if (File.Exists((string)a.FilePath)) File.Delete((string)a.FilePath); } catch (Exception ex) { WpfMessageBox.Show(ex.Message); }
        _db.DeleteArchive((long)(long)a.Id);
        RefreshArchives();
    }

    private void BtnArchOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        dynamic? a = SelectedArchive();
        string? dir = a is null ? null : Path.GetDirectoryName((string)a.FilePath);
        dir ??= AppState.DefaultBackupDir;
        try { Process.Start("explorer.exe", $"\"{dir}\""); } catch (Exception ex) { WpfMessageBox.Show(ex.Message); }
    }

    // ---------- Logs ----------
    private void BtnLogsRefresh_Click(object sender, RoutedEventArgs e) => RefreshLogs();
    private void RefreshLogs()
    {
        LogsGrid.ItemsSource = _db.GetLogs(500);
    }

    // ---------- Settings ----------
    private void RefreshToolsStatus()
    {
        bool ok = ToolLocator.AreToolsAvailable(out string missing);
        string dump = ToolLocator.TryResolveDump() ?? "(не найден)";
        string mysql = ToolLocator.TryResolveMysql() ?? "(не найден)";
        CfgToolsStatus.Text = (ok
            ? "OK: утилиты найдены."
            : $"ВНИМАНИЕ: не найдены: {missing}. Нажмите «Авто-поиск» или укажите пути вручную и сохраните.")
            + $"{Environment.NewLine}mysqldump: {dump}{Environment.NewLine}mysql: {mysql}";
        SetStatus(ok ? (bool?)true : false, ok ? "Утилиты OK" : "Нет mysqldump/mysql — см. Настройки");
    }

    private void BrowseForExe(TextBox target, string exeName)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Исполняемые файлы (*.exe)|*.exe",
            FileName = exeName,
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() == true) target.Text = dlg.FileName;
    }

    private void BtnCfgDumpBrowse_Click(object sender, RoutedEventArgs e) => BrowseForExe(CfgDumpPath, "mysqldump.exe");
    private void BtnCfgMysqlBrowse_Click(object sender, RoutedEventArgs e) => BrowseForExe(CfgMysqlPath, "mysql.exe");

    private void BtnCfgToolsAuto_Click(object sender, RoutedEventArgs e)
    {
        var (dump, mysql) = ToolLocator.AutoDetect();
        if (dump != null) CfgDumpPath.Text = dump;
        if (mysql != null) CfgMysqlPath.Text = mysql;
        if (dump is null && mysql is null)
            WpfMessageBox.Show("Утилиты не найдены. Установите MariaDB/MySQL Client или укажите пути вручную.");
        else
        {
            AppState.SaveToolPaths(CfgDumpPath.Text, CfgMysqlPath.Text);
            RefreshToolsStatus();
            WpfMessageBox.Show("Пути найдены и сохранены.");
        }
    }

    private void BtnCfgToolsSave_Click(object sender, RoutedEventArgs e)
    {
        AppState.SaveToolPaths(CfgDumpPath.Text, CfgMysqlPath.Text);
        RefreshToolsStatus();
        _db.AddLog("INFO", $"Пути утилит: dump='{CfgDumpPath.Text}', mysql='{CfgMysqlPath.Text}'");
        RefreshLogs();
        WpfMessageBox.Show("Пути сохранены.");
    }

    private void BtnCfgBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Forms.FolderBrowserDialog { SelectedPath = CfgDefaultFolder.Text };
        if (dlg.ShowDialog() == Forms.DialogResult.OK) CfgDefaultFolder.Text = dlg.SelectedPath;
    }

    private void BtnCfgSave_Click(object sender, RoutedEventArgs e)
    {
        AppState.SaveDefaultFolder(CfgDefaultFolder.Text.Trim());
        WpfMessageBox.Show("Сохранено.");
    }

    // ---------- helpers ----------
    private static string? Prompt(string title, string label, string defaultValue = "")
    {
        var w = new Window { Title = title, Width = 380, Height = 150, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = WpfApp.Current.MainWindow };
        var sp = new StackPanel { Margin = new Thickness(10) };
        sp.Children.Add(new TextBlock { Text = label });
        var tb = new TextBox { Margin = new Thickness(0, 6, 0, 6), Text = defaultValue };
        sp.Children.Add(tb);
        var btns = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
        var cancel = new Button { Content = "Отмена", Width = 80, IsCancel = true };
        string? result = null;
        ok.Click += (_, _) => { result = tb.Text.Trim(); w.DialogResult = true; w.Close(); };
        btns.Children.Add(ok); btns.Children.Add(cancel);
        sp.Children.Add(btns);
        w.Content = sp;
        return w.ShowDialog() == true ? result : null;
    }
}
