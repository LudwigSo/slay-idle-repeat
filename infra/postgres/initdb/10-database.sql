-- Slay Idle Repeat — Postgres bootstrap.
--
-- Runs ONCE, as superuser, against the database named by POSTGRES_DB, the first
-- time the data directory is initialised. Delete the volume
-- (`docker compose down -v`) to make it run again.
--
-- 14 §7.1 🔒: Postgres is "the system of record for everything, per command".
--
-- ============================================================================
-- ⚠️ THERE ARE NO TABLES IN THIS FILE, ON PURPOSE.
--
-- The schema — profiles (JSONB/typed split), run snapshots, idempotency
-- outcomes, the append-only economy event log, player messages (28 A), ghosts,
-- ratings, ladder, seasons, entitlements — and the migration runner that
-- creates them are M5-05's deliverable (14 §7). Inventing a guess at them here
-- would give M5 a schema to fight with rather than a clean database to create.
--
-- What this stack guarantees today, and all it guarantees, is: the database and
-- the login role that the api service's ConnectionStrings__Postgres names both
-- exist, with settings that make results reproducible across machines. Those two
-- come from the image's POSTGRES_DB / POSTGRES_USER / POSTGRES_PASSWORD
-- variables in docker-compose.yml; this file adds the settings.
-- ============================================================================

-- UTC everywhere. 14 §16.4 commits snapshots, idempotency outcomes and events in
-- one transaction; every timestamp in that transaction must mean the same thing
-- regardless of the developer's timezone. `timestamptz` plus a UTC session
-- default is how that is kept true even for hand-written psql queries.
DO $$
BEGIN
    EXECUTE format('ALTER DATABASE %I SET timezone TO ''UTC''', current_database());
    EXECUTE format('ALTER DATABASE %I SET datestyle TO ''ISO, YMD''', current_database());
    -- Fail fast rather than hang forever if a query takes a lock it cannot get.
    -- Generous values: these are guards against a deadlocked dev box, not a
    -- production SLA.
    EXECUTE format('ALTER DATABASE %I SET lock_timeout TO ''10s''', current_database());
    EXECUTE format('ALTER DATABASE %I SET idle_in_transaction_session_timeout TO ''60s''', current_database());

    EXECUTE format(
        'COMMENT ON DATABASE %I IS %L',
        current_database(),
        'Slay Idle Repeat — system of record (14 §7.1). Schema and migrations land with M5-05; this database is created empty by the local compose stack (M0-03).');
END
$$;

-- A visible, queryable statement of the above, so someone who connects and finds
-- no tables learns why in the place they are already looking.
CREATE SCHEMA IF NOT EXISTS meta;
COMMENT ON SCHEMA meta IS 'Local-stack bookkeeping only. The application schema is created by M5-05 migrations.';

CREATE TABLE meta.bootstrap (
    id                integer PRIMARY KEY GENERATED ALWAYS AS IDENTITY,
    created_at        timestamptz NOT NULL DEFAULT now(),
    created_by        text        NOT NULL,
    note              text        NOT NULL
);
COMMENT ON TABLE meta.bootstrap IS 'Written once by infra/postgres/initdb. Not application data, and not a migration history — M5-05 brings its own.';

INSERT INTO meta.bootstrap (created_by, note) VALUES (
    'infra/postgres/initdb/10-database.sql (M0-03)',
    'Database and role created by the local docker compose stack. No application schema exists yet: tables, indexes and the migration runner are M5-05 (14 §7). If you are looking for the profiles or run-snapshot tables, they have not been written.'
);
