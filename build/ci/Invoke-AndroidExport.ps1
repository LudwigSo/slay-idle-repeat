#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Exports the client to an Android debug APK through the custom build template
    (Gradle) and asserts the APK actually contains .NET.

.DESCRIPTION
    14 §14: "build an Android debug APK through the custom export template with the
    MAX plugin included (see 12 §3.2 - a plain export will not produce a working ad
    build)".

    This implements the O23 Android export recipe (M0-05a, executed for
    real on the developer machine). It does not re-research it.

    🔒 THE CENTRAL RULE OF THIS SCRIPT: IT NEVER GATES ON THE EXIT CODE.

    Two independent findings force that, and both produce a green tick over a broken
    artefact:

      * O23 failure 5 - with no .sln next to the project, Godot exports SUCCESSFULLY
        and produces a valid ~80 MB APK containing ZERO managed code. The C# files
        ship as loose source in the assets and no assembly is built. The tell is the
        Gradle output flavour: a .NET build lands in apk/mono/, an engine-only one in
        apk/standard/.
      * M7-10x finding 2 - Godot exits 0 even on a fatal in-game error. An exit code
        says the process ended, not that it did the job.

    So every gate below is stated over the ARTEFACT or over the LOG, never over $LASTEXITCODE.
    A non-zero exit is still reported, but it is one failure among several rather than
    the whole check.

    🔒 IT ALSO NEVER CALLS --build-solutions. O23 failure 1: that flag implies
    --editor, the headless editor ignores --quit-after, and the process never
    terminates. On a runner that means a hang until the job timeout. --export-debug
    builds the project itself.

    ⚠️ THE CI-HOSTILE PART, and the reason this script exists rather than three YAML
    lines. O23 failure 3: Godot reads the Android SDK, the JDK and the debug keystore
    from its own EDITOR SETTINGS and ignores ANDROID_HOME entirely. That configuration
    lives in per-machine state outside the repository, so this script materialises it
    explicitly (-WriteEditorSettings) rather than hoping the machine has it.

.PARAMETER RepositoryRoot
    Defaults to the repository containing this script.

.PARAMETER GodotExecutable
    Full path to the Godot 4.7.1 .NET/mono editor binary. Defaults to $env:GODOT_BIN,
    then to a short list of known locations.

    ⚠️ There is no fallback to a bare "godot" on PATH, on purpose: the mono build and
    the plain build have the same executable name, and the plain one produces an APK
    with no .NET in it - which is O23 failure 5 arriving through a different door.

    🔴 IT MUST NOT BE THE _console.exe WRAPPER, AND O23's RECIPE SAYS TO USE IT.
    MEASURED, M7-10: with Godot_v4.7.1-stable_mono_win64_console.exe the export writes a
    complete, correct APK and then NEVER TERMINATES - the engine child exits, the wrapper
    is left at 0% CPU on a single thread, and it sat there nine minutes before being
    killed. The same export through the plain .exe exits 0 in 59 seconds. On a runner the
    wrapper form is a job that hangs to its timeout AFTER having done the work, which is
    the most expensive way for a correct build to fail. This script rejects the wrapper by
    name and bounds its wait regardless.

.PARAMETER AndroidSdkRoot
    Defaults to $env:ANDROID_HOME, then $env:ANDROID_SDK_ROOT.

.PARAMETER JavaHome
    JDK 17. Defaults to $env:JAVA_HOME. O23: the template's validateJavaVersion task
    fails hard on anything lower, and Java 8 is the machine default.

.PARAMETER DebugKeystore
    Defaults to ~/.android/debug.keystore.

.PARAMETER WriteEditorSettings
    Patch the Godot editor settings file with the SDK, JDK and keystore paths above.
    Required on any machine that has not run the O23 spike by hand.

.PARAMETER SkipInstallBuildTemplate
    Skip --install-android-build-template. The steady-state incremental path once
    android/ exists; the first run on a clean checkout must NOT skip it.

.EXAMPLE
    pwsh build/ci/Invoke-AndroidExport.ps1 -WriteEditorSettings

.NOTES
    Exit codes: 0 pass / 1 fail, matching every other script in build/ci.
#>
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$GodotExecutable,
    [string]$AndroidSdkRoot,
    [string]$JavaHome,
    [string]$DebugKeystore,
    [switch]$WriteEditorSettings,
    [switch]$SkipInstallBuildTemplate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '_common.ps1')

$repoRoot = Get-RepositoryRoot -Override $RepositoryRoot
$projectDirectory = Join-Path $repoRoot 'src' 'SlayIdleRepeat.Client'
$exportRelativePath = 'bin/android/SlayIdleRepeat.apk'
$apkPath = Join-Path $projectDirectory 'bin' 'android' 'SlayIdleRepeat.apk'
$presetName = 'Android'

# The assembly Godot publishes for this project, from project.godot's
# dotnet/project/assembly_name. Named here so the content gate below asserts on OUR
# assembly rather than on "some .dll", which every engine-only APK also has none of.
$assemblyName = 'SlayIdleRepeat.Client'

$failures = [System.Collections.Generic.List[string]]::new()

# ---------------------------------------------------------------------------
# Toolchain
# ---------------------------------------------------------------------------

Write-Section 'Android export (14 §14, 12 §3.2) - toolchain'

function Resolve-GodotExecutable {
    param([string]$Explicit)

    $candidates = @()
    if ($Explicit) { $candidates += $Explicit }
    if ($env:GODOT_BIN) { $candidates += $env:GODOT_BIN }

    # Known locations, developer machine first. Deliberately NOT a PATH lookup - see
    # the -GodotExecutable remarks: a non-mono binary of the same name exports an APK
    # with no .NET in it and reports success doing so.
    #
    # 🔒 The PLAIN .exe, never the _console.exe wrapper beside it. O23's recipe names the
    # wrapper; M7-10 MEASURED that the wrapper does not terminate after a headless export.
    $candidates += @(
        'G:\tools\godot\4.7.1-mono\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64.exe',
        '/usr/local/bin/Godot_v4.7.1-stable_mono_linux.x86_64',
        "$HOME/godot/Godot_v4.7.1-stable_mono_linux_x86_64/Godot_v4.7.1-stable_mono_linux.x86_64"
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return $null
}

$godot = Resolve-GodotExecutable -Explicit $GodotExecutable

if ($godot -and [IO.Path]::GetFileNameWithoutExtension($godot).EndsWith('_console', [StringComparison]::OrdinalIgnoreCase)) {
    # Refused rather than warned about: the wrapper does the work and THEN hangs, so
    # accepting it means every run burns its whole timeout budget after having already
    # produced a correct APK.
    Write-CiError -Message (
        "'$godot' is the _console.exe wrapper. MEASURED in M7-10: it writes a complete APK and then " +
        'never terminates in headless mode - nine minutes at 0% CPU on one thread - while the plain ' +
        '.exe finishes the same export in 59 seconds. Pass the plain executable beside it. O23 recipe ' +
        'names the wrapper; this is a correction to it.')
    exit 1
}

if (-not $godot) {
    Write-CiError -Message (
        'No Godot 4.7.1 .NET/mono editor binary was found. Pass -GodotExecutable or set ' +
        'GODOT_BIN. It must be the MONO build: the plain build exports an APK with no .NET ' +
        'in it and exits 0 doing so.')
    exit 1
}

if (-not $AndroidSdkRoot) { $AndroidSdkRoot = $env:ANDROID_HOME }
if (-not $AndroidSdkRoot) { $AndroidSdkRoot = $env:ANDROID_SDK_ROOT }
if (-not $JavaHome) { $JavaHome = $env:JAVA_HOME }
if (-not $DebugKeystore) { $DebugKeystore = Join-Path $HOME '.android' 'debug.keystore' }

foreach ($required in @(
    @{ Name = 'Android SDK root'; Path = $AndroidSdkRoot; Hint = 'Set ANDROID_HOME or pass -AndroidSdkRoot.' },
    @{ Name = 'JDK 17 home'; Path = $JavaHome; Hint = "Set JAVA_HOME. O23: the template's validateJavaVersion task fails hard below 17." },
    @{ Name = 'Android debug keystore'; Path = $DebugKeystore; Hint = 'Generate it with keytool - see O23 one-time setup step 6.' }
)) {
    if (-not $required.Path -or -not (Test-Path -LiteralPath $required.Path)) {
        $failures.Add("$($required.Name) not found at '$($required.Path)'. $($required.Hint)")
    }
}

if ($failures.Count -gt 0) {
    Exit-WithFailures -Failures $failures -CheckName 'Android export'
}

$buildTools = Join-Path $AndroidSdkRoot 'build-tools'

Write-Host "Repository  : $repoRoot"
Write-Host "Project     : $projectDirectory"
Write-Host "Godot       : $godot"
Write-Host "Android SDK : $AndroidSdkRoot"
Write-Host "JDK 17      : $JavaHome"
Write-Host "Keystore    : $DebugKeystore"

$env:ANDROID_HOME = $AndroidSdkRoot
$env:ANDROID_SDK_ROOT = $AndroidSdkRoot
$env:JAVA_HOME = $JavaHome
$env:PATH = (Join-Path $JavaHome 'bin') + [IO.Path]::PathSeparator + $env:PATH

# ---------------------------------------------------------------------------
# O23 failure 3 - the editor settings, which are where Godot really reads the
# SDK/JDK/keystore from. ANDROID_HOME above is for Gradle, not for Godot.
# ---------------------------------------------------------------------------

function Get-EditorSettingsPath {
    if ($env:APPDATA) { return Join-Path $env:APPDATA 'Godot' 'editor_settings-4.7.tres' }
    return Join-Path $HOME '.config' 'godot' 'editor_settings-4.7.tres'
}

$editorSettingsPath = Get-EditorSettingsPath

if ($WriteEditorSettings) {
    Write-Section 'Materialising Godot editor settings (O23 failure 3)'

    # Forward slashes on every platform: Godot writes and reads these as resource
    # paths, and a backslash in a .tres string is an escape.
    $slash = { param($p) ($p -replace '\\', '/') }

    $wanted = [ordered]@{
        'export/android/android_sdk_path' = (& $slash $AndroidSdkRoot)
        'export/android/java_sdk_path' = (& $slash $JavaHome)
        'export/android/debug_keystore' = (& $slash $DebugKeystore)
        'export/android/debug_keystore_pass' = 'android'
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $editorSettingsPath) | Out-Null

    if (-not (Test-Path -LiteralPath $editorSettingsPath)) {
        # Generated by one headless --import. Done here rather than assumed, because a
        # fresh runner has no such file and patching a missing file silently produces
        # a file Godot ignores.
        Write-Host 'No editor settings file yet - generating one with a headless import.'
        & $godot --headless --path $projectDirectory --import *>&1 | Out-Null
    }

    if (Test-Path -LiteralPath $editorSettingsPath) {
        $lines = [System.Collections.Generic.List[string]](Get-Content -LiteralPath $editorSettingsPath)

        foreach ($key in $wanted.Keys) {
            $value = '{0} = "{1}"' -f $key, $wanted[$key]
            $index = -1

            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i] -match ('^\s*' + [Regex]::Escape($key) + '\s*=')) { $index = $i; break }
            }

            if ($index -ge 0) { $lines[$index] = $value } else { $lines.Add($value) }
        }

        Set-Content -LiteralPath $editorSettingsPath -Value $lines
        Write-Host "Patched $editorSettingsPath"
    }
    else {
        $failures.Add(
            "Godot produced no editor settings at '$editorSettingsPath', so the Android SDK path " +
            'cannot be set and the export will look for the SDK in its own default guess (O23 failure 3).')
    }
}
else {
    Write-Host "Editor settings : $editorSettingsPath (not written - pass -WriteEditorSettings on a fresh machine)"
}

# ---------------------------------------------------------------------------
# Import, then export. Never --build-solutions (O23 failure 1: it hangs forever).
# ---------------------------------------------------------------------------

# ---------------------------------------------------------------------------
# 🔒 Every Godot invocation is BOUNDED. A hang has to become a named failure of this
# check, not a silent consumption of the job's whole timeout: the wrapper finding above
# is one way it happens, O23 failure 1 (--build-solutions implies --editor and ignores
# --quit-after) is another, and Gradle fetching its own wrapper on a cold runner is a
# third that is merely slow. A check that cannot tell "slow" from "wedged" reports the
# same thing for both, hours apart.
# ---------------------------------------------------------------------------

function Invoke-GodotBounded {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$What,
        [int]$TimeoutSeconds = 1800
    )

    $stdout = New-TemporaryFile
    $stderr = New-TemporaryFile

    # 🔒 Quoted here, because Start-Process joins -ArgumentList with spaces and quotes
    # NOTHING. MEASURED: passing the preset name "Windows Desktop" unquoted reaches the engine
    # as two arguments and it reports `Invalid export preset name: Windows` - a preset that was
    # never asked for. "Android" has no space and so hid this; the first preset name with one
    # would have found it as a confusing failure rather than as a quoting bug.
    $quoted = @($Arguments | ForEach-Object {
        if ($_ -match '\s') { '"' + $_ + '"' } else { $_ }
    })

    # Start-Process with redirected files rather than a pipeline, because the output has
    # to survive the process being KILLED - a pipeline that never sees EOF hands back
    # nothing at all, which is exactly the case being diagnosed.
    $startedAt = Get-Date
    $process = Start-Process -FilePath $godot -ArgumentList $quoted `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr `
        -PassThru -NoNewWindow

    $exited = $process.WaitForExit($TimeoutSeconds * 1000)
    $elapsed = [Math]::Round(((Get-Date) - $startedAt).TotalSeconds, 1)

    if (-not $exited) {
        try { $process.Kill($true) } catch { }
    }

    $lines = @()
    foreach ($file in @($stdout, $stderr)) {
        if (Test-Path -LiteralPath $file) {
            $lines += @(Get-Content -LiteralPath $file -ErrorAction SilentlyContinue)
            Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue
        }
    }

    return [pscustomobject]@{
        What = $What
        Exited = $exited
        ExitCode = if ($exited) { $process.ExitCode } else { $null }
        Seconds = $elapsed
        Lines = $lines
    }
}

Write-Section 'Importing assets'

$import = Invoke-GodotBounded `
    -Arguments @('--headless', '--path', $projectDirectory, '--import') `
    -What 'asset import' -TimeoutSeconds 900

Write-Host ($import.Lines | Select-Object -Last 5 | Out-String)
Write-Host "import: exited=$($import.Exited) code=$($import.ExitCode) in $($import.Seconds)s (reported, not gated on)"

if (-not $import.Exited) {
    $failures.Add(
        'The asset import did not terminate within its bound. Nothing downstream is trustworthy, ' +
        'because the export reads what the import wrote.')
}

Write-Section 'Exporting the debug APK through the custom template'

if (Test-Path -LiteralPath $apkPath) {
    # Removed first so the content gates below cannot pass against a previous run's
    # artefact. A stale APK is the one input that makes every assertion here a lie.
    Remove-Item -LiteralPath $apkPath -Force
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $apkPath) | Out-Null

# 🔒 The Gradle flavour tree goes too, for the same reason the APK does and it is the
# subtler of the two: the flavour directories are the evidence that the .NET path ran, and
# Gradle only ADDS to them. A mono/ left by an earlier good run would keep answering "the
# mono flavour was built" for every later run that built nothing of the kind - which is the
# one input that turns O23 failure 5's gate into a rubber stamp.
$gradleApkOutputs = Join-Path $projectDirectory 'android' 'build' 'build' 'outputs' 'apk'

if (Test-Path -LiteralPath $gradleApkOutputs) {
    Remove-Item -LiteralPath $gradleApkOutputs -Recurse -Force
}

$exportArguments = @('--headless', '--path', $projectDirectory)
if (-not $SkipInstallBuildTemplate) { $exportArguments += '--install-android-build-template' }
$exportArguments += @('--export-debug', $presetName, $exportRelativePath)

# The first run on a clean checkout fetches the Gradle wrapper and compiles the whole
# template, which is minutes; the incremental run is ~1. One bound covers both.
$export = Invoke-GodotBounded -Arguments $exportArguments -What 'Android export' -TimeoutSeconds 2700
$exportLog = $export.Lines
$exportExit = $export.ExitCode
$exportText = ($exportLog | Out-String)

Write-Host ($exportLog | Select-Object -Last 30 | Out-String)
Write-Host "export: exited=$($export.Exited) code=$($exportExit) in $($export.Seconds)s"
Write-Host '  (the code is reported, NEVER gated on - O23 failure 5, M7-10x 2)'

# ---------------------------------------------------------------------------
# The gates. Every one of these is about the artefact or the log.
# ---------------------------------------------------------------------------

Write-Section 'Asserting the APK contains .NET (O23 failure 5)'

if (-not (Test-Path -LiteralPath $apkPath)) {
    $failures.Add("No APK was produced at '$apkPath'.")
}
else {
    $apk = Get-Item -LiteralPath $apkPath
    $megabytes = [Math]::Round($apk.Length / 1MB, 2)

    Write-Host "APK: $($apk.FullName) ($megabytes MB)"

    if ($apk.Length -lt 20MB) {
        $failures.Add(
            "The APK is only $megabytes MB. A real export of this project is ~90 MB; anything this " +
            'small is not a packaged game.')
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $zip = $null

    try {
        $zip = [System.IO.Compression.ZipFile]::OpenRead($apk.FullName)
        $entries = @($zip.Entries | ForEach-Object { $_.FullName })
    }
    finally {
        if ($zip) { $zip.Dispose() }
    }

    Write-Host "APK entries: $($entries.Count)"

    # 🔒 The three gates that tell a .NET build from the engine-only APK O23 failure 5
    # produced. Each is checked separately so a failure names WHICH half is missing:
    # the runtime without the assemblies is a different defect from the reverse.
    $monoRuntime = @($entries | Where-Object { $_ -like 'lib/arm64-v8a/libmonosgen-2.0.so' })
    $ownAssembly = @($entries | Where-Object { $_ -like "*$assemblyName.dll" })
    $coreLibrary = @($entries | Where-Object { $_ -like '*System.Private.CoreLib.dll' })

    if ($monoRuntime.Count -eq 0) {
        $failures.Add(
            'The APK contains no lib/arm64-v8a/libmonosgen-2.0.so, so it has no .NET runtime. This is ' +
            'O23 failure 5: a missing or incomplete .sln makes the export exit 0 with a valid APK ' +
            'containing zero managed code.')
    }

    if ($ownAssembly.Count -eq 0) {
        $failures.Add(
            "The APK contains no $assemblyName.dll, so none of this game's own code is in it. The C# " +
            'files may have shipped as loose source in the assets - check for a standard-flavour ' +
            'Gradle output below.')
    }

    if ($coreLibrary.Count -eq 0) {
        $failures.Add(
            'The APK contains no System.Private.CoreLib.dll, so the managed base class library was ' +
            'never published into it.')
    }

    if ($monoRuntime.Count -gt 0 -and $ownAssembly.Count -gt 0 -and $coreLibrary.Count -gt 0) {
        $managed = @($entries | Where-Object { $_ -like '*.dll' }).Count

        Write-Host "  libmonosgen-2.0.so     : present"
        Write-Host "  $assemblyName.dll : $($ownAssembly[0])"
        Write-Host "  managed assemblies     : $managed"
    }

    # The package id, which is what a store and a device identify the build by.
    $aapt2 = $null

    if (Test-Path -LiteralPath $buildTools) {
        $aapt2 = Get-ChildItem -LiteralPath $buildTools -Directory |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName ($IsWindows ? 'aapt2.exe' : 'aapt2') } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
    }

    if ($aapt2) {
        $badging = (& $aapt2 dump badging $apk.FullName 2>&1 | Out-String)
        $expectedPackage = 'de.ludwigso.slayidlerepeat'

        if ($badging -notmatch [Regex]::Escape("name='$expectedPackage'")) {
            $failures.Add(
                "The APK's package id is not '$expectedPackage'. project.godot's " +
                'android/package/unique_name is what it must match.')
        }
        else {
            Write-Host "  package id             : $expectedPackage"
        }
    }
    else {
        Write-CiWarning -Message (
            "No aapt2 under '$buildTools', so the package id was not checked. The .NET content gates " +
            'above still ran.')
    }
}

# ---------------------------------------------------------------------------
# The Gradle flavour, which is the give-away O23 failure 5 named.
# ---------------------------------------------------------------------------

Write-Section 'Asserting the Gradle build took the mono flavour'

$gradleOutputRoot = $gradleApkOutputs

if (Test-Path -LiteralPath $gradleOutputRoot) {
    $flavours = @(Get-ChildItem -LiteralPath $gradleOutputRoot -Directory | ForEach-Object { $_.Name })

    Write-Host "Gradle apk flavours: $($flavours -join ', ')"

    if ($flavours -contains 'standard' -and $flavours -notcontains 'mono') {
        $failures.Add(
            'The Gradle build produced a STANDARD-flavour APK and no mono one. That is exactly O23 ' +
            "failure 5's give-away: Godot fell back to an engine-only build because it could not use " +
            "the solution. Check that $assemblyName.sln sits beside project.godot and declares the " +
            'ExportDebug and ExportRelease configurations - dotnet new sln emits only Debug|Release.')
    }
    elseif ($flavours -notcontains 'mono') {
        $failures.Add(
            "The Gradle build produced no mono-flavour APK (flavours: $($flavours -join ', ')), so " +
            'nothing proves the .NET path ran.')
    }
}
else {
    Write-CiWarning -Message (
        "No Gradle output tree at '$gradleOutputRoot'. On an incremental run with " +
        '-SkipInstallBuildTemplate this can be absent; the APK content gates above are the ones that ' +
        'matter, and they ran.')
}

# ---------------------------------------------------------------------------
# The log. Godot exits 0 on a fatal error (M7-10x 2), so the log is a gate too.
# ---------------------------------------------------------------------------

Write-Section 'Scanning the export log (M7-10x 2 - exit 0 is not success)'

# ⚠️ Two ERROR lines are expected on EVERY headless run, including successful ones,
# and are excluded by name rather than by a blanket "ignore errors":
#
#   * EditorSettings not instantiated yet ... "export/android/shutdown_adb_on_exit"
#     - a Godot shutdown-ordering message, documented in O23's evidence section.
#   * the C#/.NET "still experimental" line - a WARNING wearing the ERROR block's
#     formatting. 🔒 Its ABSENCE is the interesting signal: it means the mono module
#     was never engaged at all.
$benignErrorPatterns = @(
    'EditorSettings not instantiated yet',
    'shutdown_adb_on_exit'
)

$errorLines = @(
    $exportLog |
        Where-Object { $_ -is [string] -and $_ -match '^\s*(ERROR|SCRIPT ERROR|USER ERROR|FATAL)' } |
        Where-Object {
            $line = $_
            -not ($benignErrorPatterns | Where-Object { $line -match [Regex]::Escape($_) })
        }
)

if ($errorLines.Count -gt 0) {
    # 🔒 CAPPED, and the cap is not cosmetic. MEASURED in the O23-failure-5 probe: removing the
    # solution makes Godot log two errors PER C# FILE, and the unbounded version of this loop
    # reported 112 problems - which pushed the four artefact gates (no mono runtime, no assembly,
    # no CoreLib, standard flavour) off the top of the log. Those four are the diagnosis; these are
    # the symptom, repeated once per file. A check that buries its own conclusion under its
    # evidence has failed at the only job it has after going red.
    $shown = 5

    foreach ($line in ($errorLines | Select-Object -First $shown)) {
        $failures.Add("The export log carries an error the exit code did not report: $line")
    }

    if ($errorLines.Count -gt $shown) {
        $failures.Add(
            "... and $($errorLines.Count - $shown) further error line(s) in the export log, not listed. " +
            'The full log is above; the artefact gates are the diagnosis.')
    }
}
else {
    Write-Host 'No unexpected ERROR lines in the export log.'
}

# 🔒 MEASURED, M7-10: 4.7.1 does NOT print the "C#/.NET is still experimental" notice on this
# path, even though the export is a genuine .NET build - 186 managed assemblies and the mono
# flavour. O23 recorded the notice, and the iOS job's comments propose its presence as a gate;
# on Android that gate would fail every correct build. So it is kept only as a DIAGNOSTIC, shown
# when something else has already gone wrong, and never as a standing warning: a warning that
# fires on every green run is how a team learns to stop reading warnings.
if ($failures.Count -gt 0 -and
    $exportText -notmatch 'C#/\.NET is still experimental' -and
    $exportText -notmatch 'C#/\.NET is experimental') {
    Write-Host (
        'Diagnostic: the engine''s "C#/.NET is experimental" notice is absent from the log. On its own ' +
        'this means nothing (4.7.1 does not print it on the Android path), but combined with the ' +
        'failures above it is consistent with the mono module never having been engaged.')
}

if (-not $export.Exited) {
    # 🔴 Deliberately NOT fatal on its own when every artefact gate above passed. The export
    # having produced a correct, complete APK and then failed to exit is a real defect and is
    # named as one, but it is a defect in the PROCESS, not in the artefact - and reporting it
    # as "the APK is bad" would send the next reader to look at the wrong thing.
    $message = (
        "Godot did not terminate within its bound (killed after $($export.Seconds)s). This is the " +
        'wrapper finding in the -GodotExecutable notes: check that the plain executable is being used ' +
        'and not the _console.exe beside it.')

    if ($failures.Count -eq 0) {
        Write-CiWarning -Message ($message + ' Every artefact gate passed, so the APK itself is good.')
    }
    else {
        $failures.Add($message)
    }
}
elseif ($exportExit -ne 0) {
    $failures.Add(
        "Godot exited $exportExit. Reported as one failure among the artefact gates rather than as the " +
        'whole check, because the reverse - exit 0 with a broken artefact - is the failure mode this ' +
        'script exists for.')
}

# ---------------------------------------------------------------------------
# Where the MSBuild log went. O23 failure 6: it is NOT on stdout in headless mode,
# so a C# build failure otherwise surfaces only as "Check MSBuild panel for details".
# ---------------------------------------------------------------------------

$buildLogRoot = if ($env:APPDATA) {
    Join-Path $env:APPDATA 'Godot' 'mono' 'build_logs'
}
else {
    Join-Path $HOME '.local' 'share' 'godot' 'mono' 'build_logs'
}

Write-Section 'MSBuild logs (O23 failure 6 - not on stdout)'
Write-Host "Log root: $buildLogRoot"

if (Test-Path -LiteralPath $buildLogRoot) {
    Write-Host 'CI must upload this directory as an artefact; without it a C# build failure reads only'
    Write-Host 'as "Failed to build project. Check MSBuild panel for details."'

    if (Test-RunningInGitHubActions -and $env:GITHUB_OUTPUT) {
        "msbuild_log_root=$buildLogRoot" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    }
}
else {
    Write-CiWarning -Message "No MSBuild log directory at '$buildLogRoot'."
}

Exit-WithFailures -Failures $failures -CheckName 'Android export'
