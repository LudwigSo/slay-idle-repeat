# O23 spike — Godot 4 C#/.NET Android export through the custom build template

- **Task:** M0-05a (milestone M0)
- **Open question:** `game-design/16_DECISION_LOG.md` O23 — *"Godot 4.x C# (.NET) mobile export maturity"*
- **Scope:** Android leg only, executed for real on the developer machine. The iOS leg is **M0-05b** and is untouched here.
- **Date executed:** 2026-08-11
- **Spike project:** `spikes/godot-android-export/`

---

## Verdict

**Yes, with caveats — it works.** A Godot 4.7.1 C#/.NET project exports to a working Android debug APK through the **custom build template (Gradle) path**, headlessly, from the command line, with the correct package id. The produced APK contains the Mono runtime (`libmonosgen-2.0.so`), the project's own managed assembly, and 170 managed assemblies including `System.Private.CoreLib.dll` — so this is genuinely a .NET build, not an engine-only export. **No fallback is needed: the project does not have to pin an older engine, does not need a GDScript UI shell, and does not need to wait for a point release.** D1 stands on the Android side.

The caveats are real but all are process caveats, not blockers:

1. Godot itself still labels this configuration **"Exporting to Android when using C#/.NET is still experimental."** It is a *warning*, not a blocking error — the export proceeds — but the engine is telling us this path has less test coverage than GDScript. It should be treated as a supported-but-watch-it path, and the M7 CI job is the thing that keeps us honest.
2. **The .NET path silently raises `minSdkVersion` from 24 to 29** (Android 10). The Gradle template defaults to `minSdk 24`, but the produced APK reports 29. This is a **product decision that has now been made by a toolchain default** and should be confirmed against the target device coverage in the design docs.
3. Three of the failures below are *silent-wrong-output* failures rather than hard errors — most importantly, a missing `.sln` produces a perfectly valid-looking APK that simply has **no .NET in it**. CI must assert on APK *contents*, not on exit code.

**This verdict does not close O23.** O23 covers "mobile export maturity — *especially iOS*". Only Android is answered. See *Residual risk*.

---

## The version pin

This is a milestone output. The solution-wide `net8.0` target is **confirmed correct** for this engine version: `Godot.NET.Sdk 4.7.1` ships `GodotSharp` as `lib/net8.0`, and the Android publish compiled with `/define:...NET8_0...` against `Microsoft.NETCore.App.Ref 8.0.22`.

| Component | Pinned version | Notes |
|---|---|---|
| **Godot** | **4.7.1-stable, .NET/Mono build** (`4.7.1.stable.mono.official.a13da4feb`) | Newest 4.x stable at time of spike. **This is the recommended pin.** |
| Godot export templates | `4.7.1.stable.mono` (`Godot_v4.7.1-stable_mono_export_templates.tpz`) | Must be the **mono** templates; the non-mono ones will not produce a .NET build. |
| **JDK** | **Temurin 17.0.20+8** | Godot's `config.gradle` sets `javaVersion = JavaVersion.VERSION_17` and the build has a `validateJavaVersion` task that fails hard on anything lower. Java 8 (the machine default) is not usable. |
| .NET SDK | **8.0.319** | Pinned via `spikes/godot-android-export/global.json`. See failure 4. |
| Android cmdline-tools | 22.0 (`commandlinetools-win-15859902`) | |
| Android platform | `android-36` | `compileSdk 36`, `targetSdk 36` |
| Android build-tools | **36.1.0** | Exact version named by the template's `config.gradle`. |
| Android platform-tools | 37.0.1 | Only needed for `adb`/device work, not for the export itself. |
| Android NDK | **not required** | `config.gradle` declares `ndkVersion 29.0.14206865`, but the export was **re-verified with the NDK directory renamed away and still succeeded**. Do not install it in CI — it saves ~2 GB. |
| Gradle | 8.11.1 | Fetched automatically by the wrapper inside the build template. Not installed manually. |
| Android Gradle Plugin | 8.6.1 | Fixed by the template. |
| Kotlin | 2.1.21 | Fixed by the template. |

All version numbers above were read out of the export template's own
`android_source.zip → config.gradle` rather than guessed from documentation.

### Produced artefact

| | |
|---|---|
| Path | `spikes/godot-android-export/bin/SlayIdleRepeatSpike.apk` |
| Size | 95.72 MB (arm64-v8a only, debug) |
| Package id | `de.ludwigso.slayidlerepeat` |
| App label | `Slay. Idle. Repeat.` |
| minSdk / targetSdk | 29 / 36 |
| Signing | v2 scheme, `CN=Android Debug, O=Android, C=US` |
| Gradle output flavor | `apk/**mono**/debug/android_monoDebug.apk` |

---

## The exact command sequence

Copy-pasteable. This is the shape the M7 CI job needs. PowerShell; the paths are
this machine's, the structure is what matters.

### One-time toolchain setup

```powershell
# 1. JDK 17
winget install --id EclipseAdoptium.Temurin.17.JDK --exact --silent `
  --accept-package-agreements --accept-source-agreements

# 2. Android command-line tools -> $ANDROID_HOME/cmdline-tools/latest
Invoke-WebRequest `
  -Uri "https://dl.google.com/android/repository/commandlinetools-win-15859902_latest.zip" `
  -OutFile "$env:TEMP\cmdline-tools.zip"
Expand-Archive "$env:TEMP\cmdline-tools.zip" -DestinationPath "G:\tools\android-sdk\cmdline-tools\_tmp"
Move-Item "G:\tools\android-sdk\cmdline-tools\_tmp\cmdline-tools" `
          "G:\tools\android-sdk\cmdline-tools\latest"

# 3. Environment
$env:JAVA_HOME        = "C:\Program Files\Eclipse Adoptium\jdk-17.0.20.8-hotspot"
$env:ANDROID_HOME     = "G:\tools\android-sdk"
$env:ANDROID_SDK_ROOT = "G:\tools\android-sdk"
$env:GRADLE_USER_HOME = "G:\tools\gradle-home"     # cacheable in CI
$env:PATH = "$env:JAVA_HOME\bin;$env:PATH"

# 4. Licences + SDK packages (NDK deliberately omitted -- not required)
$sdkm = "$env:ANDROID_HOME\cmdline-tools\latest\bin\sdkmanager.bat"
("y`n" * 200) | & $sdkm --sdk_root="$env:ANDROID_HOME" --licenses
& $sdkm --sdk_root="$env:ANDROID_HOME" "platform-tools" "platforms;android-36" "build-tools;36.1.0"

# 5. Godot 4.7.1 mono editor + MONO export templates
#    Editor:    Godot_v4.7.1-stable_mono_win64.zip     -> G:\tools\godot\4.7.1-mono\
#    Templates: Godot_v4.7.1-stable_mono_export_templates.tpz
#    Unpack the .tpz and copy the CONTENTS of its templates/ folder into:
#      %APPDATA%\Godot\export_templates\4.7.1.stable.mono\
#    (Linux CI: ~/.local/share/godot/export_templates/4.7.1.stable.mono/)

# 6. Android debug keystore
keytool -keyalg RSA -genkeypair -alias androiddebugkey -keypass android `
  -keystore "$env:USERPROFILE\.android\debug.keystore" -storepass android `
  -dname "CN=Android Debug,O=Android,C=US" -validity 9999 -deststoretype pkcs12
```

### Godot editor settings — required, and not in the repo

Godot reads the Android SDK / JDK / keystore locations from **editor settings**,
not from `ANDROID_HOME`. See failure 3. Patch
`%APPDATA%\Godot\editor_settings-4.7.tres` so it contains:

```
export/android/android_sdk_path = "G:/tools/android-sdk"
export/android/java_sdk_path = "C:/Program Files/Eclipse Adoptium/jdk-17.0.20.8-hotspot"
export/android/debug_keystore = "C:/Users/Ludwig/.android/debug.keystore"
export/android/debug_keystore_pass = "android"
```

The file is generated by running the editor once (`--headless --path <proj> --import`).

### The build

```powershell
$GODOT = "G:\tools\godot\4.7.1-mono\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe"
$PROJ  = "G:\Git\slay-idle-repeat\spikes\godot-android-export"

# a. Import assets (also generates editor settings on a fresh machine)
& $GODOT --headless --path $PROJ --import

# b. Install the Android build template AND export, in one invocation.
New-Item -ItemType Directory -Force -Path "$PROJ\bin"
& $GODOT --headless --path $PROJ `
    --install-android-build-template `
    --export-debug "Android" "bin/SlayIdleRepeatSpike.apk"
```

Once `android/` exists, the steady-state (incremental) CI command is just:

```powershell
& $GODOT --headless --path $PROJ --export-debug "Android" "bin/SlayIdleRepeatSpike.apk"
```

### The verification CI must run (do not skip — see failure 5)

```powershell
$BT = "$env:ANDROID_HOME\build-tools\36.1.0"

# Package id must match
& "$BT\aapt2.exe" dump badging "$PROJ\bin\SlayIdleRepeatSpike.apk" | Select-Object -First 1

# The APK must actually contain .NET. This is the check that catches the
# "valid APK with no .NET in it" failure mode.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$z = [System.IO.Compression.ZipFile]::OpenRead("$PROJ\bin\SlayIdleRepeatSpike.apk")
$hasMono = $z.Entries | Where-Object { $_.FullName -eq 'lib/arm64-v8a/libmonosgen-2.0.so' }
$hasAsm  = $z.Entries | Where-Object { $_.FullName -like '*publish/arm64/GodotAndroidSpike.dll' }
$z.Dispose()
if (-not $hasMono -or -not $hasAsm) { throw "APK contains no .NET runtime/assembly" }
```

---

## Every failure encountered, in order

### Failure 1 — `--build-solutions` hangs forever in headless mode

`godot --headless --path <proj> --build-solutions --quit-after 3` never
terminates and produces no output at all. `--build-solutions` implies
`--editor`, and the headless editor ignores `--quit-after`. Had to kill the
process.

**Fix:** don't use it. Either let `--export-debug` build the project itself
(it does), or build explicitly with `dotnet build`. **CI must never call
`--build-solutions`** — it will hang the runner until the job timeout.

### Failure 2 — export refused: ETC2/ASTC texture compression required

```
ERROR: Cannot export project with preset "Android" due to configuration errors:
Exporting to Android when using C#/.NET is still experimental.
ETC2/ASTC texture compression is required for Android export. In Project
Settings, search for 'ETC2' ...
```

**Fix:** add to `project.godot`:

```ini
[rendering]
textures/vram_compression/import_etc2_astc=true
```

Note that the C#/.NET "experimental" line is printed in the same block but is a
**warning**, not the blocker — once ETC2 was enabled the export proceeded.

### Failure 3 — Godot ignores `ANDROID_HOME`; SDK path comes from editor settings

Even with `ANDROID_HOME` and `ANDROID_SDK_ROOT` correctly exported, Godot logged:

```
Unable to open Android 'build-tools' directory.
```

It was using its own default guess,
`export/android/android_sdk_path = "C:\Users\Ludwig\AppData\Local/Android/Sdk"`,
which does not exist on this machine.

**Fix:** set `export/android/android_sdk_path`, `export/android/java_sdk_path`
and `export/android/debug_keystore` in `%APPDATA%\Godot\editor_settings-4.7.tres`.
**This is the single most CI-hostile finding in the spike:** the configuration
lives in per-machine editor state outside the repo, so the CI job must
materialise that file as an explicit step.

### Failure 4 — the newest installed .NET SDK was a preview

The machine's default SDK resolved to `10.0.400-preview.0.26322.102`, producing
`NETSDK1057: You are using a preview version of .NET`. It happened to build, but
shipping a mobile artefact off a preview SDK is not acceptable.

**Fix:** `spikes/godot-android-export/global.json` pins `8.0.319` with
`rollForward: latestFeature`. Scoped to the spike directory; it does not affect
`SlayIdleRepeat.sln`.

### Failure 5 — no `.sln` ⇒ a valid APK with **no .NET in it** (the dangerous one)

With only a `.csproj` present, the export **exited successfully and produced an
80 MB APK**, while logging:

```
ERROR: Export .NET Project: This project contains C# files but no solution file
was found at the following path:
  G:\...\spikes\godot-android-export\GodotAndroidSpike.sln
ERROR: System.InvalidOperationException: res://Main.cs is a C# file but no solution file exists.
WARNING: Project export for preset "Android" completed with warnings.
```

The give-away was the Gradle output path: `apk/**standard**/debug/android_debug.apk`.
The build template has `standard` and `mono` product flavors, and without a
solution Godot fell back to `standard` — an engine-only build with the C# files
shipped as **loose source in the APK assets** and no managed assemblies.

**Fix:** a solution file must exist next to the project, named after the
assembly (`GodotAndroidSpike.sln`). Additionally, `dotnet new sln` is **not
sufficient** — it emits only `Debug|Release`, whereas Godot builds the
`ExportDebug` / `ExportRelease` configurations. The committed `.sln` declares all
three. A successful .NET export is identified by the
`apk/**mono**/debug/android_monoDebug.apk` output path.

### Failure 6 — `android/` lives inside the project and poisons the C# compile

After adding the solution, the export failed with:

```
error CS0101: The namespace "SlayIdleRepeat.Spike" already contains a definition for "SpikeMath".
error CS0111: The type "Main" already defines a member named "_Ready" ...
```

with duplicate file paths:

```
android\build\src\main\assets\SpikeMath.cs
android\build\build\intermediates\assets\standardDebug\mergeStandardDebugAssets\Main.cs
SpikeMath.cs
```

"Use Gradle Build" installs the build template into `res://android/`, i.e.
**inside** the Godot project. The failed run from failure 5 had shipped the
`.cs` files into `android/build/src/main/assets/`, and the SDK's default
`**/*.cs` glob then compiled every copy alongside the originals.

**Fix**, in the `.csproj` — and **every real Godot C# project on the Gradle path
needs this**:

```xml
<ItemGroup>
  <Compile Remove="android/**" />
  <Compile Remove="bin/**" />
</ItemGroup>
```

Also delete the poisoned `android/` tree once (`--install-android-build-template`
regenerates it). Note the MSBuild log is **not** printed to stdout in headless
mode; it is written to
`%APPDATA%\Godot\mono\build_logs\<hash>_ExportDebug\msbuild_log.txt` and
`msbuild_issues.csv`. **CI must upload that directory as an artefact**, otherwise
a C# build failure surfaces only as the useless line
`Failed to build project. Check MSBuild panel for details.`

### Failure 7 — cosmetic: no project icon

`ERROR: No project icon specified.` Fixed by adding `icon.svg` and
`config/icon="res://icon.svg"`.

---

## Evidence

### The successful export produced a `mono`-flavor APK

```
=== APK ===
Name                       MB
----                       --
SlayIdleRepeatSpike.apk 95,72

=== gradle apk outputs ===
FullName                                                                                                                MB
--------                                                                                                                --
G:\Git\slay-idle-repeat\spikes\godot-android-export\android\build\build\outputs\apk\mono\debug\android_monoDebug.apk 95,72

=== errors ===
ERROR: EditorSettings not instantiated yet when getting setting "export/android/shutdown_adb_on_exit".
```

(The one remaining `ERROR` is a harmless Godot shutdown-ordering message that is
printed on every headless run, including successful ones.)

### Package id check

```
$ aapt2 dump badging bin/SlayIdleRepeatSpike.apk
package: name='de.ludwigso.slayidlerepeat' versionCode='1' versionName='0.1.0' platformBuildVersionName='16' platformBuildVersionCode='36' compileSdkVersion='36' compileSdkVersionCodename='16'
install-location:'auto'
minSdkVersion:'29'
targetSdkVersion:'36'
application-label:'Slay. Idle. Repeat.'
```

```
$ apkanalyzer manifest print bin/SlayIdleRepeatSpike.apk
<?xml version="1.0" encoding="utf-8"?>
<manifest
    xmlns:android="http://schemas.android.com/apk/res/android"
    android:versionCode="1"
    android:versionName="0.1.0"
    android:installLocation="0"
    android:compileSdkVersion="36"
    android:compileSdkVersionCodename="16"
    package="de.ludwigso.slayidlerepeat"
    platformBuildVersionCode="36"
    platformBuildVersionName="16">
```

### The APK really contains .NET

```
total entries: 309

=== native libs (arm64) ===
lib/arm64-v8a/libc++_shared.so                                   1,31 MB
lib/arm64-v8a/libgodot_android.so                               72,88 MB
lib/arm64-v8a/libmono-component-debugger.so                      0,19 MB
lib/arm64-v8a/libmono-component-diagnostics_tracing.so           0,25 MB
lib/arm64-v8a/libmono-component-hot_reload.so                    0,06 MB
lib/arm64-v8a/libmono-component-marshal-ilgen.so                 0,03 MB
lib/arm64-v8a/libmonosgen-2.0.so                                 2,95 MB
lib/arm64-v8a/libSystem.Globalization.Native.so                  0,07 MB
lib/arm64-v8a/libSystem.IO.Compression.Native.so                 0,70 MB
lib/arm64-v8a/libSystem.Native.so                                0,09 MB
lib/arm64-v8a/libSystem.Security.Cryptography.Native.Android.so  0,15 MB

=== spike + core managed assemblies ===
assets/.godot/mono/publish/arm64/GodotAndroidSpike.deps.json            28,80 KB
assets/.godot/mono/publish/arm64/GodotAndroidSpike.dll                   9,00 KB
assets/.godot/mono/publish/arm64/GodotAndroidSpike.pdb                  15,90 KB
assets/.godot/mono/publish/arm64/GodotAndroidSpike.runtimeconfig.json    0,40 KB
assets/.godot/mono/publish/arm64/GodotSharp.dll                       5790,00 KB
assets/.godot/mono/publish/arm64/System.Linq.dll                       131,80 KB
assets/.godot/mono/publish/arm64/System.Private.CoreLib.dll           4259,30 KB
assets/.godot/mono/publish/arm64/mscorlib.dll                           58,30 KB

=== total .dll entries in apk ===
170
```

### Signature

```
$ apksigner verify --print-certs --verbose bin/SlayIdleRepeatSpike.apk
Verifies
Verified using v1 scheme (JAR signing): false
Verified using v2 scheme (APK Signature Scheme v2): true
Number of signers: 1
Signer #1 certificate DN: CN=Android Debug, O=Android, C=US
Signer #1 key algorithm: RSA
Signer #1 key size (bits): 2048
```

### The managed code is genuinely executed (desktop leg)

The same C# runs correctly under the desktop export of the identical project,
confirming the assembly is not inert:

```
$ godot --headless --path spikes/godot-android-export --quit-after 3
Godot Engine v4.7.1.stable.mono.official.a13da4feb - https://godotengine.org

[O23] ---- Godot C#/.NET Android spike ----
[O23] SpikeMath.IdleYield(3, 14) = 168
[O23] SpikeMath.Fingerprint(7,1,9,4) = 9-7-4-1
[O23] framework = .NET 10.0.9
[O23] process arch = X64
[O23] os = Microsoft Windows 10.0.19045
[O23] godot = 4.7.1-stable (official)
[O23] RESULT: managed code executed correctly.
```

---

## Known sharp edges for CI

1. **Editor settings are a required, out-of-repo input.** The Android SDK path,
   JDK path and debug keystore come from
   `%APPDATA%\Godot\editor_settings-4.7.tres`, **not** from `ANDROID_HOME`. The
   CI job must generate and patch this file explicitly (failure 3).
2. **Never call `--build-solutions`** — it hangs the runner forever (failure 1).
3. **Assert on APK contents, not exit code.** A missing `.sln` yields exit 0 and
   a plausible APK with zero .NET inside (failure 5). Gate on
   `libmonosgen-2.0.so` + the project assembly, or on the Gradle output path
   containing `/mono/`.
4. **Upload `%APPDATA%\Godot\mono\build_logs\` as an artefact.** C# build
   failures are otherwise undiagnosable from the console (failure 6).
5. **Licence accepts are required**: `sdkmanager --licenses` must be fed `y`
   repeatedly before any package install. Non-interactive CI needs this piped or
   the `licenses/` directory restored from cache.
6. **First-run download sizes** (all cacheable):
   | Item | Size |
   |---|---|
   | Godot mono editor | 109 MB |
   | Godot **mono export templates** (`.tpz`) | 1146 MB (1901 MB unpacked) |
   | Android cmdline-tools | 148 MB |
   | Android SDK after install (platform 36 + build-tools 36.1.0 + platform-tools, **no NDK**) | ~800 MB (2820 MB *with* the NDK) |
   | Gradle + AGP deps into `GRADLE_USER_HOME` | 1180 MB |
   | `android/` build template dir inside the project | 1260 MB |
   | NuGet packages | restored from nuget.org (`Godot.NET.Sdk`, `GodotSharp`, `Godot.SourceGenerators` 4.7.1, plus the `Microsoft.NETCore.App.Runtime.Mono.android-arm64` runtime pack) |
   Cacheable directories: `$GRADLE_USER_HOME`, `$ANDROID_HOME`, `~/.nuget/packages`,
   and the Godot `export_templates/4.7.1.stable.mono/` directory.
7. **Do not install the NDK.** Verified unnecessary by re-running the export with
   the NDK directory renamed away; it still succeeded. Saves ~2 GB.
8. **Build times** (this machine): cold first Gradle run exceeded 9 minutes and
   was dominated by dependency downloads. With a warm Gradle cache, a clean run
   including `--install-android-build-template` took **~2 min 10 s**, and a
   steady-state incremental re-export took **~95 s**.
9. **`android/` is generated, ~1.26 GB, and git-ignored.** It is unpacked by
   `--install-android-build-template`. No `build.gradle` edit was needed for this
   spike. When M0-04/M7 inject the AppLovin MAX AARs, that edit lands *inside a
   git-ignored generated tree* — so it must be applied as a committed patch file
   or a script run after template installation, never as a hand edit. This is a
   concrete constraint on how `12_MONETIZATION_ADS.md` §3.2's "manual AAR copy
   and `build.gradle` edit" gets automated.
10. **Godot prints an `ERROR:` line on every successful headless run**
    (`EditorSettings not instantiated yet ... shutdown_adb_on_exit`). Any CI log
    scraping for `ERROR` will produce false failures.
11. **Windows-specific caveat:** this spike ran on Windows. The CI runner will
    most likely be Linux, where the export-template directory is
    `~/.local/share/godot/export_templates/` and the editor settings path differs.
    The command *shapes* transfer; the paths do not.

---

## Residual risk

**What this spike did not prove:**

- **Nothing was run on a device or emulator.** The APK was built, signed, and
  verified to contain the Mono runtime and the project assembly, but never
  launched. A device/emulator run would still catch: managed code failing at
  runtime on arm64 (the desktop leg ran x64), Mono AOT/interpreter issues,
  linker/trimming stripping something reflected-on at runtime, missing
  globalization data, and JNI/interop crashes on startup. **Recommendation:** M7
  CI should add an emulator smoke test that boots the app and greps `logcat` for
  the `[O23] RESULT:` style marker line.
- **Release builds were not attempted** — only `--export-debug`. Release adds
  R8/proguard, resource shrinking and real signing keys, all of which are common
  additional failure points for .NET-on-Android.
- **Only `arm64-v8a` was built.** `armeabi-v7a` and the x86 variants (needed for
  most emulators) were disabled in the preset.
- **No AppLovin MAX AAR was injected.** This spike proved the *custom template
  path works*; it did not prove the *ad build* works. That is the M0-04 / O14
  pairing, and the interaction between the two (sharp edge 9) is untested.
- **`minSdk 29` was accepted implicitly** and has not been checked against the
  intended device coverage.

**What must still happen before O23 can be closed:**

- **M0-05b — the iOS leg.** O23's original wording flags iOS as the *especially*
  risky half, and nothing here speaks to it. It must answer: does Godot 4.7.1
  C#/.NET export to iOS at all; does the `Podfile` / `pod install --repo-update`
  / Xcode path in `12_MONETIZATION_ADS.md` §3.2 work; and does it need a paid
  Apple Developer account and Team ID merely to *build*, or only to sign and
  distribute? Until that is answered, **D1 is only half-validated**, and the
  fallback options in `16` O23 remain live for iOS even though they are now
  closed for Android.
