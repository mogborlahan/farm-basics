-- Sprint 1 Foundation Migration (WP-10..WP-14)
-- Sources: Doc/02-ENG §3.1, §3.4, §3.6, §7, §9; PRECEDENCE #15, #16; ADR-003, ADR-008; F-25(1); Rules 6, 7, 8

CREATE SCHEMA IF NOT EXISTS identity;
CREATE SCHEMA IF NOT EXISTS admin;
CREATE SCHEMA IF NOT EXISTS audit;
CREATE SCHEMA IF NOT EXISTS platform;

-- ============================================================================
-- 1. Domain Enums (02-ENG §3.1 + 02C-ENG / ADR-003, §7)
-- ============================================================================
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'identity_status') THEN
        CREATE TYPE identity_status AS ENUM ('pending_verification', 'active', 'suspended', 'deactivated');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'role_type') THEN
        -- BRD A-RB-01: never includes platform_admin or platform_reviewer
        CREATE TYPE role_type AS ENUM ('farmer', 'agent', 'vendor', 'buyer', 'org_admin', 'regulator');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'organization_type') THEN
        CREATE TYPE organization_type AS ENUM (
            'ngo', 'government_agency', 'private_agribusiness', 'cooperative',
            'vendor_company', 'buyer_company', 'agency', 'soil_test_lab', 'commercial_farm'
        );
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'org_verification_status') THEN
        CREATE TYPE org_verification_status AS ENUM ('pending_verification', 'verified', 'rejected');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'fob_status') THEN
        CREATE TYPE fob_status AS ENUM ('active', 'lost', 'revoked', 'replaced');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'device_status') THEN
        CREATE TYPE device_status AS ENUM ('active', 'lost', 'deactivated');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'trust_tier') THEN
        CREATE TYPE trust_tier AS ENUM ('unverified', 'verified', 'trusted');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'consent_purpose') THEN
        CREATE TYPE consent_purpose AS ENUM (
            'certification_submission', 'land_history_sharing', 'vendor_listing_disclosure',
            'marketing_contact', 'analytics_and_model_training'
        );
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'scope_type') THEN
        CREATE TYPE scope_type AS ENUM ('global', 'region', 'organization');
    END IF;
END $$;

-- ============================================================================
-- 2. Geography Hierarchy (WP-10 / 02-ENG §3.1 / 06-DATA §2)
-- ============================================================================
CREATE TABLE IF NOT EXISTS identity.state (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    name text NOT NULL UNIQUE,
    code varchar(4) NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS identity.lga (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    state_id uuid NOT NULL REFERENCES identity.state(id),
    name text NOT NULL,
    code varchar(8) NOT NULL,
    UNIQUE (state_id, name),
    UNIQUE (state_id, code)
);

CREATE TABLE IF NOT EXISTS identity.ward (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    lga_id uuid NOT NULL REFERENCES identity.lga(id),
    name text NOT NULL,
    code varchar(16),
    UNIQUE (lga_id, name)
);

CREATE TABLE IF NOT EXISTS identity.community (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    ward_id uuid NOT NULL REFERENCES identity.ward(id),
    name text NOT NULL,
    UNIQUE (ward_id, name)
);

-- ============================================================================
-- 3. Identity, Organization, RoleProfile, Fob, Tablet, Consent (WP-11)
-- ============================================================================
CREATE TABLE IF NOT EXISTS identity.organization (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    name text NOT NULL,
    type organization_type NOT NULL,
    registration_number text,
    verification_status org_verification_status NOT NULL DEFAULT 'pending_verification',
    primary_contact_identity_id uuid,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz
);

CREATE TABLE IF NOT EXISTS identity.identity (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    nin_verification_ref text UNIQUE,
    first_name text NOT NULL DEFAULT '',
    last_name text NOT NULL DEFAULT '',
    date_of_birth date,
    -- PRECEDENCE #15: phone is non-unique (shared household phones permitted); keep non-unique index.
    phone_primary text NOT NULL DEFAULT '',
    phone_verified_at timestamptz,
    email_primary text UNIQUE,
    email_verified_at timestamptz,
    -- PRECEDENCE #16 & ADR-008: zero local password_hash column (Keycloak owns credentials).
    state_id uuid REFERENCES identity.state(id),
    lga_id uuid REFERENCES identity.lga(id),
    ward_id uuid REFERENCES identity.ward(id),
    community_id uuid REFERENCES identity.community(id),
    status identity_status NOT NULL DEFAULT 'pending_verification',
    merged_into_identity_id uuid REFERENCES identity.identity(id),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz
);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_schema = 'identity'
          AND table_name = 'organization'
          AND constraint_name = 'fk_org_primary_contact'
    ) THEN
        ALTER TABLE identity.organization
            ADD CONSTRAINT fk_org_primary_contact
            FOREIGN KEY (primary_contact_identity_id) REFERENCES identity.identity(id);
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_identity_phone ON identity.identity(phone_primary);
CREATE INDEX IF NOT EXISTS idx_identity_status ON identity.identity(status) WHERE status <> 'active';

CREATE TABLE IF NOT EXISTS identity.role_profile (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    identity_id uuid NOT NULL REFERENCES identity.identity(id),
    role_type role_type NOT NULL,
    role_specific_id text,
    linked_organization_id uuid REFERENCES identity.organization(id),
    created_at timestamptz NOT NULL DEFAULT now(),
    -- F-25(1): NULLS NOT DISTINCT prevents duplicate independent role profiles when linked_organization_id IS NULL.
    CONSTRAINT uq_role_profile_identity_role_org
        UNIQUE NULLS NOT DISTINCT (identity_id, role_type, linked_organization_id)
);

CREATE INDEX IF NOT EXISTS idx_role_profile_identity ON identity.role_profile(identity_id);

CREATE TABLE IF NOT EXISTS identity.fob_credential (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    fob_serial text NOT NULL UNIQUE,
    farmer_identity_id uuid NOT NULL REFERENCES identity.identity(id),
    status fob_status NOT NULL DEFAULT 'active',
    issued_by_agent_id uuid NOT NULL REFERENCES identity.identity(id),
    replaces_fob_id uuid REFERENCES identity.fob_credential(id),
    issued_at timestamptz NOT NULL DEFAULT now(),
    revoked_at timestamptz,
    CONSTRAINT chk_fob_irrecoverable CHECK (
        NOT (status = 'active' AND revoked_at IS NOT NULL)
    )
);

CREATE INDEX IF NOT EXISTS idx_fob_farmer ON identity.fob_credential(farmer_identity_id) WHERE status = 'active';

CREATE OR REPLACE FUNCTION identity.enforce_fob_irrecoverable()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF OLD.status IN ('lost', 'revoked', 'replaced') AND NEW.status = 'active' THEN
        RAISE EXCEPTION 'Revoked, lost, or replaced fobs cannot return to active status (Rule 12 / BR-A-02)'
            USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_fob_credential_irrecoverable ON identity.fob_credential;
CREATE TRIGGER trg_fob_credential_irrecoverable
    BEFORE UPDATE ON identity.fob_credential
    FOR EACH ROW
    EXECUTE FUNCTION identity.enforce_fob_irrecoverable();

CREATE TABLE IF NOT EXISTS identity.tablet_device (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    device_serial text NOT NULL UNIQUE,
    assigned_agent_id uuid REFERENCES identity.identity(id),
    status device_status NOT NULL DEFAULT 'active'
);

CREATE TABLE IF NOT EXISTS identity.trust_profile (
    identity_id uuid PRIMARY KEY REFERENCES identity.identity(id),
    organization_id uuid REFERENCES identity.organization(id),
    verification_tier trust_tier NOT NULL DEFAULT 'unverified',
    reliability_score numeric(5,4),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT chk_trust_subject CHECK (
        (identity_id IS NOT NULL)::int + (organization_id IS NOT NULL)::int >= 1
    )
);

CREATE TABLE IF NOT EXISTS identity.consent_grant (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    identity_id uuid NOT NULL REFERENCES identity.identity(id),
    purpose consent_purpose NOT NULL,
    subject_ref_type text,
    subject_ref_id uuid,
    granted_at timestamptz NOT NULL DEFAULT now(),
    granted_via text NOT NULL,
    withdrawn_at timestamptz,
    withdrawal_reason text,
    expires_at timestamptz,
    CONSTRAINT chk_consent_subject CHECK (
        (subject_ref_type IS NULL AND subject_ref_id IS NULL) OR
        (subject_ref_type IS NOT NULL AND subject_ref_id IS NOT NULL)
    ),
    CONSTRAINT chk_consent_not_withdrawn_before_granted CHECK (
        withdrawn_at IS NULL OR withdrawn_at >= granted_at
    )
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_one_live_consent_per_subject
    ON identity.consent_grant(identity_id, purpose, subject_ref_type, subject_ref_id)
    NULLS NOT DISTINCT
    WHERE withdrawn_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_consent_grant_identity
    ON identity.consent_grant(identity_id, purpose)
    WHERE withdrawn_at IS NULL;

-- ============================================================================
-- 4. Admin RBAC Schema (WP-12 / 02-ENG §7 / Rule 8)
-- ============================================================================
CREATE TABLE IF NOT EXISTS admin.admin_role (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code text NOT NULL UNIQUE,
    display_name text NOT NULL
);

CREATE TABLE IF NOT EXISTS admin.admin_permission (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    resource text NOT NULL,
    action text NOT NULL,
    UNIQUE (resource, action)
);

CREATE TABLE IF NOT EXISTS admin.admin_role_permission (
    admin_role_id uuid NOT NULL REFERENCES admin.admin_role(id),
    permission_id uuid NOT NULL REFERENCES admin.admin_permission(id),
    PRIMARY KEY (admin_role_id, permission_id)
);

CREATE TABLE IF NOT EXISTS admin.admin_role_assignment (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    identity_id uuid NOT NULL REFERENCES identity.identity(id),
    admin_role_id uuid NOT NULL REFERENCES admin.admin_role(id),
    scope_type scope_type NOT NULL,
    scope_ref_id uuid,
    granted_by_admin_id uuid NOT NULL REFERENCES identity.identity(id),
    granted_at timestamptz NOT NULL DEFAULT now(),
    revoked_at timestamptz,
    revoked_by_admin_id uuid REFERENCES identity.identity(id),
    CONSTRAINT chk_scope_ref CHECK (
        (scope_type = 'global' AND scope_ref_id IS NULL) OR
        (scope_type IN ('region', 'organization') AND scope_ref_id IS NOT NULL)
    )
);

CREATE INDEX IF NOT EXISTS idx_ara_identity_active
    ON admin.admin_role_assignment(identity_id)
    WHERE revoked_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_ara_unused
    ON admin.admin_role_assignment(granted_at)
    WHERE revoked_at IS NULL;

-- Seed baseline RBAC roles & permissions (Part B §2 + WP-12)
INSERT INTO admin.admin_role (code, display_name) VALUES
    ('SuperAdmin', 'Super Administrator'),
    ('IdentityTrustAdmin', 'Identity & Trust Administrator'),
    ('MarketplaceOperationsAdmin', 'Marketplace Operations Administrator'),
    ('Finance', 'Finance & Ledger Approver'),
    ('ContentGovernanceAdmin', 'Content Governance Administrator'),
    ('SupportAdmin', 'Support Operations Administrator'),
    ('AuditComplianceAdmin', 'Audit & Compliance Administrator')
ON CONFLICT (code) DO NOTHING;

INSERT INTO admin.admin_permission (resource, action) VALUES
    ('admin', 'ping'),
    ('audit', 'search'),
    ('audit', 'manage'),
    ('rbac', 'manage'),
    ('master_data', 'approve'),
    ('geography', 'seed')
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
JOIN admin.admin_permission p ON p.resource = 'audit' AND p.action IN ('search', 'manage')
WHERE r.code = 'AuditComplianceAdmin'
ON CONFLICT DO NOTHING;

-- ============================================================================
-- 5. Partitioned Append-Only Audit Log (WP-13 / 02-ENG §3.4, §9 / Rule 7)
-- ============================================================================
CREATE TABLE IF NOT EXISTS audit.audit_log_entry (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    domain text NOT NULL,
    event_type text NOT NULL,
    entity_type text NOT NULL,
    entity_id uuid NOT NULL,
    actor_id uuid REFERENCES identity.identity(id),
    payload jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (id, created_at)
) PARTITION BY RANGE (created_at);

CREATE INDEX IF NOT EXISTS idx_audit_entity ON audit.audit_log_entry(entity_type, entity_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_audit_actor ON audit.audit_log_entry(actor_id, created_at DESC);

CREATE OR REPLACE FUNCTION audit.enforce_append_only()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'audit.audit_log_entry is append-only (Rule 7): % is forbidden', TG_OP
        USING ERRCODE = '23514';
END;
$$;

DROP TRIGGER IF EXISTS trg_audit_log_entry_append_only ON audit.audit_log_entry;
CREATE TRIGGER trg_audit_log_entry_append_only
    BEFORE UPDATE OR DELETE ON audit.audit_log_entry
    FOR EACH ROW
    EXECUTE FUNCTION audit.enforce_append_only();

-- Create rolling monthly partitions [-1 month .. +3 months] from UTC now (indefinite retention).
DO $$
DECLARE
    m int;
    part_start timestamptz;
    part_end timestamptz;
    part_name text;
BEGIN
    FOR m IN -1..3 LOOP
        part_start := date_trunc('month', now() AT TIME ZONE 'UTC') + make_interval(months => m);
        part_end := part_start + interval '1 month';
        part_name := 'audit_log_entry_' || to_char(part_start, 'YYYY_MM');
        EXECUTE format(
            'CREATE TABLE IF NOT EXISTS audit.%I PARTITION OF audit.audit_log_entry FOR VALUES FROM (%L) TO (%L);',
            part_name,
            part_start AT TIME ZONE 'UTC',
            part_end AT TIME ZONE 'UTC'
        );
    END LOOP;
END $$;

-- ============================================================================
-- 6. Platform Request Idempotency & Sprint 0 Spike Table (WP-14 / 02-ENG §3.6)
-- ============================================================================
CREATE TABLE IF NOT EXISTS platform.idempotency_keys (
    id bigserial PRIMARY KEY,
    idempotency_key uuid NOT NULL,
    identity_id uuid NULL REFERENCES identity.identity(id),
    route text NOT NULL,
    request_fingerprint char(64) NOT NULL,
    response_status_code integer NULL,
    response_body jsonb NULL,
    locked_until timestamptz NOT NULL DEFAULT (now() + interval '30 seconds'),
    expires_at timestamptz NOT NULL DEFAULT (now() + interval '24 hours'),
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_idempotency_key_per_identity_route
        UNIQUE NULLS NOT DISTINCT (idempotency_key, identity_id, route)
);

CREATE INDEX IF NOT EXISTS idx_idempotency_expiry ON platform.idempotency_keys(expires_at);
CREATE INDEX IF NOT EXISTS idx_idempotency_inflight ON platform.idempotency_keys(route) WHERE response_status_code IS NULL;

CREATE TABLE IF NOT EXISTS platform.sync_spike_records (
    id uuid PRIMARY KEY,
    client_guid uuid NOT NULL UNIQUE,
    entity_type text NOT NULL,
    payload_json jsonb NOT NULL,
    client_recorded_at timestamptz NOT NULL,
    server_received_at timestamptz NOT NULL DEFAULT now()
);
