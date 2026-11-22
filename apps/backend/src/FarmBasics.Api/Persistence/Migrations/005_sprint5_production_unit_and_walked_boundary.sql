-- Sprint 5-6 ProductionUnit & Walked Boundary Migration (WP-50..WP-56)
-- Sources: Doc/02B-ENG §3.1-§3.2, §6; Doc/02D-ENG §1-§2; Doc/02-ENG §4.1, §4.3; Doc/02C-ENG §1.3; F-FB-04/05/06

CREATE SCHEMA IF NOT EXISTS field;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'drainage_class') THEN
        CREATE TYPE drainage_class AS ENUM ('poor', 'moderate', 'good');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'tracking_status') THEN
        CREATE TYPE tracking_status AS ENUM (
            'created', 'initializing', 'recording', 'paused', 'stopped',
            'processing', 'invalid', 'valid', 'review', 'confirmed', 'saved'
        );
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'system_type') THEN
        -- Doc/02D-ENG §1: 16-member lockstep enum aligned with PRD-Volume-E §3.1 / E1 §1
        CREATE TYPE system_type AS ENUM (
            'open_field',
            'greenhouse',
            'shade_net_house',
            'container_farm',
            'dutch_bucket',
            'substrate_trough',
            'hydroponic_nft',
            'hydroponic_dwc',
            'aeroponic',
            'aquaponic',
            'vertical_rack',
            'kratky',
            'ebb_flow',
            'wick_system',
            'hydroponic_fodder',
            'fogponic'
        );
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'capture_method') THEN
        CREATE TYPE capture_method AS ENUM (
            'gps_walked_boundary',
            'gps_pin_drop_with_footprint',
            'manual_footprint_entry',
            'address_only'
        );
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'area_basis') THEN
        CREATE TYPE area_basis AS ENUM ('computed_geodesic', 'declared');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'growing_medium_class') THEN
        CREATE TYPE growing_medium_class AS ENUM ('soil_based', 'soilless');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'growing_medium') THEN
        -- Doc/02D-ENG §1b: 11-member enum aligned with PRD-Volume-E FR-SA-002
        CREATE TYPE growing_medium AS ENUM (
            'native_soil',
            'potting_mix',
            'none_water_culture',
            'coco_coir',
            'rockwool',
            'clay_pebbles',
            'perlite',
            'gravel',
            'rice_husk',
            'wood_sawdust',
            'other'
        );
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'tenancy_type') THEN
        CREATE TYPE tenancy_type AS ENUM (
            'owner', 'lessee', 'sharecropper', 'communal_allocation', 'family_land'
        );
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'tenancy_status') THEN
        CREATE TYPE tenancy_status AS ENUM ('active', 'ended');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'overlap_status') THEN
        CREATE TYPE overlap_status AS ENUM (
            'pending_review', 'confirmed_same_land', 'confirmed_dispute', 'confirmed_gps_noise', 'resolved'
        );
    END IF;
END $$;

-- ============================================================================
-- 1. ProductionUnit (02B-ENG §3.2 + 02D-ENG §2)
-- ============================================================================
CREATE TABLE IF NOT EXISTS field.production_unit (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    farm_identity_id uuid NOT NULL REFERENCES identity.identity(id),
    name text NOT NULL,
    system_type system_type NOT NULL,
    capture_method capture_method NOT NULL,
    growing_medium_class growing_medium_class NOT NULL,
    boundary geography(Polygon, 4326),
    centroid geography(Point, 4326),
    lga_id uuid REFERENCES identity.lga(id),
    address_text text,
    area_square_meters numeric(14,2),
    area_hectares numeric(10,4),
    area_acres numeric(10,4),
    perimeter_meters numeric(12,2),
    area_basis area_basis,
    tier_count smallint NOT NULL DEFAULT 1,
    elevation_m numeric(8,2),
    soil_type text,
    drainage_class drainage_class,
    slope_percent numeric(5,2),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz,
    CONSTRAINT chk_tier_count CHECK (tier_count BETWEEN 1 AND 20),
    CONSTRAINT chk_area_positive CHECK (area_square_meters IS NULL OR area_square_meters > 0),
    CONSTRAINT chk_capture_shape CHECK (
           (capture_method = 'gps_walked_boundary'
              AND boundary IS NOT NULL AND centroid IS NOT NULL
              AND area_square_meters IS NOT NULL AND area_basis = 'computed_geodesic')
        OR (capture_method = 'gps_pin_drop_with_footprint'
              AND boundary IS NULL AND centroid IS NOT NULL
              AND area_square_meters IS NOT NULL AND area_basis = 'declared')
        OR (capture_method = 'manual_footprint_entry'
              AND boundary IS NULL AND area_square_meters IS NOT NULL
              AND area_basis = 'declared' AND lga_id IS NOT NULL)
        OR (capture_method = 'address_only'
              AND boundary IS NULL AND centroid IS NOT NULL
              AND address_text IS NOT NULL AND lga_id IS NOT NULL)
    ),
    CONSTRAINT chk_medium_class CHECK (
           (system_type = 'open_field' AND growing_medium_class = 'soil_based')
        OR (system_type IN (
                'hydroponic_nft', 'hydroponic_dwc', 'aeroponic', 'aquaponic', 'vertical_rack',
                'dutch_bucket', 'substrate_trough', 'kratky', 'ebb_flow', 'wick_system',
                'hydroponic_fodder', 'fogponic'
            ) AND growing_medium_class = 'soilless')
        OR (system_type IN ('greenhouse', 'shade_net_house', 'container_farm'))
    ),
    CONSTRAINT chk_walked_only_boundary CHECK (boundary IS NULL OR capture_method = 'gps_walked_boundary')
);

CREATE INDEX IF NOT EXISTS idx_pu_boundary_gist
    ON field.production_unit USING GIST (boundary)
    WHERE boundary IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_pu_centroid_gist
    ON field.production_unit USING GIST (centroid)
    WHERE centroid IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_pu_lga
    ON field.production_unit(lga_id);
CREATE INDEX IF NOT EXISTS idx_pu_farm_identity
    ON field.production_unit(farm_identity_id);

CREATE OR REPLACE FUNCTION field.production_unit_derive()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.boundary IS NOT NULL THEN
        NEW.area_square_meters := ST_Area(NEW.boundary);
        NEW.area_hectares      := NEW.area_square_meters / 10000;
        NEW.area_acres         := NEW.area_square_meters / 4046.8564224;
        NEW.perimeter_meters   := ST_Perimeter(NEW.boundary);
        NEW.centroid           := ST_Centroid(NEW.boundary::geometry)::geography;
        NEW.area_basis         := 'computed_geodesic';
    ELSE
        IF NEW.area_square_meters IS NOT NULL THEN
            NEW.area_hectares := NEW.area_square_meters / 10000;
            NEW.area_acres    := NEW.area_square_meters / 4046.8564224;
            NEW.area_basis    := 'declared';
        END IF;
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_pu_derive ON field.production_unit;
CREATE TRIGGER trg_pu_derive
    BEFORE INSERT OR UPDATE OF boundary, area_square_meters
    ON field.production_unit
    FOR EACH ROW
    EXECUTE FUNCTION field.production_unit_derive();

-- ============================================================================
-- 2. Boundary Tracking Session & Immutable Raw Tracking Points (02-ENG §4.1, F-FB-06)
-- ============================================================================
CREATE TABLE IF NOT EXISTS field.field_tracking_session (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    farm_identity_id uuid NOT NULL REFERENCES identity.identity(id),
    started_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz,
    status tracking_status NOT NULL DEFAULT 'created',
    distance_meters numeric(12,2) DEFAULT 0,
    accepted_point_count integer DEFAULT 0,
    computed_boundary geography(Polygon, 4326),
    computed_area_sqm numeric(14,2),
    computed_perimeter_m numeric(12,2),
    validation_issue text,
    resulting_production_unit_id uuid REFERENCES field.production_unit(id),
    updated_at timestamptz
);

CREATE INDEX IF NOT EXISTS idx_tracking_session_farmer
    ON field.field_tracking_session(farm_identity_id, started_at DESC);

CREATE TABLE IF NOT EXISTS field.tracking_point (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id uuid NOT NULL REFERENCES field.field_tracking_session(id),
    client_guid uuid NOT NULL,
    sequence_number integer NOT NULL,
    location geography(Point, 4326) NOT NULL,
    accuracy_meters numeric(6,2),
    speed_mps numeric(6,2),
    recorded_at timestamptz NOT NULL,
    accepted boolean NOT NULL DEFAULT true,
    rejection_reason text,
    UNIQUE (session_id, client_guid)
);

CREATE INDEX IF NOT EXISTS idx_tracking_point_session_seq
    ON field.tracking_point(session_id, sequence_number ASC, recorded_at ASC);

CREATE OR REPLACE FUNCTION field.enforce_tracking_point_no_delete()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'field.tracking_point rows are permanently retained (F-FB-06): % is forbidden', TG_OP
        USING ERRCODE = '42501';
END;
$$;

DROP TRIGGER IF EXISTS trg_tracking_point_no_delete ON field.tracking_point;
CREATE TRIGGER trg_tracking_point_no_delete
    BEFORE DELETE ON field.tracking_point
    FOR EACH ROW
    EXECUTE FUNCTION field.enforce_tracking_point_no_delete();

-- ============================================================================
-- 3. Field Tenancy & Walked-Only Overlap Detection (02-ENG §4.3, 02B-ENG §2/A-3, 02C-ENG §1.3)
-- ============================================================================
CREATE TABLE IF NOT EXISTS field.field_tenancy (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    production_unit_id uuid NOT NULL REFERENCES field.production_unit(id),
    identity_id uuid REFERENCES identity.identity(id),
    organization_id uuid REFERENCES identity.organization(id),
    farm_organization_id uuid REFERENCES identity.organization(id),
    tenancy_type tenancy_type NOT NULL,
    start_date date NOT NULL,
    end_date date,
    status tenancy_status NOT NULL DEFAULT 'active',
    CONSTRAINT chk_tenancy_holder CHECK (
        (identity_id IS NOT NULL)::int + (organization_id IS NOT NULL)::int = 1
    )
);

CREATE INDEX IF NOT EXISTS idx_tenancy_unit
    ON field.field_tenancy(production_unit_id, status);

CREATE TABLE IF NOT EXISTS field.field_overlap_event (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    new_production_unit_id uuid NOT NULL REFERENCES field.production_unit(id),
    existing_production_unit_id uuid NOT NULL REFERENCES field.production_unit(id),
    overlap_area_sqm numeric(14,2) NOT NULL,
    overlap_pct_of_new numeric(5,2) NOT NULL,
    overlap_pct_of_existing numeric(5,2) NOT NULL,
    status overlap_status NOT NULL DEFAULT 'pending_review',
    resolution_notes text,
    detected_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_overlap_pending
    ON field.field_overlap_event(status)
    WHERE status = 'pending_review';

-- ============================================================================
-- 4. CEA Capability Flags Off by Default per 02B-ENG §6 & PRECEDENCE P25
-- ============================================================================
INSERT INTO platform.capability_module (module_code, display_name, enabled_by_default, description) VALUES
    ('cea.walked_units', 'CEA Walked-Boundary Non-Open-Field Units', false, 'Enables walked greenhouse, shade-net house, and container production unit confirmation (02B-ENG §6).'),
    ('cea.soilless', 'CEA Non-Walked & Soilless Production Units', false, 'Enables pin-drop, manual-footprint, address-only, and soilless CEA unit capture (02B-ENG §6).'),
    ('cea.solution_advice', 'CEA Solution Advice & Lab Family', false, 'Enables hydroponic/soilless nutrient solution testing workflows (02B-ENG §6).')
ON CONFLICT (module_code) DO NOTHING;
