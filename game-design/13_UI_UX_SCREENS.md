# 13 — UI / UX & Screen Inventory

Portrait only. One-handed. Designed for a 6.1" phone at 1080×2340, safe-area aware, scaling from 16:9 to 20:9.

---

## 1. Screen inventory

| # | Screen | Purpose |
|---|---|---|
| S01 | Splash / Loading | Boot, auth, server session, profile fetch, content hash check |
| S02 | FTUE Tutorial Run | Scripted first run (see `02` §8) |
| S03 | **Home / Camp** | Hub. Energy, Legend bar, daily quests, ad widgets, Continue CTA |
| S04 | Chapter Select | Chapter list, difficulty tier picker, power warning, run confirm |
| S05 | **Board** | The run. Die, hero token, track, HUD |
| S06 | **Battle** | Auto-battle replay, speed toggle, skip |
| S07 | **Perk Draft** | 3 cards, reroll, skip, ad-4th-option |
| S08 | Shop Tile | 4 offers, gold, refresh |
| S09 | Event Card | Title, body, 2–3 options |
| S10 | Minigame (×3) | `MG_CHEST_PICK`, `MG_TIMING_BAR`, `MG_MEMORY_RUNE` — `MG_DICE_DUEL` is removed (`16` D58) |
| S11 | Campfire / Shrine | 2–3 choice cards |
| ~~S12~~ | ~~Die Panel~~ | ⚠️ **Removed.** It disclosed the composed die because *a hidden die is a hostile die*; the die is an ordinary 1..6 with nothing to disclose (`04` §4). The number stays retired rather than reused. |
| S13 | Death / Revive | Revive offer |
| S14 | Run Results | Reward tally, ad-double, retry, home |
| S15 | **Hero** | Equipped gear, pets, mount, full stat sheet, loadout presets |
| S16 | Inventory | Gear grid, filters, sort, compare, lock, auto-salvage rules |
| S17 | **Forge** | Merge, Enhance, Salvage tabs |
| S18 | **Talents** | 3 branch tabs, node grid, respec, preview |
| S19 | **Menagerie** | Pets grid, pet detail, mounts grid, mount detail |
| S20 | **Arena** | Rating, tier, 3 opponent cards, duel log, season timer |
| S21 | PvP Loadout | Perk budget builder, PvP gear/pets/talent presets |
| S22 | Leaderboard | Global ladder, every player ranked; top 50 initial view, own position pinned, search by name |
| S23 | Shop | Daily, Honor, Materials, **Plus** tabs |
| S24 | Codex | Perks, enemies, gear, pets discovered; mastery bonuses |
| S25 | Daily Quests / Login | Quest list, login calendar, Lucky Wheel |
| S26 | Settings | Audio, haptics, speed, accessibility, account, privacy, restore purchase |
| S27 | Profile | Name, PvP tier label, lifetime stats |
| S28 | Dungeon Select | 3 dungeon cards, tier, literal payout, entries remaining, ad entry (`25` §8) |
| S29 | Dungeon Board | S05 without forks, stage pips or shop; material counter replaces Gold (`25` §8) |
| S30 | **Events Hub** | Live and upcoming events, track progress, ENTER (`26` §6) |
| S31 | Event Track | Milestone ladder, point total, how currency is earned (`26` §6) |
| S32 | Event Shop | Event-currency stock list with per-player counts (`26` §6) |
| S33 | **Guild Home** | Guild Quests, streak, Guild Boss card, Phrase Board, Guild Log (`27` §10) |
| S34 | Guild Roster | 30 rows: role, last-active, weekly contribution, report (`27` §10) |
| S35 | Guild Browser | Search, filter, sort by activity; create guild (`27` §10) |
| S36 | Guild Boss | Shared HP bar, contribution list, attempts, bracket preview (`27` §10) |
| S37 | **Inbox** | Server messages by category, attachments, CLAIM ALL (`28` Part A) |
| S38 | **Feats** | 140 feats in 9 categories, tier pips, Renown header (`28` Part D) |

**37 numbered screens** (S12 is retired, not reused), and **39 distinct layouts** — S10 is three separate minigame screens. The bottom navigation stays at **five** items (`13` §2) — Dungeons enter from Chapter Select, Events and Guilds from Home cards. Adding a sixth nav item breaks one-handed reach on a 6.1" phone, and none of these three is a hub the player visits more than once a day.

### 1.1 New surfaces on existing screens

| Screen | Addition | Source |
|---|---|---|
| S03 Home | Chest-class pity counters on the chest widget; Event card (only while live); Guild card | `24` §9, `26` §6, `27` §10 |
| S14 Run Results | Session-floor announcement line; `DROP_RUN` mercy counters in the tally footer | `24` §9 |
| S16 Inventory | Focus selector; per-item Reforge / Retune entry points | `24` §9 |
| S17 Forge | **Reforge** and **Retune** tabs; Set Token counter; Focus row | `24` §9 |
| S19 Menagerie | Beast Mark exchange row with all three tiers | `24` §9 |
| S23 Shop | Every chest listing states its class and current pity counter before purchase | `24` §9 |
| S07 Perk Draft | The three `DRAFT` counters, each naming its unit rather than a bare number: *"Rare or better guaranteed in 2 more drafts you pick from"* | `24` §9 |
| S26 Settings | **Odds & Guarantees** page — every rate and every pity `N`, in plain language, in EN and DE | `24` §1.1 |
| S26 Settings | **Account** row: link status, provider, linked date, delete-account path | `28` Part B |
| S03 Home | Envelope icon with unread count; account-link banner (fortnightly, dismissible, unlinked players only) | `28` Parts A, B |
| S03 Home | Energy readout becomes `138/200 (+200)` with a desaturated Reserve segment behind the main bar | `28` Part C |
| S24 Codex | Gains a **Feats** sibling tab — collection and action, side by side | `28` Part D |
| S27 Profile | Renown total beside Legend Level; account link status | `28` Part D |
| S16 Inventory | **Unopened shelf**: gear chests as stored containers — class, live pity counter, OPEN / OPEN ALL | `24` §4.0 |
| S19 Menagerie | **Unopened shelf**: Pet Eggs and Mount Crates as stored containers — class, live pity counter, OPEN | `24` §4.0 |
| S05 Board | Consumable pouch in the HUD: held Draughts/Ropes, USE on the board only, armed-rope indicator | `03` §7.1 |

---

## 2. Home screen layout (S03)

```
┌─────────────────────────────────────┐
│ [avatar] Wanderer  Lv 74  DIAMOND   │  ← tap → Profile
│ ▓▓▓▓▓▓▓▓░░ Legend XP 61%            │
│ ⚡ 138/200 (+1 in 2:41)  [+40 ▶ad]  │
├─────────────────────────────────────┤
│                                     │
│        [ HERO DIORAMA ]             │  ← animated hero + mount + pets
│        chibi hero idling with       │     shows equipped gear
│        equipped gear visible        │
│                                     │
├─────────────────────────────────────┤
│  DAILY  ▸ 2/3 quests   [CLAIM ●]    │
│  🎁 Free chest ▶ad   🎡 Wheel ▶ad    │
├─────────────────────────────────────┤
│   ┏━━━━━━━━━━━━━━━━━━━━━━━━━━━┓     │
│   ┃  ▶  CONTINUE               ┃     │  ← Chapter 5 · Normal
│   ┃     Frostbound Reach       ┃     │
│   ┗━━━━━━━━━━━━━━━━━━━━━━━━━━━┛     │
├─────────────────────────────────────┤
│ [Hero][Forge][Talents][Beasts][Arena]│  ← bottom nav, 5 items + Shop in header
└─────────────────────────────────────┘
```

**Two taps from launch to rolling a die.** Continue → confirm → board.

⏳ **Reserve a fourth widget slot in the daily strip** for the planned **Idle Reward** (`16` O37). The rail must flex to a fourth card without re-flowing the screen. Nothing else about it is designed, and **no "while you were away" language appears anywhere in the UI** until it is — the game has no offline accrual beyond Energy regeneration, and promising accrual it does not have is worse than not mentioning it.

---

## 3. Board screen (S05)

```
┌─────────────────────────────────────┐
│ ❤ 4,120/5,600 ▓▓▓▓▓▓▓░░  Stage 2/3  │  ← HP bar, stage pips
│ 💰 1,340  [🧪 2]          [≡ perks] │  ← gold, consumable pouch, perks
├─────────────────────────────────────┤
│                                     │
│         ・  ⚔  ✨                    │  ← the WHOLE track, always (`04` §4, `16` D42)
│           ╲ │ ╱                     │
│            🎁                       │
│             │                       │
│        ┌────┴────┐                  │  ← fork: two labelled branches
│      [Perilous] [Sheltered]         │
│        ⚔☠🎁      ✨🔥🏪              │
│             │                       │
│           [🐎🧙]                     │  ← hero token on mount, 40% height
│             │                       │
│         (dimmed resolved tiles)     │
├─────────────────────────────────────┤
│              ╭─────╮                │
│              │  🎲 │  ← big roll button, thumb-reachable
│              ╰─────╯                │
│   Your dice  [3] [5 ×2]             │  ← fixed dice, spent instead of rolling
└─────────────────────────────────────┘
```

Rules:
- The roll button is the largest interactive element on screen and sits in the bottom-third thumb zone.
- The perks button `[≡ perks]` opens a scrollable list of everything drafted this run with current tiers. It must be reachable at all times; a player must never lose track of their build.
- ⚠️ The roll button has **no long press**. It opened the Die Panel (S12), which is gone with the die's faces (`04` §4), so a roll is one tap and there is no second gesture on the control.
- 🔒 **The whole track is drawn, always** — no fog, no preview range, nothing clipped or scrolled to (`16` D42). It is the reason a *fixed* die is worth choosing a number for: the player is reading what the numbers reach. A track too long for one row **wraps**; a board the player has to drag to see is not a board that is completely visible.
- 🔒 **The fixed-die tray** sits directly above the roll button, inside the same thumb zone, as one control per number held (`04` §6). Pressing one spends it and moves exactly that far. It is disabled by exactly what disables the roll, and hidden when the run owns none. A granted die opens a **six-way number prompt** in its place — the player names the number (`04` §6.2) — and that prompt is live in every state, including the ones that refuse a roll, because naming a number moves nothing.
- **The consumable pouch** sits in the top HUD row (⚠️ it used to sit beside the reroll pips, which are gone with the reroll — `04` §5): a compact `[🧪 n]` button showing the held count (cap 4 — `03` §7.1). Tapping it fans out the held items (Health Draughts, Escape Ropes) as mini-cards with a one-line effect and a USE button each. USE is enabled only in `AWAIT_ROLL` and never during battle (D3); the Draught's USE is additionally disabled at full HP. An **armed Escape Rope** shows as a small rope icon hovering over the hero token until it fires. When empty, the pouch renders at 40% opacity but stays visible — the affordance must be learnable before the first purchase. `03` §7.1 owns the designs.

---

## 4. Perk draft screen (S07)

Three cards fill the screen vertically. Each card shows: category colour bar, rarity gem, icon, name, tier badge (`I` / `II` / `UPGRADE →III`), effect text with **real numbers substituted**, and a one-line synergy hint if it interacts with an owned perk (*"Synergy: Crit Cascade"*).

- Owned-perk upgrades get a gold border and animate in slightly larger.
- The reroll button shows remaining free rerolls; the ad-reroll is a separate, visually quieter button.
- The 4th-option ad slot appears as a dashed placeholder card below the three.
- Skip is a small text button at the bottom, showing what it grants (`Skip → +60 Gold, +1 reroll`).

**Never auto-advance this screen.** It is the game's main decision.

---

## 5. Battle screen (S06)

```
┌─────────────────────────────────────┐
│ ⚔ Rimefang Warden        [ARMORED]  │  ← enemy banner + elite modifier
├─────────────────────────────────────┤
│  [biome backdrop, parallax 3 layers]│
│                                     │
│   🧙 ← hero            enemy → 🧊    │
│   🐾🐾 pets orbiting                 │
│                                     │
│      -1,204!   (floating combat text)│
├─────────────────────────────────────┤
│ ❤ ▓▓▓▓▓▓▓░░░  4,120    ▓▓▓░░ 9,800 ❤│
│ [×1 ×2 ×3]                  [SKIP ⏭]│
└─────────────────────────────────────┘
```

- Speed toggle and skip are always visible.
- Status effect icons sit under each HP bar with stack counts.
- On boss phase change, a full-width band flashes the phase name.

---

## 6. Forge screen (S17)

Three tabs. The **Merge** tab is the star:

- Select a target item → the UI auto-suggests the 3 best fodder items → shows a big before/after stat delta → one **MERGE** button.
- The merge animation is short (1.2 s) and celebratory: three items spiral in, a flash, the new rarity frame slams into place.
- Merge Dust substitution appears as a "use dust instead" toggle on any of the three fodder slots.

The **Enhance** tab shows the success percentage as a large number, the `AD_ENHANCE_LUCK` boost as a clearly-labelled additive `+15%`, and a running "stones invested" total. Failure animation must be brief and non-punitive in tone — the item is never at risk.

---

## 7. Visual & UI style rules

| Rule | Detail |
|---|---|
| Aspect | Portrait only, 9:16 to 9:20, safe-area padding |
| Base resolution | 1080×1920 design canvas, scaled |
| Minimum touch target | 48×48 dp, 8 dp spacing |
| Font 🔒 | **Baloo 2** (rounded heavy display) for headings, numbers and buttons; **Nunito Sans** for body and long text. Both SIL Open Font License — free for app embedding, no attribution burden. Baloo 2 has script-specific siblings (Baloo Chettan, Baloo Da, etc.) if localisation expands beyond Latin. |
| Panels | Rounded 24 dp corners, 3 dp dark outline, subtle inner gradient, soft drop shadow |
| Buttons | Chunky, high-contrast, 3 dp outline, pressed state = 4 dp downward offset + darker fill |
| Rarity colours | C `#9AA5B1` · B `#4CAF50` · A `#3B82F6` · S `#F5A623` · SS `#C13BE8` |
| Rank & status display | PvP tier and Slay Plus are **text labels in the tier colour**, never frames or badges. There are no cosmetic assets in v1. |
| Numbers | Always abbreviated above 10k (`12.4k`, `3.1M`), full value on long-press |
| Currency icons | Always adjacent to their number, never text-only |
| Animation budget | No screen transition longer than 300 ms; all skippable |

Everything must be readable at arm's length on a phone in daylight. Bold outlines and high contrast are not just a style choice here — they are the legibility requirement.

---

## 8. Accessibility (required in v1)

| Feature | Requirement |
|---|---|
| Reduced motion | Disables screen shake, parallax, particle bursts; shortens all animations to 100 ms |
| No-timer mode | Removes all soft timers; every prompt waits indefinitely. ⚠️ Its headline subject was the 4 s reroll countdown, which is gone with the reroll (`04` §4) — the setting still governs every other prompt. |
| Colourblind support | Rarity conveyed by **frame shape + gem symbol**, not colour alone. Three palettes: default, deuteranopia, tritanopia. |
| Text size | 3 steps (100 / 115 / 130%) with layouts that reflow |
| Haptics toggle | On/off |
| Sound | Separate music / SFX / UI sliders; ducking for ads |
| Battle skip | Always available — this is also an accessibility feature |
| Left-handed mode | Mirrors the roll button and bottom nav |

---

## 9. Audio direction

✅ **Fully specified in `20_AUDIO_MANIFEST.md`** — 12 AI-generated music tracks and 94 SFX, with per-asset prompts, a technical spec and a QA checklist.

---

## 10. Localisation 🔒

**Launch language: English only** (`16` D43 — amends D20). German is descoped to post-launch: `loc/de.json` and its `##TODO_DE##` rows stay **frozen, not deleted**, and the `en`/`de` key-parity test stays live.

All user-facing strings are keys in `res://data/loc/*.json` regardless, so adding a language later is a translation job rather than an engineering one. That is the whole reason the descope is cheap.

| Rule | Detail |
|---|---|
| Source language | English |
| Launch set | `en` |
| Layout tolerance | 🔒 **Keep ~30% width tolerance anyway.** Every label, button and panel is designed and tested as if it held a German string at the largest text-size setting. German returns post-launch, and re-laying out every screen at that point is the expensive outcome — the tolerance costs nothing now and buys the whole locale later. |
| Numbers and dates | Locale-aware formatting from day one (German uses `.` for thousands and `,` for decimals) |
| Quality | 🔒 **Nothing ships machine-translated.** D20's rule is honoured by shipping one locale rather than weakened — DE returns only when a named human localiser is assigned and the EN string set is frozen. |
| Future expansion | The high-ROI set after DE is FR, ES, PT-BR, RU, TR — Latin/Cyrillic only, so no font work is needed. CJK would require font siblings and wider UI tolerance; treat it as a separate project. |

---

## 11. Connection states 🔒

Because PvE is server-authoritative (`14` §2), the UI must handle connection loss gracefully on every screen.

| State | Presentation |
|---|---|
| **Connected** | No indicator at all. Never show a green tick — a permanent connection badge just reminds the player something can break. |
| **Reconnecting** | After 2 s of failed retry, a small pill slides in top-centre: *"Reconnecting…"* with a slow pulse. Non-blocking. The current screen stays interactive for read-only actions. |
| **Offline, read-only** | Buttons that require the server dim to 40% with a small cloud-slash glyph. Tapping one shows an inline toast: *"Waiting for connection."* — never a modal. |
| **Resynced** | A brief 0.4 s green flash on the affected HUD elements and, if any state changed, a one-line toast: *"Caught up."* |
| **Run resumed** | If the app reopens into an in-progress run, show a 1.2 s card: *"Picking up where you left off — Chapter 5, Stage 2."* |

🔒 **Never** show a full-screen blocking connection error during a run. The run state is safe on the server for 48 hours; the interface should communicate calm, not alarm.
