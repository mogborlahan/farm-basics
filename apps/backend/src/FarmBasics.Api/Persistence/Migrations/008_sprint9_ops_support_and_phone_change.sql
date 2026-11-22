-- Sprint 9 Harvest Disposition, Field Ops Console, Scoped Analytics, Support Enablement & In-Person Phone Recovery (WP-90..WP-94)
-- Sources: Doc/PRD-Volume-D §4.7 & §4.12; Doc/03-ENG §13; Doc/16-OPS §9.2 & gaps 1/3/4/6; PRECEDENCE P3-P6; Rule 7, Rule 8

-- ============================================================================
-- 1. Seed FieldOperationsAdmin & CustomerSupportAgent Roles & Permissions
--    P3 Safe Build Posture: CustomerSupportAgent NEVER receives fob:revoke.
-- ============================================================================
INSERT INTO admin.admin_role (code, display_name) VALUES
    ('FieldOperationsAdmin', 'Field Operations Administrator'),
    ('CustomerSupportAgent', 'Customer Support Agent')
ON CONFLICT (code) DO NOTHING;

INSERT INTO admin.admin_permission (resource, action) VALUES
    ('field_ops', 'read'),
    ('analytics', 'read'),
    ('support', 'read'),
    ('support', 'escalate_fob')
ON CONFLICT (resource, action) DO NOTHING;

-- SuperAdmin holds every admin permission
INSERT INTO admin.admin_role_permission (admin_role_id, permission_id)
SELECT r.id, p.id
FROM admin.admin_role r
CROSS JOIN admin.admin_permission p
WHERE r.code = 'SuperAdmin'
ON CONFLICT DO NOTHING;

-- FieldOperationsAdmin: ping, field_ops:read, analytics:read, tablet:deactivate
INSERT INTO admin.admin_role_permission (admin_role_id, permission_id)
SELECT r.id, p.id
FROM admin.admin_role r
JOIN admin.admin_permission p ON
    (p.resource = 'admin' AND p.action = 'ping')
    OR (p.resource = 'field_ops' AND p.action = 'read')
    OR (p.resource = 'analytics' AND p.action = 'read')
    OR (p.resource = 'tablet' AND p.action = 'deactivate')
WHERE r.code = 'FieldOperationsAdmin'
ON CONFLICT DO NOTHING;

-- CustomerSupportAgent: ping, support:read, support:escalate_fob (explicitly NO fob:revoke per P3)
INSERT INTO admin.admin_role_permission (admin_role_id, permission_id)
SELECT r.id, p.id
FROM admin.admin_role r
JOIN admin.admin_permission p ON
    (p.resource = 'admin' AND p.action = 'ping')
    OR (p.resource = 'support' AND p.action = 'read')
    OR (p.resource = 'support' AND p.action = 'escalate_fob')
WHERE r.code = 'CustomerSupportAgent'
ON CONFLICT DO NOTHING;

-- ============================================================================
-- 2. Review Category for Support-Initiated Fob Revocation Escalation (WP-93 / P3)
-- ============================================================================
INSERT INTO review.review_category (
    code, display_name, sla_hours, requires_secondary_review, min_qualification_level, allows_appeal
) VALUES (
    'SUPPORT_FOB_REVOCATION_ESCALATION',
    'Urgent Support Fob Revocation Escalation',
    1,
    false,
    2,
    false
)
ON CONFLICT (code) DO NOTHING;

-- ============================================================================
-- 3. In-Person Agent-Verified Phone Change Request Table (WP-94 / P6)
-- ============================================================================
CREATE TABLE IF NOT EXISTS identity.phone_change_request (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    identity_id uuid NOT NULL REFERENCES identity.identity(id),
    old_phone text NOT NULL,
    new_phone text NOT NULL,
    initiated_by_agent_id uuid NOT NULL REFERENCES identity.identity(id),
    agent_session_id uuid NOT NULL REFERENCES identity.agent_session(id),
    tablet_device_id uuid NOT NULL REFERENCES identity.tablet_device(id),
    otp_code_hash text NOT NULL,
    otp_expires_at timestamptz NOT NULL,
    status text NOT NULL DEFAULT 'pending_otp'
        CHECK (status IN ('pending_otp', 'completed', 'expired', 'cancelled')),
    created_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz
);

CREATE INDEX IF NOT EXISTS idx_phone_change_request_identity_status
    ON identity.phone_change_request(identity_id, status);
