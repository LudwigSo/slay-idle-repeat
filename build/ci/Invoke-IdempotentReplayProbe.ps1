# Idempotent-replay probe — the StoreBackedAdapterExemptions owner for PostgresIdempotencyStore.
# A duplicate (commandId, sequence, payload) must replay the STORED response byte-identically —
# across an API restart, so only the durable record can be answering (14 §16.3).
#
# Runs ONLY in the compose-boot job, against the stack that job booted. Never a test suite.

. (Join-Path $PSScriptRoot 'probes' '_persistence-probe-common.ps1')

$player = 'PLAYER_ci-replay'

Add-SeedPlayer -PlayerId $player

$first = Send-PlayerCommand -PlayerId $player -CommandId 'CMD_ci-replay-1' -Sequence 1
Assert-Accepted -Body $first -Because 'the seeded player sent its first BEGIN_SESSION'

# Restart the API: every in-process cache and placeholder dies with it. What answers the duplicate
# afterwards is the Postgres record or nothing.
docker compose restart api | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'docker compose restart api failed.' }
Wait-ForApiHealthy

$replayed = Send-PlayerCommand -PlayerId $player -CommandId 'CMD_ci-replay-1' -Sequence 1

if ($replayed -cne $first) {
    throw "the replayed duplicate differs from the first response.`nFirst:    $first`nReplayed: $replayed"
}

Write-Host 'Idempotent replay probe: PASS — the duplicate replayed the stored bytes across a restart.'
