-- Staging layer: copies of the sources loaded by Airflow
CREATE DATABASE IF NOT EXISTS stg;
-- Data mart for the reports service
CREATE DATABASE IF NOT EXISTS mart;

-- CRM snapshot without names or contact details (personal data minimization)
CREATE TABLE IF NOT EXISTS stg.crm_prostheses
(
    customer_id      UInt32,
    username         String,
    country          LowCardinality(String),
    serial_number    String,
    prosthesis_model LowCardinality(String),
    side             LowCardinality(String),
    installed_at     Date
)
ENGINE = MergeTree
ORDER BY serial_number;

CREATE TABLE IF NOT EXISTS stg.telemetry_events
(
    event_id      UInt64,
    serial_number String,
    event_time    DateTime('UTC'),
    event_type    LowCardinality(String),
    battery_level UInt8,
    response_ms   UInt32,
    grip_force_n  Nullable(Float32)
)
ENGINE = MergeTree
PARTITION BY toDate(event_time)
ORDER BY (serial_number, event_time)
TTL toDate(event_time) + INTERVAL 90 DAY;

-- Mart: one row = user x prosthesis x day.
-- The sorting key starts with username, so a single user's report reads
-- only that user's granules (point lookup via the primary index).
-- Partition = day: the DAG swaps the partition atomically (REPLACE PARTITION),
-- so re-running the same date never creates duplicates.
CREATE TABLE IF NOT EXISTS mart.user_usage_report
(
    report_date        Date,
    username           String,
    customer_id        UInt32,
    country            LowCardinality(String),
    serial_number      String,
    prosthesis_model   LowCardinality(String),
    side               LowCardinality(String),
    events_total       UInt32,
    movements_count    UInt32,
    errors_count       UInt32,
    active_minutes     UInt32,
    first_event_at     DateTime('UTC'),
    last_event_at      DateTime('UTC'),
    avg_battery_level  Float32,
    min_battery_level  UInt8,
    avg_response_ms    Float32,
    p95_response_ms    Float32,
    max_response_ms    UInt32,
    avg_grip_force_n   Float32,
    max_grip_force_n   Float32,
    loaded_at          DateTime('UTC') DEFAULT now()
)
ENGINE = MergeTree
PARTITION BY report_date
ORDER BY (username, report_date, serial_number)
TTL report_date + INTERVAL 3 YEAR;

-- Buffer with the same structure: a day is built here, then its partition is moved into the mart
CREATE TABLE IF NOT EXISTS mart.user_usage_report_build AS mart.user_usage_report;

-- ETL account (Airflow): writes to stg and mart
CREATE USER IF NOT EXISTS etl IDENTIFIED WITH sha256_password BY 'etl_password';
GRANT SELECT, INSERT, ALTER, TRUNCATE ON stg.* TO etl;
GRANT SELECT, INSERT, ALTER, TRUNCATE ON mart.* TO etl;

-- Reports service account (API): read-only access to the mart
CREATE USER IF NOT EXISTS reports_api IDENTIFIED WITH sha256_password BY 'reports_api_password';
GRANT SELECT ON mart.user_usage_report TO reports_api;
