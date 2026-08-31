-- M5-08 — the segment-send audit log (28 A6). Every segment send is logged with the predicate, the
-- dry-run count, the actual count and the operator: this is economy-affecting action and must be
-- attributable.
--
-- ⚠️ operator is an UNVERIFIED string. There is no operator identity system anywhere in this
-- repository — no admin authentication, no accounts, no roles — so this column holds whatever the
-- person at the terminal typed into --operator. It is required rather than optional, and recorded
-- rather than trusted; the day an admin identity exists, this column is what it replaces.
--
-- Append-only by convention rather than by grant: the same shape as economy_events, and for the same
-- reason — a row that can be edited is a record of a send that can be edited afterwards.

CREATE TABLE mail_segment_sends (
    send_id        bigserial   PRIMARY KEY,
    sent_at_utc    timestamptz NOT NULL DEFAULT now(),
    operator       text        NOT NULL,
    predicate      text        NOT NULL,
    template_id    text        NOT NULL,
    attachments    text        NOT NULL,
    dry_run_count  integer     NOT NULL,
    actual_count   integer     NOT NULL
);

-- The question an incident asks is "what was sent, in what order, around this time", so the index is
-- the send order itself.
CREATE INDEX ix_mail_segment_sends_time ON mail_segment_sends (sent_at_utc);
