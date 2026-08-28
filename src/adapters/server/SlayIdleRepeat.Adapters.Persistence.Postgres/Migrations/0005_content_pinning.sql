-- M5-09 — the content pins: which content version a run and a session are judged against.
--
-- A typed column beside the snapshot, never a field inside it. The pin is transport-tier
-- bookkeeping the domain never reads, and putting it in the stored document would bump the
-- snapshot schema version — re-baselining every reference vector for a stamp no rule looks at.
--
-- Why a run needs one at all: the server is the source of truth for content, and content ships
-- without a client build. A run opened before a balance patch must keep being played against the
-- numbers it opened on, or a patch silently changes a run already in flight and its replay stops
-- reproducing its original outcome.
--
-- Nullable on runs, deliberately: rows written before this migration have no pin, and a command on
-- one falls back to the current version out loud rather than being refused. Backfilling them with
-- the current stamp would be a lie — those runs were opened against something nobody recorded.

ALTER TABLE runs ADD COLUMN content_version text;

COMMENT ON COLUMN runs.content_version IS
    'The 64 lowercase hex content stamp this run was opened against, fixed for the run''s whole life. NULL for rows written before content pinning existed; those fall back to the current version with a logged warning.';

-- One row per player, holding the version their current session said hello on. Deliberately NOT an
-- auth or session table: it carries no token, no device, no expiry of its own, and no identity
-- beyond the player key. It exists so a client waved through once is not turned back by the very
-- stamp its own hello established, and so a live session counts as a reference the retention sweep
-- must respect.
CREATE TABLE content_session_pins (
    player_id        text        PRIMARY KEY,
    content_version  text        NOT NULL,
    pinned_at_utc    timestamptz NOT NULL,
    last_seen_at_utc timestamptz NOT NULL
);

COMMENT ON TABLE content_session_pins IS
    'One row per player: the content version their current session began against. Replaced on the next session begin, never appended to — a session has exactly one answer at a time.';

-- No foreign key to players, for 0001's reason: a pin may be written for a player row this store
-- has never seen, and the pairing guarantee lives in the one transaction that writes both.

-- Both directions of the retention sweep read these. The runs index is partial because a NULL pin
-- is not a reference to anything, and the index exists to answer "what is still referenced?".
CREATE INDEX ix_runs_content_version ON runs (content_version) WHERE content_version IS NOT NULL;

CREATE INDEX ix_content_session_pins_version ON content_session_pins (content_version, last_seen_at_utc);
