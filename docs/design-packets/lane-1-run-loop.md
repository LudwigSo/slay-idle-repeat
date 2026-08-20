# Lane 1 · Run loop

**Packet 2 of 7 — generated from `docs/SCREEN_DESIGN_BRIEF.md`, do not edit by hand.**

This packet covers **S05 Board, S06 Battle, S07 Perk Draft, tile cards, Stage Gate, S10 minigames, S13, S14**. Everything below the token slot is the shared constraint set, identical in
every packet; design only this lane's surfaces. Artboard names follow §4: `S05-Board`,
`S05-Board-fork`, `X-CurseCard`.

> **Note on numbering.** These packets are the **lanes** of §4. The “Tranche” numbers in §7 below are a
> *build order that cuts across lanes* and do not line up with lane numbers — read §7 as sequencing
> advice, not as a description of this packet's contents.

---

## 🔒 Locked tokens — **paste the Lane 0 block here before starting**

```
<<< PASTE THE TOKEN BLOCK EMITTED BY LANE 0 (see §5.1) >>>
```

These values are already decided. Use them exactly; do not re-derive a palette, a type scale or
the nine perk-category hexes. If the slot above is still empty, **stop and run Lane 0 first** —
a lane designed without it will not match the rest of the game.


---

# Screen Design Handover — Slay Idle Repeat

**For: Claude Design.** Current as of 2026-08-20.

This document states the design as it stands. Where it disagrees with a file in `game-design/`, this
document is right and the file is behind — the owning docs are propagated separately.

---


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


---

## 6. The screens — this lane only

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
