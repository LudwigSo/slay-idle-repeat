# 12 — Monetization: Rewarded Ads & Slay Plus

🔒 LOCKED DECISIONS
1. Monetization is **rewarded ads** plus **one** paid product: **Slay Plus**, an auto-renewing subscription.
2. **Price: €4.99 / $4.99 per month.** Monthly only. No annual tier in v1.
3. Plus **removes all ads AND auto-grants every rewarded-ad benefit** without watching.
4. There are **no** other paid items. No currency packs, no gear, no gacha, no battle pass, no lifetime unlock, no consumables.
5. **No banner ads in v1.** Interstitials and rewarded video only.
6. **No cosmetic rewards exist in the game.** Plus grants utility and time, never appearance.
7. Ad network: **AppLovin MAX** mediation.

---

## 1. The fairness contract

Because Plus auto-grants every rewarded-ad benefit, the design is bound by one rule that must be checked on every single placement:

> **Every rewarded-ad benefit must be fully reachable by a free player who watches ads. Therefore no ad reward may exist that a free player cannot obtain by watching, and no ad reward may be uncapped.**

Consequences that fall out of this rule:

- Every placement has a **hard daily or per-run cap**. An uncapped ad reward would give a subscriber infinite value.
- A **subscriber receives exactly the capped amount**, granted automatically. Not more.
- A player who watches every available ad and a subscriber are **on the identical power curve**. The subscription buys *time and attention*, never *power*.
- Free players who watch **no** ads sit on a slower curve. The economy simulator (`21_ECONOMY_SIMULATOR_SPEC.md`) enforces that this gap never exceeds **45% at day 30**.

State this contract in the store listing. It is the product's differentiator.

---

## 2. Slay Plus (the subscription)

| Property | Value |
|---|---|
| Product ID | `slayidlerepeat.plus.monthly` |
| Type | Auto-renewing subscription, monthly |
| Price | **€4.99 / $4.99** per month, regional equivalents via store price tiers |
| Free trial | **7 days**, once per account 📐 TUNABLE |
| Grants | 1. No interstitial ads, ever. 2. Every rewarded placement in §4 converts to an instant one-tap **CLAIM** with no ad, at the same daily cap. 3. Unlimited talent and loadout presets (free players get 3). 4. A "Plus" tag beside the player name on the ladder — **text only, no art asset.** |
| Does **not** grant | Any stat, currency, drop rate, energy cap, chapter, difficulty tier, pet, mount, gear or PvP advantage beyond the capped ad-reward equivalence. **No pity-counter progress of any kind** (`24` §2). **No information advantage in PvP** — see §2.5. |
| Restore | Standard platform subscription restore, plus server-side entitlement bound to the account |
| Family sharing | Enabled where the platform allows |

### 2.1 Entitlement is server-owned 🔒

Because PvE is server-authoritative (`14_TECHNICAL_ARCHITECTURE.md` §2), the Plus entitlement lives on the server, not the device.

```
Store (Google Play / App Store)
    → server-to-server subscription notification webhook
    → Slay Idle Repeat backend updates player.entitlements.plus = { active, expiresAtUtc, source }
    → client reads entitlement from the session payload; never decides for itself
```

The client **never** grants Plus benefits based on a local receipt. It asks the server. This closes the whole class of receipt-spoofing exploits and makes lapse handling trivially correct.

### 2.2 Lapse handling 🔒

When a subscription lapses, expires or is refunded:

| Rule | Behaviour |
|---|---|
| Progress | **Nothing is lost. Ever.** All levels, gear, pets, mounts, talents, currencies and PvP rating are untouched. |
| Ad rewards | Auto-grant stops. The player sees the normal `▶ ad` buttons again and can obtain exactly the same rewards by watching. |
| Interstitials | Return, subject to the caps in §6. |
| Presets | Presets beyond the free 3 become **read-only**, not deleted. The player can still load them; they cannot save new ones until they resubscribe or delete down to 3. |
| Grace period | Honour the platform grace/billing-retry window (Google: up to 30 days; Apple: up to 60 days). Plus stays active throughout. |
| Re-subscribe | Instant restoration, no re-onboarding. |
| Notification | One in-app message when Plus ends. No nagging, no repeated prompts. |

> This is the single most important thing to get right about a subscription in a game with no other purchase. **A lapsed subscriber must never feel punished — only un-accelerated.** If a player who cancels experiences their account getting worse, the reviews will say "pay to keep what you had", and that reputation is unrecoverable.

### 2.3 Why a subscription is the harder sell here — and how the design answers it

⚠️ **NEEDS DETAIL / RISK:** A recurring charge to remove ads is a harder proposition than a one-time unlock, and this genre's audience is vocal about it. The design mitigations below are deliberate; they should be validated with a store-page test before launch.

| Risk | Mitigation built into the design |
|---|---|
| "Renting the ability to not be annoyed" | Plus is framed and priced as *time saving*: it auto-grants ~30 rewarded ads per day, which is 15–25 minutes of watching. The store copy states the ad-minutes saved per day explicitly. |
| Subscription fatigue / principled refusal | Interstitial load is kept genuinely low (§6) so the free experience is tolerable rather than coercive. A player who never subscribes must still enjoy the game. |
| Lapse resentment | §2.2 — nothing is lost, ever. |
| Review damage | Honest store copy (§2.4) and no dark patterns anywhere in the purchase flow. |
| Regulatory | Clear auto-renew disclosure, one-tap cancel path surfaced inside the app, no pre-checked trial upsells. |

**Open recommendation, not accepted:** offer a **€24.99 lifetime unlock** alongside the subscription. It converts the substantial audience that refuses subscriptions on principle, and at ~5 months' equivalent it is not obviously worse revenue. Currently held as the standing hedge against risk **R1** in `16_DECISION_LOG.md` Part C — to be reconsidered if week-1 review sentiment turns on the subscription.

### 2.4 Presentation rules 🔒

- Surfaced **only** in the Shop's "Plus" tab and via **one** non-modal banner on the Home screen after Legend Level 8.
- Never a pop-up. Never mid-run. Never after a death. Never with a countdown timer or fake discount.
- The purchase screen honestly lists what it does *and* states:

> *"You can earn every one of these rewards for free by watching ads. Plus saves you about 20 minutes of watching a day, and supports development. Your progress is yours forever, subscribed or not."*

- The renewal price, renewal date and a direct link to the platform's cancel flow are shown **inside the app**, on the Plus tab, at all times while subscribed.

---

### 2.5 Battle-log replay history is free for everyone 🔒

Replay history for the last **50 runs and duels** was previously a Plus grant. It is now a **base feature available to every player**, subscribed or not.

The reason is narrow and important: duel replays let a player study the opponents they faced, and in an asynchronous ladder where you choose one of three candidates (`11` §4.2), that is a **PvP information advantage**. The fairness contract in §1 bounds *power*; it said nothing about *information*, and a subscriber-only scouting tool is exactly the kind of quiet edge that turns "no pay-to-win" from a promise into a technicality.

Making replays free costs the subscription one bullet point and costs the game nothing — the logs are already written to object storage for anti-cheat purposes (`14` §7.1).

**The general rule this establishes:** Plus may grant *time* and *convenience*. It may never grant *power* or *information*.

---

## 3. Ad network integration — AppLovin MAX 🔒

### 3.1 The official plugin

**AppLovin ships an official, MIT-licensed Godot 4 plugin:** <https://github.com/AppLovin/AppLovin-MAX-Godot>

| Property | Detail |
|---|---|
| Engine support | **Godot 4.x** (explicitly 4.x only) |
| Platforms | Android and iOS, with native Java / Objective-C++ implementations |
| Latest release | 1.2.0 (April 2025), 7 releases to date — actively maintained |
| Licence | MIT |
| Distribution | Godot Asset Library, or the repo's `/addons`, `/ios`, `/android` directories |
| **Language** | ⚠️ **GDScript only.** There is no C# binding. |

### 3.2 The C# gap, and where it is quarantined 🔒

Slay Idle Repeat is a **C# project** (D1), and the plugin exposes a GDScript API (`AppLovinMAX.initialize(...)`, `AppLovinMAX.InitializationListener`). This does **not** require a native bridge — Godot's .NET build interoperates with GDScript freely — but it does require a thin shim.

Per **D22 (ports and adapters, `23_PORTS_AND_ADAPTERS.md`)**, every line of that shim lives inside **one adapter project** and nothing outside it knows AppLovin exists:

```
SlayIdleRepeat.Application/Ports/Client/IRewardedAdPort.cs      ← the game's entire ad vocabulary
        ▲
        │  implemented by
        │
SlayIdleRepeat.Adapters.Ads.AppLovin/
   ├── AppLovinRewardedAdAdapter.cs   : IRewardedAdPort
   ├── MaxBridge.cs                   C# wrapper: GetNode() → Call() → Connect() to signals
   ├── max_bridge.gd                  autoload: wraps AppLovinMAX, re-emits callbacks as signals
   ├── addons/applovin_max/           the vendor GDScript plugin (MIT)
   └── PlacementMap.cs                AdPlacementId → MAX ad-unit ID
```

Three adapters satisfy that one port:

| Adapter | Used by | Behaviour |
|---|---|---|
| `AppLovinRewardedAdAdapter` | Free players | Real ad via MAX |
| `AutoGrantAdAdapter` | **Slay Plus subscribers** | Returns `Completed` immediately, no ad, under the identical daily caps |
| `FakeRewardedAdAdapter` | Tests, CI, economy simulator | Scriptable outcomes; no SDK, no Gradle, no fill |

🔒 **The Plus promise is therefore an adapter swap, not a branch.** There is no `if (isSubscriber)` anywhere in the game — the composition root picks the adapter from the server-issued entitlement (`23` §7.2). That is the cleanest possible expression of the fairness contract in §1, and it makes the two paths structurally incapable of drifting apart.

It also means **the game compiles and runs with no ad SDK present at all**, which is what lets the economy simulator and every application test run headless.

**Revised effort: 3–5 engineering days**, not the 2–3 weeks previously budgeted for a from-scratch native plugin. The saving is real and material.

What remains genuinely non-trivial is the **build pipeline**, not the code:

| Platform | Requirement |
|---|---|
| Android | Custom Godot build template, Gradle, **Java 17** (Godot 4.3+), manual AAR copy and `build.gradle` edit |
| iOS *(post-launch — `16` D34)* | Export project → author a `Podfile` → `pod install --repo-update` → build in Xcode. App Store Team ID and Bundle Identifier required at export time. ⚠️ **Corrected by the O23 spike:** Godot never emits a `Podfile` — that step is AppLovin's alone — and iOS C# is **NativeAOT + trimming**, not Mono. 🔴 The MAX Godot plugin has unfixed iOS build (#61) *and* runtime-init (#60) failures and is 3 minors behind the 4.7.1 pin. See `docs/spikes/O23-godot-ios-export.md`. |

Both mean **the ad build cannot be produced by a plain one-click Godot export.** CI must run the full custom-template path from day one, or ads will only ever work on one developer's machine.

### 3.3 Integration requirements

| Concern | Specification |
|---|---|
| Mediation | **AppLovin MAX**, with at minimum AdMob, Meta Audience Network, Unity Ads and Liftoff as bidding partners |
| Formats used | Rewarded video, interstitial. **No banners, no MREC, no native, no app-open ads.** |
| Preloading | Keep 2 rewarded and 1 interstitial preloaded at all times. Reload immediately on show. |
| Reward verification | Use MAX **server-side reward callbacks (S2S)** into the Slay Idle Repeat backend. Since the backend is authoritative for all progression, the reward is granted server-side on callback and the client cannot fabricate a completion. ⚠️ **See the risk below — this is the one thing to verify before committing.** |
| Consent | GDPR/DSA consent via MAX's consent flow (EU), ATT prompt (iOS) **after** FTUE, COPPA-safe configuration, and a documented no-personalised-ads path. |
| Test mode | The composition root registers `FakeRewardedAdAdapter` in QA and CI builds — scriptable outcomes, no SDK, no fill dependency. An adapter swap, not a branch. |

⚠️ **NEEDS DETAIL — VERIFY EARLY:** the plugin's public documentation does not mention **user-identifier setting or server-side rewarded callbacks**. Our entire ad-reward model depends on S2S grants (§7), because the client is not trusted with progression. Before writing the shim, confirm on a spike build whether the Godot plugin exposes MAX's `setUserId` / S2S reward callback surface.

- **If it does:** proceed exactly as specified.
- **If it does not:** either extend the plugin (it is MIT-licensed and the native layer is right there — a small upstream patch or fork), or fall back to client-asserted completion with a signed nonce and server-side rate/plausibility limits. The fallback is weaker but tolerable, because ad rewards are all hard-capped (§1) and nothing in the game is purchasable — the worst case is a player granting themselves rewards they could have earned by watching, which is a far smaller problem than it would be in a pay-to-win economy.

---

## 4. The rewarded ad catalogue (29 placements)

### 4.1 In-run placements (13)

| # | ID | Trigger point | Reward | Cap |
|---|---|---|---|---|
| 1 | `AD_REVIVE` | Hero reaches 0 HP | Revive at 50% Max HP, restart that battle, 2 s invulnerability. **Works on bosses.** 🔒 | **1 per run** |
| 2 | `AD_DOUBLE_RUN_REWARDS` | Run results screen | ×2 all banked run rewards | 1 per run |
| 3 | `AD_REROLL_PERK` | Perk draft screen | Reroll the 3 options | 2 per run |
| 4 | `AD_EXTRA_PERK_CHOICE` | Perk draft screen | Add a 4th option from a rarity-upgraded pool | 1 per run |
| ~~5~~ | ~~`AD_REROLL_DICE`~~ | — | ⚠️ Removed with the die's special faces and the reroll (`04` §5, `16` D41). The in-run placement list is **12, not 13**, and the global in-run impression cap moves 17 → 15 with it. |
| 6 | `AD_SHOP_REFRESH` | Shop tile, after the free refresh | Refresh all 4 offers | 2 per run |
| 7 | `AD_SHOP_FREEBIE` | Shop tile | Take one offer for free | 1 per run |
| 8 | `AD_SKIP_CURSE` | Landing on `TILE_CURSE` | Nullify the curse, keep any attached reward | 1 per run |
| 9 | `AD_DOUBLE_CHEST` | `TILE_TREASURE` resolution | ×2 the treasure contents | 2 per run |
| 10 | `AD_RETRY_MINIGAME` | Minigame failure | One retry | 1 per run |
| 11 | `AD_CAMPFIRE_HEAL` | `TILE_CAMPFIRE` | Additional 30% Max HP heal on top of the chosen option | 1 per run |
| 12 | `AD_ELITE_GUARANTEE` | Before an Elite battle | Guarantee the Elite drops A-rarity or better | 1 per run |
| 13 | `AD_BOSS_SECOND_WIND` | **Boss pre-fight banner**, before the battle starts | Arms a one-time auto-heal: the first time you drop below 30% HP in that fight, heal 40% Max HP | 1 per run |

> Note on #13: offered **before** the boss battle begins, never during it. The auto-heal becomes part of the build snapshot the simulator consumes, so the pre-computed-log model (`05` §Intro) and the "no taps during combat" lock both hold.

**Maximum in-run: 17 impressions per run.** In practice a player takes 3–6.

### 4.2 Meta placements (16)

| # | ID | Location | Reward | Cap |
|---|---|---|---|---|
| 14 | `AD_ENERGY` | Home / low-energy prompt | +40 Energy | 4/day |
| 15 | `AD_DOUBLE_QUEST` | Daily quest claim | ×2 one quest's reward | 3/day |
| 16 | `AD_FREE_GEAR_CHEST` | Home screen chest widget | One free gear chest (chapter-appropriate rarity, defined in `24` §4.0a) | 2/day |
| 17 | `AD_FREE_PET_EGG` | Menagerie | One free Pet Egg | 1/day |
| 18 | `AD_ENHANCE_LUCK` | Forge, before an enhance attempt | +15 percentage points to the success roll | 3/day |
| 19 | `AD_DOUBLE_HONOR` | After a Ghost Duel | ×2 Honor from that duel | 3/day |
| 20 | `AD_EXTRA_DUEL` | Arena | +1 duel attempt | 2/day |
| 21 | `AD_MERGE_DUST` | Forge | Merge Dust bundle (scales with progress) | 2/day |
| 22 | `AD_CROWNS` | Home | Crowns bundle (scales with progress) | 3/day |
| 23 | `AD_FEED_BUNDLE` | Menagerie | Beast Feed bundle | 2/day |
| 24 | `AD_LUCKY_WHEEL` | Home widget | An extra spin of the daily Lucky Wheel | 2/day |
| 25 | `AD_SHOP_REDRAW` | Shop, Daily tab | Redraw the 6 daily offers | 1/day |
| 26 | `AD_FREE_RETRY` | Run results, after a loss | Re-enter the same chapter with no Energy cost | 2/day |
| 27 | `AD_DOUBLE_LEGEND_XP` | Chapter select | ×2 Legend XP for the next run | 1/day |
| 28 | `AD_ENHANCE_STONES` | Forge | Enhance Stone bundle | 2/day |
| 29 | `AD_EXTRA_DUNGEON` | Dungeon select (`25` §6) | +1 Resource Dungeon entry, any dungeon | 3/day |

**Maximum meta: 36 impressions/day** across **16** meta placements. Total catalogue: **29 placements.**

🔒 **No placement in this catalogue may advance, reset or protect a pity counter** (`24` §2). `AD_ELITE_GUARANTEE` and `AD_ENHANCE_LUCK` remain one-shot boosts to a single roll and touch no counter.

### 4.3 Global caps

Meta caps are **per day**; in-run caps are **per run**. A player doing 10 runs could theoretically be offered 170 + 36 impressions, so the global daily cap is what actually bounds the experience.

| Cap | Value |
|---|---|
| Total rewarded impressions per day (soft cap) | **44** — set above the 36 meta placements so a player who exhausts the meta layer can still take in-run rewards 📐 |
| Behaviour past the soft cap | Buttons remain visible but show "Come back tomorrow" |
| Minimum gap between two rewarded ads | 20 s |
| Ad load failure or no fill | **Grant the reward anyway, and consume the cap slot.** 🔒 Never punish a player for a network problem or an unfilled slot — but do not let airplane mode become a free-rewards exploit. Since rewards are granted server-side, the server applies this rule, not the client. |

📐 TUNABLE: every cap in this section lives in `data/ads.json` and is remotely configurable.

---

## 5. Reward scaling

Currency-bundle ads (`AD_CROWNS`, `AD_MERGE_DUST`, `AD_FEED_BUNDLE`, `AD_ENHANCE_STONES`) scale with player progress or they become worthless by chapter 5.

```
AdBundleValue(currency) = BaseValue(currency) * (1 + 0.35 * highestChapterCleared)
```

**BaseValues 🔒 (ruled in `16` A7):**

| Placement | BaseValue |
|---|---|
| `AD_CROWNS` | **600 Crowns** — 🔒 the anchor (`16` A7) |
| `AD_ENHANCE_STONES` | 24 Enhance Stones 📐 |
| `AD_MERGE_DUST` | 100 Merge Dust 📐 |
| `AD_FEED_BUNDLE` | 75 Beast Feed 📐 |

The three material bundles are each worth exactly 600 Crowns at the Materials-tab rates (`10` §5.2: 25 / 6 / 8 Crowns per unit), which are themselves the Honor-shop ratios (`11` §7) re-expressed in Crowns — so every material ad is one `AD_CROWNS` of value, spent in a different currency. 📐 TUNABLE, in `data/ads.json`; if the Materials-tab rates move, these move with them.

---

## 6. Interstitial ads

Interstitials are the only *forced* ads and the main thing Plus removes. They must be rare enough that the free game does not feel hostile — this is load-bearing for the subscription's reputation.

| Placement | Frequency |
|---|---|
| After a **Stage Gate** | Every 3rd stage gate crossed, **counted across runs** (a run has 2 gates), max **1 per run** |
| Returning to Home from a run | Every 4th return |
| Never | Mid-battle, mid-draft, during FTUE, in the first 3 days after install, after a death, while a rewarded reward is being granted, or during a reconnect |

| Cap | Value |
|---|---|
| Interstitials per session | 4 |
| Interstitials per day | 12 |
| Minimum gap | 120 s |

**Banner ads: not shipped in v1.** 🔒 Banner revenue is a small fraction of rewarded revenue and a persistent banner in a portrait board game costs real screen space and perceived quality. Revisit only with data.

### 6.1 First-3-days grace period

No interstitials for the first 72 hours after install. Rewarded ads are available (they're opt-in and valuable). This protects D1/D3 retention, which is where the whole business lives. The Plus offer is not shown before Legend Level 8.

---

## 7. Technical interface

The port is `IRewardedAdPort`, owned by `SlayIdleRepeat.Application` (`23` §4.1):

```csharp
public interface IRewardedAdPort {
    bool IsReady(AdPlacementId placement);
    Task<AdOutcome> ShowAsync(AdPlacementId placement, CancellationToken ct);
    Task PreloadAsync(AdPlacementId placement, CancellationToken ct);
}

public readonly record struct AdOutcome(AdResultKind Kind, string? VerificationToken);
public enum AdResultKind { Completed, Dismissed, NoFill, Error }
```

`VerificationToken` is in the signature deliberately: it carries the S2S token when the plugin supports server-side callbacks, and the signed client nonce when it does not (see the O14 fallback above). Either mechanism fits **without an application change**.

The rest of the codebase is unaware of which implementation is active. The **server** decides, via the entitlement in the session payload; the composition root acts on it.

**Reward flow (server-authoritative):**
```
Client: POST /ad/claim { placementId }
Server: validate cap, validate entitlement or MAX server-side callback token
Server: apply reward to the authoritative profile
Server: return updated profile delta
Client: animate the reward it was told about
```
The client never applies an ad reward locally.

---

## 8. What we will not do

An explicit "no" list, because in this genre the pressure to add these will be constant:

- ❌ Timed offers, countdown popups, "limited time" framing
- ❌ Starter packs, first-purchase bonuses, value-comparison badges
- ❌ Currency, energy, gear or gacha sold for money
- ❌ Battle pass or season pass
- ❌ VIP levels, spend milestones, subscription *tiers*
- ❌ Any Plus benefit that is a power advantage beyond the capped ad equivalence
- ❌ Ads shown after a defeat
- ❌ Interstitials during FTUE
- ❌ Banner, MREC, native or app-open ads
- ❌ Punishing lapsed subscribers in any way
- ❌ Cosmetic monetization (there are no cosmetics in v1 at all)

---

## 9. Revenue model and risk

⚠️ **NEEDS DETAIL:** No validated revenue model exists. For planning, the shape of this business:

- Rewarded-ad ARPDAU in this genre typically lands in **$0.02–$0.09** depending on geography and ad depth. Our design (29 placements, 44/day cap, MAX mediation) sits at the deep end of that range.
- Subscription conversion for a "remove ads" sub in a free mobile game is typically **0.5–2% of MAU**, materially lower than the 2–5% a one-time unlock achieves — but each converter is worth far more over time. Break-even against a €5.99 one-time purchase is ~1.5 months of retention.
- Median subscription lifetime in casual mobile is **3–6 months**, so a converter is worth roughly €12–25 net of store fees.
- The absence of whales means revenue scales close to linearly with DAU. **This game lives or dies on retention and volume, not on monetisation depth.**

The clear implication for the whole project: **retention work is revenue work.** Time spent on run feel, drop cadence and progression clarity is worth more than time spent on ad placements.

### 9.1 Metrics to instrument from day one

`plus_offer_viewed`, `plus_trial_started`, `plus_trial_converted`, `plus_renewed`, `plus_cancelled`, `plus_lapsed`, `plus_resubscribed`, plus per-placement `ad_offered / started / completed / failed`, and the derived **ad-minutes-per-DAU** figure that justifies the subscription's price in store copy.
