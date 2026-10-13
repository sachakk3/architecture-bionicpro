-- Airflow account: read-only
CREATE ROLE crm_reader LOGIN PASSWORD 'crm_reader_password';
GRANT SELECT ON customers, prostheses TO crm_reader;
