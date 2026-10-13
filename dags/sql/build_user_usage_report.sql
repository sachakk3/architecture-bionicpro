-- Aggregates one day of telemetry per customer and prosthesis.
-- The date is passed as the day parameter (ClickHouse server-side binding).
INSERT INTO mart.user_usage_report_build
SELECT
    toDate(e.event_time)                                  AS report_date,
    p.username,
    p.customer_id,
    p.country,
    e.serial_number,
    p.prosthesis_model,
    p.side,
    count()                                               AS events_total,
    countIf(e.event_type = 'movement')                    AS movements_count,
    countIf(e.event_type = 'error')                       AS errors_count,
    uniqExact(toStartOfMinute(e.event_time))              AS active_minutes,
    min(e.event_time)                                     AS first_event_at,
    max(e.event_time)                                     AS last_event_at,
    avg(e.battery_level)                                  AS avg_battery_level,
    min(e.battery_level)                                  AS min_battery_level,
    avg(e.response_ms)                                    AS avg_response_ms,
    quantile(0.95)(e.response_ms)                         AS p95_response_ms,
    max(e.response_ms)                                    AS max_response_ms,
    ifNull(avgIf(e.grip_force_n, e.event_type = 'movement'), 0) AS avg_grip_force_n,
    ifNull(maxIf(e.grip_force_n, e.event_type = 'movement'), 0) AS max_grip_force_n,
    now()                                                 AS loaded_at
FROM stg.telemetry_events AS e
INNER JOIN stg.crm_prostheses AS p ON p.serial_number = e.serial_number
WHERE toDate(e.event_time) = {day:Date}
GROUP BY report_date, p.username, p.customer_id, p.country,
         e.serial_number, p.prosthesis_model, p.side
