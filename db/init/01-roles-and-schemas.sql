-- Per-service schemas and roles.
--
-- Each service connects as its own role with grants on only its own schema. One instance is
-- cheaper than seven, but a compromised Catalog service still cannot read the users table,
-- and the split can later become separate instances without touching application code.

\set ON_ERROR_STOP on

DO $$
DECLARE
    svc   text;
    -- Development-only passwords. In GCP these roles authenticate with IAM database
    -- authentication and no password exists to leak.
    services text[] := ARRAY['identity', 'catalog', 'basket', 'ordering', 'inventory', 'payment'];
BEGIN
    FOREACH svc IN ARRAY services LOOP
        EXECUTE format('CREATE SCHEMA IF NOT EXISTS %I', svc);

        IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'sama_' || svc) THEN
            EXECUTE format('CREATE ROLE %I LOGIN PASSWORD %L', 'sama_' || svc, 'sama_dev_password');
        END IF;

        EXECUTE format('GRANT USAGE, CREATE ON SCHEMA %I TO %I', svc, 'sama_' || svc);
        EXECUTE format('ALTER SCHEMA %I OWNER TO %I', svc, 'sama_' || svc);

        -- Explicitly deny the default: without this, PUBLIC retains connect rights and any
        -- role could enumerate another service's tables.
        EXECUTE format('REVOKE ALL ON SCHEMA %I FROM PUBLIC', svc);
    END LOOP;
END
$$;

-- No role may create objects in public; every table must live in an owning service's schema.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
