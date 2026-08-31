-- M5-06 — the anonymous device account, its token families and the deletion record.
--
-- Five tables, not four: the identified deletion record and the anonymous tombstone are separate
-- because the hard-delete sweep REMOVES one and KEEPS the other. Folding them together would make
-- the surviving row carry the player id it exists to forget.
--
-- Nothing here stores a secret or a refresh token in the clear: auth_devices.secret_hash and
-- auth_refresh_tokens.token_hash are SHA-256 digests, which is why both are bytea rather than text.
--
-- No column keys on a guild: the membership/contribution anonymisation a later milestone owns is a
-- new scope row for the sweep, not a migration of these tables.

CREATE TABLE auth_devices (
    device_id        text        PRIMARY KEY,
    player_id        text        NOT NULL,
    secret_hash      bytea       NOT NULL,
    created_at_utc   timestamptz NOT NULL,
    last_seen_at_utc timestamptz NOT NULL
);

CREATE INDEX ix_auth_devices_player ON auth_devices (player_id);

-- revoked_at_utc NULL means live. revoked_reason is unconstrained text rather than an enum: the
-- reason vocabulary lives in code, and a CHECK here would need a migration every time it grows.
CREATE TABLE auth_token_families (
    family_id      text        PRIMARY KEY,
    device_id      text        NOT NULL,
    player_id      text        NOT NULL,
    created_at_utc timestamptz NOT NULL,
    revoked_at_utc timestamptz,
    revoked_reason text
);

CREATE INDEX ix_auth_token_families_player ON auth_token_families (player_id);

-- rotated_at_utc NULL means live. The family index is what a reuse revocation reads: presenting a
-- rotated token revokes every sibling, and without it that sweep is a table scan on the hot path.
CREATE TABLE auth_refresh_tokens (
    token_hash     bytea       PRIMARY KEY,
    family_id      text        NOT NULL,
    device_id      text        NOT NULL,
    player_id      text        NOT NULL,
    issued_at_utc  timestamptz NOT NULL,
    expires_at_utc timestamptz NOT NULL,
    rotated_at_utc timestamptz
);

CREATE INDEX ix_auth_refresh_tokens_family ON auth_refresh_tokens (family_id);

CREATE TABLE auth_account_deletions (
    player_id              text        PRIMARY KEY,
    requested_at_utc       timestamptz NOT NULL,
    hard_delete_due_at_utc timestamptz NOT NULL
);

CREATE INDEX ix_auth_account_deletions_due ON auth_account_deletions (hard_delete_due_at_utc);

-- What survives the hard delete: timestamps under a digest of the player id, and nothing else. No
-- id, no name, no device — the row can answer "an account was deleted then" and no other question.
CREATE TABLE auth_deletion_tombstones (
    player_digest       bytea       PRIMARY KEY,
    requested_at_utc    timestamptz NOT NULL,
    hard_deleted_at_utc timestamptz NOT NULL
);
