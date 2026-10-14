using MySqlBackupUtility.Models;

namespace MySqlBackupUtility.Scheduling;

/// <summary>
/// Встроенный планировщик (фоновый таймер в процессе программы).
/// ТЗ п.2.3 требует Task Scheduler/службу; в данном MVP — встроенный планировщик:
/// работает пока программа запущена (в т.ч. свернутая в трей). Проверка каждые 30 сек.
/// </summary>
public static class SchedulerService
{
    public static DateTime ComputeNextRun(BackupJob job, DateTime fromLocal)
    {
        if (!TimeSpan.TryParse(job.TimeOfDay, out var tod))
            tod = new TimeSpan(3, 0, 0);

        switch (job.Kind)
        {
            case ScheduleKind.Daily:
                {
                    var cand = fromLocal.Date + tod;
                    if (cand <= fromLocal) cand = cand.AddDays(1);
                    return cand;
                }
            case ScheduleKind.Weekly:
                {
                    var days = job.DaysOfWeek.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => int.TryParse(s.Trim(), out var d) ? d % 7 : -1)
                        .Where(d => d >= 0).ToHashSet();
                    if (days.Count == 0) days = new HashSet<int> { (int)fromLocal.DayOfWeek };
                    for (int i = 0; i < 8; i++)
                    {
                        var cand = (fromLocal.Date + tod).AddDays(i);
                        if (cand > fromLocal && days.Contains((int)cand.DayOfWeek))
                            return cand;
                    }
                    return fromLocal.AddDays(7);
                }
            case ScheduleKind.Monthly:
                {
                    int dom = Math.Clamp(job.DayOfMonth, 1, 28);
                    var cand = new DateTime(fromLocal.Year, fromLocal.Month, dom) + tod;
                    if (cand <= fromLocal) cand = cand.AddMonths(1);
                    // clamp day if month shorter
                    int dim = DateTime.DaysInMonth(cand.Year, cand.Month);
                    cand = new DateTime(cand.Year, cand.Month, Math.Min(dom, dim)) + tod;
                    if (cand <= fromLocal) // редкий край
                    {
                        var n = cand.AddMonths(1);
                        dim = DateTime.DaysInMonth(n.Year, n.Month);
                        cand = new DateTime(n.Year, n.Month, Math.Min(dom, dim)) + tod;
                    }
                    return cand;
                }
            case ScheduleKind.Interval:
                {
                    int mins = Math.Max(5, job.IntervalMinutes);
                    DateTime baseTime = fromLocal;
                    if (DateTime.TryParse(job.LastRunUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var lastUtc))
                        baseTime = lastUtc.ToLocalTime().AddMinutes(mins);
                    else
                        baseTime = fromLocal.AddMinutes(mins);
                    if (baseTime <= fromLocal) baseTime = fromLocal.AddMinutes(mins);
                    return baseTime;
                }
            default:
                return fromLocal.AddDays(1);
        }
    }

    public static bool IsDue(BackupJob job, DateTime nowLocal)
    {
        if (!job.Enabled) return false;
        if (job.NextRunUtc is null) return true; // ещё не посчитано — пора
        if (DateTime.TryParse(job.NextRunUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var nextUtc))
            return DateTime.UtcNow >= nextUtc.AddSeconds(-5);
        return true;
    }
}
