-- Sprint 11 Break-Glass Action & Retroactive Second-SuperAdmin Review (WP-B2 / S11.3.M1)
-- Sources: Doc/04A-ENG §6 & §11.7; Doc/08-ENG §5.2; Doc/PRD-Volume-D §4.15 FR-SC-003 (BR-D-08)

CREATE TABLE IF NOT EXISTS admin.break_glass_action (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    invoked_by_admin_id uuid NOT NULL REFERENCES identity.identity(id),
    action_description text NOT NULL,
    justification text NOT NULL,
    retroactive boolean NOT NULL DEFAULT true,
    status text NOT NULL DEFAULT 'pending_review'
        CHECK (status IN ('pending_review', 'reviewed')),
    invoked_at timestamptz NOT NULL DEFAULT now(),
    review_due_at timestamptz NOT NULL DEFAULT (now() + interval '48 hours'),
    reviewed_by_admin_id uuid REFERENCES identity.identity(id),
    review_notes text,
    reviewed_at timestamptz,
    CONSTRAINT chk_break_glass_distinct_reviewer CHECK (
        reviewed_by_admin_id IS NULL OR reviewed_by_admin_id <> invoked_by_admin_id
    ),
    CONSTRAINT chk_break_glass_reviewed_fields CHECK (
        status <> 'reviewed' OR (reviewed_by_admin_id IS NOT NULL AND reviewed_at IS NOT NULL)
    )
);

CREATE INDEX IF NOT EXISTS idx_break_glass_pending_due
    ON admin.break_glass_action(review_due_at)
    WHERE status = 'pending_review';

INSERT INTO admin.admin_permission (resource, action) VALUES
    ('break_glass', 'invoke'),
    ('break_glass', 'review')
ON CONFLICT (resource, action) DO NOTHING;

INSERT INTO admin.admin_role_permission (admin_role_id, permission_id)
SELECT r.id, p.id
FROM admin.admin_role r
JOIN admin.admin_permission p ON
    p.resource = 'break_glass' AND p.action IN ('invoke', 'review')
WHERE r.code = 'SuperAdmin'
ON CONFLICT DO NOTHING;
