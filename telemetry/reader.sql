-- Airflow account: read-only
CREATE ROLE telemetry_reader LOGIN PASSWORD 'telemetry_reader_password';
GRANT SELECT ON telemetry_events TO telemetry_reader;
