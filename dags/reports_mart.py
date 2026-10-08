"""Daily build of the mart.user_usage_report reporting mart in ClickHouse.

extract_crm ────────┐
                    ├─> transform_join ─> load_mart ─> check_quality
extract_telemetry ──┘

The 02:00 UTC run builds the report for the previous calendar day (UTC),
leaving two hours for late prosthesis telemetry to arrive. Every run is
idempotent: the day's partition in the mart is swapped atomically.
"""

from __future__ import annotations

import logging
from datetime import date, timedelta
from pathlib import Path

import clickhouse_connect
import pendulum
from airflow.decorators import dag, task
from airflow.hooks.base import BaseHook
from airflow.providers.postgres.hooks.postgres import PostgresHook

log = logging.getLogger(__name__)

CRM_CONN_ID = "crm_db"
TELEMETRY_CONN_ID = "telemetry_db"
CLICKHOUSE_CONN_ID = "clickhouse_olap"
BATCH_SIZE = 50_000

CRM_COLUMNS = [
    "customer_id", "username", "country", "serial_number",
    "prosthesis_model", "side", "installed_at",
]
TELEMETRY_COLUMNS = [
    "event_id", "serial_number", "event_time", "event_type",
    "battery_level", "response_ms", "grip_force_n",
]

BUILD_MART_SQL = (Path(__file__).parent / "sql" / "build_user_usage_report.sql").read_text()


def clickhouse():
    conn = BaseHook.get_connection(CLICKHOUSE_CONN_ID)
    return clickhouse_connect.get_client(
        host=conn.host,
        port=conn.port or 8123,
        username=conn.login,
        password=conn.password,
    )


def partition_id(day: str) -> str:
    # Date partitions have IDs like YYYYMMDD; fromisoformat also validates the string
    return date.fromisoformat(day).strftime("%Y%m%d")


@dag(
    dag_id="reports_mart",
    description="Телеметрия + CRM → витрина отчётов в ClickHouse",
    schedule="0 2 * * *",
    start_date=pendulum.datetime(2026, 9, 25, 2, tz="UTC"),
    catchup=True,
    is_paused_upon_creation=False,
    max_active_runs=1,
    default_args={
        "owner": "bionicpro-data",
        "retries": 3,
        "retry_delay": timedelta(minutes=5),
        "retry_exponential_backoff": True,
    },
    tags=["reports", "clickhouse"],
)
def reports_mart():
    @task
    def report_day(data_interval_end: pendulum.DateTime | None = None) -> str:
        return data_interval_end.subtract(days=1).to_date_string()

    @task
    def extract_crm() -> int:
        """Full CRM snapshot: prostheses and their owners, without names or contact details."""
        rows = PostgresHook(postgres_conn_id=CRM_CONN_ID).get_records(
            """
            SELECT c.customer_id, c.username, c.country, p.serial_number,
                   p.model, p.side, p.installed_at
            FROM prostheses p
            JOIN customers c USING (customer_id)
            """
        )
        ch = clickhouse()
        ch.command("TRUNCATE TABLE stg.crm_prostheses")
        if rows:
            ch.insert("stg.crm_prostheses", rows, column_names=CRM_COLUMNS)
        log.info("CRM: загружено %d протезов", len(rows))
        return len(rows)

    @task
    def extract_telemetry(day: str) -> int:
        """Raw events for the day; the day's partition in stg is overwritten."""
        ch = clickhouse()
        ch.command(f"ALTER TABLE stg.telemetry_events DROP PARTITION ID '{partition_id(day)}'")

        conn = PostgresHook(postgres_conn_id=TELEMETRY_CONN_ID).get_conn()
        total = 0
        try:
            # Server-side cursor: the day's events are read in batches, not all into memory
            with conn.cursor(name="telemetry_day") as cur:
                cur.execute(
                    f"""
                    SELECT event_id, serial_number, event_time, event_type,
                           battery_level, response_ms, grip_force_n::float8
                    FROM telemetry_events
                    WHERE event_time >= %(day)s::timestamp AT TIME ZONE 'UTC'
                      AND event_time <  (%(day)s::date + 1)::timestamp AT TIME ZONE 'UTC'
                    """,
                    {"day": day},
                )
                while batch := cur.fetchmany(BATCH_SIZE):
                    ch.insert("stg.telemetry_events", batch, column_names=TELEMETRY_COLUMNS)
                    total += len(batch)
        finally:
            conn.close()
        log.info("Телеметрия за %s: загружено %d событий", day, total)
        return total

    @task
    def transform_join(day: str) -> int:
        """Aggregates telemetry per customer and prosthesis into the buffer table."""
        ch = clickhouse()
        ch.command(f"ALTER TABLE mart.user_usage_report_build DROP PARTITION ID '{partition_id(day)}'")
        ch.command(BUILD_MART_SQL, parameters={"day": day})
        rows = ch.command(
            "SELECT count() FROM mart.user_usage_report_build WHERE report_date = {day:Date}",
            parameters={"day": day},
        )
        log.info("Витрина за %s: собрано %d строк", day, rows)
        return rows

    @task
    def load_mart(day: str, built_rows: int) -> None:
        """Atomically swaps the day's partition: readers see either the old or the new data."""
        ch = clickhouse()
        pid = partition_id(day)
        if built_rows:
            ch.command(
                f"ALTER TABLE mart.user_usage_report "
                f"REPLACE PARTITION ID '{pid}' FROM mart.user_usage_report_build"
            )
        else:
            ch.command(f"ALTER TABLE mart.user_usage_report DROP PARTITION ID '{pid}'")
        ch.command(f"ALTER TABLE mart.user_usage_report_build DROP PARTITION ID '{pid}'")

    @task
    def check_quality(day: str) -> None:
        ch = clickhouse()
        params = {"day": day}
        mart_events, mart_rows, mart_keys = ch.query(
            """
            SELECT sum(events_total), count(), uniqExact(username, serial_number)
            FROM mart.user_usage_report WHERE report_date = {day:Date}
            """,
            parameters=params,
        ).first_row
        stg_events, orphan_events = ch.query(
            """
            SELECT countIf(p.serial_number != ''), countIf(p.serial_number = '')
            FROM stg.telemetry_events AS e
            LEFT JOIN stg.crm_prostheses AS p ON p.serial_number = e.serial_number
            WHERE toDate(e.event_time) = {day:Date}
            """,
            parameters=params,
        ).first_row

        if mart_rows != mart_keys:
            raise ValueError(f"{day}: дубли в витрине ({mart_rows} строк, {mart_keys} ключей)")
        if mart_events != stg_events:
            raise ValueError(f"{day}: в витрине {mart_events} событий, в stg {stg_events}")
        if orphan_events:
            log.warning("%s: %d событий от протезов, которых нет в CRM", day, orphan_events)
        log.info("%s: проверка пройдена, %d строк, %d событий", day, mart_rows, mart_events)

    day = report_day()
    built = transform_join(day)
    [extract_crm(), extract_telemetry(day)] >> built
    load_mart(day, built) >> check_quality(day)


reports_mart()
