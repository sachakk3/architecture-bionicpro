using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ReportsApi;

/// <summary>
/// Reads prepared mart rows through the ClickHouse HTTP interface.
/// All aggregates are computed by the reports_mart DAG; this is only a lookup by the primary key (username, report_date).
/// </summary>
public sealed class ClickHouseReportStore(HttpClient http, IOptions<ClickHouseOptions> options)
{
    // Values are passed as ClickHouse query parameters ({name:Type}), never concatenated into the SQL
    private const string Sql = """
        SELECT
            report_date, serial_number, prosthesis_model, side,
            events_total, movements_count, errors_count, active_minutes,
            first_event_at, last_event_at,
            avg_battery_level, min_battery_level,
            avg_response_ms, p95_response_ms, max_response_ms,
            avg_grip_force_n, max_grip_force_n, loaded_at
        FROM mart.user_usage_report
        WHERE username = {username:String}
          AND report_date BETWEEN {from:Date} AND {to:Date}
        ORDER BY report_date, serial_number
        FORMAT JSONEachRow
        """;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<IReadOnlyList<DailyUsage>> GetDailyUsageAsync(
        string username, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var query = string.Join('&',
            "date_time_output_format=iso",
            "max_execution_time=5",
            $"param_username={Uri.EscapeDataString(username)}",
            $"param_from={from:yyyy-MM-dd}",
            $"param_to={to:yyyy-MM-dd}");

        var ch = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(ch.Url, "/?" + query))
        {
            Content = new StringContent(Sql, Encoding.UTF8, "text/plain"),
        };
        request.Headers.Add("X-ClickHouse-User", ch.User);
        request.Headers.Add("X-ClickHouse-Key", ch.Password);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new ClickHouseException((int)response.StatusCode, error);
        }

        var rows = new List<DailyUsage>();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length > 0)
            {
                rows.Add(JsonSerializer.Deserialize<DailyUsage>(line, Json)!);
            }
        }
        return rows;
    }
}

public sealed class ClickHouseException(int statusCode, string message)
    : Exception($"ClickHouse вернул {statusCode}: {message}");
