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

1. **Does a headless CLI iOS export work at all?** Not rhetorical — engine issue
   [#76749](https://github.com/godotengine/godot/issues/76749), *"Cannot build in
   headless mode for iOS"*, has been **open since 2023-05-05** with the export
   failing on CI while succeeding locally, and offering no diagnostic beyond
   `ERROR: Project export for preset '…' failed.` (§8 R3.) Everything else in
   this document assumes this works.
2. Does the export produce `<Assembly>_aot.xcframework`, i.e. is there actually
   .NET in the build? (§5)
3. Does the NativeAOT publish survive **our** code — the composition root, the
   JSON content pipeline, `Core`'s reflection-free promise? (§8 R1 — the risk
   most likely to force a `16` O23 fallback.)
4. Is engine issue [#118161](https://github.com/godotengine/godot/issues/118161)
   (broken arm64 simulator slice, open, 4.6.2) still present at 4.7.1? Two
   minutes with `lipo -info`. (§8 R5)
5. Does the AppLovin MAX plugin link against 4.7.1 iOS headers when rebuilt from
   source, and does its SDK actually initialise? (§7)
6. What is the real wall-clock time of a cold export + `xcodebuild archive`, i.e.
   what does the `ios-export` CI job cost at the 10× macOS minute multiplier? (§8 R9)

Items 1–4 need **no Apple Developer account** — see §4.1. Items 1 and 2 do not
even need a device. That is the cheapest, highest-value half-day available to
this project, and it should be bought the day a Mac exists.

---

## 1. Verdict — as far as it can be taken without a Mac

**Four things are settled, and they were settled by reading the engine, not by guessing.**

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
   `has_valid_export_configuration`, 4.7.1-stable. **[S]** There is no flag, no
   env var and no headless mode that gets around it. The `ios-export` CI job must
   run on a `macos-*` runner or not exist.

2. ✅ **Godot 4.7.1 does support C#/.NET on iOS, and the whole path is
   scriptable — in fact it produces an `.ipa` by default.** `export_project_only`
   defaults to `false`, and `get_binary_extensions()` returns `ipa` in that case;
   the exporter shells out to `xcodebuild archive` and `xcodebuild -exportArchive`
   itself. **[S]** (§4, §5)

3. ⚠️ **It is labelled experimental, and on iOS "experimental" means
   NativeAOT.** Godot's own words: *"iOS support is currently experimental and
   has a few limitations"* (docs) and *"Exporting to an Apple Embedded platform
   when using C#/.NET is experimental"* (the warning the export itself prints).
   `Godot.NET.Sdk/Sdk/iOS.props` at 4.7.1-stable is, in its entirety,
   `PublishAot`, `PublishAotUsingRuntimePack`, `UseNativeAOTRuntime` and
   `TrimmerSingleWarn=false`. **[S]** iOS is the only platform where our managed
   code is **ahead-of-time compiled and trimmed** rather than JIT'd on Mono — a
   materially different runtime from the one every test, the economy simulator,
   the server and the Android build exercise. **This, not the build plumbing, is
   the real O23 risk on iOS.** (§8 R1)

4. 🔴 **There is an unfixed, closed-as-not-planned crash report against our exact
   pin.** [#121736](https://github.com/godotengine/godot/issues/121736),
   *"[.NET / iOS 26] C# game crashes ~0.3s after launch in RELEASE builds"* —
   filed 2026-07-24 against **`4.7.1.stable.mono.official`** and 4.6.2, closed
   **as not planned** on 2026-08-05. Release builds only, C# only (GDScript
   unaffected), `EXC_BREAKPOINT` in iOS 26's type-aware allocator during render
   target creation, before game logic runs. **[S]** See §8 R2 for the important
   qualifications — it is not established that this would hit us — but a
   dismissed .NET-only iOS crash on the pinned version is exactly the class of
   fact O23 exists to surface in week 1.

**What is not settled:** whether headless CLI export works on a runner at all;
whether *our* game survives NativeAOT; whether the ad SDK can be made to build
(§7 — the answer there is bad); and every runtime behaviour, because nothing ran.

### The one place `12` §3.2 is wrong

> | iOS | Export project → author a `Podfile` → `pod install --repo-update` → build in Xcode. App Store Team ID and Bundle Identifier required at export time. |

The **Team ID / Bundle Identifier** half is exactly right and is enforced by a
hard error (§3). The **CocoaPods** half is misattributed. Godot 4.7 never
generates a `Podfile`, never runs `pod install`, and its iOS plugin format has no
CocoaPods key at all — a grep for `Podfile|cocoapod|pod install|\.podspec` across
the iOS export plugin, the Apple-embedded export platform and the mono export
plugin at 4.7.1-stable returns **zero matches**, and neither the iOS export nor
the iOS plugin documentation page mentions CocoaPods. **[S]** CocoaPods enters
solely because **AppLovin's own integration instructions** tell you to
hand-author a `Podfile` next to the generated `.xcodeproj` and run
`pod install --repo-update`.

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
| **Godot** | **4.7.1-stable, .NET/Mono build**, released **2026-07-14** (4.7-stable was 2026-06-18) | **[S]** [maintenance release announcement](https://godotengine.org/article/maintenance-release-godot-4-7-1/), [download archive](https://godotengine.org/download/archive/). Tag `4.7.1-stable` → commit `a13da4feb8d8aefc283c3763d33a2f170a18d541`, whose short form `a13da4feb` **matches the build hash the Android leg recorded** (`4.7.1.stable.mono.official.a13da4feb`). The pin is real and the two legs are on the same engine. |
| Newer builds in the archive | `4.7.2-rc1` (2026-08-03), `4.8-dev3` (2026-08-07) | **[S]** relevant only as a place a fix for §8 R5 might land |
| Export templates | `Godot_v4.7.1-stable_mono_export_templates.tpz` → `4.7.1.stable.mono/`. The exporter looks for **`ios.zip`** inside it. | **[S]** same `.tpz` the Android leg used. **Must be the mono templates.** |
| macOS Godot editor archive | `Godot_v4.7.1-stable_mono_macos.universal.zip` — universal (arm64 + x86_64), self-contained, needs the .NET SDK installed separately | **[S]** [godotengine.org/download/macos](https://godotengine.org/download/macos/) |
| **Host OS** | **macOS, mandatory** | **[S]** engine source, §1.1. Also docs: *"You must export for iOS from a computer running macOS with Xcode installed."* |
| **Minimum Xcode** | 🔴 **No such number is published.** The Requirements section says only *"with Xcode installed"*; the compiling-for-iOS page lists Python 3.9+, SCons 4.4+ and "Xcode" with no version; the system-requirements page has no Xcode row. | **[U] — sourced negative.** Godot publishes no Xcode compatibility matrix. The practical floor is whatever Xcode the export template's `.xcframework` slices were built against, and that is exactly what breaks (§8 R5). |
| **Minimum iOS deployment target** | **15.0** — the default of `application/min_ios_version`. **Bumped in 4.7**: both 4.5-stable and 4.6-stable returned `"14.0"`. | **[S]** `EditorExportPlatformIOS::get_minimum_deployment_target()`, `platform/ios/export/export_plugin.h` @ 4.7.1-stable. It is a **preset option**, so it is ours to raise, not to discover. |
| ⚠️ Deployment-target mismatch | The .NET ILC step injects `-miphoneos-version-min=12.0` / `-mios-simulator-version-min=12.0`, i.e. the managed half is built against **12.0** while the preset default is **15.0**. | **[S]** `Godot.NET.Sdk/Sdk/iOS.targets`. Harmless in principle (lower floor), but worth knowing when reading linker warnings. |
| Renderer constraint | Metal + `forward_plus`/`mobile` requires `min_ios_version` ≥ **14.0** (already satisfied by the 15.0 default) | **[S]** `EditorExportPlatformIOS::has_valid_export_configuration` |
| **Device floor implied by our renderer** | 🔴 **Apple A12 or newer** — iPhone XS / XR / iPad mini 5 and later | **[S]** the exporter force-adds the `iphone-ipad-minimum-performance-a12` required-device-capability when `rendering/renderer/rendering_method.mobile` is `mobile` or `forward_plus`. **This is the iOS twin of the Android leg's silent `minSdk 29`** — a product decision made by a toolchain default. Our spike project uses the `mobile` renderer, so it applies. |
| iOS architecture | **`arm64` only.** `_get_supported_architectures()` pushes exactly one entry, so `architectures/arm64` is the only architecture key. | **[S]** |
| Simulator | The engine's simulator template is **x64 only** (docs), but the .NET half builds **both** `iossimulator-arm64` and `iossimulator-x64` and `lipo`s them together. The two halves disagree, which is the shape of engine issue #118161. **The simulator also only supports the `Compatibility` renderer**, so a simulator run does not exercise our `mobile` renderer. | **[S]** |
| **CocoaPods** | **Not required by Godot.** No minimum version exists because the engine never invokes it. Required only by AppLovin's integration instructions (§7). GitHub `macos-15` and `macos-26` both ship CocoaPods **1.17.0** preinstalled. | **[S]** |
| **.NET target framework** | **`net8.0`.** Godot 4.7.1's project generator uses `net8.0` and overrides to `net9.0` **only for Android**. Our solution-wide `net8.0` pin is correct for iOS. | **[S]** `GodotTools.ProjectEditor/ProjectGenerator.cs` (`GodotMinimumRequiredTfm => "net8.0"`). The 4.7 docs prose (*"Godot 4.5 requires .NET 8 or later, but exporting to Android requires .NET 9 or later"*) is stale wording with correct substance. |
| **.NET runtime on iOS** | **NativeAOT / ILC**, with trimming on. Not Mono, not an interpreter. | **[S]** `Godot.NET.Sdk/Sdk/iOS.props`; [*Current state of C# platform support in Godot 4.2*](https://godotengine.org/article/platform-state-in-csharp-for-godot-4-2/), 2024-01-26; engine PR [#82729](https://github.com/godotengine/godot/pull/82729) (merged 2023-10-09, milestone 4.2), fixed up by [#84945](https://github.com/godotengine/godot/pull/84945) (2023-11-16) which introduced the `_aot` xcframework suffix and the `.xcarchive`/`.ipa` path. |
| **Experimental?** | **Yes, explicitly.** *"Projects written in C# can be exported to iOS as of Godot 4.2, but support is experimental and some limitations apply."* / *"iOS support is currently experimental and has a few limitations."* | **[S]** |
| CI runner | **`macos-15`** (arm64, GA). `macos-14` is **deprecated** and fully unsupported from **2026-11-02**. Never `macos-latest` — its Xcode default already moved once in 2026. | **[S]** [runner-images](https://github.com/actions/runner-images) |
| Runner Xcode | `macos-15` (image `20260727.0256.1`, OS 15.7.7): **16.4 default**, plus 16.0–16.3 and 26.0.1–26.3. `macos-26` (OS 26.5.2): **26.6 default**, and **no Xcode 16 at all**. | **[S]** per-image manifests. **Pin `macos-15` and `xcode-select` explicitly** — it is the only runner that still offers an Xcode 16 line, and Xcode 26 has already broken Godot iOS export twice (#118543, #111213). |
| Runner .NET SDKs | 8.0.101 / 8.0.204 / 8.0.303 / **8.0.423** / 9.x / 10.x. Our `global.json` pin (`8.0.319`, `rollForward: latestFeature`) is satisfied by **8.0.423**. | **[S]** |

### Known open engine issues for C# on iOS at 4.4 → 4.7

Ordered by how directly they hit a 4.7.1 pin.

| Issue | Title | Opened | State | Versions | Why it lands on us |
|---|---|---|---|---|---|
| 🔴 [#121736](https://github.com/godotengine/godot/issues/121736) | *[.NET / iOS 26] C# game crashes ~0.3s after launch in RELEASE builds — RenderingDevice heap corruption trapped by iOS 26's type-aware allocator* | 2026-07-24 | **closed as not planned** (2026-08-05) | **4.7.1.stable.mono**, 4.6.2 | `EXC_BREAKPOINT (SIGTRAP)` 0.3–5 s after launch on iPhone 16 Pro / iOS 26.5.2, during render-target creation, before game logic. **Release only; debug runs clean. C# only — GDScript unaffected.** Dismissed without a fix. See §8 R2 for why this may or may not be ours. |
| 🔴 [#76749](https://github.com/godotengine/godot/issues/76749) | *Cannot build in headless mode for iOS* | 2023-05-05 | **open** | 4.0.2 | Headless `--export-release` fails on a CI server while the same export succeeds locally, with no diagnostic beyond `ERROR: Project export for preset '…' failed.` and a suspicious `NULL cString`. Reporter's workaround: run `--export-pack` first to force a reimport. **This is the assumption the entire recipe rests on, and it is unverified at 4.7.** |
| [#111213](https://github.com/godotengine/godot/issues/111213) | *error code 0 when exporting to ios (xcode 26, godot 4.5)* | 2025-10-03 | **open** | 4.5 | `"failed to run xcodebuild with code 0"` during archive. Part of why the CI job pins Xcode 16.4 rather than the runner default. |
| [#104118](https://github.com/godotengine/godot/issues/104118) | *`RendererDummy::MaterialStorage::DummyShader` leak on headless export to iOS* | 2025-03-14 | **open** | 4.4 regression | Leaked RID at exit — and it **only reproduces on GitHub Actions macOS runners**, not on physical Macs. A local Mac proving the recipe does not prove the CI job. |
| [#118161](https://github.com/godotengine/godot/issues/118161) | *iOS 4.6.2 export template ships broken arm64 simulator xcframework slice on Apple Silicon* | 2026-04-03 | **open** | broken 4.6.2, OK 4.6.1 | The xcframework advertises `ios-arm64_x86_64-simulator` but `libgodot.a` is x86_64 only, so Xcode on Apple Silicon fails to link with undefined Godot C++ symbols. **Whether 4.7.1 carries it is [U]** — Mac-day check #4. |
| [#116165](https://github.com/godotengine/godot/issues/116165) | *Unable to run C# applications on iOS due to code signing identifier mismatch* | 2026-02-11 | **open** | 4.5.1, 4.6.0 | Install fails because the code-signing identifier is `csharp-ios-signing` rather than the bundle id. Workaround is a manual re-sign of the framework. C#-specific. |
| [#110052](https://github.com/godotengine/godot/issues/110052) | *iOS export: automatic signing doesn't work* | 2025-08-28 | **open** | 4.4.1, 4.5, 4.5.1 | Setting *any* signing field flips the project to Manual signing — which matches the engine logic exactly (§3.1). |
| [#96072](https://github.com/godotengine/godot/issues/96072) | *Node Instantiation Fails with NativeAOT Enabled in Export Release Mode* | 2024-08-25 | **open** | 4.2/4.3 mono | `[Export]`ed `Resource` properties produce out-of-bounds property indices in iOS release AOT builds. Workaround: use `ResourceLoader.Load()` instead. **The trimming/reflection risk, made concrete.** |
| [#115715](https://github.com/godotengine/godot/issues/115715) | *Can't set `<IlcDisableReflection>true</IlcDisableReflection>` for C# Android/iOS* | 2026-02-01 | **open** | 4.5 | 75 MB → 11 MB binary, but the mono plugin fails to initialise on iOS. Relevant if IPA size becomes a problem. |
| [#118386](https://github.com/godotengine/godot/issues/118386) | *Export PKG/ZIP doesn't compile C# AOT module* | 2026-04-10 | **open** | 4.6.2 mono | `--export-pack` publishes .NET but does not regenerate AOT, leaving stale native code. Interacts badly with #76749's `--export-pack`-first workaround. |
| [#122265](https://github.com/godotengine/godot/issues/122265) | *iOS exporter silently fails if `targeted_device` wrongly set in `export_presets.cfg`* | 2026-08-10 | **open** | — | Another preset-driven silent failure. Reinforces §5: assert on artefacts. |
| [#100123](https://github.com/godotengine/godot/issues/100123) → [#100187](https://github.com/godotengine/godot/pull/100187) | *Export Error with .NET 9 on iOS* (`MSB3030: icudt.dat` missing) | 2024-11-04 | **fixed in 4.5+** | broken ≤ 4.4 | Historical, but it establishes that the iOS path is sensitive to the .NET SDK band. Keep the `global.json` pin. |
| [#86019](https://github.com/godotengine/godot/issues/86019) | *Linking .NET Godot project files into iOS Xcode project breaks build* | 2023-12-11 | **open** | — | The docs' own "Active development considerations" workflow (drag the Godot project into Xcode) breaks .NET builds. **Do not use it.** |

**[U] — negative results worth recording:** no issue exists for **.NET 10 + iOS**;
no open issue about `lipo` failing during a C#/iOS export in 4.4–4.7; and nothing
is filed against **4.7.x specifically** beyond #121736. Absence of reports for an
engine released four weeks ago is not evidence of absence.

---

## 3. Where every input is supplied — the Android leg's finding #3, applied to iOS

This is the highest-value section of the document. The Android leg's worst
discovery was that **Godot ignores `ANDROID_HOME` and reads the SDK/JDK/keystore
paths out of a per-machine `editor_settings-4.7.tres` that is not in the repo.**

**iOS reads nothing signing-related from editor settings at all.** The complete
set of iOS editor settings registered at 4.7.1-stable is:

```cpp
#ifdef MACOS_ENABLED
    EDITOR_DEF("export/ios/ios_deploy", "");
    ...PROPERTY_HINT_GLOBAL_FILE...
#endif
```

— `platform/ios/export/export.cpp`, `register_ios_exporter()`. **[S]** That one
setting is a path to the third-party `ios-deploy` binary, used purely to
enumerate physical devices for one-click deploy. It plays **no role in export or
signing**. A repo-wide grep of `EDITOR_DEF|EDITOR_GET|EditorSettings` across the
Apple-embedded export plugin returns exactly that one hit. **The Android
`editor_settings-*.tres` trap has no iOS twin.**

**But there is a different out-of-repo file, and it is the one that will bite CI.**

| Input | Where it comes from | Notes |
|---|---|---|
| **Xcode / iOS SDK location** | **`xcode-select -p` / `DEVELOPER_DIR`**, i.e. macOS-global state | **[S]** No editor setting, no Godot env var. If it points at `/Library/Developer/CommandLineTools` instead of `/Applications/Xcode.app/Contents/Developer`, the export dies inside `clang` with `MSB3073`; the docs' own troubleshooting section is about exactly this. The .NET ILC step separately runs `xcrun xcode-select -p` to find its SDKs. **CI must set this explicitly.** |
| **App Store Team ID** | 🔴 **Export preset only** — `application/app_store_team_id`. No environment override exists. | **[S]** `ERR_FAIL_COND_V_MSG(team_id.length() == 0, ERR_CANT_OPEN, "App Store Team ID not specified - cannot configure the project.")`. Registered with the `required` flag. Ten characters, e.g. `ABCDE12XYZ`. Godot does **not** validate the length — a malformed value surfaces much later as `JSON text did not start with array or object…`. |
| **Bundle identifier** | **Export preset only** — `application/bundle_identifier`, `required`. Ours: `de.ludwigso.slayidlerepeat`. | **[S]** |
| **Signing identity** | Export preset — `application/code_sign_identity_debug` / `_release`. **Empty means `"Apple Development"` / `"Apple Distribution"`**, not "unsigned". No environment override. | **[S]** The identity itself must be a certificate **in the runner's keychain**; `_codesign` runs plain `codesign -f -s <identity>` over `<binary_dir>/dylibs`. Godot has no keychain settings of its own. |
| 🔴 **Provisioning profile UUID** | **`res://.godot/export_credentials.cfg` — NOT `export_presets.cfg`.** Both `application/provisioning_profile_uuid_debug` and `_release` carry `PROPERTY_USAGE_SECRET`, and `EditorExport::save_presets()` routes every `PROPERTY_USAGE_SECRET` option into the credentials file instead of the committed preset. | **[S]** `editor/export/editor_export.cpp`. Docs: *"`.godot/export_credentials.cfg`: This file contains export options that are considered confidential… It should generally **not** be committed to version control"* and *"Since the credentials file is usually kept out of version control systems, some export options will be missing if you clone the project to a new machine."* **`.godot/` is gitignored in this repo, so on a fresh CI checkout these are empty. This is the iOS analogue of the Android editor-settings trap.** |
| **Provisioning profile specifier** | Export preset — `application/provisioning_profile_specifier_debug` / `_release`. **Not** secret, so these *are* committed. | **[S]** |
| **Export method** | Export preset — `application/export_method_debug` (default **1 = Development**) / `_release` (default **0 = App Store**). Enum: `App Store, Development, Ad-Hoc, Enterprise`. | **[S]** |
| **`export_options.plist`** | 🔴 **Generated by Godot, not authored by us.** Templated out of `ios.zip` and filled from the preset via `$team_id`, `$export_method`, `$provisioning_profile_*`, `$code_sign_identity_*`, `$code_sign_style_*`, then handed to `xcodebuild -exportArchive -exportOptionsPlist`. | **[S]** `12` §3.2's mental model of hand-authoring iOS build config is wrong here too: **the preset is the source of truth and the plist is derived.** Do not hand-edit it; it is regenerated. |
| **Deployment target** | Export preset — `application/min_ios_version`, default `15.0` → `IPHONEOS_DEPLOYMENT_TARGET`. | **[S]** |
| **Targeted device family** | Export preset — `application/targeted_device_family`, default **2 (iPhone & iPad)**. Get it wrong and the exporter fails silently (#122265). | **[S]** |
| **Ad plugin enable flag** | Export preset — one `plugins/<PluginName>` boolean per `.gdip` discovered under `res://ios/plugins/`. | **[S]** |
| **Script encryption key** | Credentials file, or environment `GODOT_SCRIPT_ENCRYPTION_KEY`. | **[S]** Not used by us today. |
| **Keychain / certificates / installed profiles** | **Runner state.** Import a `.p12` into a temporary keychain; put profiles in `~/Library/MobileDevice/Provisioning Profiles/`, or rely on `-allowProvisioningUpdates`. | **[U]** — standard iOS CI practice, unexercised here. |

### 3.1 The environment overrides — and two landmines in them

Only the four provisioning options have environment overrides, and **environment
wins over the preset** (`EditorExportPreset::get_or_env` returns the env value
whenever it is non-empty). **[S]**

```cpp
const String ENV_APPLE_PLATFORM_PROFILE_UUID_DEBUG        = "GODOT_APPLE_PLATFORM_PROVISIONING_PROFILE_UUID_DEBUG";
const String ENV_APPLE_PLATFORM_PROFILE_UUID_RELEASE      = "GODOT_APPLE_PLATFORM_PROVISIONING_PROFILE_UUID_RELEASE";
const String ENV_APPLE_PLATFORM_PROFILE_SPECIFIER_DEBUG   = "GODOT_APPLE_PLATFORM_PROFILE_SPECIFIER_DEBUG";
const String ENV_APPLE_PLATFORM_PROFILE_SPECIFIER_RELEASE = "GODOT_APPLE_PLATFORM_PROFILE_SPECIFIER_RELEASE";
```

— `editor/export/editor_export_platform_apple_embedded.h` @ 4.7.1-stable. **[S]**

**Landmine 1 — the documentation publishes names that do not exist.** The 4.7
*Exporting for iOS* tutorial page's environment-variable table still lists
`GODOT_IOS_PROVISIONING_PROFILE_UUID_DEBUG` / `_RELEASE`. **Those strings appear
nowhere in the 4.7.1 source**, and the table omits the two `PROFILE_SPECIFIER`
variables entirely. The class reference has the correct `GODOT_APPLE_PLATFORM_*`
names. **Use `GODOT_APPLE_PLATFORM_*`.** Exporting the old names too costs
nothing and guards against a future rename, but do not rely on them.

**Landmine 2 — 🔴 an engine bug: the RELEASE profile-UUID environment variable is
never read.** In the `CodeSigningDetails` constructor:

```cpp
debug_provisioning_profile_uuid   = p_preset->get_or_env("application/provisioning_profile_uuid_debug",
                                                          ENV_APPLE_PLATFORM_PROFILE_UUID_DEBUG);
release_provisioning_profile_uuid = p_preset->get_or_env("application/provisioning_profile_uuid_release",
                                                          ENV_APPLE_PLATFORM_PROFILE_UUID_DEBUG);  // <-- DEBUG constant
```

`ENV_APPLE_PLATFORM_PROFILE_UUID_RELEASE` is declared and dead. **[S]** Present
in 4.6-stable, 4.7-stable, 4.7.1-stable and `master`; 4.4-stable was correct, so
it was introduced in the 4.6 cycle. **[U]** No issue appears to be filed for it.

**Consequence for CI:** setting `GODOT_APPLE_PLATFORM_PROVISIONING_PROFILE_UUID_RELEASE`
silently does nothing, and if the DEBUG variable is also set, a **release** build
picks up the **debug** profile UUID. For release builds, supply the UUID through
`.godot/export_credentials.cfg` or use `GODOT_APPLE_PLATFORM_PROFILE_SPECIFIER_RELEASE`
(which is wired correctly). Filing this upstream is a cheap contribution.

**Landmine 3 — automatic vs manual signing is inferred, never chosen.**

```cpp
debug_manual_signing = !debug_provisioning_profile_uuid.is_empty()
                    || (debug_signing_identity != "Apple Development" && debug_signing_identity != "Apple Distribution");
debug_manual_signing |= !debug_provisioning_profile_specifier.is_empty();
```

**[S]** Leave UUID, specifier and identity all empty and you get
`CODE_SIGN_STYLE = Automatic`; set *any one* of them and you get `Manual` and
must then supply all of them. That is exactly open issue
[#110052](https://github.com/godotengine/godot/issues/110052). **Decide
automatic-or-manual deliberately and set the three fields as a set.**

### 3.2 The one-line summary

On Android the CI-hostile state was a per-machine **editor settings file**. On
iOS the export reads **no editor settings at all** — the state is the **committed
export preset**, the **gitignored `.godot/export_credentials.cfg`** (provisioning
UUIDs only), the **runner's keychain**, and **`xcode-select`**. Everything except
the credentials file and the keychain can be committed, which is a genuine
improvement over Android. The Team ID is the exception on policy grounds, not
technical ones: it is account-specific, so it should be injected by the CI step
the same way the Android leg keeps the keystore out of the preset.

---

## 4. The recipe

Copy-pasteable, and **untested**. Treat every command as a hypothesis. Run it in
the order given; the assertions in §5 are not optional.

### 4.0 One-time toolchain setup (macOS)

```bash
# 1. Xcode. On a GitHub macos-15 runner it is preinstalled -- select it EXPLICITLY.
#    Do not inherit the runner default and do not use macos-latest (§2).
sudo xcode-select --switch /Applications/Xcode_16.4.app
xcodebuild -version
xcode-select -p          # must print .../Xcode.app/Contents/Developer, NOT CommandLineTools

# 2. .NET SDK -- pinned by the repo's global.json (8.0.x). Do not let a preview win.
dotnet --list-sdks

# 3. Godot 4.7.1 MONO editor + MONO export templates.
#    Editor:    Godot_v4.7.1-stable_mono_macos.universal.zip     [S] universal; needs the .NET SDK separately
#    Templates: Godot_v4.7.1-stable_mono_export_templates.tpz    [S] same file the Android leg used
#    Unpack the .tpz and copy the CONTENTS of its templates/ folder into:
#      ~/Library/Application Support/Godot/export_templates/4.7.1.stable.mono/
#    The exporter looks for ios.zip in there.
GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot

# 4. ASSERT THE EDITOR IS THE .NET FLAVOUR. A non-mono editor exports iOS happily
#    and silently produces a build with no managed code, warning about nothing (§5).
"$GODOT" --version    # must contain ".mono", e.g. 4.7.1.stable.mono.official.a13da4feb

# 5. CocoaPods -- ONLY needed once the AppLovin plugin is in play (§7). Not for a base build.
pod --version    # 1.17.0 on macos-15
```

### 4.1 Stage 1 — the export that needs **no Apple Developer account**

This is the part to run first, and it answers O23's engine question on its own.

🔴 **Set `application/export_project_only=true` in the preset before running
this.** It defaults to **`false`**, and with the default Godot drives
`xcodebuild archive` + `-exportArchive` itself and produces an `.ipa` — which
needs working signing at Godot-export time. `get_binary_extensions()` returns
`xcodeproj` only when `export_project_only` is true, and the class reference
recommends exactly this setting *"when combining Godot with Fastlane or other
build pipelines"*. **[S]** The CLI does **not** validate the output extension for
a project export, so naming the file `.xcodeproj` while leaving
`export_project_only=false` silently produces the other artefact type.

```bash
PROJ=spikes/godot-ios-export        # a Godot C# project; see §5 for what it must contain
OUT=$PWD/build/ios

# a. Import assets. #76749's reporter needed this (as --export-pack) to make a
#    headless export work at all; do it unconditionally.
"$GODOT" --headless --path "$PROJ" --import

# b. Export ONLY the Xcode project. No xcodebuild, no signing, no account.
mkdir -p "$OUT"                      # dest_dir MUST already exist -- Godot will not create it
"$GODOT" --headless --path "$PROJ" --export-debug "iOS" "$OUT/SlayIdleRepeatSpike.xcodeproj"
```

The path argument is split as `dest_dir = <base dir>` and
`binary_name = <filename without extension>`. **[S]** So the above writes
`build/ios/SlayIdleRepeatSpike.xcodeproj/` **and** a sibling
`build/ios/SlayIdleRepeatSpike/`. The exporter's own leftover-detection lists what
it expects to find in that sibling directory, which is the best available
description of the output: `<name>-Info.plist`, `<name>.entitlements`,
`Launch Screen.storyboard`, `export_options.plist`, `dummy.*`, `*.gdip`,
`dylibs/`, `Images.xcassets/`, `*.lproj/`, `godot-publish-dotnet/` and
`*.xcframework` / `*.framework`. **[S]**

⚠️ **`application/app_store_team_id` must still be non-empty** even here — the
export aborts on a blank Team ID before it writes anything (§3). A **[U]**
question worth two minutes on the Mac: does a syntactically valid but fictitious
ten-character Team ID get far enough to generate the project? Godot itself only
checks for non-emptiness, so it probably does — and if so, **stage 1 needs no
Apple relationship whatsoever**, which is exactly what makes a non-vacuous CI job
possible before anyone buys an account.

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

**[S]** unsigned `xcodebuild` builds are standard CI practice; **[U]** that a
Godot-generated project tolerates them, because the generated scheme may carry
signing settings the flags do not fully override.

### 4.2 Stage 2 — the signed export (needs an Apple Developer account)

```bash
# Preset carries: app_store_team_id, bundle_identifier, code_sign_identity_*,
#                 export_method_*, provisioning_profile_specifier_*,
#                 export_project_only=false
# Environment carries the DEBUG provisioning profile UUID. Note the RELEASE
# counterpart is silently ignored by a 4.6+ engine bug -- see 3.1 landmine 2.
export GODOT_APPLE_PLATFORM_PROVISIONING_PROFILE_UUID_DEBUG="$PROFILE_UUID"
export GODOT_APPLE_PLATFORM_PROFILE_SPECIFIER_RELEASE="$PROFILE_NAME"   # release path

# Import the signing certificate into a throwaway keychain first (not shown).

"$GODOT" --headless --path "$PROJ" --export-debug "iOS" "$OUT/SlayIdleRepeatSpike.ipa"
# With export_project_only=false Godot itself now runs, in order:
#   xcodebuild -project ... -scheme ... -sdk iphoneos -configuration Debug \
#              -destination generic/platform=ios archive -allowProvisioningUpdates \
#              -archivePath <name>.xcarchive
#     success detected by scraping stdout for "** ARCHIVE SUCCEEDED **"
#   xcodebuild -exportArchive -archivePath ... \
#              -exportOptionsPlist <binary_dir>/export_options.plist \
#              -allowProvisioningUpdates -exportPath <dest_dir>
#     success detected by scraping stdout for "** EXPORT SUCCEEDED **"
```

**[S]** both invocations, the `-allowProvisioningUpdates` flags, the generated
`export_options.plist` and the stdout-scraping success detection are read
directly out of `editor_export_platform_apple_embedded.cpp` at 4.7.1-stable.

**A free Apple ID will not do for CI.** Personal-Team ("free provisioning")
profiles expire after **7 days**, cap at **3 devices** and **10 App IDs**. Fine
for one developer poking at a device; useless for a build server. Stage 2 means
the **paid Apple Developer Program**, with a real annual cost this project has
not incurred.

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
`.xcworkspace`, **not** the `.xcodeproj` — but Godot's own
`export_project_only=false` path drives the `.xcodeproj`. **Stage 3 and Godot's
built-in archive step are mutually exclusive.** With ads in the build,
`export_project_only` must be `true` and CI owns the `xcodebuild` calls. `12`
§3.2's "build in Xcode" is doing a lot of quiet work in that sentence.

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

and `_ExportBeginImpl` opens with the same guard:

```csharp
private void _ExportBeginImpl(string[] features, bool isDebug, string path, long flags)
{
    _ = flags; // Unused.
    if (!ProjectContainsDotNet())
        return;
```

— `modules/mono/editor/GodotTools/GodotTools/Export/ExportPlugin.cs` @ 4.7.1-stable. **[S]**

No `.sln` ⇒ no `dotnet` export feature ⇒ no `dotnet publish` ⇒ no AOT compile ⇒
no xcframework ⇒ **an Xcode project that builds and launches with no managed code
in it**, and an export that reports success. There is no `standard`-vs-`mono`
flavour on iOS, so the Android tell (the `/mono/` output path) does not transfer.

**There is a partial guard, and it is not airtight.** `_ExportFile` raises the
same "no solution file was found" error the Android leg saw — but only when a
`.cs` resource is actually walked into the PCK. It does not fire if the `.cs`
files are excluded by export filters, live outside `res://`, or are not imported
as `CSharpScript` resources.

**And there is a second silent path with no guard at all:** running a **non-.NET**
Godot editor binary. `MODULE_MONO_ENABLED` off means the mono export plugin does
not exist, nothing warns, and the iOS export simply succeeds without .NET. Hence
the `--version` check in §4.0 step 4.

### What a real .NET iOS export contains

iOS is **NativeAOT**, so there are **no managed `.dll`s to look for** — no
`libmonosgen`, no `Data/Mono`, no `dotnet/` folder. The primary artefact is a
native AOT xcframework:

```csharp
string xcFrameworkPath = Path.Combine(GodotSharpDirs.ProjectBaseOutputPath,
    publishConfig.BuildConfig, $"{GodotSharpDirs.ProjectAssemblyName}_aot.xcframework");
if (!BuildManager.GenerateXCFrameworkBlocking(outputPaths, xcFrameworkPath))
    throw new InvalidOperationException("Failed to generate xcframework.");
AddAppleEmbeddedPlatformEmbeddedFramework(xcFrameworkPath);
```

**[S]** `GenerateXCFrameworkBlocking` shells out to `xcodebuild -create-xcframework`
— a third, separate `xcodebuild` dependency beyond archive and export.

| Artefact | Path | Confidence |
|---|---|---|
| **AOT xcframework** (built on the host) | `res://.godot/mono/temp/bin/<ExportDebug\|ExportRelease>/<AssemblyName>_aot.xcframework` | **[S]** name and location |
| **AOT xcframework** (copied into the export) | `<binary_dir>/dylibs/<ExportDebug\|ExportRelease>/<AssemblyName>_aot.xcframework` — `_copy_asset` strips the `.godot/mono/temp/bin/` prefix and lands embedded frameworks under `dylibs/` | **[S]** for `dylibs/` and the `_aot.xcframework` name; **[U]** for the `<BuildConfig>` path segment, which is derived from `_copy_asset`'s string-replace logic |
| **Remaining publish payload** | `<binary_dir>/dylibs/data_<CSharpProjectName>_ios_arm64/…` via `AddSharedObject`. `dotnet/embed_build_outputs` is **force-hidden on iOS** (*"Hide unsupported .NET embedding option"*), so iOS takes this branch and **not** the Android-style `res://.godot/mono/publish/<arch>/` PCK-embedding branch. **Do not assert on `.godot/mono/publish/` for iOS.** | **[S]** |
| **ICU data** | `icudt.dat`, added via `AddAppleEmbeddedPlatformBundleFile`. **.NET 8 only** — PR #100187 skips it on .NET 9+. We are on `net8.0`, so expect it. | **[S]** |
| **Symbols** | `<AssemblyName>.framework.dSYM` (renamed from dotnet's `.dsym` by `iOS.targets`, because `create-xcframework` demands that spelling) | **[S]** |
| **Host-side publish intermediate** | `res://.godot/mono/temp/bin/godot-publish-dotnet/<BuildConfig>-<rid>/`, `<rid>` ∈ `ios-arm64`, `iossimulator-arm64`, `iossimulator-x64`. It deliberately survives the build — `UseTempDir = false` for iOS, *"xcode project links directly to files in the publish dir, so use one that sticks around"*. | **[S]** for the directory name and the rids; **[U]** whether the leaf is `<BuildConfig>-<rid>` or bare `<rid>` — **glob, do not hard-code** |
| **Publish sanity check** | The plugin itself throws `"Publish succeeded but project assembly not found at '…' or '…'"` if neither `<AssemblyName>.dll` nor `<AssemblyName>.dylib` exists in the publish output (iOS `soExt` = `dylib`) | **[S]** |

### The assertion CI must run

Mirroring the Android leg's managed-assembly check. **Assert on artefacts and on
stdout, never on the exit code.**

```bash
BIN="$OUT/SlayIdleRepeatSpike"     # the sibling directory of the .xcodeproj
ASM=SlayIdleRepeatSpike            # [dotnet] project/assembly_name

# 0. The editor must be the .NET flavour at all (§4.0 step 4).
"$GODOT" --version | grep -q '\.mono' || { echo "::error::not a .NET Godot build"; exit 1; }

# 1. The export must have engaged the mono module. Its ABSENCE is the tell:
#    a .NET iOS export always prints this warning, on every run.
grep -q 'when using C#/.NET is experimental' export.log \
  || { echo "::error::mono module never engaged — this export contains NO .NET"; exit 1; }

# 2. The AOT xcframework must exist and be non-empty. THE load-bearing assertion.
find "$BIN/dylibs" -type d -name "${ASM}_aot.xcframework" | grep -q . \
  || { echo "::error::no ${ASM}_aot.xcframework — this export contains NO .NET"; exit 1; }

# 3. The NativeAOT publish must exist for the DEVICE rid, not only the simulator.
find . -type d -path '*godot-publish-dotnet*ios-arm64*' | grep -q . \
  || { echo "::error::no device-rid NativeAOT publish output"; exit 1; }

# 4. The shared-object payload must be present.
find "$BIN/dylibs" -type d -name "data_*_ios_arm64" | grep -q . \
  || { echo "::error::no data_*_ios_arm64 payload"; exit 1; }

# 5. Guards engine issue #118161 as well as a half-published AOT build: the
#    xcframework must carry a real arm64 DEVICE slice.
lipo -info "$BIN/dylibs"/*/"${ASM}_aot.xcframework"/ios-arm64*/*   # [U] exact inner layout
```

**[S]** for the existence and naming of `<Assembly>_aot.xcframework`, the
`dylibs/` parent, `data_<CSharpProjectName>_ios_arm64/`, `godot-publish-dotnet/`
and the experimental warning string. **[U]** for the exact inner layout of the
`.xcframework` and the in-`.ipa` path (`Payload/*.app/Frameworks/…`), which is
decided by the pbxproj embed phase. Assertions 0–3 are the load-bearing ones and
their inputs are all sourced; 4 and 5 need their globs confirmed on the Mac.

### Three more inherited failure modes

- **ETC2/ASTC is required on iOS too.** `has_valid_project_configuration` calls
  `ResourceImporterTextureSettings::should_import_etc2_astc()` and returns
  invalid if it is off; the platform pushes the `etc2` and `astc` features
  (*"Vulkan and OpenGL ES 3.0 both mandate ETC2 support"*). **[S]** So the Android
  leg's failure 2 fix — `rendering/textures/vram_compression/import_etc2_astc=true`
  in `project.godot` — is **already the right setting for iOS**, and iOS needs no
  additional VRAM-compression flag. One less thing.
- **The generated iOS tree must not poison the C# compile.** The Android leg
  needed `<Compile Remove="android/**" />` because `--install-android-build-template`
  writes *inside* the Godot project. On iOS the export destination is **ours to
  choose** (`export_path`), so the mitigation is stronger: **point it outside the
  Godot project**, e.g. `build/ios/` at the repository root. Add
  `<Compile Remove="build/**" />` anyway, and **never** use the docs'
  "Active development considerations" workflow of dragging the Godot project into
  Xcode — engine issue [#86019](https://github.com/godotengine/godot/issues/86019)
  says it breaks .NET builds.
- **`--build-solutions` still hangs headless.** Godot-wide behaviour, not an
  Android one. **CI must never call it on any platform.** **[S]** by the Android
  leg's execution.

---

## 6. The gated CI job

`.github/workflows/ci.yml` gains an `ios-export` job following the established
`android-export` pattern exactly: `if: false`, a comment naming the milestone
that enables it and this document as its specification, and — per the convention
in `.github/workflows/README.md` — an `::error::` and `exit 1` if it is ever
actually run, so it can never pass vacuously. It is pinned to **`macos-15`**
(§2) because §1.1 makes any other runner impossible, and because `macos-15` is
the only current image that still carries an Xcode 16 line.

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
a new plugin API*. Issue #61's thread confirms the rebuild works: a commenter
reported clearing the errors on 4.4.1, and the reporter followed with *"I managed
to build with 4.5 headers."*

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
| **A** | **Fork and rebuild** the MAX plugin from source against 4.7.1 iOS headers | Clone Godot at 4.7.1, run SCons to produce iOS headers, rebuild both `debug` and `release` xcframeworks in Xcode, re-vendor. Issue #61's reporters did succeed at this on 4.4.1/4.5, so it is known-possible. **~3–5 days** for the first pass, on top of `12` §3.2's 3–5 days for the shim. | 🔴 **Repeat on every Godot minor upgrade, forever**, plus #60's unfixed runtime init bug, plus #62/#65's defects, plus writing our own C# interop with no vendor support. | Viable, expensive, and permanent. This is the "vendor and self-maintain" path M0-04 §10 Q5 called the expected steady state. |
| **B** | **Switch mediation to AdMob** via [`poingstudios/godot-admob-plugin`](https://github.com/poingstudios/godot-admob-plugin) | Reopens **D15**. Rewrites `Adapters.Ads.AppLovin` → `Adapters.Ads.AdMob` and `PlacementMap`. The O14 S2S design survives in shape (AdMob has its own SSV with a `custom_data` equivalent) but the verification details must be re-spiked. **~5–8 days** including a new O14-shaped spike. | 🟢 Low — actively maintained (pushed **2026-08-10**, release **v5.0.0** 2026-07-21), supports Godot 4.2+, iOS, and has a **documented first-class C# API** (`PoingStudios.AdMob.Api`, with C# rewarded-ad samples). v5.0.0 also mediates 17+ networks **including AppLovin**, so AppLovin demand is not lost, only demoted from mediator to bidder. | **Recommended if A's ongoing cost is judged unacceptable.** It is the only option that is simultaneously maintained, iOS-capable and C#-native. |
| **C** | **Hand-write a native iOS shim** against Godot's iOS plugin API | Objective-C/Swift, Godot source + SCons to generate headers, `.a`/`.xcframework` in debug and release, a `.gdip`, and the MAX iOS SDK integrated by hand. **~2–3 weeks**, i.e. back to the estimate `12` §3.2 was proud of having avoided. | 🔴 Identical to A's treadmill — you rebuild on every Godot minor — with none of A's head start. | **Reject.** Strictly dominated by A. |
| **D** | **Ship Android-first**, iOS later | Near zero engineering. Android's ad path is the one M0-05a already proved buildable. | 🟡 Halves the addressable market and defers, rather than answers, O23. | A schedule lever, not a solution. Reasonable *combined* with B (ship Android on MAX, land iOS on AdMob) but that means maintaining two ad adapters. |
| **E** | **Ship iOS with no ads**, Slay Plus only | Zero — `AutoGrantAdAdapter` and `FakeRewardedAdAdapter` already exist by design (§7.3). | 🔴 Zero ad revenue from iOS free players, permanently, and `12` §1's fairness contract quietly becomes "iOS free players get the Plus grant for free", which is a different game. | Emergency valve only. |

**Recommendation, to be ruled on at the M15 kickoff and recorded in `16` against
D15 — not decided by this spike:** the AppLovin MAX Godot plugin no longer
justifies D15's rationale (*"AppLovin ships an official MIT-licensed Godot 4
plugin, which removes the main integration risk"*). It did not remove the risk;
it transferred it to us. **Sequence a decision between A and B before M15
starts**, and make the deciding experiment the cheap one: *does the plugin,
rebuilt from 4.7.1 headers, link and initialise?* That is one Mac-day. If yes, A
is survivable. If no, B.

Two options that were checked and are **not** available: **Unity LevelPlay /
ironSource** has no official or maintained community Godot plugin (only hobbyist
repos last pushed in 2024), and `godot-sdk-integrations/godot-admob` — though
alive and explicitly tested on 4.7 — is **GDScript only**, which puts us straight
back into unproven interop work.

---

## 8. Risk register

Ranked by *probability × cost to the project*. 🔴 marks the ones that could still
force `16` O23's fallback discussion (engine version pin / GDScript UI shell /
wait for a point release).

| # | Risk | Trigger — how you find out | Mitigation | Fallback pressure |
|---|---|---|---|---|
| **R1** | 🔴 **NativeAOT + trimming breaks our game at runtime, on iOS only.** iOS is the sole platform where our C# is AOT-compiled and trimmed; every test, the simulator, the server and the Android build run Mono/JIT. Godot needs reflection, and [#96072](https://github.com/godotengine/godot/issues/96072) (open) shows scene instantiation failing in AOT release builds over `[Export]`ed `Resource` properties. Our composition root, JSON content deserialisation and any `Activator`/`Type.GetType` usage are exposed. | A build that exports and launches, then throws `MissingMetadataException` / `NotSupportedException` / behaves subtly wrong — **only on device, only in release**. It will not show up in any test suite we own. | Ban reflection-dependent patterns in `Core`/`Application` **now**, while there is no code to fix — add an architecture rule (M0-08 already owns banned-API greps). Prefer source generators and `System.Text.Json` source-gen contexts over runtime reflection. Avoid `[Export]` on Resource-typed properties per #96072. Add a device smoke test the moment a Mac exists. | 🔴 **Highest.** If our architecture cannot be made AOT-safe, the fallback is a GDScript UI shell with C# confined to `Core` — precisely `16` O23's second option. Discovering this in month 4 is the disaster O23 was raised to prevent. |
| **R2** | 🔴 **A dismissed, unfixed .NET-only crash exists against our exact pin.** [#121736](https://github.com/godotengine/godot/issues/121736), filed 2026-07-24 against **`4.7.1.stable.mono.official`**, **closed as not planned** 2026-08-05: `EXC_BREAKPOINT` 0.3–5 s after launch on iOS 26, release builds only, C# only, in iOS 26's type-aware allocator during render-target creation. **Qualifications that matter:** it was reported on `net9.0`/`net10.0` (we pin `net8.0`), on a 3D scene with a `SubViewportContainer` (we are a 2D idle game), and the reporter's own diagnosis is a layout-sensitive heisenbug. It may well not be ours. But it was *dismissed*, not *disproved*. | A release build that dies on launch on a current-iOS device while debug builds run clean. **Debug-only testing will never see this.** | Test **release** builds on a **current-iOS** device early, not at submission time. If it reproduces, the levers are: try `net8.0` (untested in the report), simplify the render setup, or move the engine pin. Watch the issue for a reopen. | 🔴 **High.** A reproducible launch crash on the pinned version with no upstream owner is the textbook trigger for `16` O23's *engine version pin* / *wait for a point release* options. |
| **R3** | 🔴 **Headless CLI iOS export may simply not work on a runner.** [#76749](https://github.com/godotengine/godot/issues/76749) — *"Cannot build in headless mode for iOS"* — has been **open since 2023-05-05**: the export succeeds locally on macOS and fails on CI with nothing but `ERROR: Project export for preset '…' failed.` [#111213](https://github.com/godotengine/godot/issues/111213) reports `"failed to run xcodebuild with code 0"`. [#104118](https://github.com/godotengine/godot/issues/104118) is a headless-export leak that **only reproduces on GitHub Actions macOS runners**. And **[U]** the .NET export plugin surfaces some errors through `EditorInterface.PopupDialogCentered`, which headless never renders — so an xcframework-generation failure may produce no readable diagnostic at all. | The CI job fails with an opaque one-line error while the same commands work on a developer's Mac. | Run `--import` (or `--export-pack`) first, per #76749's workaround — but note [#118386](https://github.com/godotengine/godot/issues/118386), which says `--export-pack` does not regenerate AOT. Capture the full editor log as an artefact. **Prove the recipe on a local Mac first, then on the runner separately** — one does not imply the other. | 🟡 Not an engine-pin question, but it can turn "iOS CI" into "iOS built by hand on one machine", which is precisely the outcome `12` §3.2 warns against. |
| **R4** | **Provisioning-profile secrets are not in the repo, and one env override is broken.** UUIDs live in the gitignored `.godot/export_credentials.cfg` (§3), and `GODOT_APPLE_PLATFORM_PROVISIONING_PROFILE_UUID_RELEASE` is **declared but never read** (§3.1) — release builds silently pick up the *debug* UUID if that variable is set. Automatic-vs-manual signing is *inferred* from which fields are non-empty ([#110052](https://github.com/godotengine/godot/issues/110052)). | A release `.ipa` signed with the wrong profile, or a build that flips to Manual signing and fails for want of fields nobody set. | Use `GODOT_APPLE_PLATFORM_PROFILE_SPECIFIER_RELEASE` (correctly wired) for release, or materialise `.godot/export_credentials.cfg` in CI. Set identity + profile as a deliberate set. File the RELEASE env-var bug upstream — it is a two-character fix. | 🟢 None — process, plus a small upstream contribution. |
| **R5** | **The 4.7.1 iOS export template may ship a broken simulator slice.** [#118161](https://github.com/godotengine/godot/issues/118161) is **open**, filed 2026-04-03 against 4.6.2 as a regression from 4.6.1: the xcframework advertises arm64-simulator but contains only x86_64, so Xcode on Apple Silicon fails to link. Whether 4.7.1 carries the fix is **[U]**. Compounded by the docs saying the engine simulator template is x64-only while the .NET half builds arm64 *and* x64 simulator slices. | `xcodebuild` on an Apple-Silicon Mac or an arm64 `macos-15` runner failing with undefined `_err_print_error` / `Dictionary::Dictionary()`. Device builds are unaffected. | `lipo -info` the template's simulator slice **before** trusting a green build. If 4.7.1 is affected: build device-only, take `macos-15-intel` for the simulator leg, or move the pin to a 4.7.x that carries the fix. | 🟡 Could force a **point-release wait** — `16` O23's third option — but only for simulator builds. |
| **R6** | **The paid Apple Developer Program is an unbudgeted, unavoidable prerequisite.** Free-provisioning Personal Team profiles expire in **7 days**, cap at 3 devices and 10 App IDs. No signed build, no TestFlight, no App Store, no usable CI signing without the paid account. | The first attempt to produce anything installable. | Split the work as §4 does: stage 1 (unsigned, engine question) needs no account and answers O23. Buy the account only when a real device build is needed. | 🟢 None on the engine — a budget and calendar item. |
| **R7** | **`12` §3.2's iOS recipe is wrong in two places** and someone will follow it. Godot generates no Podfile and does not use CocoaPods (§1); `export_options.plist` is generated from the preset, not hand-authored (§3). The 4.7 docs *also* publish two environment-variable names that do not exist in the engine (§3.1) and an "Active development considerations" workflow that breaks .NET builds (#86019). | Somebody budgets time for a step that does not exist, or hand-edits a generated file that the next export deletes. | Amend `12` §3.2 at the M15 kickoff, pointing here. Keep the Podfile as a **committed template copied in by a script** (§4.3). | 🟢 None — documentation. |
| **R8** | **`mobile` renderer silently imposes an A12 device floor** (iPhone XS / XR and later) via the forced `iphone-ipad-minimum-performance-a12` capability. Exactly the Android leg's silent `minSdk 29`, in a different coat. | Nobody notices until the App Store listing shows a device list narrower than intended. | Decide the device-coverage target explicitly and record it, together with `application/min_ios_version` (default **15.0**, raised from 14.0 in 4.7) and the Android `minSdk 29`, as one product decision rather than three toolchain defaults. | 🟢 None — product. |
| **R9** | **CI cost.** macOS runners bill at a **10× minute multiplier** on private repositories. A cold export plus `xcodebuild archive` is minutes, not seconds, and the mono export templates alone are ~1.1 GB to fetch. | The first month's Actions bill. | Do not run `ios-export` on every push. Gate it on `main`/`milestone/**` and manual dispatch, cache the export templates and NuGet, and keep the routine PR signal on the Linux jobs. Decide when M7-10 turns the job on. | 🟢 None — cost. |
| **R10** | **The silent-no-.NET export** (§5). Same missing-`.sln` root cause as Android failure 5, plus a second path with no guard at all (a non-mono editor binary). | Only by asserting on artefacts and on stdout. Exit code 0 and a working app prove nothing. | §5's assertions, in the CI job, non-optional. Keep the committed `.sln` with `ExportDebug`/`ExportRelease` configurations — `dotnet new sln` is **not** sufficient (Android failure 5). | 🟢 None, if the assertion exists. Severe if it does not. |
| **R11** | **`xcode-select` points at the Command Line Tools**, or the runner's Xcode default moves under us. Godot then invokes `clang` against a non-existent iPhone SDK path and dies with `MSB3073`. Xcode 26 has already broken Godot iOS export twice (#118543, #111213), and `macos-26` ships **no Xcode 16 at all**. | An `MSB3073` with a `/Library/Developer/CommandLineTools/...iPhoneOS.sdk` path, or a sudden failure after a runner-image refresh. | `sudo xcode-select --switch /Applications/Xcode_16.4.app` as an explicit CI step, `xcode-select -p` echoed into the log, runner pinned to `macos-15` and **never** `macos-latest`. | 🟢 None — one line, taken seriously. |
| **R12** | **The AppLovin MAX iOS plugin cannot be made to build at 4.7.1** (§7). | A rebuild-from-source attempt that still fails to link, or links and then fails `initialize()` (#60). | §7.4 — decide A vs B **before M15**. Everything up to M15 runs on `FakeRewardedAdAdapter` regardless (§7.3). | 🟡 Does not touch the engine pin, but it reopens **D15** and can change the platform launch order. |

---

## 9. What a Mac must confirm first — an ordered checklist

Work top to bottom. Stop and write down the result of each. **Items 1–9 need no
Apple Developer account.** Budget: one sitting.

1. **Toolchain.** `xcode-select -p` prints an `Xcode.app` path; `xcodebuild -version`
   works; `dotnet --list-sdks` shows an 8.0.x that satisfies `global.json`; the
   Godot editor reports **`4.7.1.stable.mono.official.a13da4feb`** — the `.mono`
   is the part that matters (§5).
2. **Templates + the #118161 check.** `ios.zip` is present in
   `~/Library/Application Support/Godot/export_templates/4.7.1.stable.mono/`.
   Before building anything, `lipo -info` the simulator slice inside it and settle
   **R5 / engine issue #118161** on 4.7.1. Two minutes; saves a day of confused
   link errors.
3. **Does a fictitious Team ID get past the hard error?** Put a syntactically
   valid ten-character placeholder in `application/app_store_team_id` and run the
   export. If the project generates, the whole engine question is answerable with
   **no Apple relationship at all** — record it, because it decides what CI can
   do before anyone buys an account (§4.1 **[U]**).
4. **Stage-1 export** with `application/export_project_only=true` (§4.1). Capture
   the **complete** console output to a file. Expect the harmless
   `ERROR:`-shaped lines the Android leg documented — **do not scrape logs for
   `ERROR`** — and confirm the *"…C#/.NET is experimental"* warning **is**
   present (§5 assertion 1).
5. **The .NET assertion (§5).** `<Assembly>_aot.xcframework` under `dylibs/`;
   `godot-publish-dotnet/…ios-arm64…` exists. **If either is missing, the export
   produced no .NET and everything after this is meaningless.** Record the exact
   paths — §5's `[U]` globs need fixing here.
6. **Unsigned `xcodebuild`.** Does the generated project build with
   `CODE_SIGNING_ALLOWED=NO` (§4.1c)? Record whether Godot's generated scheme
   fights the flags. This is what makes a *non-vacuous* CI job possible without
   an account.
7. **Where the MSBuild log went.** The Android leg found C# build failures are
   invisible on stdout and land in `%APPDATA%\Godot\mono\build_logs\`. Confirm the
   macOS equivalent (`~/Library/Application Support/Godot/mono/build_logs/`) so
   CI can upload it. Without this, a managed build failure reads only as
   *"Failed to build project. Check MSBuild panel for details."* — and per **R3**,
   an xcframework failure may render to a dialog headless never shows.
8. **Timings and sizes.** Cold and warm export, cold and warm `xcodebuild`, and
   the on-disk size of the export template and the generated project. Feeds R9.
9. 🔴 **Repeat items 4–6 on a GitHub Actions `macos-15` runner, not just
   locally.** #76749 and #104118 both describe failures that appear **only** on
   hosted runners. A green local Mac does not prove the CI job (**R3**).
10. *(needs an account)* **Signed stage-2 export** (§4.2): keychain import,
    provisioning profile, `export_project_only=false`, and confirm Godot really
    drives `xcodebuild archive` → `-exportArchive` → `.ipa`. While there, verify
    §3.1 landmine 2 empirically — set only
    `GODOT_APPLE_PLATFORM_PROVISIONING_PROFILE_UUID_RELEASE` and confirm it is
    ignored, then file the bug upstream.
11. *(needs an account and a device)* 🔴 **Run a RELEASE build on a current-iOS,
    A12+ device.** Confirm the managed code executes — the Android leg's
    `[O23] RESULT:` marker line, on iOS. **This is the only thing that answers R1
    and R2**, the two risks that can still reopen D1, and **a debug build does not
    substitute**: both #121736 and #96072 are release-only.
12. *(needs §7 resolved)* **The ad build.** Rebuild the MAX plugin from 4.7.1
    headers; does it link, and does `initialize()` fire? One day, and it decides
    §7.4 A vs B.

When items 1–9 are done, this document should be rewritten with the **[U]** rows
replaced by observations, and `docs/spikes/README.md` updated. When item 11 is
done, and only then, O23 can be closed.

---

## 10. Sources

Engine, at the **4.7.1-stable** tag unless noted:

- [`editor/export/editor_export_platform_apple_embedded.cpp`](https://github.com/godotengine/godot/blob/4.7.1-stable/editor/export/editor_export_platform_apple_embedded.cpp) / [`.h`](https://github.com/godotengine/godot/blob/4.7.1-stable/editor/export/editor_export_platform_apple_embedded.h) — the macOS-only refusal, the Team ID hard error, the export options and defaults, the ETC2/ASTC check, the `xcodebuild archive`/`-exportArchive` calls, `get_binary_extensions`, the `ENV_APPLE_PLATFORM_*` constants and the RELEASE-UUID bug, the manual/automatic signing inference, plugin discovery.
- [`platform/ios/export/export_plugin.h`](https://github.com/godotengine/godot/blob/4.7.1-stable/platform/ios/export/export_plugin.h) / [`.cpp`](https://github.com/godotengine/godot/blob/4.7.1-stable/platform/ios/export/export_plugin.cpp) / [`export.cpp`](https://github.com/godotengine/godot/blob/4.7.1-stable/platform/ios/export/export.cpp) — `get_minimum_deployment_target() == "15.0"`, `targeted_device_family`, the Metal/iOS-14 rule, `IPHONEOS_DEPLOYMENT_TARGET`, and the single `export/ios/ios_deploy` editor setting.
- [`platform/ios/doc_classes/EditorExportPlatformIOS.xml`](https://github.com/godotengine/godot/blob/4.7.1-stable/platform/ios/doc_classes/EditorExportPlatformIOS.xml) — every `application/*` option and the `GODOT_APPLE_PLATFORM_*` overrides.
- [`modules/mono/editor/GodotTools/GodotTools/Export/ExportPlugin.cs`](https://github.com/godotengine/godot/blob/4.7.1-stable/modules/mono/editor/GodotTools/GodotTools/Export/ExportPlugin.cs) — `ProjectContainsDotNet()`, the iOS NativeAOT publish, `godot-publish-dotnet/`, `<Assembly>_aot.xcframework`, the simulator target, the `.dat` bundle-file rule.
- `modules/mono/editor/Godot.NET.Sdk/Godot.NET.Sdk/Sdk/iOS.props` and `iOS.targets` — `PublishAot` / `UseNativeAOTRuntime`, `-miphoneos-version-min=12.0`, the `.dsym` rename.
- `modules/mono/editor/GodotTools/GodotTools.ProjectEditor/ProjectGenerator.cs` — `net8.0` as the generated TFM, `net9.0` for Android only.
- [`editor/export/editor_export.cpp`](https://github.com/godotengine/godot/blob/4.7.1-stable/editor/export/editor_export.cpp) — `PROPERTY_USAGE_SECRET` → `.godot/export_credentials.cfg`.

Documentation and releases:

- [Exporting for iOS (4.7)](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_ios.html) — Requirements, Team ID/Bundle ID, the generated `.xcodeproj`, the `xcode-select` troubleshooting section, the (**outdated**) environment-variable table, the simulator/Compatibility-renderer warning.
- [C#/.NET (4.7)](https://docs.godotengine.org/en/4.7/tutorials/scripting/c_sharp/index.html#doc-c-sharp-platforms) — *"iOS support is currently experimental and has a few limitations."*
- [Exporting projects — configuration files (4.7)](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_projects.html#configuration-files) — `.godot/export_credentials.cfg`.
- [Creating iOS plugins (4.7)](https://docs.godotengine.org/en/4.7/tutorials/platform/ios/ios_plugin.html) — `res://ios/plugins/`, `.gdip`, the same-headers requirement.
- [*Current state of C# platform support in Godot 4.2*](https://godotengine.org/article/platform-state-in-csharp-for-godot-4-2/), 2024-01-26 — NativeAOT, trimming, reflection.
- Engine PRs [#82729](https://github.com/godotengine/godot/pull/82729), [#84945](https://github.com/godotengine/godot/pull/84945), [#100187](https://github.com/godotengine/godot/pull/100187).
- Engine issues [#121736](https://github.com/godotengine/godot/issues/121736), [#76749](https://github.com/godotengine/godot/issues/76749), [#111213](https://github.com/godotengine/godot/issues/111213), [#104118](https://github.com/godotengine/godot/issues/104118), [#118161](https://github.com/godotengine/godot/issues/118161), [#116165](https://github.com/godotengine/godot/issues/116165), [#110052](https://github.com/godotengine/godot/issues/110052), [#96072](https://github.com/godotengine/godot/issues/96072), [#115715](https://github.com/godotengine/godot/issues/115715), [#118386](https://github.com/godotengine/godot/issues/118386), [#122265](https://github.com/godotengine/godot/issues/122265), [#100123](https://github.com/godotengine/godot/issues/100123), [#86019](https://github.com/godotengine/godot/issues/86019).
- [Godot 4.7.1-stable release](https://github.com/godotengine/godot-builds/releases/tag/4.7.1-stable) and [announcement](https://godotengine.org/article/maintenance-release-godot-4-7-1/), 2026-07-14; [download archive](https://godotengine.org/download/archive/).

AppLovin MAX Godot:

- [Repository](https://github.com/AppLovin/AppLovin-MAX-Godot) and issues [#59](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/59), [#60](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/60), [#61](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/61), [#66](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/66), [#67](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/67).
- Godot forum: [4.5 iOS undefined symbols](https://forum.godotengine.org/t/godot-4-5-ios-export-gives-undefined-symbols-errors-in-xcode-due-to-not-updated-plugin/125054) (2025-10-14), [iOS plugins lack a maintainer](https://forum.godotengine.org/t/its-official-godot-ios-plugin-obsolete/130507) (2026-01).
- Alternatives: [`poingstudios/godot-admob-plugin`](https://github.com/poingstudios/godot-admob-plugin) (v5.0.0, 2026-07-21), [`godot-sdk-integrations/godot-admob`](https://github.com/godot-sdk-integrations/godot-admob) (GDScript only).

CI and Apple:

- [`actions/runner-images`](https://github.com/actions/runner-images) — `macos-15` GA, `macos-14` deprecated ([#13518](https://github.com/actions/runner-images/issues/13518), unsupported from 2026-11-02); per-image manifests for `macos-15` and `macos-26`.
- [GitHub Actions billing](https://docs.github.com/en/actions/concepts/billing-and-usage) — the 10× macOS minute multiplier.
- [Choosing a membership](https://developer.apple.com/support/compare-memberships/) and the free-provisioning limits (7-day profiles, 3 devices, 10 App IDs).

In-repo:

- [`O23-godot-android-export.md`](O23-godot-android-export.md) — M0-05a, the executed Android leg.
- [`O14-applovin-max-s2s.md`](O14-applovin-max-s2s.md) — M0-04, the S2S verdict and the first abandonment finding.
