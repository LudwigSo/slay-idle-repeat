# Shared plumbing for the M5-05 persistence probes. Dot-sourced by the Invoke-*Probe.ps1 scripts.
#
# 🔒 These probes are CI infrastructure, not a test tier: they run in the compose-boot job of
# .github/workflows/ci.yml against the live compose stack, and NOWHERE else. No test project may
# depend on them (build/ci/test-suites.json, $noIntegrationTier). They are the owners the
# StoreBackedAdapterExemptions register in SlayIdleRepeat.Contract.Tests names: each exercises a
# store-backed adapter the unit tier cannot reach, and deleting or unwiring one fails that register.

$ErrorActionPreference = 'Stop'

$script:ApiBaseUrl = 'http://127.0.0.1:8080'

function Get-SeedDocument {
    # The committed seed: a fresh player's StoredSlice exactly as SnapshotCodec writes it.
    # Guarded by SeedPlayerFixtureTests in SlayIdleRepeat.Contract.Tests — a SchemaVersion bump
    # turns that unit test red, and whoever bumps it regenerates this file in the same change.
    $path = Join-Path $PSScriptRoot 'seed-player.json'
    if (-not (Test-Path $path)) {
        throw "seed-player.json is missing beside the probes. It is a committed fixture; see SeedPlayerFixtureTests."
    }
    return (Get-Content $path -Raw).Trim()
}

function Invoke-ProbePsql {
    param([Parameter(Mandatory)][string]$Sql)

    # -T: no pseudo-tty (we want clean stdout). -tA: tuples only, unaligned — bare values.
    $out = docker compose exec -T postgres psql -U sir_app -d slayidlerepeat -v ON_ERROR_STOP=1 -tAc $Sql
    if ($LASTEXITCODE -ne 0) {
        throw "psql failed (exit $LASTEXITCODE) for: $Sql"
    }
    return ($out | Out-String).Trim()
}

function Add-SeedPlayer {
    param([Parameter(Mandatory)][string]$PlayerId)

    # One INSERT per probe player, id rewritten into the document so the row and its own bytes
    # agree. Idempotent (ON CONFLICT DO NOTHING) so a re-run of a probe cannot fail on its own seed.
    # The typed columns are minimal here on purpose: the first accepted command's save rewrites the
    # row through the adapter, which extracts the real values — and the round-trip probe asserts
    # exactly that.
    $doc = Get-SeedDocument
    $encoded = $doc -replace "'", "''"
    Invoke-ProbePsql @"
INSERT INTO players (player_id, doc, display_name, legend_level, last_applied_at_utc, updated_at_utc)
SELECT '$PlayerId',
       jsonb_set('$encoded'::jsonb, '{Player,Id}', to_jsonb('$PlayerId'::text)),
       'CI Probe', 1, now(), now()
ON CONFLICT (player_id) DO NOTHING;
"@ | Out-Null
}

# One access token per probe player, minted once and reused. A JWT is stateless, so a token minted
# before the idempotent-replay probe restarts the API is still the token it presents afterwards.
$script:ProbeAccessTokens = @{}

function Get-ProbeAccessToken {
    param([Parameter(Mandatory)][string]$PlayerId)

    if ($script:ProbeAccessTokens.ContainsKey($PlayerId)) {
        return $script:ProbeAccessTokens[$PlayerId]
    }

    # 🔒 The probes authenticate for real. Naming a player at the door used to be enough, and that
    # was the placeholder resolver M5-06 deleted — refusing a bare player id is the entire point of
    # what replaced it, so a probe that still sent one would be asserting against a 401.
    #
    # The device row is seeded through psql exactly as the player row is, rather than by calling
    # POST /auth/device: that endpoint mints its OWN player id, and these probes assert against a
    # player id they choose and a committed seed document whose bytes are pinned by a unit test.
    # Seeding the credential keeps both, while the token itself still comes from the real
    # POST /auth/session — so the session path, and the auth store under it, are exercised live.
    $deviceId = "DEVICE_ci_probe_$PlayerId"
    $secret = "ci-probe-device-secret-$PlayerId"

    $digest = [System.Security.Cryptography.SHA256]::HashData(
        [System.Text.Encoding]::UTF8.GetBytes($secret))
    $hex = ($digest | ForEach-Object { $_.ToString('x2') }) -join ''

    Invoke-ProbePsql @"
INSERT INTO auth_devices (device_id, player_id, secret_hash, created_at_utc, last_seen_at_utc)
VALUES ('$deviceId', '$PlayerId', decode('$hex', 'hex'), now(), now())
ON CONFLICT (device_id) DO NOTHING;
"@ | Out-Null

    $credentials = @{ deviceId = $deviceId; deviceSecret = $secret } | ConvertTo-Json -Compress

    $response = Invoke-WebRequest -Uri "$script:ApiBaseUrl/auth/session" -Method Post `
        -ContentType 'application/json; charset=utf-8' `
        -Body $credentials -TimeoutSec 30 -SkipHttpErrorCheck

    if ($response.StatusCode -ne 200) {
        throw "POST /auth/session answered $($response.StatusCode) for the seeded probe device — the probe cannot authenticate, so nothing below it is being exercised."
    }

    $token = ($response.Content | ConvertFrom-Json).accessToken

    if ([string]::IsNullOrWhiteSpace($token)) {
        throw "POST /auth/session answered 200 with no accessToken. A probe carrying an empty bearer would fail as a 401 and read as a persistence fault."
    }

    $script:ProbeAccessTokens[$PlayerId] = $token
    return $token
}

function Send-PlayerCommand {
    param(
        [Parameter(Mandatory)][string]$PlayerId,
        [Parameter(Mandatory)][string]$CommandId,
        [Parameter(Mandatory)][long]$Sequence
    )

    $envelope = @{
        protocolVersion = 1
        commandId       = $CommandId
        sequence        = $Sequence
        type            = 'BEGIN_SESSION'
        payload         = @{ clientVersion = 'ci-probe'; contentHash = 'ci-probe' }
    } | ConvertTo-Json -Compress

    # Raw bytes matter: the idempotency guarantee is a BYTE-identical replay, so the probes compare
    # response bodies as strings, never re-parsed objects.
    # -SkipHttpErrorCheck: without it pwsh throws on any non-2xx BEFORE the explicit status check
    # below, replacing its diagnostic with a generic terminating error.
    $response = Invoke-WebRequest -Uri "$script:ApiBaseUrl/player/command" -Method Post `
        -Headers @{ Authorization = "Bearer $(Get-ProbeAccessToken -PlayerId $PlayerId)" } `
        -ContentType 'application/json; charset=utf-8' `
        -Body $envelope -TimeoutSec 30 -SkipHttpErrorCheck

    if ($response.StatusCode -ne 200) {
        throw "POST /player/command answered $($response.StatusCode) — the envelope should be an in-protocol 200 conversation."
    }
    return $response.Content
}

function Assert-Accepted {
    param(
        [Parameter(Mandatory)][string]$Body,
        [Parameter(Mandatory)][string]$Because
    )

    if ($Body -match '"rejected"\s*:\s*true') {
        throw "$Because — but the command was rejected: $Body"
    }
    if ($Body -notmatch '"outcome"') {
        throw "$Because — but the body carries no outcome: $Body"
    }
}

function Wait-ForApiHealthy {
    & (Join-Path $PSScriptRoot '..' 'Wait-ForHttpOk.ps1') `
        -Url "$script:ApiBaseUrl/health" `
        -ExpectedBodyPattern '"status"\s*:\s*"ok"' `
        -TimeoutSeconds 180
}
