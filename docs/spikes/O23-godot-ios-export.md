# O23 spike — Godot 4 C#/.NET iOS export through the CocoaPods/Xcode path

- **Task:** M0-05b (milestone M0)
- **Open question:** `game-design/16_DECISION_LOG.md` O23 — *"Godot 4.x C# (.NET) mobile export maturity — especially iOS"*
- **Scope:** iOS leg only. The Android leg is **M0-05a**, executed for real, written up in [`O23-godot-android-export.md`](O23-godot-android-export.md). Read that one first; this document is deliberately its mirror image.
- **Method:** **Source-and-docs recipe.** No Mac, no Apple Developer account (M0 kickoff decision 3). **Nothing here was executed.**
- **Date of investigation:** 2026-08-11

---

## Status — read this before anything else

> 🔒 **NOT EXECUTED. O23 REMAINS OPEN.**
>
> No iOS export was run. No Xcode project was generated. No `.ipa` exists. Nothing
> in this document is a verified build. **Any statement anywhere that "iOS export
> is verified" is wrong** — the honest summary is: *here is exactly what to run,
> here is what will break, here is what we still do not know.*
>
> `16` O23's fallback options — **engine version pin, GDScript UI shell, wait for
> a point release** — remain live for iOS. They are closed for Android only.

What this *is*: a recipe assembled by reading the **Godot 4.7.1-stable engine
source at the exact tag we pin** (`a13da4feb`, the same build hash the Android leg
recorded), the 4.7 documentation, the engine issue tracker, and the AppLovin MAX
Godot repository. Every claim below is either **[S] sourced** with a link, or
explicitly marked **[U] unverified** with the check that would settle it. A
fabricated recipe is worse than no recipe, because somebody will follow it.

### What a Mac would settle in one afternoon

In rough order of value:

1. Does `godot --headless --export-debug "iOS" build/ios/` complete at all on a
   C# project at 4.7.1, and does it produce `<Assembly>_aot.xcframework`? (§5)
2. Does the NativeAOT publish survive **our** code — the composition root, the
   JSON content pipeline, `Core`'s reflection-free promise? (§8 R1 — the risk
   most likely to force a `16` O23 fallback.)
3. Is engine issue [#118161](https://github.com/godotengine/godot/issues/118161)
   (broken arm64 simulator slice, open, 4.6.2) still present at 4.7.1? (§8 R4)
4. Does the AppLovin MAX plugin link against 4.7.1 iOS headers when rebuilt from
   source, and does its SDK actually initialise? (§7)
5. What is the real wall-clock time of a cold export + `xcodebuild archive`, i.e.
   what does the `ios-export` CI job cost at the 10× macOS minute multiplier? (§8 R8)

Items 1–3 need **no Apple Developer account** — see §4.1. Item 1 does not even
need a device. That is the cheapest, highest-value half-day available to this
project, and it should be bought the day a Mac exists.

---

## 1. Verdict — as far as it can be taken without a Mac

**Three things are settled, and they were settled by reading the engine, not by guessing.**

1. 🔴 **A non-macOS runner cannot do this. At all. By design.** The
   Apple-embedded exporter contains a hard, unconditional refusal for .NET
   builds off macOS — not a warning, a `return false`:

   ```cpp
   #if defined(MODULE_MONO_ENABLED) && !defined(MACOS_ENABLED)
       // TODO: Remove this restriction when we don't rely on macOS tools to package up the native libraries anymore.
       r_error += TTR("Exporting to an Apple Embedded platform when using C#/.NET is experimental and requires macOS.") + "\n";
       return false;
   #else
   ```

   — `editor/export/editor_export_platform_apple_embedded.cpp`,
   [`has_valid_export_configuration`, 4.7.1-stable](https://github.com/godotengine/godot/blob/4.7.1-stable/editor/export/editor_export_platform_apple_embedded.cpp).
   **[S]** There is no flag, no env var and no headless mode that gets around it.
   The `ios-export` CI job must run on a `macos-*` runner or not exist.

2. ✅ **Godot 4.7.1 does support C#/.NET on iOS, and the whole path is
   scriptable.** The exporter itself shells out to `xcodebuild archive` and
   `xcodebuild -exportArchive` when `application/export_project_only=false`, so
   `godot --headless --export-debug` can go all the way to an `.ipa` in one
   invocation — *if* signing inputs are present. **[S]** (§4, §5)

3. ⚠️ **It is labelled experimental, and on iOS "experimental" means
   NativeAOT.** Godot's own words: *"Exporting to an Apple Embedded platform when
   using C#/.NET is experimental."* **[S]** iOS is the only platform where our
   managed code is **ahead-of-time compiled and trimmed** rather than JIT'd on
   Mono — a materially different runtime from the one every test, the economy
   simulator, the server and the Android build exercise. **This, not the build
   plumbing, is the real O23 risk on iOS.** (§8 R1)

**What is not settled:** whether *our* game survives NativeAOT; whether the ad
SDK can be made to build at all (§7 — the answer there is bad); and every
runtime behaviour, because nothing ran.

### The one place `12` §3.2 is wrong

> | iOS | Export project → author a `Podfile` → `pod install --repo-update` → build in Xcode. App Store Team ID and Bundle Identifier required at export time. |

The **Team ID / Bundle Identifier** half is exactly right and is enforced by a
hard error (§4). The **CocoaPods** half is misattributed. Godot 4.7 never
generates a `Podfile`, never runs `pod install`, and its iOS plugin format has no
CocoaPods key at all — the 4.7 iOS export and iOS plugin documentation pages
mention CocoaPods zero times, and `Podfile` appears nowhere in the exporter
source. **[S]** CocoaPods enters solely because **AppLovin's own integration
instructions** tell you to hand-author a `Podfile` next to the generated
`.xcodeproj` and run `pod install --repo-update`.

That matters for two reasons:

- A **base** iOS build — the thing that answers O23 — needs no CocoaPods at all.
  The two questions can and should be separated. Answer the engine question
  first; it is cheap and it is the one D1 rests on.
- The `Podfile` is hand-authored **inside the generated Xcode project**, which is
  regenerated (and, with `application/delete_old_export_files_unconditionally`,
  deleted) on every export. So it is exactly the same trap as the Android leg's
  sharp edge 9: **it must be a committed template copied in by a script, never a
  hand edit.** Same property, different file.

---

## 2. Version matrix

Everything in this table is read from the **4.7.1-stable tag** or the 4.7
documentation. Where a number could not be sourced, the row says so rather than
inventing one.

| Component | Value | Source |
|---|---|---|
| **Godot** | **4.7.1-stable, .NET/Mono build**, released **2026-07-14** | **[S]** [release](https://github.com/godotengine/godot-builds/releases/tag/4.7.1-stable). Tag `4.7.1-stable` → commit `a13da4feb8d8aefc283c3763d33a2f170a18d541`, whose short form `a13da4feb` **matches the build hash the Android leg recorded** (`4.7.1.stable.mono.official.a13da4feb`). The pin is real and the two legs are on the same engine. |
| Export templates | `Godot_v4.7.1-stable_mono_export_templates.tpz` → `4.7.1.stable.mono/` | **[S]** same `.tpz` the Android leg used. The exporter looks for `ios.zip` inside the template directory (`exists_export_template(get_platform_name() + ".zip")`). **Must be the mono templates.** |
| macOS Godot editor archive | filename **[U]** — expected `Godot_v4.7.1-stable_mono_macos.universal.zip` by convention; the release asset list did not render when fetched. **Check the downloads page.** | **[U]** |
| **Host OS** | **macOS, mandatory** | **[S]** engine source, §1.1. Also docs: *"You must export for iOS from a computer running macOS with Xcode installed."* |
| **Minimum Xcode** | **Not specified by Godot.** The docs' Requirements section says only "with Xcode installed" — no version floor anywhere in the docs or the exporter. | **[S]** [Exporting for iOS, 4.7](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_ios.html). **The practical floor is whatever Xcode the export template's `.xcframework` slices were built against** — and that is exactly what breaks (§8 R4). |
| **Minimum iOS deployment target** | **15.0**, the default of `application/min_ios_version` | **[S]** `EditorExportPlatformIOS::get_minimum_deployment_target() { return "15.0"; }` — `platform/ios/export/export_plugin.h` @ 4.7.1-stable. It is a **preset option**, so it is ours to raise, not to discover. |
| Renderer constraint | Metal + `forward_plus`/`mobile` requires `min_ios_version` ≥ **14.0** (already satisfied by the 15.0 default) | **[S]** `EditorExportPlatformIOS::has_valid_export_configuration` |
| **Device floor implied by our renderer** | 🔴 **Apple A12 or newer** — iPhone XS / XR / iPad mini 5 and later | **[S]** the exporter force-adds the `iphone-ipad-minimum-performance-a12` required-device-capability when `rendering/renderer/rendering_method.mobile` is `mobile` or `forward_plus`. **This is the iOS twin of the Android leg's silent `minSdk 29`** — a product decision made by a toolchain default. Our spike project uses the `mobile` renderer, so it applies. |
| iOS architecture | `arm64` only (`architectures/arm64`; it is the only architecture the platform offers) | **[S]** `EditorExportPlatformIOS` |
| Simulator | Also built, always, as a second .NET publish target (`iossimulator-arm64` + `iossimulator-x64`, `lipo`'d together). **The simulator only supports the `Compatibility` renderer** — so a simulator run does not exercise our `mobile` renderer. | **[S]** `modules/mono/.../Export/ExportPlugin.cs`; docs warning |
| **CocoaPods** | **Not required by Godot.** No minimum version exists because the engine never invokes it. Required only by AppLovin's integration instructions (§7). GitHub `macos-15` ships CocoaPods **1.17.0** preinstalled. | **[S]** |
| **.NET** | `net8.0`, SDK pinned by `global.json`. iOS uses **NativeAOT**, which has had iOS support since .NET 8. | **[S]** [*Current state of C# platform support in Godot 4.2*](https://godotengine.org/article/platform-state-in-csharp-for-godot-4-2/), 2024-01-26; engine PR [#82729](https://github.com/godotengine/godot/pull/82729) |
| **Experimental?** | **Yes, explicitly.** *"Projects written in C# can be exported to iOS as of Godot 4.2, but support is experimental and some limitations apply."* (docs) and *"iOS support is currently experimental and has a few limitations."* (C#/.NET docs index) | **[S]** |
| CI runner | **`macos-15`** (arm64, GA). `macos-14` is **deprecated** in `actions/runner-images`. | **[S]** [runner-images README](https://github.com/actions/runner-images) |
| Runner Xcode | image `20260727.0256.1` ships **16.0, 16.1, 16.2, 16.3, 16.4 (default), 26.0.1, 26.1.1, 26.2, 26.3** | **[S]** [macos-15-arm64 readme](https://github.com/actions/runner-images/blob/main/images/macos/macos-15-arm64-Readme.md) |
| Runner .NET SDKs | 8.0.101 / 8.0.204 / 8.0.303 / **8.0.423** / 9.x / 10.x. Our `global.json` pin (`8.0.319`, `rollForward: latestFeature`) is satisfied by **8.0.423**. | **[S]** same |

### Known open engine issues for C# on iOS at 4.x

| Issue | Title | Opened | State | Why it lands on us |
|---|---|---|---|---|
| [#118161](https://github.com/godotengine/godot/issues/118161) | *iOS 4.6.2 export template ships broken arm64 simulator xcframework slice on Apple Silicon* | 2026-04-03 | **open** | The template advertises arm64-simulator support but ships an x86_64-only archive, so Xcode on Apple Silicon fails to link with undefined Godot C++ symbols. Affects **all** iOS exports, not just C#. Recorded against 4.6.2 as a regression from 4.6.1; **whether 4.7.1 is affected is [U]** — this is Mac-day check #3. |
| [#96072](https://github.com/godotengine/godot/issues/96072) | *Node Instantiation Fails with NativeAOT Enabled in Export Release Mode* | 2024-08-25 | **open** | Scene instantiation breaks in release/NativeAOT builds when `[Export]` is used on `Resource`-typed properties; reported on iOS App Store builds. Workaround is to avoid `[Export]` on Object/Resource types and use `ResourceLoader.Load()`. **This is the trimming/reflection risk made concrete.** |
| [#100123](https://github.com/godotengine/godot/issues/100123) | *Export Error with .NET 9 on iOS* | 2024-11-04 | closed | Historical, but it establishes that the iOS path is sensitive to the .NET SDK band. Keep the `global.json` pin. |

**[U] — not established:** whether any 4.7.x-specific C#-on-iOS regression exists.
The tracker search surfaced nothing filed against 4.7.x, but absence of reports
for an engine released four weeks ago is not evidence of absence.

---

## 3. Where every input is supplied — the Android leg's finding #3, applied to iOS

This is the highest-value section of the document. The Android leg's worst
discovery was that **Godot ignores `ANDROID_HOME` and reads the SDK/JDK/keystore
paths out of a per-machine `editor_settings-4.7.tres` that is not in the repo.**
The iOS answer is different, and — for once — better.

| Input | Where it comes from | Notes |
|---|---|---|
| **Xcode / iOS SDK location** | **`xcode-select -p`**, i.e. macOS-global state | **[S]** No editor setting, no env var. If it points at `/Library/Developer/CommandLineTools` instead of `/Applications/Xcode.app/Contents/Developer`, the export dies inside `clang` with `MSB3073`. The docs' own troubleshooting section is about exactly this. **CI must run `sudo xcode-select -switch` (or `DEVELOPER_DIR=`) explicitly** — this is the closest iOS analogue of the Android editor-settings trap, and it is one line. |
| **App Store Team ID** | 🔴 **Export preset only** — `application/app_store_team_id`. No environment override exists. | **[S]** `ERR_FAIL_COND_V_MSG(team_id.length() == 0, ERR_CANT_OPEN, "App Store Team ID not specified - cannot configure the project.")`. Declared `required=true`. Ten characters, e.g. `ABCDE12XYZ`. **A blank Team ID aborts the export before anything is written.** |
| **Bundle identifier** | **Export preset only** — `application/bundle_identifier`, `required=true`. Ours: `de.ludwigso.slayidlerepeat`. | **[S]** |
| **Signing identity** | Export preset — `application/code_sign_identity_debug` / `_release`. **Empty means "Apple Development" / "Apple Distribution"**, not "unsigned". | **[S]** The identity itself must be a certificate in the **runner's keychain** — that is the part CI has to import, and the part no preset can carry. |
| **Provisioning profile** | Export preset — `application/provisioning_profile_uuid_debug` / `_release` (marked `PROPERTY_USAGE_SECRET`) and `application/provisioning_profile_specifier_debug` / `_release`. **These four are the only iOS options with environment overrides.** | **[S]** ⚠️ **Name discrepancy — set both spellings.** The 4.7.1 class reference names `GODOT_APPLE_PLATFORM_PROVISIONING_PROFILE_UUID_DEBUG` / `_RELEASE` and `GODOT_APPLE_PLATFORM_PROFILE_SPECIFIER_DEBUG` / `_RELEASE`; the 4.7 *Exporting for iOS* tutorial page still documents the older `GODOT_IOS_PROVISIONING_PROFILE_UUID_DEBUG` / `_RELEASE`. The class reference is generated from the engine and wins, but the cost of exporting both names is zero. If the profile UUID is left empty, **Xcode is expected to download or create a profile automatically** — which requires being signed in, i.e. an account. |
| **Export method** | Export preset — `application/export_method_debug` (default **1 = Development**) / `_release` (default **0 = App Store**). Enum: `App Store, Development, Ad-Hoc, Enterprise`. Written into `export_options.plist` as `$export_method`. | **[S]** |
| **`ExportOptions.plist`** | 🔴 **Generated by Godot, not authored by us.** `export_options.plist` is templated out of the export template and filled from the preset (`$team_id`, `$export_method`, `$provisioning_profile_*`). Godot then passes it to `xcodebuild -exportArchive -exportOptionsPlist`. | **[S]** The `12` §3.2 mental model of hand-authoring iOS build config is wrong on this point too: **the preset is the source of truth, and the plist is derived.** Do not hand-edit it; it is regenerated. |
| **Deployment target** | Export preset — `application/min_ios_version`, default `15.0` → `IPHONEOS_DEPLOYMENT_TARGET`. | **[S]** |
| **Ad plugin enable flag** | Export preset — one `plugins/<PluginName>` boolean per `.gdip` discovered under `res://ios/plugins/`. | **[S]** |
| **Script encryption key** | Environment — `GODOT_SCRIPT_ENCRYPTION_KEY`. | **[S]** Not used by us today. |
| **Keychain / certificates** | **Runner state.** Import a `.p12` into a temporary keychain before the export. | **[U]** — standard iOS CI practice, but unexercised here. |

**The one-line summary:** on Android the CI-hostile state was a per-machine
**editor settings file**; on iOS it is the **export preset plus the runner's
keychain**. The preset is a committed file, which is a large improvement — but
the Team ID inside it is account-specific, so `export_presets.cfg` cannot be
fully committed with real values until an Apple Developer account exists. Keep it
committed with the Team ID injected by the CI step, exactly the way the Android
leg keeps the keystore out of the preset.

---

## 4. The recipe

Copy-pasteable, and **untested**. Treat every command as a hypothesis. Run it in
the order given; the assertions in §5 are not optional.

### 4.0 One-time toolchain setup (macOS)

```bash
# 1. Xcode. Install from the App Store or `xcodes`, then point the toolchain at it.
#    On a GitHub macos-15 runner Xcode is preinstalled; select it explicitly.
sudo xcode-select --switch /Applications/Xcode_16.4.app
xcodebuild -version
xcode-select -p          # must print .../Xcode.app/Contents/Developer, NOT CommandLineTools

# 2. .NET SDK — pinned by the repo's global.json (8.0.x). Do not let a preview win.
dotnet --list-sdks

# 3. Godot 4.7.1 MONO editor + MONO export templates.
#    Editor:    Godot_v4.7.1-stable_mono_macos.universal.zip     [U] verify the exact asset name
#    Templates: Godot_v4.7.1-stable_mono_export_templates.tpz    [S] same file the Android leg used
#    Unpack the .tpz and copy the CONTENTS of its templates/ folder into:
#      ~/Library/Application Support/Godot/export_templates/4.7.1.stable.mono/
#    The exporter looks for ios.zip in there.
GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot

# 4. CocoaPods — ONLY needed once the AppLovin plugin is in play (§7). Not for a base build.
pod --version    # 1.17.0 on macos-15
```

### 4.1 Stage 1 — the export that needs **no Apple Developer account**

This is the part to run first, and it answers O23's engine question on its own.

```bash
PROJ=spikes/godot-ios-export        # a Godot C# project; see §5 for what it must contain
OUT=$PWD/build/ios

# a. Import assets. Also materialises editor state on a fresh machine.
"$GODOT" --headless --path "$PROJ" --import

# b. Export ONLY the Xcode project. No xcodebuild, no signing, no account.
#    application/export_project_only=true in the preset makes Godot stop after
#    generating the project — the engine's own documented Fastlane escape hatch.
mkdir -p "$OUT"
"$GODOT" --headless --path "$PROJ" --export-debug "iOS" "$OUT/SlayIdleRepeatSpike.xcodeproj"
```

⚠️ **`application/app_store_team_id` must still be non-empty** even here — the
export aborts on a blank Team ID before it writes anything (§3). A **[U]**
question worth two minutes on the Mac: does a syntactically valid but fictitious
ten-character Team ID get far enough to generate the project? If it does,
stage 1 needs no Apple relationship whatsoever. If it does not, stage 1 needs a
free Apple ID's Personal Team ID and nothing more.

Then compile it without signing anything:

```bash
# c. Build unsigned. This proves the engine + NativeAOT + linker path works and
#    needs no certificate, no profile and no paid account.
xcodebuild \
  -project "$OUT/SlayIdleRepeatSpike.xcodeproj" \
  -scheme SlayIdleRepeatSpike \
  -configuration Debug \
  -sdk iphoneos \
  -derivedDataPath "$OUT/dd" \
  CODE_SIGN_IDENTITY="" CODE_SIGNING_REQUIRED=NO CODE_SIGNING_ALLOWED=NO \
  build
```

**[S]** unsigned `xcodebuild` builds are standard CI practice (`CODE_SIGN_IDENTITY=""`,
`CODE_SIGNING_REQUIRED=NO`, `CODE_SIGNING_ALLOWED=NO`); **[U]** that a
Godot-generated project tolerates them, because Godot's generated scheme may
carry signing settings the flags do not fully override.

### 4.2 Stage 2 — the signed export (needs an Apple Developer account)

```bash
# Preset carries: app_store_team_id, bundle_identifier, code_sign_identity_*,
#                 export_method_*, export_project_only=false
# Environment carries the provisioning profile. Export BOTH spellings (§3).
export GODOT_APPLE_PLATFORM_PROVISIONING_PROFILE_UUID_DEBUG="$PROFILE_UUID"
export GODOT_IOS_PROVISIONING_PROFILE_UUID_DEBUG="$PROFILE_UUID"

# Import the signing certificate into a throwaway keychain first (not shown).

"$GODOT" --headless --path "$PROJ" --export-debug "iOS" "$OUT/SlayIdleRepeatSpike.xcodeproj"
# With export_project_only=false Godot itself now runs, in order:
#   xcodebuild ... -allowProvisioningUpdates   -> SlayIdleRepeatSpike.xcarchive
#   xcodebuild -exportArchive -exportOptionsPlist <binary_dir>/export_options.plist
#              -allowProvisioningUpdates       -> SlayIdleRepeatSpike.ipa
```

**[S]** both `xcodebuild` invocations, the `-allowProvisioningUpdates` flags and
the generated `export_options.plist` are read directly out of
`editor_export_platform_apple_embedded.cpp` at 4.7.1-stable.

**A free Apple ID will not do for CI.** Personal-Team ("free provisioning")
profiles expire after **7 days**, cap at **3 devices** and **10 App IDs**. Fine
for one developer poking at a device; useless for a build server. Stage 2 means
the **paid Apple Developer Program**, and that is a real prerequisite with a real
annual cost that this project has not incurred.

### 4.3 Stage 3 — the ad build (`12` §3.2's CocoaPods path)

Only reachable once §7 is resolved. Shape, for completeness:

```bash
# 1. Vendor the plugin's .gdip + xcframeworks under res://ios/plugins/, enable it
#    in the preset (plugins/<Name>=true), and export with export_project_only=true.
# 2. Copy the COMMITTED Podfile template next to the generated .xcodeproj.
#    NEVER hand-edit it in place: the export regenerates/deletes that directory.
cp build/ios/Podfile.template "$OUT/Podfile"
( cd "$OUT" && pod install --repo-update )
# 3. Build the WORKSPACE that CocoaPods created, not the project.
xcodebuild -workspace "$OUT/SlayIdleRepeatSpike.xcworkspace" -scheme SlayIdleRepeatSpike ...
```

⚠️ Note the step-3 change: after `pod install` the buildable unit is the
`.xcworkspace`, **not** the `.xcodeproj` — but Godot's own `export_project_only=false`
path drives the `.xcodeproj`. **Stage 3 and Godot's built-in archive step are
mutually exclusive.** With ads in the build, `export_project_only` must be `true`
and CI owns the `xcodebuild` calls. `12` §3.2's "build in Xcode" is doing a lot
of quiet work in that sentence.

---

## 5. The silent-failure analogue — an iOS build that "succeeds" with no .NET in it

The Android leg's most dangerous finding was failure 5: a missing/incomplete
`.sln` produced **exit 0 and a valid ~80 MB APK with zero .NET inside**, because
Godot silently fell back to the `standard` (non-mono) Gradle product flavour.

**The same root cause exists on iOS, and it is the same line of code.** The mono
export plugin's entire test for "is this a .NET project" is:

```csharp
private static bool ProjectContainsDotNet()
{
    return File.Exists(GodotSharpDirs.ProjectSlnPath);
}

public override string[] _GetExportFeatures(EditorExportPlatform platform, bool debug)
{
    if (!ProjectContainsDotNet())
        return Array.Empty<string>();
    return new string[] { "dotnet" };
}
```

— `modules/mono/editor/GodotTools/GodotTools/Export/ExportPlugin.cs` @ 4.7.1-stable. **[S]**

No `.sln` ⇒ no `dotnet` export feature ⇒ `_ExportBegin` never publishes ⇒ the
Xcode project is generated with **no managed code whatsoever**, and it will build
and launch. The `.cs` files ride along as loose resources. The same
`InvalidOperationException` the Android leg saw is thrown and — as the Android
leg proved empirically — **does not fail the export**.

There is no `standard`-vs-`mono` flavour on iOS, so the Android tell (the
`/mono/` output path) does not transfer. **The iOS tell is completely different,
because iOS is NativeAOT, not Mono: there are no managed `.dll`s to look for.**
The publish produces a **native `.dylib`** which is packed into an
`.xcframework`:

```csharp
string soExt = ridOS switch {
    OS.DotNetOS.OSX or OS.DotNetOS.iOS or OS.DotNetOS.iOSSimulator => "dylib", ... };
...
string xcFrameworkPath = Path.Combine(GodotSharpDirs.ProjectBaseOutputPath,
    publishConfig.BuildConfig, $"{GodotSharpDirs.ProjectAssemblyName}_aot.xcframework");
if (!BuildManager.GenerateXCFrameworkBlocking(outputPaths, xcFrameworkPath))
    throw new InvalidOperationException("Failed to generate xcframework.");
AddAppleEmbeddedPlatformEmbeddedFramework(xcFrameworkPath);
```

**[S]** same file.

### The assertion CI must run

Mirroring the Android leg's managed-assembly check. **Assert on artefacts, never
on exit code.**

```bash
BIN="$OUT/SlayIdleRepeatSpike"     # the sibling directory of the .xcodeproj
ASM=SlayIdleRepeatSpike            # [dotnet] project/assembly_name

# 1. The AOT xcframework must exist and must have been embedded in the export.
test -d "$BIN/${ASM}_aot.xcframework" \
  || { echo "::error::no ${ASM}_aot.xcframework — this export contains NO .NET"; exit 1; }

# 2. It must contain a real arm64 device slice, not just a simulator one.
#    (Guards engine issue #118161 as well as a half-published AOT build.)
lipo -info "$BIN/${ASM}_aot.xcframework"/ios-arm64/*.dylib   # or .framework payload — [U] exact layout

# 3. The NativeAOT publish output must exist for the DEVICE rid, not only the simulator.
find . -type d -name 'godot-publish-dotnet' -print
#   expect .../godot-publish-dotnet/ExportDebug-ios-arm64/ containing ${ASM}.dylib

# 4. Belt and braces: the built .app must carry the .NET data payload.
#    ICU/.dat files are added as bundle files and the rest as shared objects under
#    data_<CSharpProjectName>_ios_arm64/.
ls "$OUT/dd/Build/Products/Debug-iphoneos/${ASM}.app/" | grep -E 'data_.*_ios_arm64|\.dat$'
```

**[S]** for the existence and naming of `godot-publish-dotnet/<BuildConfig>-<rid>/`,
`<Assembly>_aot.xcframework`, the `data_<CSharpProjectName>_ios_<arch>/` shared-object
directory and the `.dat` bundle-file special case — all read out of
`ExportPlugin.cs` at 4.7.1-stable.
**[U]** for the exact on-disk layout inside the `.xcframework` and the final
`.app`, because nothing was built. Steps 1 and 3 are the load-bearing ones and
their inputs are sourced; steps 2 and 4 need their paths confirmed on the Mac.

### Two more inherited failure modes

- **ETC2/ASTC is required on iOS too.** `has_valid_project_configuration` calls
  `ResourceImporterTextureSettings::should_import_etc2_astc()` and returns
  invalid if it is off, and the platform pushes the `etc2` and `astc` features
  (*"Vulkan and OpenGL ES 3.0 both mandate ETC2 support"*). **[S]** So the Android
  leg's failure 2 fix — `rendering/textures/vram_compression/import_etc2_astc=true`
  in `project.godot` — is **already the right setting for iOS**, and iOS needs no
  additional VRAM-compression flag. One less thing.
- **The generated `ios/` tree poisons the C# compile**, exactly like `android/`.
  The Android leg needed `<Compile Remove="android/**" />`; the iOS project needs
  the same treatment for whatever directory the iOS export writes into if that
  path is inside the project. **[U]** — Godot's iOS export destination is chosen
  by us in the preset (`export_path`), so the mitigation is stronger than
  Android's: **point it outside the Godot project directory**, e.g. `build/ios/`
  at the repository root, and the problem never arises. Add
  `<Compile Remove="build/**" />` anyway.
- **`--build-solutions` still hangs headless.** That is a Godot-wide behaviour,
  not an Android one. **CI must never call it on any platform.** **[S]** by the
  Android leg's execution.

---

## 6. The gated CI job

`.github/workflows/ci.yml` gains an `ios-export` job following the established
`android-export` pattern exactly: `if: false`, a comment naming the milestone
that enables it and this document as its specification, and — per the convention
in `.github/workflows/README.md` — an `::error::` and `exit 1` if it is ever
actually run, so it can never pass vacuously. It is pinned to **`macos-15`**
(§2) because §1.1 makes any other runner impossible.

It is registered in `.github/workflows/README.md` alongside `android-export` and
`determinism`.

---

## 7. The AppLovin MAX iOS question

`12` §3.1 records the plugin as *"actively maintained"*. M0-04 found that no
longer true. **This spike finds it worse than that, and against our 4.7.1 pin the
answer is unambiguous.**

### 7.1 Verdict

> 🔴 **Treat the AppLovin MAX Godot plugin as a dead dependency for iOS.** It is
> not a question of whether it is fixed — it is three Godot minor versions past
> anything it was ever built against, its iOS binaries are prebuilt against
> engine-internal C++ symbols that Godot does not keep stable, and nobody is
> maintaining it.

The evidence, all **[S]**:

| Fact | Value |
|---|---|
| Last code commit on `master` | **2025-04-24** (`e597986`, "Release/1.2.0") — unchanged since M0-04 checked, now ~15.5 months |
| Last release | `release_1_2_0`, **2025-04-24** |
| Last maintainer activity of any kind | **2025-04-28** |
| README's entire compatibility claim | *"We currently only support Godot 4.x."* No version table, no tested-against list |
| Issues #59–#67 | **every one still has zero maintainer comments**, including [#67](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/67), an urgent revenue complaint open since 2026-02-04 |
| **Our pin** | Godot **4.7.1**, released 2026-07-14 — **three minor versions** (4.5, 4.6, 4.7) after the plugin's last build |

### 7.2 What exactly breaks on iOS

Two distinct, unfixed failures — a **build** failure and a **runtime** failure:

- [**#61**](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/61) — *"Godot 4.5
  IOS export fails to build in Xcode 16.3 due to undefined symbol errors"*, opened
  2025-10-03, **open**, 11 comments, none from AppLovin. Undefined arm64 symbols
  such as `StringName::assign_static_unique_class_name`, `Memory::alloc_static`,
  `ClassDB::_add_class2`, `String::utf8`. Removing the plugin makes the build
  succeed. The same workflow worked on Godot 4.4.
- [**#60**](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/60) — *"Initialization
  fails on iOS versions on Godot 4.4/4.5"*, opened 2025-09-30, **open**. Worse
  than a link error: on 4.4 it *compiles* and then the SDK **fails to initialise
  at runtime**, reproducible in the plugin's own demo app. 4.3 was reportedly fine.
- [**#66**](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/66) — *"AppLovin
  plugin failing on iOS, Godot 45. Update, when?"*, opened 2025-12-02, **open**.
  On 2025-12-15 the reporter relayed that AppLovin *support* said a fix was
  *"added to our engineering backlog for an upcoming Godot release"*, hoping for
  4.6. On 2026-03-05: *"Almost one year without updates."* **Godot 4.6 and 4.7
  both shipped. Nothing was delivered.**

**Why this is structural, not a bug that might get fixed.** The plugin ships
**prebuilt** `AppLovinMAXGodotPlugin.debug.xcframework` / `.release.xcframework`
under `ios/plugins/`. Godot's iOS-plugin documentation says the library *"must
have a dependency on the Godot engine headers"* and that you *"should use the
same header files for iOS plugins and for the iOS export template."* The symbols
in #61 are **mangled Godot core C++ internals**, across which the engine makes no
compatibility guarantee. **Any prebuilt `.gdip` plugin is effectively locked to
the Godot minor it was compiled against.** A binary built on 2025-04-24 cannot be
expected to link against 4.7.1, and the observed 4.4→4.5 break is the mechanism
demonstrating it.

To be precise about one thing: the `.gdip` format itself is **not** deprecated —
a Godot core developer said as much in January 2026, adding that the
`godot-ios-plugins` repository *"isn't deprecated, but it lacks a maintainer."*
The problem is symbol drift, so the fix is *rebuild from source*, not *migrate to
a new plugin API*.

**Is it fixed? No. Is it worse on 4.7.x? Structurally, yes** — every additional
minor version widens the ABI gap. **[U]** Nobody has publicly reported trying the
plugin on 4.6 or 4.7 at all; reports stop at 4.5. Assume broken until a rebuild
against 4.7.1 headers proves otherwise.

**No fork rescues it.** All nine forks of the official repo are either empty
(`ESnider`, created 2025-10-14 with the stated intent to fix iOS and **zero
commits**), Godot-3-oriented, or stale since 2024. The one genuinely active
AppLovin-adjacent fork, `DeerRockStudios/godot-applovin-max-v2` (created
2026-07-31), states in its own README that **iOS is not addressed** — it is an
Android-only export-plugin wrapper.

**And there is no vendor help for C#.** [#59](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/59),
*"Please document how to integrate AppLovin via C#"*, has sat unanswered since
2025-06-24. The README mentions C#, .NET and Mono zero times.

### 7.3 How much runway does `FakeRewardedAdAdapter` actually buy?

`12` §3.2's architecture is the thing that saves us here, and it is worth stating
the size of the saving precisely.

`IRewardedAdPort` has three implementations. Two of them — `FakeRewardedAdAdapter`
(tests, CI, economy simulator) and `AutoGrantAdAdapter` (Slay Plus subscribers) —
**contain no SDK at all**, and the composition root picks between them from a
server-issued entitlement. So:

- **Everything except the real ad adapter is unblocked.** All of `Core`,
  `Application`, `Contracts`, `Server`, every adapter that is not
  `Adapters.Ads.AppLovin`, the economy simulator, the balance harness, the whole
  content pipeline, and every test suite.
- **The ad-facing *game* code is unblocked too**, because the port is what the
  game talks to. Placements, caps, the reward grant path, the `/ad/intent` and
  `/ad/claim` server endpoints from O14 §8, the FTUE gating — all of it can be
  built and tested end-to-end against `FakeRewardedAdAdapter`.
- **What is blocked** is exactly one adapter project and the iOS half of one
  build step. In milestone terms that is **M15**, and only its AppLovin adapter.
- **What is at risk beyond that** is the *revenue model*, not the *code*: a
  shipped iOS build with no ad SDK earns nothing from free players. Slay Plus
  still works (it is an adapter swap, not an SDK).

**So the runway is: the entire project up to M15, plus an iOS build that is
playable and testable but not monetisable.** That is a great deal of runway — it
is genuinely fine to defer this decision — but it converts cleanly into a
deadline: **the decision must be made before M15 starts**, and it now has enough
evidence to be made early rather than late.

### 7.4 Costed fallbacks

| # | Option | Cost | Ongoing cost | Verdict |
|---|---|---|---|---|
| **A** | **Fork and rebuild** the MAX plugin from source against 4.7.1 iOS headers | Clone Godot at 4.7.1, run SCons to produce iOS headers, rebuild both `debug` and `release` xcframeworks in Xcode, re-vendor. Issue #61's reporters did succeed at this on 4.4.1/4.5 — *"I managed to build with 4.5 headers"* — so it is known-possible. **~3–5 days** for the first pass, on top of `12` §3.2's 3–5 days for the shim. | 🔴 **Repeat on every Godot minor upgrade, forever**, plus #60's unfixed runtime init bug, plus #62/#65's defects, plus writing our own C# interop with no vendor support. | Viable, expensive, and permanent. This is the "vendor and self-maintain" path M0-04 §10 Q5 called the expected steady state. |
| **B** | **Switch mediation to AdMob** via [`poingstudios/godot-admob-plugin`](https://github.com/poingstudios/godot-admob-plugin) | Reopens **D15**. Rewrites `Adapters.Ads.AppLovin` → `Adapters.Ads.AdMob` and `PlacementMap`. The O14 S2S design survives in shape (AdMob has its own SSV with a `custom_data` equivalent) but the verification details must be re-spiked. **~5–8 days** including a new O14-shaped spike. | 🟢 Low — actively maintained (pushed **2026-08-10**, release **v5.0.0** 2026-07-21), supports Godot 4.2+, iOS, and has a **documented first-class C# API**. v5.0.0 also mediates 17+ networks **including AppLovin**, so AppLovin demand is not lost, only demoted from mediator to bidder. | **Recommended if A's ongoing cost is judged unacceptable.** It is the only option that is simultaneously maintained, iOS-capable and C#-native. |
| **C** | **Hand-write a native iOS shim** against Godot's iOS plugin API | Objective-C/Swift, Godot source + SCons to generate headers, `.a`/`.xcframework` in debug and release, a `.gdip`, and the MAX iOS SDK integrated by hand. **~2–3 weeks**, i.e. back to the estimate `12` §3.2 was proud of having avoided. | 🔴 Identical to A's treadmill — you rebuild on every Godot minor — with none of A's head start. | **Reject.** Strictly dominated by A. |
| **D** | **Ship Android-first**, iOS later | Near zero engineering. Android's ad path is the one the M0-05a spike already proved buildable. | 🟡 Halves the addressable market and defers, rather than answers, O23. | A schedule lever, not a solution. Reasonable as a *combination* with B (ship Android on MAX, land iOS on AdMob) but that means maintaining two ad adapters. |
| **E** | **Ship iOS with no ads**, Slay Plus only | Zero — `AutoGrantAdAdapter` and `FakeRewardedAdAdapter` already exist by design (§7.3). | 🔴 Zero ad revenue from iOS free players, permanently, and `12` §1's fairness contract quietly becomes "iOS free players get the Plus grant for free", which is a different game. | Emergency valve only. |

**Recommendation, to be ruled on at the M15 kickoff and recorded in `16` against
D15 — not decided by this spike:** the AppLovin MAX Godot plugin no longer
justifies D15's rationale (*"AppLovin ships an official MIT-licensed Godot 4
plugin, which removes the main integration risk"*). It did not remove the risk;
it transferred it to us. **Sequence a decision between A and B before M15
starts**, and make the deciding experiment the cheap one: *does the plugin, rebuilt
from 4.7.1 headers, link and initialise?* That is one Mac-day. If yes, A is
survivable. If no, B.

---

## 8. Risk register

Ranked by *probability × cost to the project*. 🔴 marks the ones that could still
force `16` O23's fallback discussion (engine version pin / GDScript UI shell /
wait for a point release).

| # | Risk | Trigger — how you find out | Mitigation | Fallback pressure |
|---|---|---|---|---|
| **R1** | 🔴 **NativeAOT + trimming breaks our game at runtime, on iOS only.** iOS is the sole platform where our C# is AOT-compiled and trimmed; every test, the simulator, the server and the Android build run Mono/JIT. Godot needs reflection, and engine issue [#96072](https://github.com/godotengine/godot/issues/96072) (open) shows scene instantiation failing in AOT release builds over `[Export]`ed `Resource` properties. Our composition root, JSON content deserialisation and any `Activator`/`Type.GetType` usage are exposed. | A build that exports and launches, then throws `MissingMetadataException` / `NotSupportedException` / silently wrong behaviour **only on device, only in release**. It will not show up in any test suite we own. | Ban reflection-dependent patterns in `Core`/`Application` **now**, while there is no code to fix — add an architecture rule (M0-08 already owns banned-API greps). Prefer source generators and `System.Text.Json` source-gen contexts over runtime reflection. Avoid `[Export]` on Resource-typed properties per #96072's workaround. Add a device smoke test the moment a Mac exists. | 🔴 **Highest.** If our architecture cannot be made AOT-safe, the fallback is a GDScript UI shell with C# confined to `Core` — precisely `16` O23's second option. Discovering this in month 4 is the disaster O23 was raised to prevent. |
| **R2** | 🔴 **The ad SDK cannot be made to build on iOS at 4.7.1** (§7). Three unfixed issues, an abandoned repo, a prebuilt binary locked to a 2025-04 engine ABI. | A rebuild-from-source attempt that still fails to link, or links and then fails `AppLovinMAX.initialize()` at runtime (#60). | §7.4 — decide A vs B **before M15**. Everything up to M15 runs on `FakeRewardedAdAdapter` regardless (§7.3). | 🟡 Does not touch the engine pin, but it reopens **D15** and can change the platform launch order. |
| **R3** | **`export_presets.cfg` cannot be fully committed.** The Team ID is account-specific and blank aborts the export with a hard error (§3). | The first CI run on a machine that is not the developer's. | Commit the preset with a placeholder and have the CI step write the real Team ID from a secret, the way the Android leg keeps the keystore out. Assert the preset is non-blank before exporting so the failure is legible. | 🟢 None — process. |
| **R4** | **The 4.7.1 iOS export template ships a broken simulator slice.** Engine issue [#118161](https://github.com/godotengine/godot/issues/118161) is **open**, filed 2026-04-03 against 4.6.2 as a regression from 4.6.1: the xcframework advertises arm64-simulator but contains only x86_64, so Xcode on Apple Silicon fails to link with undefined Godot symbols. Whether 4.7.1 carries the fix is **[U]**. | `xcodebuild` on an Apple-Silicon Mac or an arm64 `macos-15` runner failing with undefined `_err_print_error` / `Dictionary::Dictionary()`. Device builds are unaffected — this bites the **simulator**, which is also what the .NET publish always builds (§2). | Check `lipo -info` on the template's simulator slice before trusting a green build. If 4.7.1 is affected: build device-only, or take an Intel runner (`macos-15-intel`) for the simulator leg, or move the pin to whichever 4.7.x carries the fix. | 🟡 Could force a **point-release wait** — `16` O23's third option — but only for simulator builds. |
| **R5** | **The paid Apple Developer Program is an unbudgeted, unavoidable prerequisite.** Free-provisioning Personal Team profiles expire in **7 days**, cap at 3 devices and 10 App IDs. No signed build, no TestFlight, no App Store, no usable CI signing without the paid account. | The first attempt to produce anything installable. | Split the work as §4 does: stage 1 (unsigned, engine question) needs no account and answers O23. Buy the account only when a real device build is needed. | 🟢 None on the engine — it is a budget and calendar item. |
| **R6** | **`12` §3.2's iOS recipe is wrong in two places** and someone will follow it. Godot generates no Podfile and does not use CocoaPods (§1); `export_options.plist` is generated from the preset, not hand-authored (§3). | Somebody budgets time for a step that does not exist, or hand-edits a generated file that is deleted on the next export. | Amend `12` §3.2 at the M15 kickoff, pointing here. Keep the Podfile as a **committed template copied in by a script** (§4.3). | 🟢 None — documentation. |
| **R7** | **`mobile` renderer silently imposes an A12 device floor** (iPhone XS / XR and later) via the forced `iphone-ipad-minimum-performance-a12` capability. Exactly the Android leg's silent `minSdk 29`, in a different coat. | Nobody notices until the App Store listing shows a device list narrower than intended. | Decide the device-coverage target explicitly and record it, together with `application/min_ios_version` (default **15.0**) and the Android `minSdk 29`, as one product decision rather than three toolchain defaults. | 🟢 None — product. |
| **R8** | **CI cost.** macOS runners bill at a **10× minute multiplier** on private repositories. A cold export plus `xcodebuild archive` is minutes, not seconds, and the mono export templates alone are ~1.1 GB to fetch. | The first month's Actions bill. | Do not run `ios-export` on every push. Gate it on `main`/`milestone/**` and a manual dispatch, cache the export templates and NuGet, and keep the routine PR signal on the Linux jobs. Decide this when M7-10 turns the job on. | 🟢 None — cost. |
| **R9** | **The silent-no-.NET export** (§5). Same missing-`.sln` root cause as Android failure 5, different symptom: an Xcode project that builds and launches with no managed code in it. | Only by asserting on artefacts. Exit code 0 and a working app prove nothing. | §5's assertions, in the CI job, non-optional. Keep the committed `.sln` with `ExportDebug`/`ExportRelease` configurations — `dotnet new sln` is **not** sufficient (Android failure 5). | 🟢 None, if the assertion exists. Severe if it does not. |
| **R10** | **`xcode-select` points at the Command Line Tools.** Godot then invokes `clang` against a non-existent iPhone SDK path and dies with `MSB3073`. Documented by Godot as a known trap. | An `MSB3073` with a `/Library/Developer/CommandLineTools/...iPhoneOS.sdk` path in it. | `sudo xcode-select --switch /Applications/Xcode_<v>.app` (or set `DEVELOPER_DIR`) as an explicit CI step, and echo `xcode-select -p` into the log. | 🟢 None — one line. |

---

## 9. What a Mac must confirm first — an ordered checklist

Work top to bottom. Stop and write down the result of each. **Items 1–8 need no
Apple Developer account.** Budget: one sitting.

1. **Toolchain.** `xcode-select -p` prints an `Xcode.app` path; `xcodebuild -version`
   works; `dotnet --list-sdks` shows an 8.0.x that satisfies `global.json`;
   the Godot 4.7.1 **mono** editor runs and reports
   `4.7.1.stable.mono.official.a13da4feb`. Record the exact macOS editor archive
   filename — this document could not verify it (§2 **[U]**).
2. **Templates.** `ios.zip` is present in
   `~/Library/Application Support/Godot/export_templates/4.7.1.stable.mono/`.
   Then, before building anything: `lipo -info` the simulator slice inside it and
   settle **R4 / engine issue #118161** on 4.7.1. This is a two-minute check that
   can save a day of confused link errors.
3. **Does a fictitious Team ID get past the hard error?** Put a syntactically
   valid ten-character placeholder in `application/app_store_team_id` and run the
   export. If the project generates, the entire engine question is answerable
   with **no Apple relationship at all** — record that, because it changes what
   CI can do (§4.1 **[U]**).
4. **Stage-1 export.** `--headless --export-debug "iOS"` with
   `application/export_project_only=true` on the trivial C# spike project.
   Capture the **complete** console output. Expect the `ERROR:`-shaped-but-harmless
   lines the Android leg documented — **do not scrape logs for `ERROR`**.
5. **The .NET assertion (§5).** `<Assembly>_aot.xcframework` exists;
   `godot-publish-dotnet/ExportDebug-ios-arm64/<Assembly>.dylib` exists. **If
   either is missing, the export produced no .NET and everything after this is
   meaningless.** Record the exact paths — this document's step 2 and step 4
   assertions are **[U]** on layout and need their globs fixed here.
6. **Unsigned `xcodebuild`.** Does the generated project build with
   `CODE_SIGNING_ALLOWED=NO` (§4.1c)? Record whether Godot's generated scheme
   fights the flags. This is what makes a *non-vacuous* CI job possible without
   an account.
7. **Where the MSBuild log went.** The Android leg found C# build failures are
   invisible on stdout and land in `%APPDATA%\Godot\mono\build_logs\`. Find the
   macOS equivalent (`~/Library/Application Support/Godot/mono/build_logs/`) and
   confirm it, so the CI job can upload it. Without this, a managed build failure
   reads only as *"Failed to build project. Check MSBuild panel for details."*
8. **Timings and sizes.** Cold and warm export, cold and warm `xcodebuild`, and
   the on-disk size of the export template and the generated project. Feeds R8.
9. *(needs an account)* **Signed stage-2 export** (§4.2): keychain import,
   provisioning profile, `export_project_only=false`, and confirm Godot really
   drives `xcodebuild archive` → `-exportArchive` → `.ipa`. Check **which of the
   two provisioning-profile environment-variable spellings the 4.7.1 binary
   actually reads** (§3) — set both, then unset one and see which breaks.
10. *(needs an account, needs a device)* **Run it.** Boot on a real A12+ device
    and confirm the managed code executes — the Android leg's `[O23] RESULT:`
    marker line, on iOS. **This is the only thing that answers R1**, and R1 is
    the risk that can still reopen D1.
11. *(needs §7 resolved)* **The ad build.** Rebuild the MAX plugin from 4.7.1
    headers; does it link, and does `initialize()` fire? One day, and it decides
    §7.4 A vs B.

When items 1–8 are done, this document should be rewritten with the **[U]** rows
replaced by observations, and `docs/spikes/README.md` updated. When item 10 is
done, and only then, O23 can be closed.

---

## 10. Sources

Engine, at the **4.7.1-stable** tag unless noted:

- [`editor/export/editor_export_platform_apple_embedded.cpp`](https://github.com/godotengine/godot/blob/4.7.1-stable/editor/export/editor_export_platform_apple_embedded.cpp) — the macOS-only refusal, the Team ID hard error, the export options and their defaults, the ETC2/ASTC check, the `xcodebuild archive`/`-exportArchive` calls, the plugin discovery.
- [`platform/ios/export/export_plugin.h`](https://github.com/godotengine/godot/blob/4.7.1-stable/platform/ios/export/export_plugin.h) / [`.cpp`](https://github.com/godotengine/godot/blob/4.7.1-stable/platform/ios/export/export_plugin.cpp) — `get_minimum_deployment_target() == "15.0"`, `targeted_device_family`, the Metal/iOS-14 rule, `IPHONEOS_DEPLOYMENT_TARGET`.
- [`platform/ios/doc_classes/EditorExportPlatformIOS.xml`](https://github.com/godotengine/godot/blob/4.7.1-stable/platform/ios/doc_classes/EditorExportPlatformIOS.xml) — every `application/*` option, and the `GODOT_APPLE_PLATFORM_*` environment overrides.
- [`modules/mono/editor/GodotTools/GodotTools/Export/ExportPlugin.cs`](https://github.com/godotengine/godot/blob/4.7.1-stable/modules/mono/editor/GodotTools/GodotTools/Export/ExportPlugin.cs) — `ProjectContainsDotNet()`, the iOS NativeAOT publish, `godot-publish-dotnet/`, `<Assembly>_aot.xcframework`, the simulator target, the `.dat` bundle-file rule.

Documentation and releases:

- [Exporting for iOS (4.7)](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_ios.html) — Requirements, Team ID/Bundle ID, the generated `.xcodeproj`, the `xcode-select` troubleshooting section, the environment-variable table, the simulator/Compatibility-renderer warning.
- [C#/.NET (4.7)](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/index.html) — *"iOS support is currently experimental and has a few limitations."*
- [Creating iOS plugins (4.7)](https://docs.godotengine.org/en/4.7/tutorials/platform/ios/ios_plugin.html) — `res://ios/plugins/`, `.gdip`, the same-headers requirement.
- [*Current state of C# platform support in Godot 4.2*](https://godotengine.org/article/platform-state-in-csharp-for-godot-4-2/), 2024-01-26 — NativeAOT, trimming, reflection.
- Engine PR [#82729](https://github.com/godotengine/godot/pull/82729) — "Add C# iOS support".
- Engine issues [#118161](https://github.com/godotengine/godot/issues/118161), [#96072](https://github.com/godotengine/godot/issues/96072), [#100123](https://github.com/godotengine/godot/issues/100123).
- [Godot 4.7.1-stable release](https://github.com/godotengine/godot-builds/releases/tag/4.7.1-stable), 2026-07-14.

AppLovin MAX Godot:

- [Repository](https://github.com/AppLovin/AppLovin-MAX-Godot) and issues [#59](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/59), [#60](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/60), [#61](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/61), [#66](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/66), [#67](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/67).
- Godot forum: [4.5 iOS undefined symbols](https://forum.godotengine.org/t/godot-4-5-ios-export-gives-undefined-symbols-errors-in-xcode-due-to-not-updated-plugin/125054) (2025-10-14), [iOS plugins lack a maintainer](https://forum.godotengine.org/t/its-official-godot-ios-plugin-obsolete/130507) (2026-01).
- Alternatives: [`poingstudios/godot-admob-plugin`](https://github.com/poingstudios/godot-admob-plugin) (v5.0.0, 2026-07-21), [`godot-sdk-integrations/godot-admob`](https://github.com/godot-sdk-integrations/godot-admob) (GDScript only).

CI and Apple:

- [`actions/runner-images`](https://github.com/actions/runner-images) — `macos-15` GA, `macos-14` deprecated; [macos-15-arm64 image manifest](https://github.com/actions/runner-images/blob/main/images/macos/macos-15-arm64-Readme.md).
- [GitHub Actions billing](https://docs.github.com/en/actions/concepts/billing-and-usage) — the 10× macOS minute multiplier.
- [Choosing a membership](https://developer.apple.com/support/compare-memberships/) and the free-provisioning limits (7-day profiles, 3 devices, 10 App IDs).

In-repo:

- [`O23-godot-android-export.md`](O23-godot-android-export.md) — M0-05a, the executed Android leg.
- [`O14-applovin-max-s2s.md`](O14-applovin-max-s2s.md) — M0-04, the S2S verdict and the first abandonment finding.
