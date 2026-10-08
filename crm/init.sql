-- CRM: customers and their installed prostheses (source for the reports mart)
CREATE TABLE customers (
    customer_id  SERIAL PRIMARY KEY,
    username     TEXT        NOT NULL UNIQUE, -- Keycloak/LDAP login (preferred_username)
    full_name    TEXT        NOT NULL,
    email        TEXT        NOT NULL,
    phone        TEXT,
    country      CHAR(2)     NOT NULL,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE prostheses (
    serial_number TEXT PRIMARY KEY,
    customer_id   INT  NOT NULL REFERENCES customers (customer_id),
    model         TEXT NOT NULL,
    side          TEXT NOT NULL CHECK (side IN ('left', 'right')),
    installed_at  DATE NOT NULL
);

INSERT INTO customers (username, full_name, email, phone, country) VALUES
    ('prothetic1', 'Prothetic One',   'prothetic1@example.com', '+7 900 000-00-01', 'RU'),
    ('prothetic2', 'Prothetic Two',   'prothetic2@example.com', '+7 700 000-00-02', 'KZ'),
    ('prothetic3', 'Prothetic Three', 'prothetic3@example.com', '+49 30 0000003',   'DE');

INSERT INTO prostheses (serial_number, customer_id, model, side, installed_at) VALUES
    ('BP-HAND-0001', 1, 'BionicPRO Hand v2', 'right', '2025-03-14'),
    ('BP-HAND-0002', 2, 'BionicPRO Hand v2', 'left',  '2025-06-02'),
    ('BP-ARM-0003',  3, 'BionicPRO Arm v1',  'right', '2025-09-21'),
    ('BP-HAND-0004', 3, 'BionicPRO Hand v3', 'left',  '2026-02-10');
