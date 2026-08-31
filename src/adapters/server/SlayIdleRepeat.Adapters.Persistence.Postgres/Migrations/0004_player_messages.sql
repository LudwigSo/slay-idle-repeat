-- M5-05 — the inbox rows (14 §7.1). The TABLE is this task's; the IMessageRepository port, its
-- adapter and the six-value category vocabulary are the inbox task's (M5-08) — which is why
-- category is deliberately unconstrained text here: a CHECK would invent the six names on that
-- task's behalf, and the wrong six would refuse its first real row.
--
-- expires_at_utc NULL means the message never expires (the moderation/account categories);
-- read_at_utc / claimed_at_utc are NULL until the player does the thing they name.

CREATE TABLE player_messages (
    message_id      text        PRIMARY KEY,
    player_id       text        NOT NULL,
    category        text        NOT NULL,
    template_id     text        NOT NULL,
    params          jsonb       NOT NULL,
    attachments     jsonb       NOT NULL,
    created_at_utc  timestamptz NOT NULL,
    expires_at_utc  timestamptz,
    read_at_utc     timestamptz,
    claimed_at_utc  timestamptz
);

-- The inbox reads by owner and expiry ("give me this player's live messages"), so that pair is the
-- one index this task authors.
CREATE INDEX ix_player_messages_expiry ON player_messages (player_id, expires_at_utc);
