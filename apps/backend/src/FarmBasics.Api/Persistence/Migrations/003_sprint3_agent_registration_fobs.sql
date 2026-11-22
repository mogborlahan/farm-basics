-- Sprint 3 Agent-Assisted Registration, Tablet Binding, Fobs & Attestation (WP-30..WP-33)
-- Sources: Doc/02-ENG §3.1, Doc/03-ENG §2, Doc/05 §3.3 & §4.4, Doc/11A-LEGAL §5.2, ADR-009 (P2), BR-A-02, BR-A-03, P7, P11, Rule 7, Rule 12

ALTER TABLE identity.tablet_device
    ADD COLUMN IF NOT EXISTS device_fingerprint text UNIQUE,
    ADD COLUMN IF NOT EXISTS bound_organization_id uuid REFERENCES identity.organization(id),
    ADD COLUMN IF NOT EXISTS bound_at timestamptz,
    ADD COLUMN IF NOT EXISTS deactivated_at timestamptz,
    ADD COLUMN IF NOT EXISTS deactivated_by_admin_id uuid REFERENCES identity.identity(id);

CREATE TABLE IF NOT EXISTS identity.agent_session (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    agent_identity_id uuid NOT NULL REFERENCES identity.identity(id),
    tablet_device_id uuid NOT NULL REFERENCES identity.tablet_device(id),
    organization_id uuid NOT NULL REFERENCES identity.organization(id),
    started_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL,
    ended_at timestamptz NULL,
    CONSTRAINT chk_agent_session_72h_cap CHECK (
        expires_at > started_at AND expires_at <= started_at + interval '72 hours'
    )
);

CREATE INDEX IF NOT EXISTS idx_agent_session_active
    ON identity.agent_session(agent_identity_id, tablet_device_id)
    WHERE ended_at IS NULL;

ALTER TABLE identity.registration_otp_challenge
    DROP CONSTRAINT IF EXISTS registration_otp_challenge_channel_check;
ALTER TABLE identity.registration_otp_challenge
    DROP CONSTRAINT IF EXISTS chk_otp_challenge_channel;
ALTER TABLE identity.registration_otp_challenge
    ADD CONSTRAINT chk_otp_challenge_channel CHECK (channel IN ('phone', 'email', 'voice'));

-- Verified-then-discarded liveness result log: zero photo/base64 columns permitted (Doc 05 §3.3, S3.1.M3).
CREATE TABLE IF NOT EXISTS identity.liveness_check_record (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    identity_id uuid NOT NULL REFERENCES identity.identity(id),
    agent_identity_id uuid NOT NULL REFERENCES identity.identity(id),
    matched boolean NOT NULL,
    confidence_score numeric(5,4) NOT NULL,
    photo_discarded boolean NOT NULL CHECK (photo_discarded = TRUE),
    created_at timestamptz NOT NULL DEFAULT now()
);

-- Doc/11A-LEGAL §5.2 RegistrationAttestation mapping (zero audio columns per PRECEDENCE P7).
CREATE TABLE IF NOT EXISTS identity.registration_attestation (
    attestation_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES identity.identity(id),
    summary_version varchar(16) NOT NULL,
    pilot_consent_version varchar(16) NULL,
    language_code varchar(10) NOT NULL CHECK (language_code IN ('en-NG', 'ha', 'yo', 'ig', 'pcm')),
    delivery_mode varchar(32) NOT NULL CHECK (delivery_mode IN ('IN_APP_TEXT_AND_AUDIO', 'AGENT_SPOKEN_PLUS_LAMINATED_CARD', 'IVR_PLAYBACK')),
    teach_back_q1_passed boolean NOT NULL CHECK (teach_back_q1_passed = TRUE),
    teach_back_q2_passed boolean NOT NULL CHECK (teach_back_q2_passed = TRUE),
    teach_back_q3_passed boolean NOT NULL CHECK (teach_back_q3_passed = TRUE),
    teach_back_passed boolean GENERATED ALWAYS AS (teach_back_q1_passed AND teach_back_q2_passed AND teach_back_q3_passed) STORED,
    agent_identity_id uuid NULL REFERENCES identity.identity(id),
    lawful_basis_schedule_ref varchar(32) NOT NULL DEFAULT 'NDPA-2023-MULTI-V1',
    client_recorded_at timestamptz NOT NULL,
    server_received_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_registration_attestation_user
    ON identity.registration_attestation(user_id, server_received_at DESC);

CREATE OR REPLACE FUNCTION identity.enforce_registration_attestation_append_only()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'identity.registration_attestation is append-only (Rule 7 / WP-32): % is forbidden', TG_OP
        USING ERRCODE = '23514';
END;
$$;

DROP TRIGGER IF EXISTS trg_registration_attestation_append_only ON identity.registration_attestation;
CREATE TRIGGER trg_registration_attestation_append_only
    BEFORE UPDATE OR DELETE ON identity.registration_attestation
    FOR EACH ROW
    EXECUTE FUNCTION identity.enforce_registration_attestation_append_only();

-- Seed Sprint 3 admin permissions (fob:revoke, tablet:deactivate, organization:verify)
INSERT INTO admin.admin_permission (resource, action) VALUES
    ('fob', 'revoke'),
    ('tablet', 'deactivate'),
    ('organization', 'verify')
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
    (p.resource = 'fob' AND p.action = 'revoke')
    OR (p.resource = 'organization' AND p.action = 'verify')
WHERE r.code = 'IdentityTrustAdmin'
ON CONFLICT DO NOTHING;
