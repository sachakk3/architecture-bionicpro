-- Raw prosthesis telemetry events (received via the API)
CREATE TABLE telemetry_events (
    event_id      BIGSERIAL PRIMARY KEY,
    serial_number TEXT        NOT NULL,
    event_time    TIMESTAMPTZ NOT NULL,
    event_type    TEXT        NOT NULL CHECK (event_type IN ('movement', 'error')),
    battery_level SMALLINT    NOT NULL,   -- %
    response_ms   INT         NOT NULL,   -- actuator response latency
    grip_force_n  NUMERIC(6, 2)           -- grip force, N (movement events only)
);
CREATE INDEX telemetry_events_time_idx ON telemetry_events (event_time);

-- Demo data: events for the last 14 days, every 10 minutes from 07:00 to 22:00
INSERT INTO telemetry_events (serial_number, event_time, event_type, battery_level, response_ms, grip_force_n)
SELECT s.serial_number,
       t.ts + make_interval(secs => floor(random() * 600)::int),
       CASE WHEN random() < 0.02 THEN 'error' ELSE 'movement' END,
       greatest(5, 100 - (extract(hour FROM t.ts)::int - 7) * 6 - floor(random() * 5)::int),
       60 + floor(random() * 80)::int,
       round((5 + random() * 40)::numeric, 2)
FROM (VALUES ('BP-HAND-0001'), ('BP-HAND-0002'), ('BP-ARM-0003'), ('BP-HAND-0004')) AS s (serial_number)
CROSS JOIN generate_series((current_date - 14)::timestamp, (current_date - 1) + time '21:50', interval '10 minutes') AS t (ts)
WHERE extract(hour FROM t.ts) BETWEEN 7 AND 21
  AND random() < 0.8;
