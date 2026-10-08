namespace ReportsApi;

/// <summary>A row of the mart.user_usage_report mart: user x prosthesis x day.</summary>
public sealed record DailyUsage(
    DateOnly ReportDate,
    string SerialNumber,
    string ProsthesisModel,
    string Side,
    uint EventsTotal,
    uint MovementsCount,
    uint ErrorsCount,
    uint ActiveMinutes,
    DateTime FirstEventAt,
    DateTime LastEventAt,
    float AvgBatteryLevel,
    byte MinBatteryLevel,
    float AvgResponseMs,
    float P95ResponseMs,
    uint MaxResponseMs,
    float AvgGripForceN,
    float MaxGripForceN,
    DateTime LoadedAt);

public sealed record ReportTotals(
    int DaysWithActivity,
    long EventsTotal,
    long MovementsCount,
    long ErrorsCount,
    long ActiveMinutes);

public sealed record UsageReport(
    string Username,
    DateOnly From,
    DateOnly To,
    DateTime? DataUpdatedAt,
    ReportTotals Totals,
    IReadOnlyList<DailyUsage> Days)
{
    public static UsageReport Create(string username, DateOnly from, DateOnly to, IReadOnlyList<DailyUsage> days) =>
        new(
            username,
            from,
            to,
            days.Count == 0 ? null : days.Max(d => d.LoadedAt),
            new ReportTotals(
                days.Select(d => d.ReportDate).Distinct().Count(),
                days.Sum(d => (long)d.EventsTotal),
                days.Sum(d => (long)d.MovementsCount),
                days.Sum(d => (long)d.ErrorsCount),
                days.Sum(d => (long)d.ActiveMinutes)),
            days);
}
