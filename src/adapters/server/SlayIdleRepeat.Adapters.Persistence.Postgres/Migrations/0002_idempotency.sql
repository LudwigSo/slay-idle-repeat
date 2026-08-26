-- M5-05 — the idempotency outcome records (14 §16.3, §16.4).
--
-- Keyed on (scope_kind, scope_key, command_id): the same command id is legal in a run's domain and
-- in the player's lifetime domain, with different lifetimes. scope_key is the player id for the
-- player kind and player:run for the run kind; player_id/run_id are the same identities as typed
-- columns, for queries.
--
-- response_body is TEXT, never jsonb, deliberately: a duplicate replays these bytes BYTE-IDENTICALLY
-- and jsonb normalizes key order and whitespace, which would make the replay a reformatting.
--
-- expires_at_utc is NULL for run-kind records: a run's records live exactly as long as the run
-- itself (a resumable run replays ANY of its outcomes), so their expiry is the runs row's sliding
-- expires_at_utc, joined at read. Player-kind records carry their own instant. Expiry is enforced
-- at read; no retention rule is authored, so nothing here deletes.

CREATE TABLE idempotency_records (
    scope_kind      text        NOT NULL CHECK (scope_kind IN ('run', 'player')),
    scope_key       text        NOT NULL,
    player_id       text        NOT NULL,
    run_id          text,
    command_id      text        NOT NULL,
    sequence        bigint      NOT NULL,
    command_type    text        NOT NULL,
    payload         jsonb       NOT NULL,
    response_body   text        NOT NULL,
    opens_scope     text,
    recorded_at_utc timestamptz NOT NULL DEFAULT now(),
    expires_at_utc  timestamptz,
    PRIMARY KEY (scope_kind, scope_key, command_id),
    CHECK ((scope_kind = 'run') = (run_id IS NOT NULL)),
    CHECK ((scope_kind = 'player') = (expires_at_utc IS NOT NULL))
);

COMMENT ON TABLE idempotency_records IS
    'One row per processed command per scope: the full response envelope (replayed byte-identically) plus the payload identity an IDEMPOTENCY_CONFLICT is decided on. The per-scope counters live on players.last_meta_sequence / runs.last_sequence.';
