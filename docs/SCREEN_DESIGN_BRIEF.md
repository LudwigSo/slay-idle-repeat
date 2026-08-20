# Screen Design Handover — Slay Idle Repeat

**For: Claude Design.** Current as of 2026-08-20.

This document states the design as it stands. Where it disagrees with a file in `game-design/`, this
document is right and the file is behind — the owning docs are propagated separately.

---

## 1. How to use this with Claude Design

- Every screen is one **portrait artboard, 1080×1920 px**, safe-area inset **144 px top / 288 px bottom**.
- 🔒 **Units — read this before drawing anything.** The canvas is 1080×1920 px, which is
  **360×640 dp at 3× density** (it is the game's real viewport: see `window/size/viewport_width=1080`
  with `stretch/mode="canvas_items"`). Every `dp` figure in this document is a device-independent unit —
  **multiply by 3 to get canvas px**. So the 48 dp touch minimum is **144 px**, 8 dp spacing is **24 px**,
  the 24 dp panel radius is **72 px**, and a 3 dp outline is **9 px**. Drawing a dp figure as raw px makes
  every control a third of its intended size.
- Design the **shared component inventory** (§5) first, on its own artboard. Every screen assembles
  from it. Nothing below should invent a new panel, button or frame.
- Work the tranches in §7 order. Tranche 1 is the playable loop and is worth more than everything else.
- Each screen entry gives: **Reference** (what shape to borrow), **Must show** (the content contract —
  omitting any line makes the screen wrong), and **States** (what else has to be drawn).
- Numbers in *Must show* lines are real game values. Use them as placeholder copy rather than lorem —
  the layouts are tested by whether real numbers fit.

---

## 2. The product in one paragraph

A portrait, one-handed, run-based fantasy auto-battler. The player rolls a plain six-sided die to walk a
fully visible board of 43 tiles across 3 stages, fights automatically, and drafts one perk of three
after every win. Runs last 8–15 minutes and cost Energy. Outside a run there is a hub with gear, a
forge, a talent tree, a pet/mount collection, an asynchronous PvP ladder, guilds, live events and daily
errands. **No currency can be bought with money**; the only paid product is a €4.99 subscription that
saves ad-watching time and grants no power. That constraint is the product's identity and must be
legible in the UI: no fake scarcity, no countdown pressure, no "best value" badges.

---

## 3. Visual direction

### 3.1 The look, locked

> Chunky chibi fantasy characters with thick dark outlines, saturated candy-jewel colours, soft cel
> shading, and a glossy mobile-game finish — readable as a silhouette at 64 px on a phone in daylight.

| Rule | Spec |
|---|---|
| Outline | Uniform dark outline on every character and prop. `#231A2E`, **never pure black**. 3–4 px at a 512 px canvas. |
| Shading | Two-tone cel. One base, one shadow at 85 % value / +8 % saturation. One soft rim light from **upper left**, in every asset. No gradients across large areas, no airbrushing. |
| Proportions | Characters 2.5–3 heads tall. Big head, small body, oversized hands and feet. |
| Colour | Saturated, jewel-like. No muddy mid-tones. Each biome has a **locked 6-colour palette** (§3.3). |
| Detail budget | Low. If it is not readable at 64 px, remove it. Chunky shapes beat fine ornament. |
| Panels | 24 dp radius, 3 dp dark outline, subtle inner gradient, soft drop shadow. **Square, non-tapering corners** so 9-slice stretching does not distort ornament. |
| Buttons | Chunky, high contrast, 3 dp outline. Pressed = 4 dp downward offset + darker fill. |
| Type | **Baloo 2** for headings, numbers and buttons; **Nunito Sans** for body and long text. |
| Touch | Minimum 48×48 dp, 8 dp spacing. Primary action in the bottom third. |
| Numbers | Abbreviated above 10k (`12.4k`, `3.1M`), full value on long-press. Currency icon always adjacent to its number, never text-only. |
| Motion | No transition longer than 300 ms. Everything skippable. |
| Text in art | **Never render text inside a generated image.** All text is engine-rendered. |

### 3.2 Rarity, and the colourblind rule

`C #9AA5B1` · `B #4CAF50` · `A #3B82F6` · `S #F5A623` · `SS #C13BE8`

🔒 Rarity must **also** read as **frame shape + gem symbol**, never colour alone. Three palettes ship:
default, deuteranopia, tritanopia. Design the frame set so the five rarities are distinguishable in
greyscale.

### 3.3 Biome palettes (base · shadow · accent · glow · prop · sky)

| Ch | Biome | Palette |
|---|---|---|
| 1 | Greenwood Vale | `#5FBF5F` `#2F7A3F` `#F2D06B` `#FFF3A8` `#8B5E3C` `#9FE0F0` |
| 2 | Ashen Mire | `#6B5A8E` `#3B2E52` `#8FBF5F` `#C7F26B` `#4A3B2E` `#8E7BA8` |
| 3 | Sunken Crypt | `#4A6E7A` `#233A45` `#E8E3C8` `#6BF2D6` `#5A5148` `#1E2A33` |
| 4 | Emberpeak | `#C4462A` `#6E1E14` `#F2A03C` `#FFD86B` `#3A2A28` `#2A1A1E` |
| 5 | Frostbound Reach | `#7EC8E8` `#3E6E96` `#E8F6FF` `#A8E8FF` `#5A6E8E` `#2E4A6E` |
| 6 | Clockwork Vaults | `#C89A4A` `#7A5A28` `#4AC8B4` `#8FF2E0` `#5A4A3A` `#2E2A28` |
| 7 | Bloom of Decay | `#D46BA8` `#7A2E5A` `#8FE86B` `#D8FF8F` `#5A3A4A` `#3A2A38` |
| 8 | Astral Spire | `#7A5AD8` `#3A2A7A` `#F2C86B` `#C8A8FF` `#2E2A4A` `#141028` |

Design the hub and system screens against **Chapter 1 Greenwood Vale** unless a screen names a biome.

### 3.4 Reference vocabulary

🔒 **For direction only. Never copy a layout wholesale, and never name any of these inside an
image-generation prompt.**

| Reference | Take from it | Do **not** take |
|---|---|---|
| **Raid: Shadow Legends** | The meta-screen architecture: a persistent top currency header, a dense but ordered hub with widget rails, tabbed inventory with a side-by-side compare panel, the upgrade screen built around one huge success percentage, 3-opponent arena cards, a clan hub, and event milestone rails. Its information density per screen is the right target for S15–S23 and S30–S36. | Its dark, grimdark, high-detail realism; its aggressive bundle/timer merchandising; its six-plus nav items. |
| **Rogue Legend / Legend of Slime / Top Heroes** | The casual-idle surface: one enormous unmissable CTA, chunky rounded cards, celebratory merge/upgrade bursts, friendly readable icons, a hub built around an animated character diorama. This is the correct *tone* for S03 and the whole run loop. | Their AFK-income framing — the game has none today (see §9). |
| **Slay the Spire** and board roguelites generally | The run layer: a fully visible branching map read at a glance, a three-card draft as the game's headline decision, honest per-branch content previews. | Its desktop density and its palette. |
| **Modern Disney-adjacent mobile RPG UI** | Panel language, rounded chrome, generous padding, glossy finish. | — |

The sentence that resolves conflicts between these: **Raid's information architecture, Legend of Slime's
tone and shapes, Slay the Spire's run legibility.**

### 3.5 Accessibility, required in v1

Reduced motion (kills shake/parallax/particles, all animation to 100 ms) · no-timer mode (every prompt
waits indefinitely) · 3 colourblind palettes · 3 text sizes (100 / 115 / 130 %) **with layouts that
reflow** · haptics toggle · separate music/SFX/UI sliders · **left-handed mode that mirrors the roll
button and bottom nav** · battle skip always available. Every artboard must survive 130 % text.

### 3.6 Language

**English only at v1.** German is post-launch, so **keep ~30 % width tolerance in every label, button
and panel** — re-laying out every screen when DE arrives is the expensive outcome. No other language is
planned for v1.

### 3.7 Connection states — global, never per-screen

Connected: **no indicator at all** (never a green tick). Reconnecting: a small pill slides in top-centre
after 2 s, slow pulse, non-blocking. Offline: server-dependent buttons dim to 40 % with a cloud-slash
glyph and an inline toast on tap — never a modal. Resynced: 0.4 s green flash + a "Caught up." toast.
🔒 **Never a full-screen blocking error during a run.**

---

## 4. Canvas plan

| Lane | Artboards |
|---|---|
| **0 · System** | Component inventory, colour/type sheet, rarity frame set, icon sheet |
| **1 · Run loop** | S05 Board → S06 Battle → S07 Perk Draft → tile cards → Stage Gate → S13 → S14 |
| **2 · Entry** | S01 → S02 overlay → S03 Home → S04 Chapter Select → S28 Dungeon Select → S29 Dungeon Board |
| **3 · Character** | S15 Hero → S16 Inventory → S17 Forge → S18 Talents → S19 Menagerie |
| **4 · Social** | S20 Arena → S21 PvP Loadout → S22 Leaderboard → S33–S36 Guilds |
| **5 · Service** | S23 Shop → S24 Codex → S38 Feats → S25 Dailies → S37 Inbox → S26 Settings → S27 Profile → S30–S32 Events |
| **6 · Overlays** | The 17 unnumbered surfaces in §8 |

Name artboards `S05-Board`, `S05-Board-fork`, `S17-Forge-merge`, `X-CurseCard`, so states sort beside
their parent.

---

## 5. Shared component inventory — design once, before any screen

| Group | Pieces |
|---|---|
| 9-slice panels (12) | main · dark · light · parchment · wood · stone · glass · tooltip · modal · banner · tab-active · tab-inactive |
| Buttons (18) | primary / secondary / danger / ghost × normal / pressed / disabled · the large **ROLL** button (3 states) · the **ad** button (3 states) |
| Rarity frames (10) | 5 rarities × (square item slot, round portrait) — distinguishable in greyscale |
| Progress bars (9) | HP · Energy · Legend XP, each as fill + track + cap. **The Energy bar needs a second desaturated Reserve segment drawn behind the main fill.** |
| Nav & tabs (12) | Home · Hero · Forge · Talents · Menagerie · Arena · Shop · Codex · Settings · Back · Close · Info |
| Cards (11) | **9 perk-category card frames** + the owned-upgrade gold frame + the dashed ad-slot card |
| Chrome | Dividers, ribbons, banners (10) · toast/notification (4) · loading spinner-die, progress track, tip card (3) |
| Currency icons (9) | Gold · Crowns · Soul Shards · Energy · Enhance Stones · Merge Dust · Beast Feed · Honor · event currency |
| Misc icons (50) | sort · filter · lock · salvage · merge · enhance · equip · compare · star · check · cross · arrows · speed ×1/×2/×3 · skip · sound · haptics · account · chest · key · timer · warning · info … |
| Status icons (12) | burn · poison · bleed · chill · freeze · shield · regen · rage · stun · crit-up · def-down · thorns — each with a stack-count slot |
| Die faces (6) | 1–6. One die design, no skins, no special faces. |

### 5.1 🔒 The token block this artboard must emit

This artboard is the **only** place the design language gets decided, and every later lane is designed in
a separate session that cannot see it. So the inventory's deliverable is not just the pieces — it must
also emit a short, copyable **token block** naming the exact values it chose:

- **UI chrome palette** — panel fill (main / dark / light), panel outline, ink, text primary / secondary /
  disabled, modal scrim, success, danger. The character outline `#231A2E` (§3.1) is the anchor, and
  nothing in the UI may be pure black. §3.3 and §3.2 cover world and rarity colour only — neither
  defines chrome.
- **Type scale** — Baloo 2 and Nunito Sans, six steps (display, H1, H2, body, caption, micro), each in
  canvas px, plus the 115 % and 130 % reflow sizes §3.5 requires.
- **Spacing ramp** — the multiples of 8 dp actually used, given in canvas px.
- **The nine perk-category hexes** — Lightning violet · Cold ice-cyan · Fire ember-red · Poison
  toxic-green · Bleed crimson · Defense steel-teal · Offense magenta · Crit gold-green · Sustain rose.
  S07 and §5 both build on these; they are named there but never fixed to values.

Paste that block into the **Locked tokens** slot at the top of every later lane packet. A lane designed
without it invents its own greys, and will not match the rest of the game.

---

## 6. The screens

### Tranche 1 — the run loop

#### S05 · Board ✅ *scene exists*
**Reference:** Slay the Spire's map legibility, rendered in Legend of Slime's chunky puck style.
**Must show**
- Top HUD: HP bar with numbers (`4,120 / 5,600`) — **the run's central stat, because HP persists across
  every fight in the run** — stage pips `2/3`, Gold, the consumable pouch `[🧪 2]`, and a `[≡ perks]`
  button reachable at all times so a player never loses track of their build.
- 🔒 **The whole track, always.** Every tile of every stage drawn from run start. No fog, no preview
  range, nothing scrolled-to. If the track is too long for one column it **wraps** — a board the player
  must drag to see is not a visible board. This is load-bearing: it is what makes choosing a fixed die's
  number a decision.
- Hero token on its mount at ~40 % screen height. Resolved tiles dim to 55 % and lose their icon glow.
- 14 tile pucks, readable at 48 dp: Enemy ⚔ · Elite ☠ · Boss ★ · Shrine ✨ · Curse 💀 · Treasure 🎁 ·
  Shop 🏪 · Campfire 🔥 · Minigame 🎯 · Event ❓ · Portal 🌀 · Cache 🐾 · Dice Forge 🎲 · Waypoint ・
- **Forks** side by side with a clear join, never crossing lines. Each branch: a one-word label
  (**Perilous** / **Sheltered** / **Arcane** / **Feral**) and up to 3 **honest** content icons.
- **Roll button** — the largest interactive element on screen, in the bottom-third thumb zone.
  One tap, final. No long press, no second gesture.
- 🔒 **Fixed-die tray**, directly above the roll button inside the same thumb zone: one control per
  number held (`[3] [5 ×2]`), pressing one spends it and moves exactly that far. Hidden when the run
  owns none. Disabled by exactly what disables the roll. Fixed dice are **uncapped**, so the tray must
  survive a long holding.
- The number the last roll came up, held until the next roll replaces it.
- Chapter 6 only: a small gear counter (Clockwork Pressure, +5 % enemy power per roll this stage, cap +50 %).

**States:** rolling (0.8 s squash-and-stretch tumble, dust puff, result echoed as a large floating
number over the token) · moving (0.25 s per node) · fork prompt · **fixed-die number prompt** (a six-way
1–6 picker in the tray's place when a die is granted — live in *every* state, including ones that refuse
a roll, because naming a number moves nothing) · pouch fanned out · armed Escape Rope (a rope icon
hovering over the token) · empty pouch at 40 % opacity but still visible.

#### S06 · Battle ✅
**Reference:** Raid's fight framing, at Legend of Slime's scale and chunkiness.
**Must show:** enemy banner with name and elite/boss modifier tag · 3-layer parallax biome backdrop ·
hero left, enemy right, pets orbiting the hero at small scale · floating combat text · **two HP bars
with numbers** · status icons with stack counts under each bar · **speed toggle ×1/×2/×3 and SKIP,
always visible** · a full-width band flash on boss phase change.
**Note:** the mount does **not** appear in battle, and the hero never visually dismounts — the board
cuts straight to the fight. No player input during a fight, by design; skip is also an accessibility
feature.

#### S07 · Perk Draft ✅ — *the game's main decision; never auto-advances*
**Reference:** Slay the Spire's card reward, in a mobile card frame.
**Must show:** three cards filling the screen vertically, each with a **category colour bar in one of
nine colours** — Lightning violet · Cold ice-cyan · Fire ember-red · Poison toxic-green · Bleed crimson ·
Defense steel-teal · Offense magenta · Crit gold-green · Sustain rose — plus rarity gem, icon, name,
**tier badge `I` / `II` / `UPGRADE →III`**, effect text with **real numbers substituted**, and a
one-line synergy hint when it touches an owned perk (*"Synergy: Crit Cascade"*). Owned-perk upgrades get
a **gold border and animate in slightly larger**. Below the three: the reroll button with its remaining
free rerolls, a visually **quieter** ad-reroll (`AD_REROLL_PERK`, 2/run), a **dashed placeholder card**
for the ad 4th option (`AD_EXTRA_PERK_CHOICE`, 1/run), and a small skip button that states exactly what
it grants (`Skip → +60 Gold, +1 reroll`). Header carries the three pity counters, **each naming its
unit**: *"Rare or better guaranteed in 2 more drafts you pick from"*.
**Note:** categories are gated — each is entered through one base perk, so a run that has not taken
`PK_IGNITE` is never offered a Burn upgrade. The run **opens** on this screen before the first roll,
where the pool is exactly the nine category bases — that is the widest the layout ever gets. No other
action, abandon included, is legal until the draft is answered.

#### Tile cards (one artboard each, all modal over S05)
| Card | Must show |
|---|---|
| **S08 Shop Tile** ✅ | Exactly 4 offers from fixed pools — a perk, a consumable (**Health Draught · Fixed Die Token · Draft Token · Escape Rope**), a run buff (Whetstone +ATK / Heartroot Tonic +Max HP / Hawk's Eye +4 % Crit), and a heal (35 % Max HP, always present). Gold price per offer against the player's Gold. **Slots grey out when a grant would be wasted** — Draft Token at cap, consumables at the held cap of 4. The Fixed Die Token is uncapped and **never** greys out. One free refresh → `AD_SHOP_REFRESH` (2/run) → unavailable. Plus `AD_SHOP_FREEBIE` (1/run). The visit stays open for several commands and closes on an explicit LEAVE. |
| **S09 Event Card** | Title, one line of fiction, 2–3 options. Each option shows its **cost** (Gold, HP %) and requirement. One is always a free walk-away. Needs an outcome-reveal state — outcomes are weighted, not chosen. Some cards carry two options rather than three; the layout must not look broken at two. |
| **S11 Campfire / Shrine** ✅ | **Campfire, three options:** heal 40 % Max HP · upgrade one owned perk a tier (needs a perk picker) · **gain one fixed die** (opens the number prompt). Plus `AD_CAMPFIRE_HEAL` (+30 % on top, 1/run). **Shrine:** 1 of 2 run-long buffs; when the player carries a cleansable curse, a **Cleanse** offer always replaces the second option. |
| **Curse landing** | Name the curse, state the debuff and the payment it comes with, offer `AD_SKIP_CURSE` (nullify, keep the reward, 1/run). Some curses are carried and cleansable without applying a debuff, so the card must name the curse rather than only describe an effect. |
| **Treasure / Cache reveal** | Contents revealed on landing, `AD_DOUBLE_CHEST` (2/run), and the note that meta rewards bank at run end. Cache: Beast Feed, or 6 % a Pet Egg. |
| **Portal** | How far forward the jump goes (a seeded 3–6) and what is being skipped. It can never reach the boss, and in stage 3 never passes the guaranteed pre-boss campfire. |
| **Dice Forge** | **Grants one fixed die** — opens the number prompt. That is the tile's design: a guaranteed, player-chosen movement. Card states the grant plainly; the reward is the choice, not a random number. |
| **Boss pre-fight** | Boss name, phase count, and `AD_BOSS_SECOND_WIND` — offered **before** the fight, never during. |
| **Elite pre-fight** | `AD_ELITE_GUARANTEE` — A-rarity or better drop, 1/run. |
| **Stage Gate** | Full-width banner *"STAGE 2 — The Ashen Mire"*, naming its two grants: **+15 % Max HP** and a checkpoint. The heal is real and worth reading, because HP persists across the run. This is also the interstitial slot for non-payers. |

#### S10 · Minigames — three artboards
All ≤ 15 s, one-thumb, and unable to fail catastrophically. Each needs intro / play / reward-tier result,
plus a single `AD_RETRY_MINIGAME` (1/run) on failure.
- **Three Chests** — pick 1 of 3 shuffled chests; result reveals Bronze / Silver / Gold.
- **Strike the Anvil** — a marker sweeps a bar, tap inside a shrinking green zone, 3 attempts, each hit
  upgrades the tier.
- **Rune Recall** — 4-symbol Simon sequence, 2 rounds.

A minigame win also grants **one fixed die** on top of its reward table — surface it in the result state.

#### S13 · Death / Revive
**Must show:** time freezes on the battle screen, a **DEFEATED** overlay slides in. `AD_REVIVE`, **once
per run, bosses included** — restores **66 %** of Max HP, grants 2 s invulnerability, and states plainly
that **the battle restarts from its beginning**. Slay Plus shows an instant one-tap REVIVE with no ad.
Declining routes to S14 with the death multiplier shown.

#### S14 · Run Results ✅
**Reference:** Raid's post-battle tally, without the merchandising.
**Must show:** the reward tally line by line with the **completion multiplier stated and explained** —
Victory 1.00 · Death in Stage 3 0.60 / Stage 2 0.40 / Stage 1 0.25 · Abandon 0.10, and **gear already
picked up is kept in every case except abandon**. Legend XP and any level-up. First-clear bonuses.
`AD_DOUBLE_RUN_REWARDS` (1/run) as the loud secondary action; `AD_FREE_RETRY` after a loss (2/day, same
chapter, no Energy). Footer: `DROP_RUN` mercy counters, the session-floor line when it fires, and the
current Focus. RETRY and HOME.
🔒 **Death must still pay** — the screen's job is to make a 25 % return read as a return, not a loss.

---

### Tranche 2 — entry and hub

#### S01 · Splash / Loading ✅
The four boot steps it is waiting on: auth, server session, profile fetch, content-hash check. Needs
failure/retry states (no server, content mismatch, forced update). Loading elements: spinner die,
progress track, tip card.

#### S02 · FTUE — an overlay layer, not a screen
Caption + spotlight over the board. 12 tiles, no stage gates, forced die sequence. Teaches, one at a
time: roll → auto-combat → perk draft → treasure → shop → **the fixed die** → mini-boss. 🔒 **No narrator
character** — short diegetic captions on the board itself.

The fixed-die lesson is the fifth beat and is the one that needs designing: the player is granted a
fixed die before a Cursed Ground tile, names its number, and spends it to land past the curse. It is the
tutorial's only lesson about the player's own agency, so it gets the clearest caption on the board.

SKIP from the pause menu after beat 2, one confirm (*"Skip the tutorial? You keep everything it pays."*),
granting the full scripted payout. Per-beat resume after an app kill. No ads. ≤ 5 minutes total.

#### S03 · Home / Camp ✅ — the busiest screen in the game
**Reference:** Raid's bastion for the widget rails and header; Legend of Slime for the diorama and the
size of the CTA.
**Must show, top to bottom**
- Header: avatar · hero name · Legend Level · PvP tier as a **coloured text label** (tap → Profile) ·
  Legend XP bar with % · **Energy as `138/200 (+200)`**, the `(+200)` being a desaturated **Reserve**
  segment drawn *behind* the main bar, with the next-tick countdown and `+40 ▶ad` (4/day).
- Header icons: Shop, and an **envelope with an unread count** — no popup, no auto-open.
- **Animated hero diorama**: chibi hero with visibly equipped weapon/helmet/armor, mount, up to 3 pets.
- Daily strip: `2/3 quests` + CLAIM · free chest `▶ad` (2/day) showing its **chest-class pity counter**
  (*"Guaranteed A in 4"*) · Lucky Wheel `▶ad` · Crowns bundle `▶ad`.
- **Event card** — only while an event is live. When none is, **the row does not exist**: no empty
  state, no teaser.
- **Guild card**, badged when a Guild Quest is claimable or Boss attempts are unused. For the guildless
  it reads *"Find a guild"*, never nags, and never reappears after a same-day dismissal.
- A dot on the Codex/Feats nav entry when something is claimable.
- One dismissible **account-link banner** (fortnightly, unlinked players only) and, after Legend 8,
  **one** non-modal Slay Plus banner. 🔒 Never a pop-up, never a countdown.
- The **CONTINUE** CTA — largest element on screen — naming chapter, tier and biome.
- Bottom nav, five items: Hero · Forge · Talents · Beasts · Arena.
- ⏳ **Reserve one widget slot in the daily strip for an Idle Reward** (§9). Do not design its contents;
  do make sure the rail has room for a fourth widget without re-flowing.
🔒 **Two taps from launch to rolling a die.**

#### S04 · Chapter Select ✅
Chapters 1–8 with biome art, lock state and unlock condition · **tier picker** Normal / Heroic / Mythic
with their unlock rules and reward ×1.0 / ×2.5 / ×6.0 · first-clear badges per (chapter, tier) · a
read-only loadout summary (loadout changes on Hero) · `AD_DOUBLE_LEGEND_XP` (1/day) · a **"Dungeons"
entry as a fourth item**, badged when entries are unused.
**Confirm dialog:** the **20 Energy** cost and the **soft power warning** when
`PlayerPower < 0.7 × ParPower` — 🔒 warn, never block. Let them try.

#### S28 · Dungeon Select
Three cards — The Stonevault (Enhance Stones) · The Feeding Pits (Beast Feed) · The Old Mint (Crowns).
Each: material icon, unlocked tier, **the exact literal payout — "Tier 5 · 480 Enhance Stones", never a
range and never an icon** (the entire value of the feature is knowing exactly what you get), entries
remaining `2/3`, the 10 Energy cost, `AD_EXTRA_DUNGEON` (+1, 3/day). One tap to enter. Unlocks Legend 8.

#### S29 · Dungeon Board
S05 with forks, stage pips and shop affordances **removed**, and the Gold counter replaced by a
persistent **material-collected counter**. 8 nodes + a Guardian node. **One fixed-die choice at run
start**, no refresh. No revive offer.

---

### Tranche 3 — character and collection

#### S15 · Hero
**Reference:** Raid's champion screen — portrait left, slots ringed around it, stat sheet below.
**Must show:** large hero render with equipped gear visible · six gear slots (Weapon, Helmet, Armor,
Boots, Ring, Amulet — the first three drive the sprite; the other three are stat-only) · 3 pet slots
(unlock Legend 5 / 15 / 30) and 1 mount slot (Legend 20), each showing its lock state and unlock level ·
**full stat sheet with `PlayerPower` prominent** · **3 named loadout presets** (gear + pets + mount +
talents + PvP perk set) loaded in one tap. Plus grants unlimited slots; on lapse, presets beyond 3 go
**read-only, never deleted**. Equipping is free and unlimited outside a run, and impossible during one.

#### S16 · Inventory ✅
**Reference:** Raid's artifact vault — grid, filter rail, and a compare panel that never hides.
**Must show:** a grid of up to **1000 items** with rarity frame + gem, enhancement level, lock icon, new
marker · filters and sort by slot / rarity / power / quality / newest · **tapping any item always shows a
side-by-side delta against the equipped item in that slot, with a green/red arrow per stat** · per-item
LOCK (excludes from auto-salvage and merge selection) and Reforge / Retune entry points · the
**auto-salvage rule editor** (*"salvage all C and B below +3"*) · the **Focus selector** · and the
**unopened shelf**: gear chests as stored containers showing class and **live pity counter**, with OPEN
and OPEN ALL. Contents, pity and Focus all resolve **at open**; a full inventory rejects the open and
leaves the container on the shelf.

#### S17 · Forge — five tabs
**Reference:** Raid's upgrade screen, built around one huge percentage.
- **Merge** *(the star)*: select a target → the UI **auto-suggests the 3 best fodder items** → a big
  before/after stat delta → one **MERGE** button. A "use dust instead" toggle on any one fodder slot
  with its cost. 1.2 s celebratory animation: three items spiral in, a flash, the new rarity frame slams
  into place.
- **Enhance**: the success percentage as a **large number**, written as *"Success 41 % (+16 % mercy)"* ·
  `AD_ENHANCE_LUCK` as a clearly-labelled additive **+15 %** (3/day; Plus shows `Lucky attempts 2/3`) ·
  a running "stones invested" total. 🔒 Failure is brief and non-punitive in tone — **the item is never
  at risk**, ever.
- **Salvage**: multi-select with the Merge Dust and 60 % stone-refund totals previewed.
- **Reforge**: quality re-roll in Merge Dust, stated as **better-of-two — quality can only go up**.
- **Retune**: affix re-roll with **lockable affixes** (×2 / ×5 / ×12 for 1 / 2 / 3 locks), a per-item
  **wishlist of up to 3 affixes**, and the +10 % wishlist mercy counter.
Header: the **Set Token counter `7 / 12`** (12 → choose any SS item outright) and the Focus row.
**Set bonuses:** there are **three** sets — Bloodmoon (Balanced) · Ironvow (Heavy) · Stormcall (Agile) —
each with 2 / 4 / 6-piece breakpoints. The **Caster** family axis has no set, so the set panel needs a
legible *"no set bonus"* state for Caster SS items rather than an empty row.

#### S18 · Talents
**Two branch tabs — MIGHT (red) and WARD (blue).** Both unlocked at Legend Level 1; there is no third
branch and no level-gated tab. Vertically scrolling tree, nodes in a **3-column zigzag** for portrait.
Locked tiers visible but greyed **with the requirement stated** (*"14 points in MIGHT"* — tier gates at
6 / 14 / 24 points in that branch). Every node shows its rank `2/5` and the **exact delta of the next
rank in real numbers**: *"+3 % DEF (+42 DEF)"*. Keystones are single-rank, 8 points, three per branch.
Persistent header: total points, unspent points, **RESPEC** (free, unlimited, one confirm tap). A
"Preview build" toggle showing the resulting stat block before committing. 3 presets.
**40 nodes total.** The tree costs 422 points to max against ~324 obtainable in v1, so it stays
deliberately incompletable — the header should make "unspent" feel like a decision, not a chore.

#### S19 · Menagerie
**Reference:** Raid's champion collection grid, with the aspirational silhouette treatment.
Pets grid (**23**) and mounts grid (**11**), locked entries as **silhouettes with their rarity frame
visible**. **Pet detail:** large idle-animated sprite, passive aura at current level, active ability with
cooldown, the **★1–★5 star track** with its duplicate requirements (2 / 4 / 8 / 16), levelling in Beast
Feed + Crowns. **Mount detail:** stat block, its fixed **run perk** (which never scales with level),
Beast-Feed-only levelling to 30. Plus the **Beast Mark exchange row** (three tiers with counters), the
**unopened shelf** for Pet Eggs and Mount Crates (class, live pity counter, OPEN), the **disclosed
egg/crate odds and pity**, and `AD_FREE_PET_EGG` (1/day) / `AD_FEED_BUNDLE` (2/day).

---

### Tranche 4 — social

#### S20 · Arena
Rating, tier label, season timer, attempts remaining (5 free + 2 via `AD_EXTRA_DUEL`; Plus 7), and
**3 opponent candidate cards** — name, rating, Legend Level, tier label, report action. 🔒 At least one
candidate is always rated **below** the player. FIGHT enters a duel that reuses S06 with both fighters
named and a 60 s cap. Result state: Victory / Defeat, rating delta, Honor payout, `AD_DOUBLE_HONOR`
(3/day), duel log entry. Replay history of the last 50 runs and duels is **free for everyone** — never a
Plus perk, because that would be a PvP information advantage.

#### S21 · PvP Loadout
The perk-budget builder: **5 slots, 10 Perk Points**, costs Common 1 / Rare 2 / Epic 3 / Legendary 5,
**all PvP perks locked to Tier II**. **Every perk category is eligible** — the pool is simply every
combat perk the player has discovered in the Codex, so the screen needs no ineligibility treatment and
no banned-category messaging. Separate PvP gear, pet, mount and talent presets stored independently from
PvE. First entry runs a scripted, guaranteed-win tutorial duel that exists to teach this screen.
The budget meter is the screen: 10 points over 5 slots means five Legendaries is impossible, so a wide
build and a tall build must read as visibly different shapes.

#### S22 · Leaderboard
One global ladder with **every ranked player on it — no cut-off**. Top 50 initial view, refreshed every
10 minutes. Row: rank · name · tier label · rating · Legend Level · Plus tag. The player's own row is
**pinned as a sticky bottom row with the exact rank** — *"#48,201 of 312,904"*. Infinite scroll both
directions, a "jump to me" button, search by player name.
🔒 The exact rank is the point: *"you are not in the top 100"* tells a player nothing.

#### S33–S36 · Guilds
**Reference:** Raid's clan hub, minus chat.
- **S33 Guild Home** — name, 4-char tag, level, the assembled one-line description · **3 Guild Quest
  bars with the member's own contribution and the 15 % contribution cap** · the 7-day streak counter ·
  a **Guild Boss card** with attempts remaining (3/week) · the **Phrase Board** (last 50 posts, 5/member/
  day) with a picker over **60 authored phrases in 6 categories**, optionally targeting a quest or the
  boss · the read-only **Guild Log**. 🔒 There is no free text anywhere in this feature.
- **S34 Guild Roster** — up to 30 rows: name · Legend Level · role (Leader / Officer / Member) ·
  last-active with the 14-day inactivity flag · weekly contribution. Inline Leader/Officer actions
  (rate-limited to 10 role actions per day) and a **report action on every row**.
- **S35 Guild Browser** — search by name/tag, filter by join policy (Open / Request / Invite only) and
  minimum Legend Level, sorted by activity. Create flow: name + tag, policy, minimum level, a
  description assembled from up to 2 of 40 authored phrases, **20,000 Crowns**. Plus the join
  confirmation stating the **24-hour cooldown**, and a typed-confirmation disband dialog.
- **S36 Guild Boss** — boss portrait (one of the 8 chapter bosses, rotating weekly), a **shared HP bar**,
  the per-member damage contribution list, attempts remaining, FIGHT, and a **reward bracket preview**
  showing where the player's percentile and the guild's total currently land. Free, no Energy, 120 s cap,
  and **a death still contributes its damage — there is no failure state**.

---

### Tranche 5 — service

#### S23 · Shop — four tabs
- **Daily** — four permanent staples (Pet Egg 900 SS · Mount Crate 2,500 SS · S-tier Gear Chest 1,800 SS ·
  Energy refill 300 SS, escalating +150 per use per day) plus **6 rotating offers** with prices, per-day
  limits and remaining stock. `AD_SHOP_REDRAW` (1/day) redraws the block.
- **Honor** — Pet Egg, Mount Crate, S and SS gear chests (weekly stock 1), materials.
- **Materials** — Enhance Stone 25c · Merge Dust 6c · Beast Feed 8c at fixed rates, with the **daily caps
  (40 / 250 / 200) visibly counting down**.
- **Plus** — exactly one product, €4.99/month, 7-day trial. Honest copy listing what it grants, and
  stating outright that *everything here can be earned free by watching ads*. 🔒 **No bundles, no "best
  value" badge, no countdown timers, no fake discounts.** While subscribed, the renewal price, renewal
  date and a **direct link to the platform's cancel flow** are shown at all times.
- **Every chest listing anywhere states its class and current pity counter before purchase.**

#### S24 · Codex (+ S38 as a sibling tab)
Five sections with completion %: Perks · Enemies · Gear · **Pets (23)** · **Mounts (11)**. Each entry
shows its permanent all-stats bonus; undiscovered entries are silhouettes. Header states the total
completion bonus and that **passive bonuses activate at Legend 100** while entries track from the start.
Talent-Point milestones at 25 / 50 / 75 / 90 / 100 %, claimable at any level. Design the section chrome
to take a changing entry count — the per-section totals are still being re-derived.

#### S38 · Feats
Nine category tabs — Slaughter · The Road · The Die · The Draft · The Forge · The Menagerie · The Arena ·
The Dungeons · Fellowship. Each row: name, **progress bar with real numbers** (`4,187 / 10,000`), 3 tier
pips claimed separately, reward icons, CLAIM. Persistent **Renown header** with the total and the next
1,000-point milestone (+2 Talent Points). CLAIM ALL. 🔒 Nothing is missable or time-limited. Category
counts are uneven and still moving — design row chrome, not a fixed grid.

#### S25 · Daily Quests / Login / Lucky Wheel
Three panels: 3 daily quests with progress, reward icons, CLAIM, one free reroll, `AD_DOUBLE_QUEST`
(3/day), and the all-3 bonus chest · the **28-day login calendar** with days 7 / 14 / 21 / 28 highlighted ·
the **Lucky Wheel**, 8 **always-positive** segments, 1 free spin + 2 via ad.

#### S37 · Inbox
Server→player only, **no player-to-player messaging anywhere in this game**. List grouped by the six
categories (`ANNOUNCEMENT`, `COMPENSATION`, `RECONCILIATION`, `MODERATION`, `ACCOUNT`, `MILESTONE`), unread
dot, attachment icons, expiry date, per-message CLAIM and a primary **CLAIM ALL**. Needs the held state
*"Not enough inventory space."* Chest/egg/crate attachments claim as **unopened containers**, not contents.
🔒 No marketing content, ever — no Plus promotion, no event advertising, no come-back nudges.

#### S26 · Settings
Audio (separate music / SFX / UI sliders, ad ducking) · haptics · battle speed · the **accessibility block**
in §3.5 · **Account row** (link status, provider, linked date, delete-account path) · privacy · restore
purchase. Plus a dedicated **Odds & Guarantees page**: every drop rate and every pity `N` in the game, in
plain language. The die is uniform 1–6 and has no settings row of its own.

#### S27 · Profile
Name · PvP tier label · Legend Level · **Renown total beside Legend Level** (the post-cap number a player
can still watch go up) · account-link status as a plain line · lifetime stats.

#### S30–S32 · Events
- **S30 Events Hub** — live events as cards: name, art, **remaining window as a plain date**, track
  progress bar, ENTER. Upcoming events as dated grey cards. 🔒 **No countdown timers below 24 hours.**
- **S31 Event Track** — the milestone ladder (≤ 12 rungs, strictly ascending) with claimed / claimable /
  locked states, the current point total, and **one line stating how the currency is earned**.
- **S32 Event Shop** — stock priced in event currency with **per-player remaining counts**, plus the
  permanent line *"Everything here can also be earned in the Honor Shop or from Soul Shards."* Needs a
  close-out state explaining that unspent event currency auto-converts to Crowns.

---

## 7. Build order

| Tranche | Screens | Why |
|---|---|---|
| **1** | Component inventory · S05 · S06 · S07 · S14 | This is the game. Ten minutes of a player's session is these five surfaces. |
| **2** | S03 · S04 · S01 · S13 · the tile cards · Stage Gate | Closes the loop from launch to run and back. |
| **3** | S16 · S17 · S15 · S18 · S19 | The reason to run again. S16 and S17 carry the most novel components (shelf, pity counters, compare panel, five forge tabs). |
| **4** | S23 · S25 · S37 · S26 · S27 · S24 · S38 | Service layer. Mostly lists — fast once the components exist. |
| **5** | S20 · S21 · S22 · S28 · S29 | PvP and dungeons. |
| **6** | S30–S36 · the §8 overlays | Live ops and guilds. |

---

## 8. Unnumbered surfaces that still need a design

Referenced by the systems, absent from the screen register. Each needs at least a card or overlay.

| Surface | Must show |
|---|---|
| **Fixed-die number prompt** | A six-way 1–6 picker, opened by every fixed-die grant. Live in every run state. Non-blocking — the player may keep rolling with a choice outstanding. This is the most-used overlay in the game after the draft; design it first. |
| **Fork choice prompt** | Two labelled branches with honest icon previews, up to 6 s; can fire mid-move, more than once per roll |
| **Chest / egg / crate open** | Open animation and results, the per-class pity counter advancing, OPEN ALL sequencing, and the *"Not enough inventory space"* rejection |
| **Focus selector** | Slot + family picker, the ×2.5 explanation, and the 12-hour cooldown before a change takes effect |
| **Level-up / unlock celebration** | Legend Level up (+1 Talent Point, Energy to full) and the feature unlocks at 5 / 8 / 10 / 15 / 20 / 30 / 60 / 100 |
| **Rewarded-ad flow** | Confirm · loading · *"Come back tomorrow"* past the 44/day soft cap · the 20 s minimum gap · and the **grant-anyway** path when an ad fails to load |
| **Interstitial handoff** | The beat before a stage-gate interstitial and the 4th return to Home. Never for Plus, in FTUE, in the first 72 h, or after a death |
| **Account-link prompt** | Two one-off full cards (Legend 10 and 30), dismissible in one tap, stating the 500 Soul Shards + 1 chest reward |
| **Account conflict card** | Side-by-side comparison of both accounts (Legend Level, chapters cleared, gear count, last played), an irreversible choice with **typed confirmation on the discarding side**, and the 30-day recovery window |
| **Run resume card** | 1.2 s — *"Picking up where you left off — Chapter 5, Stage 2."* |
| **Abandon run confirm** | States the 0.10 multiplier and that no gear drops are kept. Reachable from the board at all times |
| **Plus lapse notice** | One in-app message: nothing was lost, and presets beyond 3 are read-only rather than deleted |
| **Hero naming** | First launch. 12 characters, default "Wanderer", profanity-filtered, with its rejection message |
| **Report / moderation confirm** | From a roster row, a guild profile, or a duel candidate card |
| **Guild disband** | Typed confirmation, Leader only, immediate |
| **Forced update / content mismatch** | From S01, and the only place a blocking full-screen error is allowed |
| **Toast set** | Reconnecting · Caught up · Waiting for connection · claim/purchase confirmations |

---

## 9. Planned, not yet specified

**Idle Reward.** A reward that accrues while the player is away is planned but not designed. Nothing
about its shape, cadence, currency or claim rule exists yet.

What that means for this handover: **reserve a fourth widget slot in the S03 daily strip** and make sure
the rail flexes to a fourth card without re-flowing the screen. Do not design the widget, do not invent a
claim flow, and do not add "while you were away" language anywhere else. When it lands it will most
likely also want a claim overlay in §8's list.

Note for whoever specifies it: the game currently has **no offline accrual of any kind** except Energy
regeneration, and that exclusivity is a locked design property. An idle reward is a change to that
property, not an addition beside it.

---

## 10. Known gaps

Real holes in the current design, not history. Design around them; do not invent answers.

- **Chapter 8 has no chapter modifier.** Chapters 1–7 each have a signature mechanic; Astral Spire has
  none. Nothing on the Chapter Select card should promise one.
- **Codex per-section totals are being re-derived** after the pet and mount counts changed. Entry counts
  in §6 are current; the aggregate completion bonus is not final.
- **Feat category counts are uneven and still moving.** The Die is a short category.
- **The Caster family axis has no set bonus** while the other three do. Its SS items need a legible
  *"no set bonus"* state on S17 and in gear tooltips.

---

## Counts

- **37 numbered screens** (S01–S38, S12 retired) → **39 distinct layouts**, S10 being three minigames.
- **10 have a client scene today**: S01, S03, S04, S05, S06, S07, S08, S11, S14, S16.
- Plus **17 unnumbered surfaces** (§8) and the FTUE overlay layer.
