# Screen Design Brief — every screen that needs to be designed

Derived from `game-design/13_UI_UX_SCREENS.md` §1 (the screen inventory) plus the owning
system documents. Each entry states **what the screen must show and what the player can do
on it** — enough to design against without re-reading the design set.

Status: **✅ client scene exists** in `src/SlayIdleRepeat.Client/game/scenes/`
(existence, not visual completeness) · **⬜ not built**.

---

## 0. Rules that apply to every screen (not repeated per screen)

- Portrait only, 1080×1920 design canvas, 9:16 → 9:20, safe-area padding. One-handed: the primary
  action lives in the bottom third.
- Fonts: **Baloo 2** (headings, numbers, buttons), **Nunito Sans** (body). Minimum touch target
  48×48 dp, 8 dp spacing. Panels: 24 dp radius, 3 dp dark outline, inner gradient, drop shadow.
  Buttons chunky; pressed = 4 dp down + darker fill.
- Rarity colours C `#9AA5B1` · B `#4CAF50` · A `#3B82F6` · S `#F5A623` · SS `#C13BE8`, and rarity
  must **also** read as frame shape + gem symbol (colourblind requirement).
- Numbers abbreviated above 10k (`12.4k`), full value on long-press. Currency icon always adjacent
  to its number, never text-only.
- No transition longer than 300 ms, all skippable. Reduced motion, no-timer mode, 3 text sizes
  (100/115/130 %), left-handed mirror and a haptics toggle must all be honoured by every layout.
- Localisation EN + DE from day one; **German runs ~30 % longer** — every label must be tested with
  the German string at 130 % text size.
- Connection states are global overlays, never per-screen modals: a *Reconnecting…* pill top-centre,
  server-dependent buttons dimmed to 40 % with a cloud-slash glyph, a resync green flash + "Caught up."
  Never a full-screen blocking error during a run.
- PvP tier and Slay Plus are **text labels in the tier colour** — no frames, badges or cosmetics
  anywhere in v1.
- Bottom navigation is fixed at **five** items: Hero · Forge · Talents · Beasts · Arena. Shop is a
  header icon. Dungeons, Events, Guilds and Inbox enter from Home cards or Chapter Select.

---

## 1. Boot and onboarding

### S01 — Splash / Loading ✅
Logo plus the four boot steps it is actually waiting on: auth, server session, profile fetch,
content-hash check. Needs visible failure/retry states (no server, content mismatch, forced update),
because everything downstream is server-authoritative.

### S02 — FTUE Tutorial Run ⬜ (overlay layer on S05/S06/S07)
Not a standalone screen: a caption + spotlight layer over the board. 12-tile scripted board, no
stage gates, forced die sequence, beats 0–10 plus 6b. Teaches roll → auto-combat → perk draft →
treasure → shop → reroll → mini-boss, one at a time. **No narrator character** — short diegetic
captions on the board itself. Needs a SKIP control (skip grants the full scripted payout and jumps
to beats 9–10) and per-beat resume after an app kill. No ads during FTUE. Total ≤ 5 minutes.

### Hero naming (first launch) ⬜
12-character name field, default "Wanderer", profanity-filtered EN + DE at creation and on every
edit, with its rejection message.

---

## 2. Hub and account

### S03 — Home / Camp ✅ — the busiest screen in the game
Top to bottom:
- Header: avatar, hero name, Legend Level, PvP tier label (tap → Profile); Legend XP bar with %;
  **Energy readout `138/200 (+200)`** — a desaturated Reserve segment drawn *behind* the main bar —
  with the next-tick countdown and a `+40 ▶ad` button (4/day).
- Header icons: Shop, and an **envelope with unread count** (no popup, no auto-open).
- Animated **hero diorama**: chibi hero with visibly equipped weapon/helmet/armor, mount and up to
  3 pets idling.
- Daily strip: `2/3 quests` with CLAIM, free chest `▶ad` (2/day) showing its **chest-class pity
  counter** ("Guaranteed A in 4"), Lucky Wheel `▶ad`, Crowns bundle `▶ad`.
- **Event card** — only while an event is live; no empty state when none is.
- **Guild card** — badged when a Guild Quest is claimable or Boss attempts are unused; reads
  "Find a guild" for the guildless and never re-appears after same-day dismissal.
- A dot on the Codex/Feats nav entry when a Feat is claimable.
- One dismissible **account-link banner** (fortnightly, unlinked players only) and, after Legend 8,
  **one** non-modal Slay Plus banner. Never a pop-up.
- The **CONTINUE** CTA, largest element, naming chapter + tier + biome. Two taps from launch to
  rolling a die.

### S27 — Profile ⬜
Name, PvP tier label + rating, Legend Level, **Renown total beside Legend Level**, account-link
status as a plain line, lifetime stats.

### S26 — Settings ⬜
Audio (separate music / SFX / UI sliders, ad ducking), haptics, battle speed, **accessibility block**
(reduced motion, no-timer mode, 3 colourblind palettes, 3 text sizes, left-handed mode), the Fair Dice
disclosure, language, **Account row** (link status, provider, linked date, delete-account path),
privacy, restore purchase. Plus a dedicated **Odds & Guarantees page**: every drop rate and every
pity `N` in the game, in plain language, in EN and DE.

### S37 — Inbox ⬜
Server→player only. List grouped by the 6 categories (`ANNOUNCEMENT`, `COMPENSATION`,
`RECONCILIATION`, `MODERATION`, `ACCOUNT`, `MILESTONE`), unread dot, attachment icons, expiry date,
per-message CLAIM and a primary **CLAIM ALL**. Needs the held state *"Not enough inventory space."*
Chest/egg/crate attachments claim as **unopened containers**, not contents. No marketing content, ever.

### S25 — Daily Quests / Login / Lucky Wheel ⬜
Three panels: 3 daily quests with progress, reward icons, CLAIM, one free reroll and
`AD_DOUBLE_QUEST` (3/day), plus the all-3 bonus chest; the **28-day login calendar** with
days 7/14/21/28 highlighted; the **Lucky Wheel** — 8 always-positive segments, 1 free spin + 2 via ad.

### S24 — Codex ⬜ (with S38 as a sibling tab)
Five sections with completion %: Perks 98 · Enemies 64 · Gear 120 · Pets 24 · Mounts 12, each entry
showing its permanent all-stats bonus, undiscovered entries as silhouettes. Header states the total
bonus (+17.2 % at full) and that **passive bonuses activate at Legend 100** while entries track from
the start. Talent-Point milestones at 25/50/75/90/100 % completion, claimable at any level.

### S38 — Feats ⬜
140 feats in 9 category tabs (Slaughter 20 · The Road 18 · The Die 14 · The Draft 16 · The Forge 18 ·
The Menagerie 14 · The Arena 14 · The Dungeons 10 · Fellowship 16). Each row: name, **progress bar
with real numbers** (`4,187 / 10,000`), 3 tier pips claimed separately, reward icons, CLAIM.
Persistent **Renown header** with the total and the next 1,000-point milestone (+2 Talent Points).
CLAIM ALL. Nothing is missable or time-limited.

---

## 3. Run entry

### S04 — Chapter Select ✅
Chapters 1–8 with biome art, lock state and unlock condition; the **difficulty tier picker**
(Normal / Heroic / Mythic with unlock rules and reward ×1.0 / ×2.5 / ×6.0); first-clear badges per
(chapter, tier); the equipped loadout summary (read-only — loadout is changed on Hero);
`AD_DOUBLE_LEGEND_XP` (1/day); and a **"Dungeons" entry as a fourth item**, badged when entries are
unused. The confirm dialog states the **20 Energy** cost and shows the **soft power warning** when
`PlayerPower < 0.7 × ParPower` — warn, never block.

### S28 — Dungeon Select ⬜
Three cards: The Stonevault (Enhance Stones), The Feeding Pits (Beast Feed), The Old Mint (Crowns).
Each shows its material icon, unlocked tier, **the exact literal payout for that tier** ("Tier 5 ·
480 Enhance Stones" — never a range, never an icon), entries remaining `2/3`, the 10 Energy cost, and
`AD_EXTRA_DUNGEON` (+1 entry, 3/day). One tap to enter. Unlocks at Legend 8.

---

## 4. Inside a run

### S05 — Board ✅ — where the player spends most of their time
- Top HUD: HP bar with numbers, **stage pips 2/3**, Gold, reroll-charge pips, the **consumable
  pouch** `[🧪 2]`, and a `[≡ perks]` button that must be reachable at all times (a player must never
  lose track of their build).
- Vertically scrolling track; hero token on its mount at ~40 % screen height; the next 6 tiles always
  visible; free-scroll ahead plus a **recenter** button; resolved tiles dimmed to 55 % and de-glowed.
- 14 tile types as chunky pucks readable at 48 dp: Enemy, Elite, Boss, Shrine, Curse, Treasure, Shop,
  Campfire, Minigame, Event, Portal, Cache, Dice Forge, Waypoint.
- **Forks** drawn side by side with a clear join — never crossing lines — each branch labelled
  (Perilous / Sheltered / Arcane / Feral) with up to 3 **honest** content icons.
- **Roll button**: the largest interactive element, bottom-third thumb zone. Long-press opens the Die
  Panel; tap-and-hold fans out the 6 faces.
- Die animation 0.8 s (0.25 s in Fast Mode), result echoed as a large floating number over the token.
- **Reroll prompt**: a 4-second ring around a `REROLL (2)` button; a tap elsewhere or a lapse accepts
  the roll and spends nothing. Wording must say it changes the **next** roll — it is not an undo.
- Consumable pouch fans out held Draughts/Ropes as mini-cards with a one-line effect and USE (enabled
  only in `AWAIT_ROLL`, Draught disabled at full HP); held cap 4; an **armed Escape Rope** shows as a
  rope icon hovering over the hero token; when empty the pouch renders at 40 % opacity but stays visible.

### S29 — Dungeon Board ⬜
S05 with forks, stage pips and shop affordances **removed**, and the Gold counter replaced by a
persistent **material-collected counter**. 8 nodes + a Guardian node, 1 reroll charge with no refresh,
no revive offer.

### S12 — Die Panel ⬜
The current 6 faces at full size, each with its **source attributed** (talent, mount, Dice Forge,
perk, curse), its kind (Pip / Star / Surge / Fortune / Void / Chain), its tier 0–3 and its exact
effect numbers. Opened by long-pressing the roll button, at any time in a run. *A hidden die is a
hostile die.*

### S06 — Battle ✅
Enemy banner with name and elite/boss modifier tag; 3-layer parallax biome backdrop; hero left, enemy
right, pets orbiting the hero; floating combat text; **two HP bars with numbers**; status-effect icons
with stack counts under each bar; **speed toggle ×1/×2/×3 and SKIP always visible**; a full-width band
flash on boss phase change. No player input during a fight, by design; skip is also an accessibility
feature.

### S07 — Perk Draft ✅ — *the game's main decision; never auto-advance*
Three cards filling the screen vertically. Each card: category colour bar (Lightning / Cold / Fire /
Poison / Bleed / Defense / Offense / Crit / Sustain), rarity gem, icon, name, **tier badge
`I` / `II` / `UPGRADE →III`**, effect text with **real numbers substituted**, and a one-line synergy
hint when it interacts with an owned perk. Owned-perk upgrades get a gold border and animate in
larger. Below: the reroll button showing remaining free rerolls, a visually quieter ad-reroll
(`AD_REROLL_PERK`, 2/run), a dashed placeholder card for the ad 4th option (`AD_EXTRA_PERK_CHOICE`,
1/run), and a small skip button stating exactly what it grants (`Skip → +60 Gold, +1 reroll`). The
header carries the three **DRAFT pity counters, each naming its unit**: *"Rare or better guaranteed in
2 more drafts you pick from"*. The run **opens** on this same screen before the first roll, and no
other action — abandon included — is legal until it is answered.

### S08 — Shop Tile ✅
Exactly 4 offers, one per pool: a perk (rarity-weighted), a consumable (Health Draught / Reroll Token /
Draft Token / Escape Rope), a run buff (Whetstone / Heartroot Tonic / Hawk's Eye with its magnitude),
and a heal (35 % Max HP, always present). Each offer shows its Gold price against the player's current
Gold. **Slots grey out** when a grant would be wasted (tokens at cap, consumables at the held cap of 4).
One free refresh, then `AD_SHOP_REFRESH` (2/run), then unavailable; plus `AD_SHOP_FREEBIE` (take one
offer free, 1/run).

### S09 — Event Card ⬜
Title, one-line fiction, and 2–3 option buttons. Each option shows its **cost** (Gold, HP %) and its
requirement if any; outcomes are uncertain and revealed after the choice, so the screen needs a result
state. One option is always a free walk-away. 30 authored events.

### S10 — Minigames (4 separate screens) ⬜
All ≤ 15 s, one-thumb, and unable to fail catastrophically. Each needs an intro, a play state and a
reward-tier result state, plus a single `AD_RETRY_MINIGAME` (1/run) on failure.
- **`MG_CHEST_PICK` Three Chests** — pick 1 of 3 shuffled chests; result reveals Bronze/Silver/Gold tier.
- **`MG_TIMING_BAR` Strike the Anvil** — a marker sweeps a bar, tap inside a shrinking green zone,
  3 attempts, each hit upgrades the reward tier (0/1/2/3 hits).
- **`MG_DICE_DUEL` Dice Duel** — best-of-3 against an NPC gambler **using the player's real upgraded
  die faces**; show both dice and the running score.
- **`MG_MEMORY_RUNE` Rune Recall** — 4-symbol Simon-style sequence, 2 rounds.

### S11 — Campfire / Shrine ✅
2–3 choice cards. **Campfire:** heal 40 % Max HP · upgrade one owned perk to its next tier (needs a
perk picker) · +2 Reroll Charges, plus `AD_CAMPFIRE_HEAL` (+30 % on top, 1/run).
**Shrine:** choose 1 of 2 run-long buffs from the authored pool; when the player carries a cleansable
curse, a **Cleanse** offer always replaces the second option.

### Stage Gate banner ⬜
Full-screen banner *"STAGE 2 — The Ashen Mire"* stating the three things it grants: +15 % Max HP,
reroll charges refreshed, checkpoint saved. This is the pacing heartbeat and the interstitial slot for
non-payers (every 3rd gate crossed, max 1 per run).

### S13 — Death / Revive ⬜
Time freezes on the battle screen; a **DEFEATED** overlay slides in. Offers `AD_REVIVE` **once per run,
bosses included**: restores 50 % Max HP, 2 s invulnerability, and states plainly that the battle
**restarts from its beginning**. Slay Plus shows an instant one-tap REVIVE. Declining routes to Run
Results with the death multiplier shown.

### S14 — Run Results ✅
Reward tally line by line with the **CompletionMultiplier stated and explained** (Victory 1.00 ·
Death S3 0.60 / S2 0.40 / S1 0.25 · Abandon 0.10; gear already picked up is kept in every case except
abandon), Legend XP and any level-up, gear gained, first-clear bonuses. `AD_DOUBLE_RUN_REWARDS` (1/run)
as the primary secondary action and `AD_FREE_RETRY` after a loss (2/day, same chapter, no Energy). The
footer carries the `DROP_RUN` mercy counters, the session-floor announcement line when it fires, and
the current Focus. RETRY and HOME.

---

## 5. Character and collection

### S15 — Hero ⬜
Large hero render with equipped gear visible. Six gear slots (Weapon, Helmet, Armor, Boots, Ring,
Amulet) — the first three drive the sprite. Up to 3 pet slots (unlock at Legend 5/15/30) and 1 mount
slot (Legend 20), each showing its lock state and unlock level. **Full stat sheet** with `PlayerPower`
prominent. **3 named loadout presets** (gear + pets + mount + talents + PvP perk set) loaded in one
tap; Plus grants unlimited slots, and presets beyond 3 go read-only on lapse rather than being deleted.
Equipping is free and unlimited outside a run and impossible during one.

### S16 — Inventory ✅
Grid of up to **1000 items** with rarity frame + gem, enhancement level, lock icon and a new-item
marker. Filters and sorting by slot, rarity, power, quality, newest. Tapping an item always shows a
**side-by-side delta against the equipped item in that slot, with a green/red arrow per stat**.
Per-item LOCK (excludes it from auto-salvage and merge selection) and Reforge / Retune entry points.
Also here: the **auto-salvage rule editor** ("salvage all C and B below +3"), the **Focus selector**,
and the **unopened shelf** — gear chests as stored containers showing class, live pity counter, OPEN
and OPEN ALL (contents, pity and Focus resolve at open; a full inventory rejects the open and leaves
the container on the shelf).

### S17 — Forge ⬜ — five tabs
- **Merge** (the star): select a target → the UI auto-suggests the 3 best fodder items → a big
  before/after stat delta → one MERGE button. A "use dust instead" toggle on any one of the three
  fodder slots with its cost. 1.2 s celebratory animation ending with the new rarity frame slamming in.
- **Enhance**: the success percentage as a large number, displayed as *"Success 41 % (+16 % mercy)"*;
  `AD_ENHANCE_LUCK` as a clearly-labelled additive **+15 %** (3/day; Plus shows `Lucky attempts 2/3`);
  a running "stones invested" total. Failure animation brief and non-punitive — **the item is never at
  risk**.
- **Salvage**: multi-select with the Merge Dust and 60 % stone-refund totals previewed.
- **Reforge**: quality re-roll priced in Merge Dust by rarity, stated as **better-of-two — quality can
  only go up**.
- **Retune**: affix re-roll with **lockable affixes** (×2 / ×5 / ×12 price for 1 / 2 / 3 locks), a
  per-item **wishlist of up to 3 desired affixes**, and the +10 % wishlist mercy counter.
The header carries the **Set Token counter `7 / 12`** (12 → choose any SS item) and the Focus row.

### S18 — Talents ⬜
Three branch tabs (MIGHT red · WARD blue · FORTUNE gold, the last locked until Legend 40). Vertically
scrolling tree, nodes in a 3-column zigzag. Locked tiers visible but greyed **with the requirement
stated** ("14 points in MIGHT"). Every node shows its current rank `2/5` and the **exact delta of the
next rank in real numbers**: *"+3 % DEF (+42 DEF)"*. Keystones are single-rank, 8 points. Persistent
header: total points, unspent points and **RESPEC** (free, unlimited, one confirm tap). A
"Preview build" toggle showing the resulting stat block before committing. 3 saved presets.

### S19 — Menagerie ⬜
Pets grid (24) and mounts grid (12), locked entries as **silhouettes with their rarity frame visible**.
Pet detail: large idle-animated sprite, passive aura at the current level, active ability with
cooldown, the **★1–★5 star track** with its duplicate requirements (2/4/8/16), and levelling costed in
Beast Feed + Crowns. Mount detail: stat block, its fixed **run perk** (which never scales with level),
and Beast-Feed-only levelling to 30. Plus the **Beast Mark exchange row** (all three tiers with their
counters), the **unopened shelf** for Pet Eggs and Mount Crates (class, live pity counter, OPEN), the
disclosed egg/crate odds and pity, and `AD_FREE_PET_EGG` (1/day) and `AD_FEED_BUNDLE` (2/day).

---

## 6. PvP

### S20 — Arena ⬜
Rating and tier label, season timer, attempts remaining (5 free + 2 via `AD_EXTRA_DUEL`; Plus 7),
and **3 opponent candidate cards** each showing name, rating, Legend Level, tier label and a report
action — at least one candidate is always rated below the player. FIGHT enters the duel, which reuses
S06 with both fighters named and labelled and a 60 s cap. Result: Victory / Defeat, rating delta,
Honor payout, `AD_DOUBLE_HONOR` (3/day), duel log entry. Replay history of the last 50 runs and duels
is **free for everyone**.

### S21 — PvP Loadout ⬜
The perk-budget builder: **5 slots, 10 Perk Points**, costs Common 1 / Rare 2 / Epic 3 / Legendary 5,
drawn from combat perks discovered in the Codex, **all locked to Tier II**. Economy and Dice & Board
perks are ineligible and should read as ineligible rather than be silently absent. Plus separate PvP
gear, pet, mount and talent presets stored independently from PvE. First entry runs a scripted,
guaranteed-win tutorial duel to teach this screen.

### S22 — Leaderboard ⬜
One global ladder with **every ranked player on it**. Initial view top 50, refreshed every 10 minutes.
Row: rank, name, tier label, rating, Legend Level, Plus tag. The player's own row is **pinned as a
sticky bottom row showing the exact rank** — *"#48,201 of 312,904"*. Infinite scroll in both
directions, a "jump to me" button, and search by player name.

---

## 7. Shop

### S23 — Shop ⬜ — four tabs
- **Daily**: the four permanent staples (Pet Egg 900 SS · Mount Crate 2,500 SS · S-tier Gear Chest
  1,800 SS · Energy refill 300 SS, escalating +150 per use per day) plus **6 rotating offers** with
  prices, per-day limits and remaining stock. `AD_SHOP_REDRAW` (1/day) redraws the whole block.
- **Honor**: Pet Egg, Mount Crate, S and SS gear chests (weekly stock 1), materials.
- **Materials**: Enhance Stones 25c, Merge Dust 6c, Beast Feed 8c at fixed rates, with the **daily
  caps** (40 / 250 / 200) visibly counting down.
- **Plus**: exactly one product, €4.99/month, 7-day trial. Honest copy listing what it grants (no
  interstitials; every rewarded placement becomes a one-tap CLAIM at the same cap; unlimited presets;
  a text-only Plus tag) and stating that *everything here can be earned free by watching ads*.
  **No bundles, no "best value" badge, no countdown, no fake discount.** While subscribed, the renewal
  price, renewal date and a direct link to the platform's cancel flow are shown at all times.
- **Every chest listing anywhere states its class and current pity counter before purchase.**

---

## 8. Live events

### S30 — Events Hub ⬜
Live events as cards: name, art, **remaining window as a plain date**, track progress bar, ENTER.
Upcoming events as dated grey cards. **No countdown timers below 24 hours.** Also absorbs the Weekly
Chapter Challenge.

### S31 — Event Track ⬜
The milestone ladder (≤ 12 rungs, strictly ascending) with claimed / claimable / locked states, the
player's current point total, and **one line stating how the event currency is earned**.

### S32 — Event Shop ⬜
Stock list priced in event currency with **per-player remaining counts**, plus the permanent line
*"Everything here can also be earned in the Honor Shop or from Soul Shards."* Needs a close-out state
explaining that unspent event currency auto-converts to Crowns.

---

## 9. Guilds

### S33 — Guild Home ⬜
Guild name, 4-character tag, level and the assembled one-line description; **3 Guild Quest progress
bars with the member's own contribution and the 15 % contribution cap**; the 7-day streak counter; a
**Guild Boss card** with attempts remaining (3/week); the **Phrase Board** (last 50 posts, 7-day TTL,
5 posts/member/day) with a picker over 60 authored phrases in 6 categories, optionally targeting one
quest or the boss; and the read-only **Guild Log**.

### S34 — Guild Roster ⬜
Up to 30 rows: name, Legend Level, role (Leader / Officer / Member), last-active with the 14-day
inactivity flag, weekly contribution. Inline Leader/Officer actions (promote, demote, kick — rate
limited to 10 role actions per day) and a **report action on every row**.

### S35 — Guild Browser ⬜
Search by name or tag, filter by join policy (Open / Request / Invite only) and minimum Legend Level,
sorted by activity. Create-guild flow: name + tag (profanity-filtered EN + DE), join policy, minimum
level, a description assembled from up to 2 of 40 authored phrases, **20,000 Crowns**. Also needs the
join confirmation stating the **24-hour cooldown**, and a typed-confirmation disband dialog.

### S36 — Guild Boss ⬜
Boss portrait (one of the 8 chapter bosses, rotating weekly), a **shared HP bar**, the per-member
damage contribution list, attempts remaining, FIGHT, and a **reward bracket preview** showing where
the player's damage percentile and the guild's total currently land. Fights are free, cost no Energy,
are capped at 120 s, and a death still contributes its damage — there is no failure state.

---

## 10. Surfaces not in the S-list that still need a design

Referenced by the systems but carrying no screen number. Each needs at least a card or overlay.

| Surface | What it must show |
|---|---|
| **Dice Forge tile** | Pick one of the 6 current faces and see the upgrade it becomes, for the rest of this run |
| **Curse tile landing** | The debuff, its attached payment, and `AD_SKIP_CURSE` (nullify, keep the reward, 1/run) |
| **Treasure / Cache reveal** | Contents revealed on landing, `AD_DOUBLE_CHEST` (2/run), and the note that meta rewards bank at run end |
| **Portal tile** | How far forward the jump goes and what content is being skipped |
| **Boss pre-fight banner** | Boss name and phases, plus `AD_BOSS_SECOND_WIND` — offered **before** the fight, never during |
| **Elite pre-fight** | `AD_ELITE_GUARANTEE` (A-rarity or better drop, 1/run) |
| **Fork choice prompt** | Two labelled branches with honest icon previews, up to 6 s; can fire mid-move, more than once per roll |
| **Chest / egg / crate open** | Open animation and results, the per-class pity counter advancing, OPEN ALL sequencing, and the *"Not enough inventory space"* rejection |
| **Focus selector** | Slot + family picker, the ×2.5 explanation, and the 12-hour cooldown before a change takes effect |
| **Level-up / unlock celebration** | Legend Level up (+1 Talent Point, Energy to full) and the feature unlocks at 5/8/10/15/20/30/40/60/100 |
| **Rewarded-ad flow** | Confirm, loading, "come back tomorrow" past the 44/day soft cap, the 20 s minimum gap, and the **grant-anyway** path on no-fill |
| **Interstitial handoff** | The beat before a stage-gate interstitial and the 4th return to Home; never for Plus, in FTUE, in the first 72 h, or after a death |
| **Account-link prompt** | Two one-off full cards (Legend 10 and 30), dismissible in one tap, stating the 500 Soul Shards + 1 chest reward |
| **Account conflict card** | Side-by-side comparison of both accounts (Legend Level, chapters cleared, gear count, last played), an irreversible choice with typed confirmation on the discarding side, and the 30-day recovery window |
| **Run resume card** | 1.2 s *"Picking up where you left off — Chapter 5, Stage 2."* |
| **Abandon run confirm** | States the 0.10 multiplier and that no gear drops are kept |
| **Plus lapse notice** | One in-app message: nothing was lost, and presets beyond 3 are read-only rather than deleted |

---

## Counts

- **38 numbered screens** (S01–S38), of which S10 is **4 separate minigame screens** → **41 distinct
  layouts**.
- **10 already have a client scene**: S01, S03, S04, S05, S06, S07, S08, S11, S14, S16.
- Plus the **17 unnumbered surfaces** in §10 and the FTUE overlay layer.
