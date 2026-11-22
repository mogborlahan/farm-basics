-- Sprint 2 Self-Service Registration Migration (WP-21 / FR-ID-001)
-- Sources: Doc/02-ENG §3.1, Doc/03-ENG §2 & §15, PRD Volume A FR-ID-001

CREATE SEQUENCE IF NOT EXISTS identity.farmer_id_seq START 100001;

CREATE TABLE IF NOT EXISTS identity.registration_otp_challenge (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    identity_id uuid NOT NULL REFERENCES identity.identity(id),
    channel text NOT NULL CHECK (channel IN ('phone', 'email')),
    otp_hash char(64) NOT NULL,
    attempt_count integer NOT NULL DEFAULT 0,
    locked_until timestamptz NULL,
    expires_at timestamptz NOT NULL,
    verified_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_otp_challenge_identity_channel
    ON identity.registration_otp_challenge(identity_id, channel, created_at DESC);
