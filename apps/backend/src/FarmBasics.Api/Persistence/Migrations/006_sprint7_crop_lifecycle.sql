-- Sprint 7 Crop Lifecycle Migration (WP-70..WP-72)
-- Sources: Doc/02-ENG §4.2; Doc/02B-ENG §3.3, §3.5; Doc/06-DATA §3; Doc/06A-DATA §3.1; PRD Volume A §3.6-§3.7, §4.3; BR-A-07, BR-A-09, F-25(7)

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'crop_category') THEN
        CREATE TYPE crop_category AS ENUM ('cereal', 'legume', 'root_tuber', 'vegetable', 'fruit', 'oilseed');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'planting_method') THEN
        CREATE TYPE planting_method AS ENUM ('broadcast', 'drill', 'dibble', 'row', 'hill', 'transplant');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'unit_of_sale') THEN
        CREATE TYPE unit_of_sale AS ENUM ('kg', 'ton', 'bag_50kg', 'bag_100kg', 'crate', 'tuber', 'bunch');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'root_depth_category') THEN
        CREATE TYPE root_depth_category AS ENUM ('shallow', 'medium', 'deep');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'cycle_stage') THEN
        CREATE TYPE cycle_stage AS ENUM (
            'planning', 'pre_planting', 'planting', 'crop_care',
            'harvest', 'post_harvest', 'closed', 'abandoned'
        );
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'sub_phase') THEN
        CREATE TYPE sub_phase AS ENUM ('establishment', 'vegetative', 'reproductive', 'maturation');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'task_status') THEN
        CREATE TYPE task_status AS ENUM ('pending', 'ok', 'warning', 'flag');
    END IF;
END $$;

-- ============================================================================
-- 1. Governed CropProfile Master Data + Provenance Columns (WP-70 / F-25(7))
-- ============================================================================
CREATE TABLE IF NOT EXISTS field.crop_profile (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    common_name text NOT NULL UNIQUE,
    botanical_name text,
    category crop_category NOT NULL,
    typical_cycle_days_min integer NOT NULL,
    typical_cycle_days_max integer NOT NULL,
    planting_method planting_method NOT NULL,
    seed_depth_cm_min numeric(5,2),
    seed_depth_cm_max numeric(5,2),
    row_spacing_cm_min numeric(6,2),
    row_spacing_cm_max numeric(6,2),
    plant_spacing_cm_min numeric(6,2),
    plant_spacing_cm_max numeric(6,2),
    seeds_per_hole_min integer,
    seeds_per_hole_max integer,
    seed_rate_kg_per_ha numeric(8,2),
    germination_days_min integer,
    germination_days_max integer,
    optimal_soil_temp_c_min numeric(4,1),
    optimal_soil_temp_c_max numeric(4,1),
    optimal_soil_ph_min numeric(3,1),
    optimal_soil_ph_max numeric(3,1),
    water_requirement_mm_per_week numeric(6,2),
    basal_n numeric(6,2),
    basal_p numeric(6,2),
    basal_k numeric(6,2),
    thinning_days_after_planting integer,
    first_weeding_days_after_planting integer,
    weeding_interval_days integer,
    harvest_moisture_content_pct numeric(4,1),
    expected_yield_per_ha_kg numeric(10,2) NOT NULL,
    storage_temp_c numeric(4,1),
    storage_humidity_pct numeric(4,1),
    shelf_life_days integer,
    requires_curing boolean NOT NULL DEFAULT false,
    root_depth_category root_depth_category NOT NULL DEFAULT 'medium',
    unit_of_sale unit_of_sale NOT NULL,
    kg_per_unit numeric(8,3) NOT NULL,
    is_active boolean NOT NULL DEFAULT true,
    source_citation text NOT NULL,
    reviewed_by_admin_id uuid REFERENCES identity.identity(id),
    version_number integer NOT NULL DEFAULT 1 CHECK (version_number >= 1),
    last_verified_at timestamptz NOT NULL DEFAULT now(),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz,
    CONSTRAINT chk_crop_cycle_days_plausible CHECK (typical_cycle_days_min > 0 AND typical_cycle_days_min <= typical_cycle_days_max),
    CONSTRAINT chk_crop_expected_yield_positive CHECK (expected_yield_per_ha_kg > 0),
    CONSTRAINT chk_crop_kg_per_unit_positive CHECK (kg_per_unit > 0),
    CONSTRAINT chk_crop_ph_range CHECK (optimal_soil_ph_min IS NULL OR optimal_soil_ph_max IS NULL OR optimal_soil_ph_min <= optimal_soil_ph_max),
    CONSTRAINT chk_crop_seed_depth_range CHECK (seed_depth_cm_min IS NULL OR seed_depth_cm_max IS NULL OR seed_depth_cm_min <= seed_depth_cm_max),
    CONSTRAINT chk_crop_row_spacing_range CHECK (row_spacing_cm_min IS NULL OR row_spacing_cm_max IS NULL OR row_spacing_cm_min <= row_spacing_cm_max)
);

-- Seed 6 Nigerian pilot-region crop profiles curated from IITA / NRCRI / IAR / NIHORT guides
INSERT INTO field.crop_profile (
    common_name, botanical_name, category, typical_cycle_days_min, typical_cycle_days_max,
    planting_method, seed_depth_cm_min, seed_depth_cm_max, row_spacing_cm_min, row_spacing_cm_max,
    plant_spacing_cm_min, plant_spacing_cm_max, seeds_per_hole_min, seeds_per_hole_max,
    seed_rate_kg_per_ha, germination_days_min, germination_days_max,
    optimal_soil_temp_c_min, optimal_soil_temp_c_max, optimal_soil_ph_min, optimal_soil_ph_max,
    water_requirement_mm_per_week, harvest_moisture_content_pct, expected_yield_per_ha_kg,
    requires_curing, root_depth_category, unit_of_sale, kg_per_unit, source_citation
) VALUES
    ('Maize', 'Zea mays', 'cereal', 90, 120, 'dibble', 3.0, 5.0, 75.0, 90.0, 25.0, 30.0, 1, 2, 20.00, 4, 7, 20.0, 32.0, 5.5, 7.0, 30.0, 13.5, 3500.00, false, 'medium', 'bag_100kg', 100.000, 'IITA & IAR Zaria Maize Agronomy Production Guide'),
    ('Cassava', 'Manihot esculenta', 'root_tuber', 270, 365, 'row', 5.0, 10.0, 90.0, 100.0, 80.0, 100.0, 1, 1, NULL, 7, 14, 22.0, 32.0, 5.5, 6.5, 25.0, 65.0, 20000.00, false, 'deep', 'ton', 1000.000, 'IITA & NRCRI Umudike Cassava Cultivation Manual'),
    ('Rice (FARO 44)', 'Oryza sativa', 'cereal', 110, 120, 'transplant', 2.0, 3.0, 20.0, 25.0, 20.0, 20.0, 2, 3, 50.00, 5, 8, 22.0, 34.0, 5.0, 6.5, 45.0, 14.0, 4500.00, false, 'shallow', 'bag_50kg', 50.000, 'AfricaRice & NCRI Badeggi FARO 44 Rice Production Guide'),
    ('Tomato (Roma VF)', 'Solanum lycopersicum', 'vegetable', 75, 95, 'transplant', 1.0, 2.0, 60.0, 90.0, 45.0, 60.0, 1, 1, 0.40, 5, 8, 18.0, 28.0, 6.0, 6.8, 30.0, 93.0, 18000.00, false, 'medium', 'crate', 35.000, 'NIHORT Ibadan Open-Field Tomato Production Bulletin'),
    ('Cowpea', 'Vigna unguiculata', 'legume', 60, 80, 'dibble', 2.5, 4.0, 60.0, 75.0, 20.0, 30.0, 2, 3, 25.00, 4, 6, 20.0, 32.0, 5.5, 7.0, 25.0, 12.0, 1500.00, true, 'medium', 'bag_100kg', 100.000, 'IITA Kano Improved Cowpea Production Handbook'),
    ('Soybean', 'Glycine max', 'oilseed', 95, 115, 'drill', 2.5, 4.0, 50.0, 75.0, 5.0, 10.0, 1, 2, 50.00, 5, 8, 20.0, 30.0, 6.0, 6.8, 30.0, 13.0, 2200.00, true, 'medium', 'bag_50kg', 50.000, 'IITA & IAR Soybean Agronomy Manual for Nigeria')
ON CONFLICT (common_name) DO NOTHING;

-- ============================================================================
-- 2. CropCycle with Snapshots & One-Active-Per-Open-Field Partial Index (WP-71)
-- ============================================================================
CREATE TABLE IF NOT EXISTS field.crop_cycle (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    production_unit_id uuid NOT NULL REFERENCES field.production_unit(id),
    crop_profile_id uuid NOT NULL REFERENCES field.crop_profile(id),
    planting_date date NOT NULL,
    stage cycle_stage NOT NULL DEFAULT 'planning',
    area_ha numeric(12,6) NOT NULL CHECK (area_ha > 0),
    unit_system_type system_type NOT NULL,
    growing_medium_class growing_medium_class NOT NULL,
    growing_method_profile_id uuid NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz
);

CREATE OR REPLACE FUNCTION field.crop_cycle_snapshot_unit()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_sys system_type;
    v_med growing_medium_class;
    v_area_ha numeric(12,6);
BEGIN
    SELECT
        pu.system_type,
        pu.growing_medium_class,
        COALESCE(pu.area_hectares, ROUND((pu.area_square_meters / 10000.0)::numeric, 6), 0.010000)
    INTO v_sys, v_med, v_area_ha
    FROM field.production_unit pu
    WHERE pu.id = NEW.production_unit_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Referenced production_unit % does not exist', NEW.production_unit_id
            USING ERRCODE = '23503';
    END IF;

    NEW.unit_system_type := v_sys;
    NEW.growing_medium_class := v_med;
    IF NEW.area_ha IS NULL OR NEW.area_ha <= 0 THEN
        NEW.area_ha := GREATEST(v_area_ha, 0.000001);
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_crop_cycle_snapshot_unit ON field.crop_cycle;
CREATE TRIGGER trg_crop_cycle_snapshot_unit
    BEFORE INSERT ON field.crop_cycle
    FOR EACH ROW
    EXECUTE FUNCTION field.crop_cycle_snapshot_unit();

-- BR-A-07 & Doc/02B-ENG §3.3 (BR-E-06): strictly 1 active cycle on open_field; relaxed on CEA units
CREATE UNIQUE INDEX IF NOT EXISTS uq_one_active_cycle_per_open_field
    ON field.crop_cycle(production_unit_id)
    WHERE stage NOT IN ('closed', 'abandoned') AND unit_system_type = 'open_field';

CREATE INDEX IF NOT EXISTS idx_cropcycle_unit
    ON field.crop_cycle(production_unit_id, created_at DESC);

-- ============================================================================
-- 3. Task Catalog & Append-Preserving OperationLog (WP-72 / BR-A-09)
-- ============================================================================
CREATE TABLE IF NOT EXISTS field.operation_task_definition (
    id text PRIMARY KEY,
    stage cycle_stage NOT NULL,
    sub_phase sub_phase,
    name text NOT NULL,
    required boolean NOT NULL DEFAULT false,
    recurring boolean NOT NULL DEFAULT false,
    depends_on text[] NOT NULL DEFAULT '{}',
    knowledge_ref_type text,
    knowledge_ref_id uuid,
    capability_code text,
    applicable_system_types system_type[] NOT NULL DEFAULT '{open_field}',
    applicable_medium_classes growing_medium_class[] NOT NULL DEFAULT '{soil_based}',
    requires_transplant boolean NOT NULL DEFAULT false,
    validation_field text
);

INSERT INTO platform.capability_module (module_code, display_name, enabled_by_default, description) VALUES
    ('core.irrigation', 'Farm Irrigation Scheduling & Tasks', false, 'Enables irrigation-specific recurring tasks (CC-06) when active (FR-CY-001 / FR-CY-002).')
ON CONFLICT (module_code) DO NOTHING;

INSERT INTO field.operation_task_definition (
    id, stage, sub_phase, name, required, recurring, depends_on,
    capability_code, applicable_system_types, applicable_medium_classes, requires_transplant, validation_field
) VALUES
    ('PRE-01', 'pre_planting', NULL, 'Land clearing & bed preparation', true, false, '{}', NULL, '{open_field}', '{soil_based}', false, NULL),
    ('PRE-06', 'pre_planting', NULL, 'Soil sampling & pH test', true, false, '{PRE-01}', NULL, '{open_field}', '{soil_based}', false, 'soilPh'),
    ('PRE-14', 'pre_planting', NULL, 'Nursery bed & seedling preparation', true, false, '{PRE-01}', NULL, '{open_field}', '{soil_based}', true, NULL),
    ('PLT-01', 'planting', NULL, 'Seed sowing & depth check', true, false, '{}', NULL, '{open_field}', '{soil_based}', false, 'seedDepthCm'),
    ('PLT-02', 'planting', NULL, 'Row & plant spacing verification', true, false, '{PLT-01}', NULL, '{open_field}', '{soil_based}', false, 'rowSpacingCm'),
    ('CC-01', 'crop_care', 'establishment', 'Germination & stand count check', true, false, '{}', NULL, '{open_field}', '{soil_based}', false, NULL),
    ('CC-05', 'crop_care', 'vegetative', 'Weeding & canopy management', true, true, '{CC-01}', NULL, '{open_field}', '{soil_based}', false, NULL),
    ('CC-06', 'crop_care', 'vegetative', 'Scheduled irrigation log', false, true, '{}', 'core.irrigation', '{open_field}', '{soil_based}', false, NULL),
    ('CC-12', 'crop_care', 'reproductive', 'Pest & disease scouting inspection', false, true, '{}', NULL, '{open_field}', '{soil_based}', false, NULL),
    ('CC-22', 'crop_care', 'maturation', 'Crop maturity assessment', true, false, '{CC-01}', NULL, '{open_field}', '{soil_based}', false, NULL),
    ('HRV-17', 'harvest', NULL, 'Harvest yield & moisture log', true, false, '{}', NULL, '{open_field}', '{soil_based}', false, 'harvestMoisturePct'),
    ('PHV-01', 'post_harvest', NULL, 'Post-harvest curing & batch disposition', true, false, '{}', NULL, '{open_field}', '{soil_based}', false, NULL)
ON CONFLICT (id) DO NOTHING;

CREATE TABLE IF NOT EXISTS field.operation_log (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    crop_cycle_id uuid NOT NULL REFERENCES field.crop_cycle(id),
    task_definition_id text NOT NULL REFERENCES field.operation_task_definition(id),
    logged_at timestamptz NOT NULL DEFAULT now(),
    server_received_at timestamptz NOT NULL DEFAULT now(),
    logged_by_identity_id uuid NOT NULL REFERENCES identity.identity(id),
    acting_agent_id uuid REFERENCES identity.identity(id),
    beneficiary_farmer_id uuid REFERENCES identity.identity(id),
    status task_status NOT NULL DEFAULT 'pending',
    data jsonb,
    validation_message text,
    override_reason text,
    override_by_identity_id uuid REFERENCES identity.identity(id),
    override_at timestamptz,
    client_guid uuid UNIQUE,
    CONSTRAINT chk_override_consistency CHECK (
        (override_reason IS NULL) = (override_by_identity_id IS NULL)
    )
);

CREATE INDEX IF NOT EXISTS idx_oplog_cycle ON field.operation_log(crop_cycle_id, logged_at DESC);
CREATE INDEX IF NOT EXISTS idx_oplog_flagged
    ON field.operation_log(crop_cycle_id)
    WHERE status = 'flag' AND override_reason IS NULL;

CREATE OR REPLACE FUNCTION field.enforce_operation_log_immutability()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'field.operation_log entries cannot be deleted (BR-A-09): % is forbidden', TG_OP
            USING ERRCODE = '42501';
    END IF;

    IF NEW.status IS DISTINCT FROM OLD.status
       OR NEW.logged_at IS DISTINCT FROM OLD.logged_at
       OR NEW.task_definition_id IS DISTINCT FROM OLD.task_definition_id
       OR NEW.crop_cycle_id IS DISTINCT FROM OLD.crop_cycle_id THEN
        RAISE EXCEPTION 'field.operation_log status, logged_at, task_definition_id, and crop_cycle_id are immutable (BR-A-09)'
            USING ERRCODE = '23514';
    END IF;

    IF OLD.status <> 'flag' AND (NEW.override_reason IS NOT NULL OR NEW.override_by_identity_id IS NOT NULL) THEN
        RAISE EXCEPTION 'Only flag-status operation_log entries can be overridden (BR-A-09)'
            USING ERRCODE = '23514';
    END IF;

    IF OLD.override_at IS NOT NULL THEN
        RAISE EXCEPTION 'An operation_log override has already been recorded and cannot be altered (BR-A-09)'
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_operation_log_immutability ON field.operation_log;
CREATE TRIGGER trg_operation_log_immutability
    BEFORE UPDATE OR DELETE ON field.operation_log
    FOR EACH ROW
    EXECUTE FUNCTION field.enforce_operation_log_immutability();

-- ============================================================================
-- 4. HarvestBatch Table (used by harvest -> post_harvest -> closed stage gates)
-- ============================================================================
CREATE TABLE IF NOT EXISTS field.harvest_batch (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    crop_cycle_id uuid NOT NULL REFERENCES field.crop_cycle(id),
    quantity_kg numeric(12,2) NOT NULL CHECK (quantity_kg > 0),
    grade text,
    moisture_pct numeric(4,1),
    harvest_date date NOT NULL,
    disposition text NOT NULL DEFAULT 'open'
        CHECK (disposition IN ('open', 'sold', 'stored', 'processed', 'lost')),
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_harvest_batch_cycle ON field.harvest_batch(crop_cycle_id);
