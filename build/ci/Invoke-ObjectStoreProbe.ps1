# Object-store probe — the StoreBackedAdapterExemptions owner for S3BattleLogStore.
#
# ⚠️ The register's weakest entry, by its own admission: no production path writes a battle log yet
# (the battle milestone owns the first producer), so this probe asserts the adapter was CONSTRUCTED
# against the live store and its drain is RUNNING — the wiring, not a put/get. The task that lands
# the first battle-log producer owes this probe the real round-trip; the marker line below is the
# API composition's own startup log.
#
# Runs ONLY in the compose-boot job, against the stack that job booted. Never a test suite.

$ErrorActionPreference = 'Stop'

# The store itself is alive (the compose-boot job also asserts this earlier; repeated here so this
# probe stays meaningful if the steps are ever reordered).
& (Join-Path $PSScriptRoot 'Wait-ForHttpOk.ps1') -Url 'http://127.0.0.1:9000/minio/health/live' -TimeoutSeconds 120

# The API's persistence composition logs exactly one marker line when the S3-backed battle-log
# store is created and its queued drain started. No marker means the adapter was never constructed
# — the ObjectStore__* variables stopped binding, and nothing else would notice until a battle.
$deadline = (Get-Date).AddSeconds(60)
do {
    $logs = docker compose logs --no-color api 2>&1 | Out-String
    if ($logs -match 'Battle-log store ready: queued drain running') { break }
    if ((Get-Date) -ge $deadline) {
        Write-Host $logs
        throw "the API never logged 'Battle-log store ready: queued drain running' — the object-store adapter was not constructed against the live store."
    }
    Start-Sleep -Seconds 3
} while ($true)

Write-Host 'Object-store probe: PASS — the battle-log adapter is constructed and its drain is running against the live store.'
