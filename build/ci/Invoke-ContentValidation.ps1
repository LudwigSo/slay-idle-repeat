#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Validates every JSON file under SlayIdleRepeat.Data.

.DESCRIPTION
    14 §6 🔒: "JSON is validated at build time against schemas in
    SlayIdleRepeat.Data/schema/. The build fails on unknown IDs, missing icons,
    out-of-range values, orphaned references or duplicate IDs."
    14 §13 names 'Content: build-time schema validation of all JSON' as a testing
    requirement CI must run.

    ⚠️ THIS IS THE FLOOR, NOT THE CEILING. The real validator - JSON Schema
    enforcement, cross-file reference resolution, the 📐-marker-vs-schema-key
    check - is M0-09 (content pipeline base). What this script does today:

      C1  Every *.json under the data root parses as strict RFC 8259 JSON.
      C2  No object in any file has a duplicate property name. System.Text.Json
          silently keeps the last one, so a duplicated key is a value that
          vanishes with no error anywhere - the cheapest possible version of
          14 §6's 'duplicate IDs' rule.
      C3  Schema <-> data pairing has no orphans in either direction: no data
          file without a schema, no schema governing nothing.

    M0-09 REPLACES THE BODY, NOT THE INTERFACE. Keep the parameters, the exit
    codes (0 pass / 1 fail) and the script path, and .github/workflows/ci.yml
    needs no edit when the real harness lands.

.PARAMETER DataRoot
    Defaults to <repo>/SlayIdleRepeat.Data.

.EXAMPLE
    pwsh build/ci/Invoke-ContentValidation.ps1
#>
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$DataRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

$root = Get-RepositoryRoot -Override $RepositoryRoot
if (-not $DataRoot) { $DataRoot = Join-Path $root 'SlayIdleRepeat.Data' }

Write-Section 'Content validation (14 §6, §13)'
Write-Host "Data root : $DataRoot"

$failures = [System.Collections.Generic.List[string]]::new()

if (-not (Test-Path -LiteralPath $DataRoot)) {
    Write-CiError "Data root '$DataRoot' does not exist."
    exit 1
}

$dataRootResolved = (Resolve-Path -LiteralPath $DataRoot).Path
$schemaDir = Join-Path $dataRootResolved 'schema'

$jsonFiles = @(Get-ChildItem -Path $dataRootResolved -Filter '*.json' -Recurse -File | Sort-Object FullName)

# ------------------------------------------------------------------------ C0
# A validator that finds nothing to validate must not report success. Today this
# is the expected state on a branch cut before M0-10 (SlayIdleRepeat.Data initial
# layout) merges; it turns green the moment the data tree lands.
if ($jsonFiles.Count -eq 0) {
    Write-CiError (
        "No JSON files found under '$dataRootResolved'. There is nothing to validate, so this " +
        "check cannot honestly pass. The data tree - schema/, tuning/ (the 16 files of 21 §3.1), " +
        "loc/, content/ - lands with M0-10.")
    exit 1
}

Write-Host "JSON files: $($jsonFiles.Count)"

# --------------------------------------------------------------------- C1/C2
function Test-DuplicateKeys {
    <#
        JsonDocument keeps every duplicate property, so enumerating and grouping
        finds what the object model would silently collapse.
    #>
    param(
        [Parameter(Mandatory)][System.Text.Json.JsonElement]$Element,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Pointer,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Found
    )

    switch ($Element.ValueKind) {
        ([System.Text.Json.JsonValueKind]::Object) {
            $names = [System.Collections.Generic.List[string]]::new()
            foreach ($property in $Element.EnumerateObject()) {
                $names.Add($property.Name)
                Test-DuplicateKeys -Element $property.Value -Pointer "$Pointer/$($property.Name)" -Found $Found
            }
            $where = if ($Pointer) { $Pointer } else { '(document root)' }
            foreach ($group in ($names | Group-Object | Where-Object { $_.Count -gt 1 })) {
                $Found.Add("$where -> property '$($group.Name)' appears $($group.Count) times")
            }
        }
        ([System.Text.Json.JsonValueKind]::Array) {
            $index = 0
            foreach ($item in $Element.EnumerateArray()) {
                Test-DuplicateKeys -Element $item -Pointer "$Pointer/$index" -Found $Found
                $index++
            }
        }
    }
}

$parsed = @{}

foreach ($file in $jsonFiles) {
    $relative = Get-RelativePath -Root $root -Path $file.FullName
    $text = Get-Content -Raw -LiteralPath $file.FullName

    if ([string]::IsNullOrWhiteSpace($text)) {
        $failures.Add("C1 $relative : file is empty. An empty content file is never intentional.")
        continue
    }

    try {
        # Strict parse: no comments, no trailing commas - exactly what the runtime
        # loader will accept, so CI cannot be more forgiving than production.
        $document = [System.Text.Json.JsonDocument]::Parse($text)
    } catch {
        $failures.Add("C1 $relative : does not parse as JSON - $($_.Exception.Message)")
        continue
    }

    try {
        $duplicates = [System.Collections.Generic.List[string]]::new()
        Test-DuplicateKeys -Element $document.RootElement -Pointer '' -Found $duplicates
        foreach ($duplicate in $duplicates) {
            $failures.Add("C2 $relative : duplicate property name at $duplicate. One of the values is silently discarded on load (14 §6 forbids duplicate IDs).")
        }
        $parsed[$relative] = $true
    } finally {
        $document.Dispose()
    }
}

# ------------------------------------------------------------------------ C3
# Pairing. The convention below is the fallback; SlayIdleRepeat.Data/schema/schema-map.json
# overrides it when M0-09/M0-10 need something the convention cannot express.
#
#   schema/<stem>.schema.json  governs  tuning/<stem>.json and/or content/<stem>.json
#   schema/loc.schema.json     governs  loc/*.json          (all locales share one schema)
#
# schema-map.json shape:
#   { "map": { "<data path or glob>": "<schema path>" , ... } }
$schemaMapPath = Join-Path $schemaDir 'schema-map.json'
$schemaMap = $null
if (Test-Path -LiteralPath $schemaMapPath) {
    Write-Host "Pairing   : schema-map.json (explicit)"
    $schemaMap = (Get-Content -Raw -LiteralPath $schemaMapPath | ConvertFrom-Json).map
} else {
    Write-Host "Pairing   : convention (no schema/schema-map.json present)"
}

$schemaFiles = @($jsonFiles | Where-Object { (Get-RelativePath -Root $dataRootResolved -Path $_.FullName) -like 'schema/*' })
$dataFiles = @($jsonFiles | Where-Object { (Get-RelativePath -Root $dataRootResolved -Path $_.FullName) -notlike 'schema/*' })

if ($schemaFiles.Count -eq 0) {
    $failures.Add("C3 SlayIdleRepeat.Data/schema/ holds no schema files, so no data file can be validated against anything. 14 §6 requires schemas to live there.")
}

$schemaUsage = @{}
foreach ($schemaFile in $schemaFiles) {
    $schemaRelative = Get-RelativePath -Root $dataRootResolved -Path $schemaFile.FullName
    if ($schemaRelative -eq 'schema/schema-map.json') { continue }
    $schemaUsage[$schemaRelative] = 0
}

function Resolve-SchemaFor {
    param([Parameter(Mandatory)][string]$DataRelativePath)

    if ($schemaMap) {
        foreach ($property in $schemaMap.PSObject.Properties) {
            if ($DataRelativePath -like $property.Name) { return $property.Value }
        }
        return $null
    }

    $directory = [IO.Path]::GetDirectoryName($DataRelativePath) -replace '\\', '/'
    $stem = [IO.Path]::GetFileNameWithoutExtension($DataRelativePath)

    if ($directory -eq 'loc') { return 'schema/loc.schema.json' }
    return "schema/$stem.schema.json"
}

foreach ($dataFile in $dataFiles) {
    $dataRelative = Get-RelativePath -Root $dataRootResolved -Path $dataFile.FullName
    $expected = Resolve-SchemaFor -DataRelativePath $dataRelative

    if (-not $expected) {
        $failures.Add("C3 ORPHAN DATA: '$dataRelative' matches no entry in schema/schema-map.json. Every content file is governed by a schema (14 §6).")
        continue
    }

    if ($schemaUsage.ContainsKey($expected)) {
        $schemaUsage[$expected] = $schemaUsage[$expected] + 1
    } else {
        $failures.Add("C3 ORPHAN DATA: '$dataRelative' expects schema '$expected', which does not exist. Either add the schema or record the real pairing in SlayIdleRepeat.Data/schema/schema-map.json.")
    }
}

foreach ($schemaRelative in $schemaUsage.Keys) {
    if ($schemaUsage[$schemaRelative] -eq 0) {
        $failures.Add("C3 ORPHAN SCHEMA: '$schemaRelative' governs no data file. Either the data it describes is missing, or the schema outlived its content and should be deleted.")
    }
}

Write-Section 'Pairing'
$pairingReport = foreach ($schemaRelative in ($schemaUsage.Keys | Sort-Object)) {
    [pscustomobject]@{ Schema = $schemaRelative; DataFiles = $schemaUsage[$schemaRelative] }
}
if ($pairingReport) { $pairingReport | Format-Table -AutoSize | Out-String -Width 200 | Write-Host }

Write-Host "Parsed OK : $($parsed.Count) of $($jsonFiles.Count)"
Write-CiWarning "Structural validation only. JSON Schema enforcement, cross-file reference resolution and the 📐-marker check arrive with M0-09."

Exit-WithFailures -Failures $failures.ToArray() -CheckName 'Content validation'
