-- Sprint 8 Double-Entry Ledger, Outbox & Reconciliation Migration (WP-80..WP-82)
-- Sources: Doc/02-ENG §3.6, §6.4; Doc/02B-ENG §2; Doc/02C-ENG §1.3; Doc/06-DATA §4; Doc/08-ENG §10; BR-C-08, BR-C-09, F-25(6)

CREATE SCHEMA IF NOT EXISTS accounts;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'account_type') THEN
        CREATE TYPE account_type AS ENUM ('asset', 'liability', 'equity', 'income', 'expense');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'normal_balance') THEN
        CREATE TYPE normal_balance AS ENUM ('debit', 'credit');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'depreciation_method') THEN
        CREATE TYPE depreciation_method AS ENUM ('straight_line');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'period_status') THEN
        CREATE TYPE period_status AS ENUM ('open', 'closed');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'outbox_channel') THEN
        CREATE TYPE outbox_channel AS ENUM ('queue_consumer', 'webhook_outbound', 'notification');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'outbox_status') THEN
        CREATE TYPE outbox_status AS ENUM ('pending', 'dispatched', 'dead_letter');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'farmbasics_ledger_app') THEN
        CREATE ROLE farmbasics_ledger_app NOLOGIN;
    END IF;
END $$;

-- ============================================================================
-- 1. Chart of Accounts (WP-80 / Doc 02 §6.4 / Doc 06 §4)
-- ============================================================================
CREATE TABLE IF NOT EXISTS accounts.account (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code text NOT NULL UNIQUE,
    name text NOT NULL,
    type account_type NOT NULL,
    parent_account_id uuid REFERENCES accounts.account(id),
    normal_balance normal_balance NOT NULL,
    accountant_signoff_status text NOT NULL DEFAULT 'draft_starter_chart'
);

INSERT INTO accounts.account (code, name, type, normal_balance, accountant_signoff_status) VALUES
    ('1000', 'Cash & Bank Balances', 'asset', 'debit', 'signed_off_starter_v1'),
    ('1010', 'Marketplace Escrow Receivable', 'asset', 'debit', 'signed_off_starter_v1'),
    ('1100', 'Harvested Produce Inventory', 'asset', 'debit', 'signed_off_starter_v1'),
    ('1200', 'Farm Inputs Inventory', 'asset', 'debit', 'signed_off_starter_v1'),
    ('1500', 'Farm Machinery & Equipment', 'asset', 'debit', 'signed_off_starter_v1'),
    ('2000', 'Accounts Payable', 'liability', 'credit', 'signed_off_starter_v1'),
    ('2100', 'Deferred Escrow Liability', 'liability', 'credit', 'signed_off_starter_v1'),
    ('3000', 'Farmer Owner Capital Equity', 'equity', 'credit', 'signed_off_starter_v1'),
    ('3100', 'Retained Farm Earnings', 'equity', 'credit', 'signed_off_starter_v1'),
    ('4000', 'Produce Sales Revenue', 'income', 'credit', 'signed_off_starter_v1'),
    ('4200', 'Grant & Subsidy Income', 'income', 'credit', 'signed_off_starter_v1'),
    ('5000', 'Seed & Planting Material Cost', 'expense', 'debit', 'signed_off_starter_v1'),
    ('5100', 'Fertilizer & Soil Amendment Cost', 'expense', 'debit', 'signed_off_starter_v1'),
    ('5200', 'Agrochemical & Pest Control Cost', 'expense', 'debit', 'signed_off_starter_v1'),
    ('5300', 'Hired Labor & Field Operations Cost', 'expense', 'debit', 'signed_off_starter_v1'),
    ('5400', 'Soil & Solution Lab Testing Fees', 'expense', 'debit', 'signed_off_starter_v1'),
    ('5500', 'Machinery Hire & Transport Cost', 'expense', 'debit', 'signed_off_starter_v1'),
    ('5600', 'Platform Fees & Commission Expense', 'expense', 'debit', 'signed_off_starter_v1'),
    ('5700', 'Equipment Depreciation Expense', 'expense', 'debit', 'signed_off_starter_v1')
ON CONFLICT (code) DO NOTHING;

-- ============================================================================
-- 2. Monthly-Partitioned Append-Only JournalEntry & Lines (BR-C-08 / BR-C-09)
-- ============================================================================
-- Zero status column on journal_entry (02-ENG §6.4): posted entries exist balanced or not at all.
CREATE TABLE IF NOT EXISTS accounts.journal_entry (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    farm_identity_id uuid NOT NULL REFERENCES identity.identity(id),
    farm_organization_id uuid REFERENCES identity.organization(id),
    recorded_by_staff_identity_id uuid REFERENCES identity.identity(id),
    entry_date date NOT NULL,
    description text NOT NULL,
    source_module text NOT NULL,
    source_ref_type text,
    source_ref_id uuid,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (id, entry_date)
) PARTITION BY RANGE (entry_date);

CREATE INDEX IF NOT EXISTS idx_je_source ON accounts.journal_entry(source_module, source_ref_type, source_ref_id);
CREATE INDEX IF NOT EXISTS idx_je_farm ON accounts.journal_entry(farm_identity_id, entry_date DESC);

DO $$
DECLARE
    m int;
    part_start date;
    part_end date;
    part_name text;
BEGIN
    FOR m IN -6..6 LOOP
        part_start := (date_trunc('month', now() AT TIME ZONE 'UTC') + make_interval(months => m))::date;
        part_end := (part_start + interval '1 month')::date;
        part_name := 'journal_entry_' || to_char(part_start, 'YYYY_MM');
        EXECUTE format(
            'CREATE TABLE IF NOT EXISTS accounts.%I PARTITION OF accounts.journal_entry FOR VALUES FROM (%L) TO (%L);',
            part_name,
            part_start,
            part_end
        );
    END LOOP;
END $$;

CREATE TABLE IF NOT EXISTS accounts.journal_entry_line (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    journal_entry_id uuid NOT NULL,
    journal_entry_date date NOT NULL,
    account_id uuid NOT NULL REFERENCES accounts.account(id),
    debit_amount numeric(14,2) NOT NULL DEFAULT 0 CHECK (debit_amount >= 0),
    credit_amount numeric(14,2) NOT NULL DEFAULT 0 CHECK (credit_amount >= 0),
    crop_cycle_id uuid REFERENCES field.crop_cycle(id),
    production_unit_id uuid REFERENCES field.production_unit(id),
    FOREIGN KEY (journal_entry_id, journal_entry_date) REFERENCES accounts.journal_entry(id, entry_date),
    CONSTRAINT chk_line_one_sided CHECK (
        (debit_amount > 0 AND credit_amount = 0) OR (credit_amount > 0 AND debit_amount = 0)
    )
);

CREATE INDEX IF NOT EXISTS idx_jel_account ON accounts.journal_entry_line(account_id);
CREATE INDEX IF NOT EXISTS idx_jel_cropcycle ON accounts.journal_entry_line(crop_cycle_id) WHERE crop_cycle_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_jel_unit ON accounts.journal_entry_line(production_unit_id) WHERE production_unit_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_jel_entry ON accounts.journal_entry_line(journal_entry_id, journal_entry_date);

-- BR-C-08: Deferred constraint trigger rejects unbalanced entries at transaction COMMIT.
CREATE OR REPLACE FUNCTION accounts.check_journal_balance()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    imbalance numeric(14,2);
BEGIN
    SELECT COALESCE(SUM(debit_amount) - SUM(credit_amount), 0) INTO imbalance
    FROM accounts.journal_entry_line
    WHERE journal_entry_id = NEW.journal_entry_id
      AND journal_entry_date = NEW.journal_entry_date;

    IF imbalance <> 0 THEN
        RAISE EXCEPTION 'Journal entry % (%) is unbalanced by % (BR-C-08)',
            NEW.journal_entry_id, NEW.journal_entry_date, imbalance
            USING ERRCODE = '23514';
    END IF;
    RETURN NULL;
END;
$$;

DROP TRIGGER IF EXISTS trg_journal_balance ON accounts.journal_entry_line;
CREATE CONSTRAINT TRIGGER trg_journal_balance
    AFTER INSERT ON accounts.journal_entry_line
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW
    EXECUTE FUNCTION accounts.check_journal_balance();

-- F-25(6): Deferred constraint trigger prevents empty header-only journal entries without lines.
CREATE OR REPLACE FUNCTION accounts.check_journal_entry_not_empty()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    line_count integer;
BEGIN
    SELECT COUNT(*)::int INTO line_count
    FROM accounts.journal_entry_line
    WHERE journal_entry_id = NEW.id
      AND journal_entry_date = NEW.entry_date;

    IF line_count < 2 THEN
        RAISE EXCEPTION 'Journal entry % (%) must contain at least 2 balanced lines (F-25(6))',
            NEW.id, NEW.entry_date
            USING ERRCODE = '23514';
    END IF;
    RETURN NULL;
END;
$$;

DROP TRIGGER IF EXISTS trg_journal_entry_not_empty ON accounts.journal_entry;
CREATE CONSTRAINT TRIGGER trg_journal_entry_not_empty
    AFTER INSERT ON accounts.journal_entry
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW
    EXECUTE FUNCTION accounts.check_journal_entry_not_empty();

-- BR-C-09: Ledger immutability enforced at BOTH database trigger level and PostgreSQL privilege level.
CREATE OR REPLACE FUNCTION accounts.enforce_ledger_append_only()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'accounts ledger tables are strictly append-only (BR-C-09): % is forbidden', TG_OP
        USING ERRCODE = '42501';
END;
$$;

DROP TRIGGER IF EXISTS trg_journal_entry_immutable ON accounts.journal_entry;
CREATE TRIGGER trg_journal_entry_immutable
    BEFORE UPDATE OR DELETE ON accounts.journal_entry
    FOR EACH ROW
    EXECUTE FUNCTION accounts.enforce_ledger_append_only();

DROP TRIGGER IF EXISTS trg_journal_entry_line_immutable ON accounts.journal_entry_line;
CREATE TRIGGER trg_journal_entry_line_immutable
    BEFORE UPDATE OR DELETE ON accounts.journal_entry_line
    FOR EACH ROW
    EXECUTE FUNCTION accounts.enforce_ledger_append_only();

REVOKE ALL ON accounts.journal_entry, accounts.journal_entry_line FROM PUBLIC;
GRANT USAGE ON SCHEMA accounts TO farmbasics_ledger_app;
GRANT SELECT ON accounts.account TO farmbasics_ledger_app;
GRANT SELECT, INSERT ON accounts.journal_entry, accounts.journal_entry_line TO farmbasics_ledger_app;
REVOKE UPDATE, DELETE, TRUNCATE ON accounts.journal_entry, accounts.journal_entry_line FROM farmbasics_ledger_app;

-- ============================================================================
-- 3. Fixed Assets, Periods, Cached Balances & Reconciliation Alerts (WP-82)
-- ============================================================================
CREATE TABLE IF NOT EXISTS accounts.fixed_asset (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    farm_identity_id uuid NOT NULL REFERENCES identity.identity(id),
    name text NOT NULL,
    acquisition_cost numeric(14,2) NOT NULL CHECK (acquisition_cost > 0),
    acquisition_date date NOT NULL,
    useful_life_months integer NOT NULL CHECK (useful_life_months > 0),
    salvage_value numeric(14,2) NOT NULL DEFAULT 0 CHECK (salvage_value >= 0),
    depreciation_method depreciation_method NOT NULL DEFAULT 'straight_line',
    linked_production_unit_id uuid REFERENCES field.production_unit(id)
);

CREATE TABLE IF NOT EXISTS accounts.accounting_period (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    farm_identity_id uuid NOT NULL REFERENCES identity.identity(id),
    start_date date NOT NULL,
    end_date date NOT NULL,
    status period_status NOT NULL DEFAULT 'open',
    closed_at timestamptz,
    CONSTRAINT chk_period_range CHECK (end_date > start_date)
);

CREATE INDEX IF NOT EXISTS idx_accounting_period_farm_dates
    ON accounts.accounting_period(farm_identity_id, start_date, end_date);

CREATE TABLE IF NOT EXISTS accounts.account_balance (
    farm_identity_id uuid NOT NULL REFERENCES identity.identity(id),
    account_id uuid NOT NULL REFERENCES accounts.account(id),
    period_id uuid NOT NULL REFERENCES accounts.accounting_period(id),
    opening_balance numeric(14,2) NOT NULL DEFAULT 0,
    debits numeric(14,2) NOT NULL DEFAULT 0,
    credits numeric(14,2) NOT NULL DEFAULT 0,
    closing_balance numeric(14,2) NOT NULL DEFAULT 0,
    PRIMARY KEY (farm_identity_id, account_id, period_id)
);

CREATE TABLE IF NOT EXISTS accounts.reconciliation_alert (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    farm_identity_id uuid NOT NULL REFERENCES identity.identity(id),
    account_id uuid NOT NULL REFERENCES accounts.account(id),
    period_id uuid NOT NULL REFERENCES accounts.accounting_period(id),
    cached_closing_balance numeric(14,2) NOT NULL,
    computed_closing_balance numeric(14,2) NOT NULL,
    drift_amount numeric(14,2) NOT NULL,
    severity text NOT NULL DEFAULT 'CRITICAL',
    resolved boolean NOT NULL DEFAULT false,
    detected_at timestamptz NOT NULL DEFAULT now()
);

-- ============================================================================
-- 4. Transactional Outbox & Exactly-Once Ledger Posting Guard (WP-81 / 02-ENG §3.6)
-- ============================================================================
CREATE TABLE IF NOT EXISTS platform.events_outbox (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    aggregate_type text NOT NULL,
    aggregate_id uuid NOT NULL,
    event_type text NOT NULL,
    payload jsonb NOT NULL,
    channel outbox_channel NOT NULL DEFAULT 'queue_consumer',
    status outbox_status NOT NULL DEFAULT 'pending',
    attempts integer NOT NULL DEFAULT 0,
    available_at timestamptz NOT NULL DEFAULT now(),
    last_error text,
    dispatched_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_outbox_pending
    ON platform.events_outbox(available_at)
    WHERE status = 'pending';

CREATE TABLE IF NOT EXISTS accounts.posted_outbox_event (
    outbox_event_id uuid PRIMARY KEY,
    journal_entry_id uuid NOT NULL,
    journal_entry_date date NOT NULL,
    posted_at timestamptz NOT NULL DEFAULT now()
);

-- Seed Sprint 8 admin permissions
INSERT INTO admin.admin_permission (resource, action) VALUES
    ('accounts', 'manage'),
    ('accounts', 'reconcile')
ON CONFLICT (resource, action) DO NOTHING;

INSERT INTO admin.admin_role_permission (admin_role_id, permission_id)
SELECT r.id, p.id
FROM admin.admin_role r
CROSS JOIN admin.admin_permission p
WHERE r.code = 'SuperAdmin'
ON CONFLICT DO NOTHING;

INSERT INTO admin.admin_role_permission (admin_role_id, permission_id)
SELECT r.id, p.id
FROM admin.admin_role r
JOIN admin.admin_permission p ON p.resource = 'accounts' AND p.action IN ('manage', 'reconcile')
WHERE r.code = 'Finance'
ON CONFLICT DO NOTHING;
