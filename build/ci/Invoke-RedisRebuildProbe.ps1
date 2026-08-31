# Redis-rebuild probe — the StoreBackedAdapterExemptions owner for RedisRunStateCache and
# RedisIdempotencyCache. Flushing Redis mid-session must cost latency, never progress (14 §7.1):
# the next command still sequences and commits, and a flushed record still replays from Postgres.
#
# Runs ONLY in the compose-boot job, against the stack that job booted. Never a test suite.

. (Join-Path $PSScriptRoot 'probes' '_persistence-probe-common.ps1')

$player = 'PLAYER_ci-redis'

Add-SeedPlayer -PlayerId $player

$first = Send-PlayerCommand -PlayerId $player -CommandId 'CMD_ci-redis-1' -Sequence 1
Assert-Accepted -Body $first -Because 'the seeded player sent its first BEGIN_SESSION'

# The flush: everything cached is gone; the authority must not notice.
docker compose exec -T redis redis-cli FLUSHALL | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'redis-cli FLUSHALL failed.' }

$second = Send-PlayerCommand -PlayerId $player -CommandId 'CMD_ci-redis-2' -Sequence 2
Assert-Accepted -Body $second -Because 'the command straight after a full Redis flush'

$replayed = Send-PlayerCommand -PlayerId $player -CommandId 'CMD_ci-redis-1' -Sequence 1
if ($replayed -cne $first) {
    throw "the flushed record's replay differs from the first response — the fallback did not reach Postgres.`nFirst:    $first`nReplayed: $replayed"
}

Write-Host 'Redis rebuild probe: PASS — a full flush cost nothing but the round-trip to Postgres.'
