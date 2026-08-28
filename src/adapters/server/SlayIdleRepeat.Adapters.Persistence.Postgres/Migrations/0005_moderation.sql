-- M5-14 — the moderation schema: plausibility observations, the shared review queue, and the
-- sanctions ladder as data.
--
-- The TABLES are this task's. There is deliberately NO Postgres implementation of IModerationStore
-- behind them yet: nothing in this repository could observe one — no in-repo fixture may reach a
-- live database, and no compose-boot probe posts a moderation row — so an adapter written now would
-- be exercised by nothing anywhere. The task that gives the review queue a human surface owes the
-- implementation and the probe that watches it. Until then the running process uses the volatile
-- store and says so at startup.
--
-- reviewed_by and applied_by are UNVERIFIED strings. Nothing in this system authenticates an
-- operator: player auth is the only auth that exists. They are recorded because a decision on the
-- sanctions ladder with no name attached is worse than one with an unverified name — not because
-- anything checked them. There is no endpoint that writes these tables.
--
-- 0001 states the house rule that these tables do NOT follow: no foreign keys, because a port whose
-- contract includes restoring or seeding one row at a time cannot honour them. player_sanctions.entry_id
-- is the exception and earns it — these three tables are one bounded context created by one file,
-- never restored apart, and a sanction whose review entry is missing is not a row worth keeping:
-- the ladder acts only on repeated, CONFIRMED manipulation, and the entry is the only evidence of
-- the confirmed half.

-- One reading of one account's cumulative anti-cheat measures. A rate needs two rows; storing a
-- rate instead would throw away the ability to recompute one over a different window.
--
-- wallet_total is a BALANCE and falls when the player spends, so the movement between two rows is
-- net income at best. legend_xp and battle_hash_mismatches only grow, and a fall in either is a
-- storage fault rather than a player action.
--
-- ⚠️ NOTHING PRUNES THIS TABLE, and it gains a row per account per sweep — twenty-four a day per
-- account at the shipped interval, forever, in a table whose only reader wants the newest row. The
-- rule the sweep can enforce once a durable store exists is to delete an account's rows older than
-- its previous one; said here rather than left for whoever first notices the size.
--
-- The primary key's own index answers the one read there is ("this account's newest reading"): a
-- btree scans in both directions, so no separate descending index is needed and none is created.
CREATE TABLE plausibility_observations (
    player_id              text        NOT NULL,
    observed_at_utc        timestamptz NOT NULL,
    wallet_total           bigint      NOT NULL,
    legend_xp              bigint      NOT NULL,
    battle_hash_mismatches integer     NOT NULL,
    PRIMARY KEY (player_id, observed_at_utc),
    CONSTRAINT ck_plausibility_observations_player
        CHECK (length(btrim(player_id)) > 0)
);

-- The SHARED review queue. The plausibility sweep is its first producer; the duel anti-cheat pass
-- and player reports route into this same table, reviewed by the same people through the same
-- ladder. source is unconstrained text for the reason 0004's category column is: a CHECK here would
-- invent the later producers' names on their behalf.
--
-- Every producer creates an entry in state OPEN. Flags go to a review queue, never to an automatic
-- action, so nothing writes CONFIRMED without a human having decided it.
--
-- The blank checks mirror what ReviewQueueEntry refuses to construct. Without them the database
-- accepts rows its only writer would reject, and the first thing to write one would be whatever
-- imports history from somewhere else.
CREATE TABLE moderation_reviews (
    entry_id        text        PRIMARY KEY,
    source          text        NOT NULL,
    subject_id      text        NOT NULL,
    state           text        NOT NULL,
    reason          text        NOT NULL,
    raised_at_utc   timestamptz NOT NULL,
    reviewed_by     text,
    reviewed_at_utc timestamptz,
    review_notes    text,
    CONSTRAINT ck_moderation_reviews_state
        CHECK (state IN ('OPEN', 'CONFIRMED', 'DISMISSED')),
    CONSTRAINT ck_moderation_reviews_text
        CHECK (length(btrim(entry_id)) > 0
            AND length(btrim(source)) > 0
            AND length(btrim(subject_id)) > 0
            AND length(btrim(reason)) > 0
            AND (reviewed_by IS NULL OR length(btrim(reviewed_by)) > 0)
            AND (review_notes IS NULL OR length(btrim(review_notes)) > 0)),
    -- A verdict is all three fields or none of them: a decided entry with no reviewer, or a
    -- reviewer on an open one, is a half-written decision nobody can audit.
    CONSTRAINT ck_moderation_reviews_verdict
        CHECK (
            (state = 'OPEN'
                AND reviewed_by IS NULL AND reviewed_at_utc IS NULL AND review_notes IS NULL)
            OR (state <> 'OPEN'
                AND reviewed_by IS NOT NULL AND reviewed_at_utc IS NOT NULL AND review_notes IS NOT NULL))
);

-- A reviewer's working set is "what is still open, oldest first" — the one read the queue exists
-- for, and a response-time commitment nobody can meet off a table scan.
CREATE INDEX ix_moderation_reviews_open ON moderation_reviews (state, raised_at_utc);

-- Every sanction on record, lifted or not.
--
-- Only ACCOUNT_ACTION is enforced on the wire (HTTP 403). SHADOW_EXCLUDE_LADDER, RATING_RESET and
-- NAME_RESET are recorded here and acted on by the surfaces that own them, none of which exists
-- yet: a shadow exclusion that refused requests would stop being shadow, and the other two are
-- one-off writes rather than standing refusals.
--
-- No secondary index. The only read there is today is "every sanction", which the background pass
-- makes once a cycle to rebuild the locked-account set; an index chosen now would be chosen for a
-- query shape nobody has written. The task that lands the durable adapter indexes it for the query
-- it actually issues.
CREATE TABLE player_sanctions (
    sanction_id     text        PRIMARY KEY,
    subject_id      text        NOT NULL,
    kind            text        NOT NULL,
    entry_id        text        NOT NULL REFERENCES moderation_reviews (entry_id),
    applied_by      text        NOT NULL,
    applied_at_utc  timestamptz NOT NULL,
    lifted_at_utc   timestamptz,
    CONSTRAINT ck_player_sanctions_kind
        CHECK (kind IN ('SHADOW_EXCLUDE_LADDER', 'RATING_RESET', 'ACCOUNT_ACTION', 'NAME_RESET')),
    CONSTRAINT ck_player_sanctions_text
        CHECK (length(btrim(sanction_id)) > 0
            AND length(btrim(subject_id)) > 0
            AND length(btrim(entry_id)) > 0
            AND length(btrim(applied_by)) > 0),
    -- Lifted before applied would make the window negative, and a negative window reads as
    -- permanently active on one comparison and never active on the other.
    CONSTRAINT ck_player_sanctions_window
        CHECK (lifted_at_utc IS NULL OR lifted_at_utc >= applied_at_utc)
);
