# Round-trip probe — the StoreBackedAdapterExemptions owner for PostgresPlayerRepository and
# PostgresRunStateStore. One real command through the live API must land in Postgres: the JSONB
# document rewritten, the typed columns extracted, the counter advanced, the outcome recorded.
#
# Runs ONLY in the compose-boot job, against the stack that job booted. Never a test suite.

. (Join-Path $PSScriptRoot 'probes' '_persistence-probe-common.ps1')

$player = 'PLAYER_ci-roundtrip'

Add-SeedPlayer -PlayerId $player

$body = Send-PlayerCommand -PlayerId $player -CommandId 'CMD_ci-roundtrip-1' -Sequence 1
Assert-Accepted -Body $body -Because 'the seeded player sent its first BEGIN_SESSION'

# The player row: rewritten through the adapter, so the typed columns now mirror the document.
$row = Invoke-ProbePsql @"
SELECT last_meta_sequence::text || '|' || display_name || '|' || (doc->'Player'->>'Id')
FROM players WHERE player_id = '$player';
"@
if ($row -ne "1|CI Probe|$player") {
    throw "the players row after one accepted command should read '1|CI Probe|$player', got '$row'. The save did not commit, or the typed columns diverged from the document."
}

# The idempotency record: the same transaction's second half.
$recorded = Invoke-ProbePsql @"
SELECT count(*) FROM idempotency_records
WHERE scope_kind = 'player' AND player_id = '$player' AND command_id = 'CMD_ci-roundtrip-1';
"@
if ($recorded -ne '1') {
    throw "expected exactly one recorded outcome for CMD_ci-roundtrip-1, found '$recorded'."
}

Write-Host 'Command round-trip probe: PASS — the accepted command is in Postgres, document, columns, counter and record.'
