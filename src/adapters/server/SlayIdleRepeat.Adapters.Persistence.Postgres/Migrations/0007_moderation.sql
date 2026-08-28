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

-- One reading of one account's cumulative anti-cheat measures. A rate needs two rows; storing a
-- rate instead would throw away the ability to recompute one over a different window.
--
-- wallet_total is a BALANCE and falls when the player spends, so the movement between two rows is
-- net income at best. legend_xp and battle_hash_mismatches only grow, and a fall in either is a
-- storage fault rather than a player action.
CREATE TABLE plausibility_observations (
    player_id              text        NOT NULL,
    observed_at_utc        timestamptz NOT NULL,
    wallet_total           bigint      NOT NULL,
    legend_xp              bigint      NOT NULL,
    battle_hash_mismatches integer     NOT NULL,
    PRIMARY KEY (player_id, observed_at_utc)
);

-- The sweep reads one account's most recent reading, over and over. Descending, so that read is the
-- index's first row rather than a scan of the account's whole history.
CREATE INDEX ix_plausibility_observations_latest
    ON plausibility_observations (player_id, observed_at_utc DESC);

-- The SHARED review queue. The plausibility sweep is its first producer; the duel anti-cheat pass
-- and player reports route into this same table, reviewed by the same people through the same
-- ladder. source is unconstrained text for the reason 0004's category column is: a CHECK here would
-- invent the later producers' names on their behalf.
--
-- Every producer creates an entry in state OPEN. Flags go to a review queue, never to an automatic
-- action, so nothing writes CONFIRMED without a human having decided it.
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
-- entry_id is NOT NULL and references the queue: the ladder acts only on repeated, CONFIRMED
-- manipulation, and the entry is the only thing that can evidence the confirmed half.
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
    -- Lifted before applied would make the window negative, and a negative window reads as
    -- permanently active on one comparison and never active on the other.
    CONSTRAINT ck_player_sanctions_window
        CHECK (lifted_at_utc IS NULL OR lifted_at_utc >= applied_at_utc)
);

-- The request path asks "which accounts are locked right now", never "what does this account
-- carry" — the answer is cached in the process and refreshed by the background pass.
CREATE INDEX ix_player_sanctions_live ON player_sanctions (kind, lifted_at_utc);
