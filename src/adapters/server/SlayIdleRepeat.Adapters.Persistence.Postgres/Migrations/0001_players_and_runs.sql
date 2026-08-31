-- M5-05 — the system-of-record rows (14 §7.1, §16.4).
--
-- doc is THE row: the exact SnapshotCodec bytes the application's rehydration reads. The typed
-- columns beside it are extracted from the same snapshot at write time, for queries and keys only —
-- a reader that answered from them instead of doc would be a second, driftable source of truth.
--
-- The sequence counters live here, beside the snapshots and never inside them: they are transport
-- state (14 §16.3), and putting them in the snapshot would bump SchemaVersion for a number the
-- domain never sees. NULL means "this scope was never opened"; 0 means "opened, first command
-- pending" — the two answers the gateway's RUN_NOT_FOUND check tells apart.

CREATE TABLE players (
    player_id           text        PRIMARY KEY,
    doc                 jsonb       NOT NULL,
    last_meta_sequence  bigint,
    display_name        text        NOT NULL,
    legend_level        integer     NOT NULL,
    last_applied_at_utc timestamptz NOT NULL,
    updated_at_utc      timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE players IS
    'One row per player: doc = the canonical stored slice (player + inline active run), typed columns extracted from it. last_meta_sequence is the 14 16.3 lifetime counter, NULL until the first recorded meta command.';

-- Deliberately NO foreign key from runs to players: the run store's contract accepts a run row
-- whose player this store has never seen (an archive restore, a probe seed), and the pair-move
-- guarantee lives in the one transaction that writes both, not in a constraint that would make
-- half the port's contract unimplementable.
CREATE TABLE runs (
    run_id              text        PRIMARY KEY,
    player_id           text        NOT NULL,
    doc                 jsonb       NOT NULL,
    last_sequence       bigint,
    phase               text        NOT NULL,
    chapter_id          integer     NOT NULL,
    tier                text        NOT NULL,
    last_applied_at_utc timestamptz NOT NULL,
    expires_at_utc      timestamptz NOT NULL,
    created_at_utc      timestamptz NOT NULL DEFAULT now(),
    updated_at_utc      timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE runs IS
    'One row per run: doc = the canonical run snapshot. expires_at_utc is the sliding 48 h lifetime, restamped on every save; expiry is enforced at read (an expired row answers as absent) — no retention rule is authored, so nothing here deletes.';

CREATE INDEX ix_runs_player ON runs (player_id);
