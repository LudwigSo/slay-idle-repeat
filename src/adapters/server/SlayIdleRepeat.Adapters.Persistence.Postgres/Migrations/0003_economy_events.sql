-- M5-05 — the append-only economy event log (14 §7.1, §16.4 item 3).
--
-- One row per domain event of every accepted command, enriched by the application with the
-- identity the domain deliberately does not carry (player, run, command, instant). sequence is the
-- event's ordinal within its command's list — the domain's own DomainEvent.Sequence — not the wire
-- counter; the wire counter is recoverable through the command's idempotency record.
--
-- Append-only by convention and by shape: no updated_at, no state columns, and the unique triple
-- makes a re-append of a replayed command's events collide instead of duplicating history.
-- No retention rule is authored (S6), so nothing here deletes or partitions; the task that authors
-- one owns both.
--
-- The appends themselves land inside the accepted command's one transaction — that wiring is the
-- unit-of-work task's; this file only makes the table exist for it.

CREATE TABLE economy_events (
    id              bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    player_id       text        NOT NULL,
    run_id          text,
    command_id      text        NOT NULL,
    sequence        integer     NOT NULL,
    occurred_at_utc timestamptz NOT NULL,
    event_type      text        NOT NULL,
    payload         jsonb       NOT NULL,
    UNIQUE (player_id, command_id, sequence)
);

CREATE INDEX ix_economy_events_player_time ON economy_events (player_id, occurred_at_utc);
