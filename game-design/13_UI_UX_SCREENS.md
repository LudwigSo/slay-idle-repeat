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
| S10 | Minigame (×4) | `MG_CHEST_PICK`, `MG_TIMING_BAR`, `MG_DICE_DUEL`, `MG_MEMORY_RUNE` |
| S11 | Campfire / Shrine | 2–3 choice cards |
| S12 | Die Panel | Current 6 faces with sources, opened from the board HUD |
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

---

## 3. Board screen (S05)

```
┌─────────────────────────────────────┐
│ ❤ 4,120/5,600 ▓▓▓▓▓▓▓░░  Stage 2/3  │  ← HP bar, stage pips
│ 💰 1,340   🎲 rerolls ●●○   [≡ perks]│
├─────────────────────────────────────┤
│                                     │
│         ・  ⚔  ✨                    │  ← upcoming tiles (scroll up to preview)
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
└─────────────────────────────────────┘
```

Rules:
- The roll button is the largest interactive element on screen and sits in the bottom-third thumb zone.
- The perks button `[≡ perks]` opens a scrollable list of everything drafted this run with current tiers. It must be reachable at all times; a player must never lose track of their build.
- The die panel (S12) is opened by long-pressing the roll button.

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
| No-timer mode | Removes the 4 s reroll countdown and all soft timers; every prompt waits indefinitely |
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

**Launch languages: English and German only.**

All user-facing strings are keys in `res://data/loc/*.json` from day one regardless, so adding languages later is a translation job rather than an engineering one.

| Rule | Detail |
|---|---|
| Source language | English |
| Launch set | `en`, `de` |
| Layout tolerance | German strings run **~30% longer** than English. Every label, button and panel must be tested with the German string at the largest text-size setting. This is the single most common launch bug in German localisation. |
| Numbers and dates | Locale-aware formatting (German uses `.` for thousands and `,` for decimals) |
| Quality | German is human-reviewed, because it is verifiable by the team. No language ships machine-translated and unreviewed. |
| Future expansion | The high-ROI next set is FR, ES, PT-BR, RU, TR — Latin/Cyrillic only, so no font work is needed. CJK would require font siblings and wider UI tolerance; treat it as a separate project. |

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
