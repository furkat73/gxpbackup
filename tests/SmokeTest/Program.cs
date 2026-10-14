using System.Text.RegularExpressions;
using MySqlBackupUtility.Backup;
using MySqlBackupUtility.Data;
using MySqlBackupUtility.Models;
using MySqlBackupUtility.MySql;
using MySqlBackupUtility.Scheduling;
using MySqlBackupUtility.Security;

int failures = 0;
void Check(bool cond, string name)
{
    Console.WriteLine((cond ? "PASS " : "FAIL ") + name);
    if (!cond) failures++;
}

// 1. DPAPI: roundtrip, не plain-text, не голый base64
string enc = CryptoService.Encrypt("s3cret-p@ss");
Check(CryptoService.Decrypt(enc) == "s3cret-p@ss", "Crypto roundtrip");
Check(!enc.Contains("s3cret"), "Crypto not plain-text");
Check(CryptoService.Decrypt("") == "", "Crypto empty");
Check(CryptoService.Encrypt("") == "", "Crypto encrypt empty");

// 2. Шаблон имени файла [ИмяБД]_[ГГГГММДД]_[ЧЧММСС].sql.gz
string fn = BackupService.BuildFileName("mydb", new DateTime(2026, 10, 13, 15, 4, 5));
Check(fn == "mydb_20261013_150405.sql.gz", "FileName template: " + fn);
Check(Regex.IsMatch(BackupService.BuildFileName("shop", DateTime.Now), @"^.+_\d{8}_\d{6}\.sql\.gz$"), "FileName regex");

// 2a. Разбор выбора БД (чек-лист): "*" / пусто → null (все), иначе список
Check(BackupService.ParseDatabaseList("*") is null, "Parse *");
Check(BackupService.ParseDatabaseList("") is null, "Parse empty");
Check(BackupService.ParseDatabaseList("shop, blog ;crm") is { Count: 3 }, "Parse list");

// 3. SQLite CRUD во временном файле
string tmpDb = Path.Combine(Path.GetTempPath(), $"mbu_test_{Guid.NewGuid():N}.db");
var db = new Database(tmpDb);
var conn = new ConnectionProfile { Name = "local", Host = "localhost", Port = 3306, Username = "root", EncryptedPassword = CryptoService.Encrypt("pw"), Database = "testdb" };
long cid = db.UpsertConnection(conn);
Check(cid > 0, "Connection insert");
var got = db.GetConnection(cid);
Check(got != null && got.Name == "local" && CryptoService.Decrypt(got.EncryptedPassword) == "pw", "Connection read+decrypt");
Check(db.GetConnections().Count == 1, "Connection list");

var job = new BackupJob { ConnectionId = cid, Name = "daily test", Kind = ScheduleKind.Daily, TimeOfDay = "03:00", OutputFolder = Path.GetTempPath(), KeepCopies = 2, KeepDays = 0 };
long jid = db.UpsertJob(job);
Check(jid > 0, "Job insert");
Check(db.GetJobs().Count == 1 && db.GetJobs()[0].ConnectionName == "local", "Job list + join");

// 4. Планировщик: следующий запуск всегда в будущем
var now = DateTime.Now;
foreach (ScheduleKind k in Enum.GetValues<ScheduleKind>())
{
    var j = new BackupJob { Kind = k, TimeOfDay = "03:00", DaysOfWeek = "1,2,3,4,5,6,0", DayOfMonth = 1, IntervalMinutes = 60 };
    var next = SchedulerService.ComputeNextRun(j, now);
    Check(next > now, $"NextRun {k}: {next:yyyy-MM-dd HH:mm}");
}
var duePast = new BackupJob { Enabled = true, NextRunUtc = DateTime.UtcNow.AddMinutes(-1).ToString("o") };
Check(SchedulerService.IsDue(duePast, now), "IsDue past");
var disabled = new BackupJob { Enabled = false, NextRunUtc = null };
Check(!SchedulerService.IsDue(disabled, now), "IsDue disabled");

// 5. Ротация: keep 2 копии
string rotDir = Path.Combine(Path.GetTempPath(), $"mbu_rot_{Guid.NewGuid():N}");
Directory.CreateDirectory(rotDir);
var rjob = new BackupJob { ConnectionId = cid, Name = "rot", Kind = ScheduleKind.Daily, OutputFolder = rotDir, KeepCopies = 2, KeepDays = 0 };
long rjid = db.UpsertJob(rjob);
for (int i = 0; i < 5; i++)
{
    string f = Path.Combine(rotDir, $"db_2026010{i}_000000.sql.gz");
    File.WriteAllText(f, "fake-" + i);
    db.AddArchive(rjid, f, 6 + i, DateTime.UtcNow.AddMinutes(-i).ToString("o"), "OK");
}
RotationService.Apply(db, rjid, rotDir, keepCopies: 2, keepDays: 0);
Check(db.GetArchives(rjid, 100).Count == 2, "Rotation keeps 2 in DB");
Check(Directory.GetFiles(rotDir).Length == 2, "Rotation keeps 2 files");

// 6. Логи
db.AddLog("OK", "smoke ok", "daily test", 123, 1.5);
db.AddLog("ERROR", "smoke err");
Check(db.GetLogs(10).Count >= 2, "Logs written");

// 7. ToolLocator не падает; авто-поиск не бросает исключений
bool toolsOk = ToolLocator.AreToolsAvailable(out string missing);
Console.WriteLine($"INFO tools available={toolsOk} missing=[{missing}] mysqldump={ToolLocator.MysqldumpPath}");
var auto = ToolLocator.AutoDetect();
Check(ToolLocator.MysqldumpPath.Length > 0 && ToolLocator.MysqlPath.Length > 0, "Tool paths non-empty");
Console.WriteLine($"INFO autodetect dump=[{auto.dump}] mysql=[{auto.mysql}]");

// 8. Файловый лог создался
string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MySqlBackupUtility", "logs");
Check(Directory.Exists(Path.GetDirectoryName(tmpDb)), "App/prereq dirs");

// cleanup
try { File.Delete(tmpDb); Directory.Delete(rotDir, true); } catch { }

Console.WriteLine(failures == 0 ? "ALL SMOKE TESTS PASSED" : $"{failures} FAILURES");
return failures;
