-- Sprint 4 Review Engine, Master Data Change Requests, Trust Overrides, Notifications & Capability Modules (WP-40..WP-43)
-- Sources: Doc/02-ENG §3.2, §3.3, §7; Doc/03-ENG §2 & §10; PRECEDENCE P16, P25; BR-D-03, BR-D-04, Rule 7

CREATE SCHEMA IF NOT EXISTS review;
CREATE SCHEMA IF NOT EXISTS notify;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'review_item_status') THEN
        CREATE TYPE review_item_status AS ENUM ('unassigned','assigned','in_review','awaiting_secondary_review','resolved','escalated');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'review_priority') THEN
        CREATE TYPE review_priority AS ENUM ('normal','elevated','urgent');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'reviewer_status') THEN
        CREATE TYPE reviewer_status AS ENUM ('active','on_leave','inactive');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'change_request_status') THEN
        CREATE TYPE change_request_status AS ENUM ('draft','pending_review','approved','rejected','applied');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'notification_channel') THEN
        CREATE TYPE notification_channel AS ENUM ('push','sms','voice_ivr','in_app','email');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'dispatch_status') THEN
        CREATE TYPE dispatch_status AS ENUM ('queued','sent','delivered','failed');
    END IF;
END $$;

-- ============================================================================
-- 1. Shared Review Engine Schema (WP-40 / WP-41 / 02-ENG §3.2)
-- ============================================================================
CREATE TABLE IF NOT EXISTS review.review_category (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code text NOT NULL UNIQUE,
    display_name text NOT NULL,
    sla_hours integer NOT NULL,
    requires_secondary_review boolean NOT NULL DEFAULT false,
    min_qualification_level integer NOT NULL DEFAULT 1,
    allows_appeal boolean NOT NULL DEFAULT true
);

INSERT INTO review.review_category (code, display_name, sla_hours, requires_secondary_review, min_qualification_level, allows_appeal) VALUES
    ('IDENTITY_NIN_MISMATCH', 'NIN Mismatch Verification', 48, false, 1, true),
    ('ORG_VERIFICATION', 'Organization Verification', 48, false, 2, true),
    ('FIELD_OVERLAP_DISPUTE', 'Field Overlap Dispute', 72, false, 2, true),
    ('LAB_ACCREDITATION', 'Laboratory Accreditation', 48, false, 2, true),
    ('VENDOR_TRUST_TIER', 'Vendor Trust Tier Escalation', 48, false, 2, true),
    ('PEST_DISEASE_REPORT', 'Pest & Disease Report Review', 24, false, 2, true),
    ('KB_CHEMICAL_PUBLISH', 'Regulated Chemical/Nutrient Publish (Dual Gate)', 48, true, 3, false),
    ('KB_CHEMICAL_FLAGGED', 'Flagged Chemical Safety Review', 24, false, 3, false),
    ('CERTIFICATION_VERIFY', 'Certification Verification', 48, false, 2, true),
    ('CERTIFICATION_FRAUD_FLAG', 'Certification Fraud Investigation', 24, false, 3, true),
    ('FORUM_PROMOTION', 'Forum Best-Practice Promotion', 48, false, 1, false)
ON CONFLICT (code) DO NOTHING;

CREATE TABLE IF NOT EXISTS review.reviewer_profile (
    identity_id uuid PRIMARY KEY REFERENCES identity.identity(id),
    status reviewer_status NOT NULL DEFAULT 'active',
    max_concurrent_assignments integer NOT NULL DEFAULT 10
);

CREATE TABLE IF NOT EXISTS review.reviewer_qualification (
    reviewer_id uuid NOT NULL REFERENCES review.reviewer_profile(identity_id),
    category_id uuid NOT NULL REFERENCES review.review_category(id),
    proficiency_level integer NOT NULL DEFAULT 1,
    PRIMARY KEY (reviewer_id, category_id)
);

CREATE TABLE IF NOT EXISTS review.review_queue_item (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    category_id uuid NOT NULL REFERENCES review.review_category(id),
    subject_ref_type text NOT NULL,
    subject_ref_id uuid NOT NULL,
    submitted_by_identity_id uuid REFERENCES identity.identity(id),
    priority review_priority NOT NULL DEFAULT 'normal',
    status review_item_status NOT NULL DEFAULT 'unassigned',
    assigned_reviewer_id uuid REFERENCES review.reviewer_profile(identity_id),
    assigned_at timestamptz,
    due_at timestamptz NOT NULL,
    resolved_at timestamptz,
    verdict_code text,
    verdict_notes text,
    verdict_payload jsonb,
    appeal_of_id uuid REFERENCES review.review_queue_item(id),
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT chk_review_terminal_has_verdict CHECK (
        status <> 'resolved' OR verdict_code IS NOT NULL
    )
);

CREATE INDEX IF NOT EXISTS idx_rqi_category_status ON review.review_queue_item(category_id, status);
CREATE INDEX IF NOT EXISTS idx_rqi_due_unresolved ON review.review_queue_item(due_at) WHERE status NOT IN ('resolved');
CREATE INDEX IF NOT EXISTS idx_rqi_subject ON review.review_queue_item(subject_ref_type, subject_ref_id);
CREATE INDEX IF NOT EXISTS idx_rqi_assigned_reviewer ON review.review_queue_item(assigned_reviewer_id) WHERE status IN ('assigned', 'in_review');

CREATE OR REPLACE FUNCTION review.enforce_appeal_distinct_reviewer()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    orig_reviewer_id uuid;
BEGIN
    IF NEW.appeal_of_id IS NOT NULL AND NEW.assigned_reviewer_id IS NOT NULL THEN
        SELECT assigned_reviewer_id INTO orig_reviewer_id
        FROM review.review_queue_item
        WHERE id = NEW.appeal_of_id;

        IF orig_reviewer_id IS NOT NULL AND NEW.assigned_reviewer_id = orig_reviewer_id THEN
            RAISE EXCEPTION 'Appeal % must be assigned to a different reviewer from original reviewer %', NEW.id, orig_reviewer_id
                USING ERRCODE = '23514';
        END IF;
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_review_appeal_distinct_reviewer ON review.review_queue_item;
CREATE TRIGGER trg_review_appeal_distinct_reviewer
    BEFORE INSERT OR UPDATE ON review.review_queue_item
    FOR EACH ROW
    EXECUTE FUNCTION review.enforce_appeal_distinct_reviewer();

CREATE TABLE IF NOT EXISTS review.review_audit_log (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    review_queue_item_id uuid NOT NULL REFERENCES review.review_queue_item(id),
    event_type text NOT NULL,
    actor_id uuid REFERENCES identity.identity(id),
    payload jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (id, created_at)
) PARTITION BY RANGE (created_at);

CREATE OR REPLACE FUNCTION review.enforce_review_audit_append_only()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'review.review_audit_log is append-only (Rule 7): % is forbidden', TG_OP
        USING ERRCODE = '23514';
END;
$$;

DROP TRIGGER IF EXISTS trg_review_audit_log_append_only ON review.review_audit_log;
CREATE TRIGGER trg_review_audit_log_append_only
    BEFORE UPDATE OR DELETE ON review.review_audit_log
    FOR EACH ROW
    EXECUTE FUNCTION review.enforce_review_audit_append_only();

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
        part_name := 'review_audit_log_' || to_char(part_start, 'YYYY_MM');
        EXECUTE format(
            'CREATE TABLE IF NOT EXISTS review.%I PARTITION OF review.review_audit_log FOR VALUES FROM (%L) TO (%L);',
            part_name,
            part_start AT TIME ZONE 'UTC',
            part_end AT TIME ZONE 'UTC'
        );
    END LOOP;
END $$;

-- ============================================================================
-- 2. Master Data Change Requests & Trust Overrides (WP-41 / WP-42 / 02-ENG §7)
-- ============================================================================
CREATE TABLE IF NOT EXISTS admin.master_data_change_request (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    table_name text NOT NULL,
    record_id uuid,
    change_type text NOT NULL,
    proposed_by_admin_id uuid NOT NULL REFERENCES identity.identity(id),
    proposed_at timestamptz NOT NULL DEFAULT now(),
    diff jsonb NOT NULL,
    status change_request_status NOT NULL DEFAULT 'draft',
    reviewed_by_admin_id uuid REFERENCES identity.identity(id),
    review_notes text,
    applied_at timestamptz,
    CONSTRAINT chk_no_self_approval CHECK (
        reviewed_by_admin_id IS NULL OR reviewed_by_admin_id <> proposed_by_admin_id
    )
);

CREATE INDEX IF NOT EXISTS idx_mdcr_pending
    ON admin.master_data_change_request(status)
    WHERE status = 'pending_review';

CREATE TABLE IF NOT EXISTS admin.trust_profile_override (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    identity_id uuid REFERENCES identity.identity(id),
    organization_id uuid REFERENCES identity.organization(id),
    overridden_tier trust_tier NOT NULL,
    reason text NOT NULL,
    granted_by_admin_id uuid NOT NULL REFERENCES identity.identity(id),
    expiry_date date,
    is_permanent boolean NOT NULL DEFAULT false,
    revoked_at timestamptz,
    CONSTRAINT chk_override_bounded CHECK (expiry_date IS NOT NULL OR is_permanent = true)
);

-- ============================================================================
-- 3. Notifications & Capability Modules (WP-43 / 02-ENG §3.3 / PRECEDENCE P25)
-- ============================================================================
CREATE TABLE IF NOT EXISTS notify.notification_template (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code text NOT NULL UNIQUE,
    channel notification_channel NOT NULL,
    body_template text NOT NULL,
    required_context_fields text[] NOT NULL DEFAULT '{}'
);

INSERT INTO notify.notification_template (code, channel, body_template, required_context_fields) VALUES
    ('IDENTITY_NIN_MISMATCH_RESOLVED', 'sms', 'Farm Basics: Your identity review outcome is {outcome}.', ARRAY['outcome']),
    ('REVIEW_SLA_BREACHED', 'in_app', 'Review item {reviewQueueItemId} exceeded SLA for {categoryCode}.', ARRAY['reviewQueueItemId', 'categoryCode']),
    ('WELCOME_FARMER', 'sms', 'Welcome to Farm Basics! Your Farmer ID is {farmerId}.', ARRAY['farmerId'])
ON CONFLICT (code) DO NOTHING;

CREATE TABLE IF NOT EXISTS notify.notification_dispatch (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    template_id uuid NOT NULL REFERENCES notify.notification_template(id),
    recipient_identity_id uuid NOT NULL REFERENCES identity.identity(id),
    channel notification_channel NOT NULL,
    source_module text NOT NULL,
    source_ref_type text,
    source_ref_id uuid,
    status dispatch_status NOT NULL DEFAULT 'queued',
    sent_at timestamptz,
    delivered_at timestamptz,
    failure_reason text,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_dispatch_recipient ON notify.notification_dispatch(recipient_identity_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_dispatch_dedup ON notify.notification_dispatch(recipient_identity_id, source_module, source_ref_type, source_ref_id, created_at DESC);

CREATE TABLE IF NOT EXISTS platform.capability_module (
    module_code text PRIMARY KEY,
    display_name text NOT NULL,
    enabled_by_default boolean NOT NULL DEFAULT false,
    description text NOT NULL
);

INSERT INTO platform.capability_module (module_code, display_name, enabled_by_default, description) VALUES
    ('cea.hydroponics', 'CEA Hydroponic Production Module', false, 'Controlled-environment hydroponic crop profiles and solution guidance (P25 off by default).'),
    ('cea.aquaponics', 'CEA Aquaponics Plant-Side Module', false, 'Aquaponics plant-side growing unit tracking behind CEA flag (P25 off by default).'),
    ('cea.greenhouse', 'CEA Greenhouse & Screenhouse Module', false, 'Protected-cultivation telemetry and stage workflows behind CEA flag (P25 off by default).'),
    ('core.registration', 'Core Farmer & Agent Registration', true, 'Baseline digital identity onboarding workflow.'),
    ('core.review_engine', 'Shared Human Review Engine', true, 'Multi-category reviewer assignment, COI, and audit workflow.')
ON CONFLICT (module_code) DO NOTHING;

CREATE TABLE IF NOT EXISTS platform.capability_module_override (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    module_code text NOT NULL REFERENCES platform.capability_module(module_code),
    scope_type scope_type NOT NULL DEFAULT 'global',
    scope_ref_id uuid NULL,
    enabled boolean NOT NULL,
    updated_by_admin_id uuid REFERENCES identity.identity(id),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_capability_override_scope
        UNIQUE NULLS NOT DISTINCT (module_code, scope_type, scope_ref_id)
);

-- Seed Sprint 4 admin permissions
INSERT INTO admin.admin_permission (resource, action) VALUES
    ('review', 'manage'),
    ('trust', 'override'),
    ('trust', 'override_permanent'),
    ('master_data', 'propose'),
    ('notification', 'dispatch'),
    ('capability', 'manage')
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
JOIN admin.admin_permission p ON
    (p.resource = 'review' AND p.action = 'manage')
    OR (p.resource = 'trust' AND p.action = 'override')
WHERE r.code = 'IdentityTrustAdmin'
ON CONFLICT DO NOTHING;

INSERT INTO admin.admin_role_permission (admin_role_id, permission_id)
SELECT r.id, p.id
FROM admin.admin_role r
JOIN admin.admin_permission p ON
    p.resource = 'master_data' AND p.action IN ('propose', 'approve')
WHERE r.code = 'ContentGovernanceAdmin'
ON CONFLICT DO NOTHING;
