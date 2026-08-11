# Spike O14 — AppLovin MAX Godot plugin: S2S rewarded callbacks and `setUserId`

| | |
|---|---|
| **Open item** | **O14** (`16_DECISION_LOG.md` Part D), raised by `12_MONETIZATION_ADS.md` §3.3 |
| **Milestone / task** | M0 / M0-04 |
| **Method** | **Source-and-docs verdict.** No AppLovin account, no SDK binaries, no device build. Scope set at the M0 kickoff. |
| **Plugin version inspected** | **1.2.0** (tag `release_1_2_0`, published **2025-04-24**) — the latest release, and `master` is byte-identical for the GDScript API (see §2.1) |
| **Date of investigation** | **2026-08-11** |
| **Verdict** | ✅ **Proceed — no plugin patch required.** With one correction to the mechanism and one newly-found gap. |

---

## 1. Verdict

**We can build the planned server-authoritative S2S reward path, and we do not need to patch or fork the plugin.**

The spike found the literal answer to O14's question to be *"no"* — `setUserId` / `setUserIdentifier` is **absent from every layer** of the plugin: GDScript, Java and Objective-C++ alike (§3). But the question turns out to have been aimed at the wrong API. MAX's S2S rewarded postback carries **two** publisher-controlled attribution macros, `{USER_ID}` **and** `{CUSTOM_DATA}`, and the plugin exposes the second one **fully and on both platforms**: `AppLovinMAX.show_rewarded_ad(ad_unit_identifier, placement, custom_data)` is wired straight through to the native `MaxRewardedAd.showAd(placement, customData)` / `-[MARewardedAd showAdForPlacement:customData:]` (§4). That is a *better* attribution channel than `{USER_ID}` for our purposes, because it is **per-impression** rather than per-session: we can mint a fresh correlation nonce for every single ad show, which is exactly what a server-authoritative, capped, replay-resistant grant wants. `{EVENT_TOKEN_ALL}` (SHA-256 over all macros plus our Event Key) makes that custom data **tamper-evident in transit** (§5).

**The correction:** `12` §3.2 says `VerificationToken` "carries the S2S token when the plugin supports server-side callbacks". There is no such token — **MAX hands the client nothing**. The postback goes AppLovin-server → our server and never touches the device. What the client carries is the **correlation nonce it minted itself and passed as `custom_data`**. The field's *shape* is unchanged and the port signature survives verbatim (§7) — but the M15 implementer must not go looking for an SDK-issued token, because none exists.

**The newly-found gap, and it is the important one:** the S2S postback fires **only on a completed view**. On no-fill, load failure, display failure or user-abandon, **AppLovin sends nothing at all**. So the locked ruling in `16` A5 / `12` §4.3 — *"Ad fill failure: grant the reward and consume the cap slot; enforced server-side"* — **cannot be driven by S2S**, because the server never hears about a fill failure from AppLovin. The no-fill grant is *necessarily* a client-asserted path (§6). This means **the signed-nonce fallback of §8 is not a fallback at all — it is required infrastructure in the happy path**, for the no-fill branch. It should be built at M15 regardless of the S2S outcome. `12` §4.3's sentence "the server applies this rule, not the client" remains true about *authority*, but the server's only *evidence* is the client's claim, so that branch needs plausibility limits and rate-limiting from day one.

---

## 2. Evidence: what was actually read

All line references are to tag **`release_1_2_0`**. Permalink base:
`https://github.com/AppLovin/AppLovin-MAX-Godot/blob/release_1_2_0/`

| File | What it is | Lines that matter |
|---|---|---|
| [`addons/applovin_max/AppLovinMAX.gd`](https://github.com/AppLovin/AppLovin-MAX-Godot/blob/release_1_2_0/addons/applovin_max/AppLovinMAX.gd) | The entire GDScript public API (1173 lines) | L28–43 listener base classes · L59–64 `RewardedAdEventListener` · L656–690 signal wiring · L693–725 rewarded API · L870–882 `ErrorCode` · L992–1006 `Reward` |
| [`Source/Android/…/AppLovinMAXGodotPlugin.java`](https://github.com/AppLovin/AppLovin-MAX-Godot/blob/release_1_2_0/Source/Android/src/main/java/com/applovin/godot/AppLovinMAXGodotPlugin.java) | Godot↔Java binding surface (`@UsedByGodot`) | L711–714 `show_rewarded_ad` |
| [`Source/Android/…/AppLovinMAXGodotManager.java`](https://github.com/AppLovin/AppLovin-MAX-Godot/blob/release_1_2_0/Source/Android/src/main/java/com/applovin/godot/AppLovinMAXGodotManager.java) | The real MAX SDK calls | L429–433 `showRewardedAd` |
| [`Source/iOS/…/AppLovinMAXGodotPlugin.mm`](https://github.com/AppLovin/AppLovin-MAX-Godot/blob/release_1_2_0/Source/iOS/AppLovin-MAX-Godot-Plugin/AppLovinMAXGodotPlugin.mm) | Godot↔ObjC++ binding surface (`ClassDB::bind_method`) | L275 binding · L749–754 `show_rewarded_ad` |
| [`Source/iOS/…/AppLovinMAXGodotManager.mm`](https://github.com/AppLovin/AppLovin-MAX-Godot/blob/release_1_2_0/Source/iOS/AppLovin-MAX-Godot-Plugin/AppLovinMAXGodotManager.mm) | The real MAX SDK calls | L391–395 `showRewardedAdWithAdUnitIdentifier:placement:customData:` |
| [`addons/applovin_max/Example/Scenes/main.gd`](https://github.com/AppLovin/AppLovin-MAX-Godot/blob/release_1_2_0/addons/applovin_max/Example/Scenes/main.gd) | Vendor's own usage example | L73–82 listener attachment |
| [`CHANGELOG.md`](https://github.com/AppLovin/AppLovin-MAX-Godot/blob/release_1_2_0/CHANGELOG.md), [`plugin.cfg`](https://github.com/AppLovin/AppLovin-MAX-Godot/blob/release_1_2_0/addons/applovin_max/plugin.cfg) | Version provenance | — |

Docs read (these are *claims*, and where they conflict with the source, the source wins — see §2.2):

- [MAX S2S Rewarded Callback API](https://support.applovin.com/en/max/advanced-features/s2s-rewarded-callback-api) — the macro list and the `EVENT_TOKEN` scheme
- [FAQ: how the server-to-server callback works](https://support.applovin.com/en/max/faq/how-server-to-server-callback-works) — firing conditions, Event Key location
- [Godot › Ad formats › Rewarded ads](https://support.applovin.com/en/max/godot/ad-formats/rewarded-ads)
- [Godot › Overview › Advanced settings](https://support.applovin.com/en/max/godot/overview/advanced-settings)

### 2.1 Version provenance

Seven releases exist; the newest is **1.2.0, 2025-04-24** (`GET /repos/AppLovin/AppLovin-MAX-Godot/releases`). `12` §3.1's recorded "1.2.0 (April 2025), 7 releases" **still holds exactly**, sixteen months later. `addons/applovin_max/plugin.cfg` reads `version="1.2.0"` on both the tag and `master`, and `AppLovinMAX.gd` is **byte-identical** between `release_1_2_0` and `master` (SHA-1 `877f5951…` for both). There is no unreleased API work sitting on the default branch.

> ⚠️ **Read this as a maintenance signal, not just a version number.** See §2.4 — the repo is not merely un-released, it is **unmaintained**, and that is now the larger risk of the two this spike touched.

### 2.2 Where the source and the docs disagree — the source wins

This matters, because O14 was raised *precisely* on the strength of a docs absence, and the docs were wrong.

| Claim | Source reality |
|---|---|
| The Godot rewarded-ads page documents `show_rewarded_ad(ad_unit_id)` — **no `custom_data` parameter shown**. | `AppLovinMAX.gd` L707 declares `show_rewarded_ad(ad_unit_identifier: String, placement: String = "", custom_data: String = "")`, and it is wired end-to-end on **both** platforms. **The capability exists and is simply undocumented for Godot.** |
| The Godot advanced-settings page documents no user-identifier API. | Correct — and unusually, the docs and source agree here. It is absent from the native layers too (§3). |

`12` §3.3's premise — *"the plugin's public documentation does not mention user-identifier setting or server-side rewarded callbacks"* — was accurate about the **documentation** and misleading about the **plugin**. Half the concern dissolves on reading the source.

### 2.3 Issue-tracker scan — corroboration

A full sweep of all 67 issues/PRs (paginated REST list, cross-checked against the GitHub issue-search API, which indexes title + body + comments):

- **`user_id` / `setUserId` / "user id": `total_count: 0`.** Nobody has ever asked for it. Independently corroborates §3.
- **S2S / server-side callback / postback / reward verification / custom data: zero matches.** No one has reported the S2S path broken — but equally, no one has publicly confirmed it working through this plugin.
- **No-fill / load-failure behaviour: zero matches.**
- **No open PRs at all**; no PR in the repo's history adds a user-identifier API. The only feature PR since 1.1.1 is [#55](https://github.com/AppLovin/AppLovin-MAX-Godot/pull/55) (Segment Targeting + Consent Flow), merged 2025-04-23.

The absence of complaints is weak positive evidence — but note the tracker is unattended (§2.4), so silence there means less than it normally would.

### 2.4 ⚠️ The plugin is effectively abandoned

This was not what O14 asked about, and it matters more than what O14 asked about.

| Signal | Value |
|---|---|
| Newest commit on `master` | **2025-04-24** (`e597986`, "Release/1.2.0") — **no code change in ~15.5 months** |
| Last maintainer activity of any kind | **2025-04-28** (closing #53/#54) |
| Issues filed since (#59–#67) | **Every one has zero comments from any OWNER/MEMBER/COLLABORATOR** |
| Branches | One (`master`), head == the 1.2.0 tag. `pushed_at` 2025-05-16 is ref housekeeping, not code. |
| Archived? | No — so it *looks* maintained from the outside |

Specific open reports that bear directly on M15:

| Issue | Substance | Why we care |
|---|---|---|
| [#66](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/66) | "AppLovin plugin failing on iOS, Godot 4.5. Update, when?" — open since 2025-12-02. A user reports AppLovin support said a fix is "in our engineering backlog"; latest comment (2026-03-05): *"Almost one year without updates."* | **Direct hit on our iOS build path.** Escalates R5. |
| [#61](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/61) | Godot 4.5 iOS builds reportedly work **only if you rebuild the plugin yourself against matching Godot headers**. Two commenters say they migrated to AdMob / `godot-admob`. | Implies we may be forced into the fork/rebuild cost anyway — for *compatibility*, not for `setUserId`. |
| [#65](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/65) / [#35](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/35) | The vendor's **own example has the reward callback's parameters in the wrong order**. Reported 2024-10, closed without a fix, re-reported 2025-10, still open. | A copy-paste landmine on precisely our grant path — see §6.1. |
| [#59](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/59) | "Please document how to integrate AppLovin via C#" — open since 2025-06-24, **zero replies**. | We are a C# project (D1). **Expect no vendor help with the shim** — `12` §3.2's 3–5 day estimate assumes we are on our own, which we are. |
| [#62](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/62) | `set_test_device_advertising_identifiers` is broken — the binding passes no parameters. Open, no reply. | Bites at M15 when setting up test devices. Budget for it. |
| [#64](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/64) | iOS `ADD_SIGNAL` declares banner `ad_info` as `Variant::STRING` instead of `DICTIONARY`. | Banner-only — **we ship no banners** (`12` locked decision 5), so this one is free. But it shows the class of defect present in the iOS signal layer. |

**This does not change the S2S verdict** — §3–§6 are facts about code that exists and will keep existing, and MIT means we can always fork. It does mean the honest headline is: *the S2S question is answered and fine; the plugin's viability is the thing to worry about.* See §9 R5 and §10 Q5.

---

## 3. Finding: `setUserId` is absent from every layer

**A repo-wide, case-insensitive search for `user_id`, `userIdentifier`, `setUserId` and `user identifier` across the GDScript API, both native binding layers and both native manager layers returns zero matches.**

This is not a GDScript-only gap that a one-line binding would close. `AppLovinSdkSettings.setUserIdentifier()` (Android) and `ALSdkSettings.userIdentifier` (iOS) are **never called anywhere in the plugin**. Consequently:

> **`{USER_ID}` in the S2S postback will always arrive empty.** Do not design against it.

The nearest neighbours in the API, and why none of them substitute:

| API | Maps to | Verdict |
|---|---|---|
| `set_extra_parameter(key, value)` (L788) | `sdk.getSettings().setExtraParameter(k, v)` | Generic SDK settings bag. **Not** the user identifier; no documented key routes it to `{USER_ID}`. Do not guess one. |
| `set_rewarded_ad_extra_parameter(unit, key, value)` (L714) | `MaxRewardedAd.setExtraParameter(k, v)` | Per-ad-unit network tuning knob, not postback attribution. |
| `set_rewarded_ad_local_extra_parameter(unit, key, value)` (L721) | `MaxRewardedAd.setLocalExtraParameter(k, v)` | Passes objects to adapters locally; never leaves the device. |
| **`show_rewarded_ad(unit, placement, custom_data)`** (L707) | **`MaxRewardedAd.showAd(placement, customData)`** | ✅ **This is the one.** See §4. |

**Cost of adding `setUserId` anyway, should M15 ever want it** — the code is trivial and the build is not:

- `AppLovinMAX.gd`: one `static func set_user_id(user_id: String)` forwarding to `_plugin`.
- `AppLovinMAXGodotPlugin.java`: one `@UsedByGodot` method → `sdk.getSettings().setUserIdentifier(userId)`.
- `AppLovinMAXGodotPlugin.mm`: one `ClassDB::bind_method` + method → `self.sdk.settings.userIdentifier = …`.

≈ 20 lines across three files. **But** `android/plugins/…/AppLovin-MAX-Godot-Plugin.aar` and the iOS `.xcframework` static libraries are **committed prebuilt binaries**, so any native change forces a full Gradle rebuild *and* an Xcode/CocoaPods rebuild of both `debug` and `release` xcframeworks, plus carrying that fork forever. **That cost is entirely avoidable, and §4 avoids it.** Recommendation: do not fork.

---

## 4. Finding: `custom_data` is exposed, end-to-end, on both platforms

This is the finding that decides the spike. The parameter survives every hop:

**GDScript** — [`AppLovinMAX.gd` L707–711](https://github.com/AppLovin/AppLovin-MAX-Godot/blob/release_1_2_0/addons/applovin_max/AppLovinMAX.gd#L707-L711):

```gdscript
static func show_rewarded_ad(ad_unit_identifier: String, placement: String = "", custom_data: String = "") -> void:
	if _plugin == null:
		return

	_plugin.show_rewarded_ad(ad_unit_identifier, placement, custom_data)
```

**Android** — [`…Plugin.java` L711–714](https://github.com/AppLovin/AppLovin-MAX-Godot/blob/release_1_2_0/Source/Android/src/main/java/com/applovin/godot/AppLovinMAXGodotPlugin.java#L711-L714) → [`…Manager.java` L429–433](https://github.com/AppLovin/AppLovin-MAX-Godot/blob/release_1_2_0/Source/Android/src/main/java/com/applovin/godot/AppLovinMAXGodotManager.java#L429-L433):

```java
public void showRewardedAd(final String adUnitId, final String placement, final String customData)
{
    MaxRewardedAd rewardedAd = retrieveRewardedAd( adUnitId );
    rewardedAd.showAd( placement, customData );
}
```

**iOS** — [`…Plugin.mm` L749–754](https://github.com/AppLovin/AppLovin-MAX-Godot/blob/release_1_2_0/Source/iOS/AppLovin-MAX-Godot-Plugin/AppLovinMAXGodotPlugin.mm#L749-L754) → [`…Manager.mm` L391–395](https://github.com/AppLovin/AppLovin-MAX-Godot/blob/release_1_2_0/Source/iOS/AppLovin-MAX-Godot-Plugin/AppLovinMAXGodotManager.mm#L391-L395):

```objc
- (void)showRewardedAdWithAdUnitIdentifier:(NSString *)adUnitIdentifier placement:(nullable NSString *)placement customData:(nullable NSString *)customData
{
    MARewardedAd *rewardedAd = [self retrieveRewardedAdForAdUnitIdentifier: adUnitIdentifier];
    [rewardedAd showAdForPlacement: placement customData: customData];
}
```

The iOS binding registers three `DEFVAL("")` defaults ([`Plugin.mm` L275](https://github.com/AppLovin/AppLovin-MAX-Godot/blob/release_1_2_0/Source/iOS/AppLovin-MAX-Godot-Plugin/AppLovinMAXGodotPlugin.mm#L275)), so the optional arguments behave identically on both platforms.

`MaxRewardedAd.showAd(placement, customData)` is the documented MAX API whose `customData` populates **`{CUSTOM_DATA}`** in the S2S postback. **Our attribution channel is intact, per-impression, and requires no plugin change.**

The `placement` parameter is also live, which is a free bonus: our 29 `AdPlacementId`s can be passed as MAX placement strings and arrive as `{PLACEMENT}` in the postback, giving the server a second, independent read on which placement is being claimed.

---

## 5. Finding: the S2S callback contract

Configured **in the AppLovin dashboard**, per rewarded ad unit ("Server Side Callback URL" on the Edit Ad Unit page) — not in the SDK, and therefore not something the plugin could have blocked.

**Transport:** HTTP **GET**, AppLovin's servers → our endpoint. Never traverses the device. 5-second response timeout; **up to two retries** on failure. Fired "soon after ad completion, but may be delayed by a few minutes."

**Macros available** (we choose which to put in the URL template):

| Macro | Meaning | Our use |
|---|---|---|
| `{CUSTOM_DATA}` | Custom string from `showAd(placement, customData)`, URL-encoded. ≤ 8192 chars. | ✅ **Our correlation nonce.** The whole mechanism. |
| `{EVENT_ID}` | Unique event ID, 40 hex chars | ✅ **Idempotency key** — dedupe the two retries |
| `{EVENT_TOKEN}` | `sha1(EVENT_ID + Event Key)` | Authenticates the *event*, not the payload |
| `{EVENT_TOKEN_ALL}` | `sha256(all macros, alphabetical, URL-decoded + Event Key)` | ✅ **Use this** — it covers `{CUSTOM_DATA}`, making the nonce tamper-evident |
| `{USER_ID}` | Publisher-defined user ID | ❌ **Always empty for us** (§3) |
| `{AD_UNIT_ID}`, `{PLACEMENT}` | Ad unit / placement name | ✅ Cross-check against the claimed placement |
| `{TS}` | Ad **load** time, epoch seconds | ✅ Freshness window (note: *load*, not *completion*) |
| `{AMOUNT}`, `{CURRENCY}` | Reward configured on the ad unit | Ignore — our reward table is server-side (`12` §4) |
| `{PLATFORM}`, `{CC}`, `{IP}`, `{IDFA}`, `{IDFV}`, `{NETWORK_NAME}`, `{PACKAGE_NAME}`, `{AD_UNIT_NAME}` | Context | Telemetry / abuse signals only |

**Verification:** shared-secret **Event Key**, found in the AppLovin dashboard under **Account → General → Keys**. Prefer **`{EVENT_TOKEN_ALL}`** over `{EVENT_TOKEN}`: the SHA-1 form hashes only `EVENT_ID + key`, so it proves the callback came from AppLovin but says nothing about whether the custom data was altered. The SHA-256 form covers every macro in the URL, which is what we need since our nonce rides in `{CUSTOM_DATA}`.

**⚠️ The Event Key is a production secret.** It must land in the secret store, never in `data/ads.json`, never in the repo, and it must be rotatable — flag this at M15 and for whatever M-milestone owns secrets management.

### 5.1 Shape for `Adapters.AdVerify.AppLovinS2S`

This is enough to shape the M15 adapter now (`23` §3, `SlayIdleRepeat.Adapters.AdVerify.AppLovinS2S`):

```
GET /ads/applovin/s2s
  ?event_id={EVENT_ID}
  &token={EVENT_TOKEN_ALL}
  &custom_data={CUSTOM_DATA}
  &ad_unit={AD_UNIT_ID}
  &placement={PLACEMENT}
  &ts={TS}
  &platform={PLATFORM}
  &country={CC}
```

Server-side algorithm:

1. Recompute `sha256(<all macros, alphabetical, URL-decoded> + EventKey)`; **constant-time compare** against `token`. Reject on mismatch.
2. `event_id` → **idempotency key**. Already seen ⇒ 200 OK, no second grant. (Two retries *will* happen.)
3. Parse `custom_data` → our nonce; look up the pending ad-claim record.
4. Cross-check `ad_unit` / `placement` against the record's `AdPlacementId` via `PlacementMap`.
5. Apply cap, apply reward to the authoritative profile, mark the record granted.
6. Return **200** quickly — the timeout is 5 s. Do the granting work behind the response if it cannot be made fast.

---

## 6. Finding: the signal surface, and no-fill vs. abandon

### 6.1 The signals

Registered via `AppLovinMAX.set_rewarded_ad_listener(listener)` ([L656–690](https://github.com/AppLovin/AppLovin-MAX-Godot/blob/release_1_2_0/addons/applovin_max/AppLovinMAX.gd#L656-L690)), which connects seven underlying plugin signals (`rewarded_on_ad_loaded`, `…_load_failed`, `…_clicked`, `…_revenue_paid`, `…_displayed`, `…_display_failed`, `…_hidden`, `…_received_reward`) to `Callable`s on a `RewardedAdEventListener` ([L59–64](https://github.com/AppLovin/AppLovin-MAX-Godot/blob/release_1_2_0/addons/applovin_max/AppLovinMAX.gd#L59-L64), extending `FullscreenAdEventListener` → `AdEventListener`, L28–43):

| Callable | Payload | Maps to |
|---|---|---|
| `on_ad_loaded` | `(ad_unit_id, AdInfo)` | preload succeeded → `IsReady` |
| `on_ad_load_failed` | `(ad_unit_id, ErrorInfo)` | **no-fill / load failure** — see §6.2 |
| `on_ad_displayed` | `(ad_unit_id, AdInfo)` | impression started |
| `on_ad_display_failed` | `(ad_unit_id, ErrorInfo, AdInfo)` | → `AdResultKind.Error` |
| `on_ad_clicked` | `(ad_unit_id, AdInfo)` | telemetry only |
| `on_ad_revenue_paid` | `(ad_unit_id, AdInfo)` | `AdInfo.revenue` → ad-revenue telemetry (`12` §9.1) |
| **`on_ad_received_reward`** | `(ad_unit_id, Reward, AdInfo)` | **the completion signal** |
| `on_ad_hidden` | `(ad_unit_id, AdInfo)` | ad closed — resolves `ShowAsync` |

> 🚩 **Landmine — do not copy the vendor example.** The plugin's own [`main.gd` L245](https://github.com/AppLovin/AppLovin-MAX-Godot/blob/release_1_2_0/addons/applovin_max/Example/Scenes/main.gd#L245) declares the handler as
> `func _on_rewarded_ad_received_reward(ad_unit_id, ad_info: AdInfo, reward: Reward)`,
> but the actual emission order is **`(ad_unit_identifier, reward, ad_info)`** — verified in both [`AppLovinMAX.gd` L687–689](https://github.com/AppLovin/AppLovin-MAX-Godot/blob/release_1_2_0/addons/applovin_max/AppLovinMAX.gd#L687-L689) and the iOS emitter `AppLovinMAXGodotManager.mm` (`emit_signal(signalName, adUnitIdentifier, rewardInfo, adInfo)`). **The example has `reward` and `ad_info` swapped**, on the one callback our entire grant depends on. Reported as [#35](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/35) (2024, closed unfixed) and again as [#65](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/65) (2025, still open). **`max_bridge.gd` must use the source order, not the example's.**

`Reward` ([L992–1006](https://github.com/AppLovin/AppLovin-MAX-Godot/blob/release_1_2_0/addons/applovin_max/AppLovinMAX.gd#L992-L1006)) carries only `label: String`, `amount: int` and `is_valid()`. It carries **no token and no identifier** — confirming §1's correction. `AdInfo` carries `ad_unit_identifier`, `placement`, `network_name`, `creative_identifier`, `revenue`, `revenue_precision`, `dsp_name` and `waterfall_info` — useful telemetry, but again **no reward token**.

**Adapter state machine** for `AppLovinRewardedAdAdapter.ShowAsync`:

```
show_rewarded_ad(unit, placement, nonce)
  ├─ on_ad_display_failed ─────────────────────────────► Error
  └─ on_ad_displayed
       ├─ on_ad_received_reward … then on_ad_hidden ───► Completed  (token = nonce)
       └─ on_ad_hidden with NO preceding reward ───────► Dismissed  (user abandoned)
```

There is **no explicit "abandoned" callback**. Abandon is inferred: `on_ad_hidden` arrived and `on_ad_received_reward` did not, *for that impression*. The adapter must therefore track a per-impression `rewardReceived` flag, keyed by the nonce, and reset it on every `ShowAsync`. **This is the single easiest thing to get wrong in the shim** — a flag that leaks across impressions silently converts abandons into grants. It deserves a dedicated `FakeRewardedAdAdapter` test at M15.

### 6.2 No-fill is cleanly distinguishable ✅

`12` §4.3 / `16` A5 require the adapter to tell no-fill from abandon, because **one grants and one does not**. It can:

[`AppLovinMAX.gd` L870–882](https://github.com/AppLovin/AppLovin-MAX-Godot/blob/release_1_2_0/addons/applovin_max/AppLovinMAX.gd#L870-L882):

```gdscript
enum ErrorCode {
	UNSPECIFIED = -1,
	NO_FILL = 204,
	AD_LOAD_FAILED = -5001,
	AD_DISPLAY_FAILED = -4205,
	NETWORK_ERROR = -1000,
	NETWORK_TIMEOUT = -1001,
	NO_NETWORK = -1009,
	FULLSCREEN_AD_ALREADY_SHOWING = -23,
	FULLSCREEN_AD_NOT_READY = -24,
	NO_ACTIVITY = -5601,
	DONT_KEEP_ACTIVITIES_ENABLED = -5602
}
```

`ErrorInfo` exposes `code`, `message`, `mediated_network_error_code`, `mediated_network_error_message`, `ad_load_failure_info` and `waterfall_info`. So:

| Condition | Signal | Mapping |
|---|---|---|
| **No fill** | `on_ad_load_failed`, `code == 204` | `AdResultKind.NoFill` → **grant + consume cap slot** (`16` A5) |
| Network down / timeout | `on_ad_load_failed`, `code ∈ {-1000, -1001, -1009}` | `NoFill` — same treatment; `12` §4.3 says never punish a network problem |
| Other load failure | `on_ad_load_failed`, other code | `NoFill` (conservative, per §4.3) — but log the code |
| Show while not ready | `-24` `FULLSCREEN_AD_NOT_READY` | `Error` — **our** bug, not the network's. Do **not** grant. |
| Already showing | `-23` | `Error`. Do not grant. |
| Display failure mid-ad | `on_ad_display_failed` | `Error`; a retry is reasonable |
| User abandoned | `on_ad_hidden` without reward | `Dismissed` — **no grant, no cap slot** |

`-23` and `-24` deserve the separate `Error` treatment precisely because folding them into `NoFill` would turn an integration bug into a free-rewards faucet.

### 6.3 ⚠️ The no-fill grant cannot be S2S-verified — a real gap

**The S2S postback fires only after a completed view.** No fill ⇒ there is no ad, no impression, no completion, and therefore **no postback, ever**. AppLovin will never tell our server that a fill failed.

So the `16` A5 ruling has an unavoidable structural consequence that the design docs do not yet record:

> **The no-fill grant path is client-asserted by construction, in the happy path, permanently.** It cannot be otherwise. No plugin, patch or fork changes this.

The server remains *authoritative* — it decides, applies the cap and writes the profile — but its only *evidence* is the client's word that a fill failed. That is precisely the threat model §8's fallback was written for. **Therefore §8 is not contingent on this spike's verdict: build it at M15 either way, scoped to the no-fill branch.**

The exposure is bounded and acceptable, for exactly the reasons `12` §3.3 already anticipated: every reward is hard-capped (`12` §1, §4.3 — 44/day), nothing is purchasable, and the worst case is a player claiming rewards they could have earned by watching. The mitigation is the ordinary one: sign the claim, rate-limit it, and alert on players whose no-fill rate is implausible (§8.3).

---

### 6.4 ⚠️ The bigger finding, which O14 did not ask for

O14 asked whether the plugin could carry our reward model. **It can.** But the spike surfaced something the question did not anticipate and that outranks it:

> **The plugin appears abandoned** — no code change since 2025-04-24, no maintainer response since 2025-04-28, and open third-party reports of it **failing on iOS with Godot 4.5** ([#66](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/66), [#61](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/61)). See §2.4.

`12` §3.1 records the plugin as "actively maintained". **On today's evidence that is no longer true**, and D15's rationale — *"AppLovin ships an official MIT-licensed Godot 4 plugin, which removes the main integration risk"* — is weaker than when it was written. The risk was not removed; it was transferred to us.

This does not reverse D15 and this spike does not propose reversing it. MIT plus complete in-repo native sources means we can always fork, and the S2S mechanism (§4–§5) is unaffected either way. But the decision deserves a conscious re-affirmation with current facts rather than inherited ones — see §10 Q5. **Crucially, this is testable now:** M0-05b's iOS leg is already scheduled, and its result should gate the M15 kickoff. That is exactly the week-1-not-month-4 outcome the spike existed to produce.

---

## 7. Consequences for `IRewardedAdPort`

**The signature in `12` §7 / `23` §4.1 survives completely unchanged.** No application-layer change is needed.

```csharp
public interface IRewardedAdPort {
    bool IsReady(AdPlacementId placement);
    Task<AdOutcome> ShowAsync(AdPlacementId placement, CancellationToken ct);
    Task PreloadAsync(AdPlacementId placement, CancellationToken ct);
}

public readonly record struct AdOutcome(AdResultKind Kind, string? VerificationToken);
public enum AdResultKind { Completed, Dismissed, NoFill, Error }
```

`AdResultKind`'s four cases map exactly onto the observed signal surface (§6.1–6.2) with nothing left over and nothing missing. `12` §3.2's claim that *"either mechanism fits without an application change"* is **confirmed** — with the mechanism described more precisely:

| | What `VerificationToken` carries |
|---|---|
| **S2S path** (`Completed`) | The **client-minted correlation nonce** that was passed as `custom_data`. The server matches it against the postback's `{CUSTOM_DATA}`. |
| **No-fill path** (`NoFill`) | The **signed client assertion** of §8. No postback will ever arrive. |
| `Dismissed` / `Error` | `null`. Nothing is claimed. |

In *both* cases it is an opaque, client-originated correlation string. The application never inspects it; it forwards it to the server on `POST /ad/claim`, exactly as `12` §7 specifies. **One field, one type, two verification regimes on the server side — which is what the design intended.**

Three consequential notes for the adapter, none of which touch the port:

1. **The adapter must mint the nonce before showing**, not after, because it is an *input* to `show_rewarded_ad`. `ShowAsync` therefore generates the nonce, calls the plugin, and returns that same nonce in the outcome.
2. **The claim will usually beat the postback.** `ShowAsync` resolves on `on_ad_hidden`, but the postback may lag "a few minutes". So `POST /ad/claim` frequently arrives **first**, with no postback yet recorded. M15 must choose a reconciliation policy — see §10, open question 1. This is a **server** concern; the port is unaffected.
3. **`PlacementMap.cs` earns a second job**: `AdPlacementId → (MAX ad-unit ID, MAX placement string)`, so `{PLACEMENT}` can be cross-checked server-side (§5.1 step 4).

---

## 8. The fallback design (build it anyway — §6.3)

Written up in full so M15 can implement without re-research. Required for the **no-fill branch regardless of the S2S verdict**; also the complete answer if S2S is ever unavailable (dashboard misconfiguration, an ad unit without a callback URL configured, or a future plugin regression).

### 8.1 The nonce

The **server** issues it. The client must never mint its own, or the scheme proves nothing.

```
POST /ad/intent  { placementId }
     ← 200 { nonce, expiresAtUtc }
```

`nonce` is the base64url of a compact signed token — HMAC-SHA256 under a **server-only** key (never shipped to the client):

| Field | Purpose |
|---|---|
| `playerId` | who |
| `placementId` | which of the 29 placements (`12` §4) |
| `issuedAtUtc` | freshness anchor |
| `expiresAtUtc` | `issuedAtUtc + 5 min` — longer than any rewarded ad, short enough to bound stockpiling |
| `jti` | random 128-bit unique id → **single-use** |
| `sig` | `HMAC-SHA256(all of the above, serverKey)` |

The nonce is what the adapter passes to `show_rewarded_ad(unit, placement, nonce)` as `custom_data`, and what comes back in `AdOutcome.VerificationToken`. **The same nonce serves both regimes** — which is exactly why the port signature does not change.

Size check: comfortably under the 8192-character `{CUSTOM_DATA}` limit (§5).

### 8.2 The claim

```
POST /ad/claim  { nonce, outcome }        // outcome ∈ { Completed, NoFill }
```

The server checks, in order, rejecting on the first failure:

1. **Signature** valid under the server key (constant-time compare).
2. **Not expired** (`now ≤ expiresAtUtc`), and not implausibly fast (`now − issuedAtUtc ≥ floor`; see §8.3).
3. **`jti` unused** — burn it atomically. Kills replay.
4. **`playerId` matches the authenticated session.** Kills cross-account claims.
5. **`placementId` matches** the placement being claimed, and is a real placement.
6. **Cap not exhausted** — per-run or per-day, per `12` §4.1–4.3, *and* the 44/day global soft cap.
7. **Minimum 20 s gap** since the player's last rewarded grant (`12` §4.3).
8. **If a matching S2S postback has already been recorded for this nonce** → this is the strong path; grant on the postback's authority and mark the claim corroborated.
9. **Else, per §8.3 plausibility limits** → grant, and flag the claim as *unverified* in telemetry.

Then, as `12` §7 requires: apply the reward to the authoritative profile, return the profile delta, and let the client animate what it was told about.

### 8.3 Plausibility limits on unverified claims

These carry the weight when there is no postback. All 📐 TUNABLE in `data/ads.json` alongside the caps:

| Limit | Value | Rationale |
|---|---|---|
| Minimum `Completed` dwell | **≥ 15 s** between `/ad/intent` and `/ad/claim` | A rewarded video cannot complete faster. A flood of 2-second completions is a script. |
| Minimum `NoFill` dwell | **≥ 1 s** | No-fill is *fast* — that is its signature. Do not require a long dwell here. |
| Unverified-claim ratio | Alert when a player's `NoFill` share exceeds **~30%** over a rolling 7-day window, against the population median | Genuine fill rates cluster tightly by geography; a permanent outlier is airplane-mode farming. |
| Consecutive `NoFill` | Soft-throttle after **10** in a row | Bounds the airplane-mode exploit `12` §4.3 explicitly names. |
| Global daily cap | **44** (`12` §4.3), enforced server-side over `Completed` + `NoFill` **combined** | 🔒 **The real ceiling.** No-fill consumes a slot precisely so it cannot be a bypass. |

**Why this is tolerable — the argument from `12` §1, unchanged.** The worst case for a fully successful attacker is *the reward set a diligent free player already gets by watching*. That is the hard ceiling, and it is not a large number: **44 rewarded impressions/day**, every one of them capped per-placement. Nothing in the game is purchasable (`12` locked decisions 4, 8), there are no whales to undercut, and Slay Plus grants **exactly** the same capped amount (`12` §2), so a cheater cannot even out-earn a subscriber. The fairness contract in §1 — *"no ad reward may be uncapped"* — is what converts this from a monetisation hole into a bounded annoyance. In a pay-to-win economy this fallback would be unacceptable; here, it costs the game nothing it can measure.

The one thing that would break the argument: **if any future placement escapes the cap**, this fallback becomes unsafe immediately. `12` §1 already forbids that. Worth restating at the M15 kickoff.

---

## 9. Residual risk — the honest limits of a source-and-docs verdict

This spike read code and documentation. It did **not** run an ad. The following are **not** established, and M15 must confirm them on a real device:

| # | Unverified | Why it could not be checked | Confidence |
|---|---|---|---|
| R1 | That `custom_data` **actually arrives** in `{CUSTOM_DATA}` on a live postback, unmangled, on both platforms | Needs an AppLovin account, a configured ad unit, a callback endpoint and a device build | **High** — the call chain is verified to the SDK boundary on both platforms, and this is standard, long-standing MAX behaviour. But the last hop is inside a closed-source SDK. |
| R2 | That `{EVENT_TOKEN_ALL}`'s exact canonicalisation matches our implementation | Only one worked example is published, and it is for the SHA-1 `{EVENT_TOKEN}` form | **Medium.** "All macros alphabetically, URL-decoded" leaves separator and empty-value handling ambiguous. **Budget a half-day of trial-and-error at M15**, and build the verifier behind a fixture-driven test. |
| R3 | Whether `{USER_ID}` arrives empty or absent when never set | Not documented | Low impact — we do not use it. Handle both. |
| R4 | Real-world no-fill rates and whether `204` is the dominant failure code in practice | Needs live traffic | Medium — §8.3's thresholds are **estimates** and must be re-tuned against real data before they gate anything. |
| R5 | 🔴 **That the plugin builds and runs at all against the Godot 4.x we ship — especially on iOS.** Third parties report it **failing on Godot 4.5 iOS** and working only after rebuilding against matching Godot headers ([#66](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/66), [#61](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/61)); the repo has had no code change since 2025-04-24 and no maintainer reply since 2025-04-28 (§2.4). | Needs the M0-05 / M0-05b build path: Java 17, Gradle, Xcode, CocoaPods | **This is now the dominant risk, and it is far larger than the S2S question O14 was raised to answer.** It belongs to O23 / M0-05b and M15, and it should be escalated on this spike's authority. |
| R6 | Postback ordering vs. the client claim under real latency | Needs live traffic | Medium — drives the §10 Q1 decision |
| R7 | Whether AppLovin ever sends a postback for a *rewarded* view the client did **not** see completed | Not documented | Low — the idempotency + nonce-matching design tolerates it either way |

**On the method itself:** a source verdict is *stronger* than a docs verdict for existence questions (§3, §4, §6.2 are settled facts about the code, not inferences) and *weaker* for behavioural questions (§5's wire format, R1–R2, R4, R6). Read this document accordingly: **the "can we?" is answered; some of the "exactly how?" is not.**

---

## 10. Open questions for the M15 kickoff

1. **Claim-before-postback reconciliation** (from §7 note 2 — the one that needs a real decision). When `POST /ad/claim` arrives with a valid nonce and no postback yet, do we:
   (a) **grant immediately** and treat a later postback as corroboration — best feel, weakest guarantee; or
   (b) **hold** the grant for a short window (~10 s) awaiting the postback, then fall back to (a) — a visible delay on every ad; or
   (c) grant immediately but mark it **provisional**, and reconcile asynchronously, alerting on players whose corroboration rate is an outlier?
   **Recommendation: (c).** It preserves the instant reward the UX wants, keeps the strong signal where it is useful — in aggregate, for abuse detection — and never makes an honest player wait on AppLovin's retry schedule. Note that (a) and (c) differ only in telemetry, and that difference is the whole value.
2. **Do we adopt §8's signed nonce for `Completed` too, or only for `NoFill`?** §6.3 forces it for `NoFill`. Using one scheme for both is simpler, costs one extra round-trip per ad (`/ad/intent`), and makes the two paths structurally identical. **Recommendation: both** — the `/ad/intent` call can be folded into the existing "ad offered" telemetry event.
3. **Event Key secret management** — where does it live, who rotates it, and what happens to in-flight postbacks during rotation? (Accept both old and new keys for a window.)
4. **Should we fork the plugin to add `setUserId` anyway**, as belt-and-braces attribution alongside `custom_data`? **Recommendation: no** — §3 shows the code is trivial but the prebuilt-binary rebuild is not, and `custom_data` is strictly better for us. Revisit only if R1 fails on device.
5. 🔴 **Plugin abandonment (§2.4, R5) — the biggest question this spike raises, and it is bigger than O14 itself.** The bridge has had no code change in 15.5 months, no maintainer reply in as long, and open third-party reports of **iOS failing on Godot 4.5**. `master` == 1.2.0, so "pin vs. track" is moot — there is nothing to track. The real question is the contingency:
   (a) vendor 1.2.0 and **plan to maintain our own fork** (MIT permits it; the native sources are complete and in-repo, so this is viable — but it means owning Gradle + Xcode plugin builds indefinitely);
   (b) **switch mediation** — [#61](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/61) records users migrating to AdMob/`godot-admob`, which would reopen **D15**;
   (c) proceed and re-evaluate after M0-05b proves or disproves the iOS build.
   **Recommendation: (c) now, and treat (a) as the expected steady state** — but this needs a real ruling, and because it can reopen D15 it belongs in `16_DECISION_LOG.md`, not only in the M15 milestone. **Sequence it against M0-05b's result before M15 starts**, because if iOS cannot be made to build, the ad shim's schedule is not the thing that breaks — the platform target is.
6. **Vendor support is not available to us.** [#59](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/59) ("document how to integrate via C#") has sat unanswered for 14 months. `12` §3.2's **3–5 engineering days** for the shim should be read as *fully self-supported*, with no vendor escalation path when something is ambiguous. Confirm that estimate still stands under that assumption — and add the `set_test_device_advertising_identifiers` breakage ([#62](https://github.com/AppLovin/AppLovin-MAX-Godot/issues/62)) to the M15 task list, since test devices are needed to verify R1 at all.
7. **Interstitials** (`12` §6) were out of scope here. `show_interstitial` has the same `placement`/`custom_data` shape in the source, but interstitials have no reward and thus no S2S path — worth a sentence at kickoff to confirm nothing is expected of them.
8. **Do the §8.3 thresholds gate anything at launch, or only alert?** **Recommendation: alert only** until R4 gives real fill-rate data. Throttling honest players in poor-connectivity geographies on day one would be a self-inflicted retention wound, and retention is revenue (`12` §9).

---

## 11. Recommended doc amendments

The spike contradicts nothing locked, but three passages in `12_MONETIZATION_ADS.md` are now inaccurate or incomplete. Left for the M15 kickoff to apply (this spike deliberately does not edit locked design docs):

| Where | Change |
|---|---|
| `12` §3.3, the ⚠️ **NEEDS DETAIL — VERIFY EARLY** block | **Resolved.** Replace with the §1 verdict: S2S is viable via `custom_data`; `setUserId` is absent but unnecessary; no fork required. Link here. |
| `12` §3.2 / §7, "*it carries the S2S token*" | **Correct the mechanism.** MAX issues the client no token. The field carries a server-issued correlation nonce in both regimes. The *signature* is unchanged (§7). |
| `12` §4.3, "*the server applies this rule, not the client*" | **Add the §6.3 caveat.** True about authority, but no postback exists for a no-fill, so the server's only evidence is the client's claim. Cross-reference §8. |
| `12` §3.1, "actively maintained" | **No longer true** (§2.4). Restate as: last release 1.2.0, 2025-04-24; no code change or maintainer response since; treat as vendored-and-self-maintained. |
| `16` Part D, **O14** | Mark **resolved**, 2026-08-11, verdict "proceed, no patch", pointing here. |
| `16` Part C / D15 | **New risk worth registering:** MAX Godot bridge abandonment + reported Godot 4.5 iOS breakage (§2.4, §6.4, R5). Gate on M0-05b's iOS result before M15. This is a D15-adjacent supply-chain risk, not an M15 task. |
