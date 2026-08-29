# Inbox probe — the StoreBackedAdapterExemptions owner for PostgresMessageRepository. A real
# message row, a real CLAIM_INBOX through the live API, and the two effects that must BOTH land:
# the wallet moves in the players row, and the message is stamped claimed in player_messages.
#
# It is deliberately a round-trip rather than a construction check: the adapter's whole job is that
# the read the rules judge and the write the claim makes are the same row, and only a live database
# can say so.
#
# Runs ONLY in the compose-boot job, against the stack that job booted. Never a test suite.

. (Join-Path $PSScriptRoot 'probes' '_persistence-probe-common.ps1')

$player  = 'PLAYER_ci-inbox'
$message = 'MSG_ci-inbox-1'
$grant   = 250

Add-SeedPlayer -PlayerId $player

# The row an ops send would write, put in by hand so the probe does not depend on the tool: the
# template is an authored one, the attachment is a wallet currency, and nothing is claimed yet.
Invoke-ProbePsql @"
INSERT INTO player_messages
  (message_id, player_id, category, template_id, params, attachments, created_at_utc, expires_at_utc)
VALUES
  ('$message', '$player', 'COMPENSATION', 'loc.mail.compensation.outage.body',
   '{"dateUtc":"2026-03-14","hours":"3"}'::jsonb,
   '[{"Type":"SOUL_SHARDS","Amount":$grant}]'::jsonb,
   now(), now() + interval '30 days')
ON CONFLICT (message_id) DO NOTHING;
"@ | Out-Null

$before = Invoke-ProbePsql @"
SELECT COALESCE(doc->'Player'->'Wallet'->>'SOUL_SHARDS', '0') FROM players WHERE player_id = '$player';
"@

$envelope = @{
    protocolVersion = 1
    commandId       = 'CMD_ci-inbox-claim-1'
    sequence        = 1
    type            = 'CLAIM_INBOX'
    payload         = @{ messageIds = @($message) }
} | ConvertTo-Json -Compress -Depth 5

$response = Invoke-WebRequest -Uri 'http://127.0.0.1:8080/player/command' -Method Post `
    -Headers @{ Authorization = "Bearer $player" } `
    -ContentType 'application/json; charset=utf-8' `
    -Body $envelope -TimeoutSec 30 -SkipHttpErrorCheck

if ($response.StatusCode -ne 200) {
    throw "POST /player/command answered $($response.StatusCode) for CLAIM_INBOX — the envelope should be an in-protocol 200 conversation."
}

Assert-Accepted -Body $response.Content -Because 'the seeded player claimed a message holding 250 Soul Shards'

# Effect one: the grant is in the player's committed document. Read from the JSONB rather than a
# typed column on purpose — the wallet has no typed column, so this is the only place it exists.
$after = Invoke-ProbePsql @"
SELECT COALESCE(doc->'Player'->'Wallet'->>'SOUL_SHARDS', '0') FROM players WHERE player_id = '$player';
"@

if ([long]$after - [long]$before -ne $grant) {
    throw "claiming a message holding $grant Soul Shards moved the wallet from '$before' to '$after'. The grant did not commit, or it did not go through the wallet seam."
}

# Effect two: the message is stamped, so a second claim pays nothing. Without this the reward is
# owed for ever and every later claim pays it again.
$claimed = Invoke-ProbePsql @"
SELECT CASE WHEN claimed_at_utc IS NULL THEN 'unclaimed' ELSE 'claimed' END
FROM player_messages WHERE message_id = '$message';
"@

if ($claimed -ne 'claimed') {
    throw "the claimed message reads '$claimed' in player_messages. A paid message that is not stamped is a reward the next claim pays a second time."
}

Write-Host 'Inbox claim probe: PASS — the attachment is in the wallet and the message is stamped claimed.'
