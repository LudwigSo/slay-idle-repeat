# Game Design

What the game *is*: pillars, loops, systems and the constraints that shape them. The full
code layout and tech stack are in
[`ARCHITECTURE.md`](ARCHITECTURE.md).

**Slay. Idle. Repeat.** — a portrait, one-handed, always-online mobile roguelike RPG. Roll a
die to move a big-headed hero along a procedurally generated board; landing on a tile fires an
event (auto-battle, shrine, shop, curse, chest, minigame). Win a fight, draft 1 of 3 run-scoped
perks. Reach the end, fight the chapter boss. Win or die, bank gear, pets, currency and Legend
XP into a permanent meta spine. Android at v1, iOS post-launch. Business model: rewarded ads
plus **Slay Plus**, a €4.99/month subscription that removes ads and auto-grants every ad
reward. **No purchasable power. No cosmetics.**

## Pillars

1. **The grind is the game, and it is honest.** Power comes from time, decisions and drop luck
   only — never a wallet.
2. **Every run is a short, complete story.** 8–12 minutes, legible in 30 seconds, satisfying
   even in death.
3. **Randomness the player can bend, and randomness that cannot bury them.** Fixed dice, draft
   rerolls, a chosen loot Focus — and a *visible floor* under every random source.
4. **Competition without an arms race.** One asynchronous PvP mode; the ladder measures play.

**Out of scope, deliberately:** idle/offline income (Energy regeneration is the one exception),
hero rosters, real-money currency or IAP bundles, loot boxes, skippable timers, cosmetics of
any kind, free-text chat, real-time multiplayer, prestige/ascension (first post-launch update),
active skill buttons in combat, banner ads, offline play.

The pillars above are **locked**, along with run-based-only sessions, full auto-battle, a
single hero with companions, and server authority over PvE. Every number stated is **tunable
data** — in a data file, changeable server-side, never hardcoded.

## The run

Three time scales: **roll → move → resolve tile → watch the auto-battle → draft a perk** (20–60
s) · **one run** costs 20 Energy: 3 stages of 12/14/16 tiles plus a boss tile, 18–22 battles,
8–12 minutes, ending in victory, death or abandon (5–14 runs/day) · **meta**: merge gear, spend
talent points, level pets, unlock the next chapter and tier, update the PvP ghost, climb the
season ladder. Run state is server-owned and resumable for 48 hours on any device; stage gates
heal, step enemy power up and lift draft rarity.

**Board.** A directed acyclic graph that reads as a mostly-linear track with authored forks
that rejoin. Forks are the board's real decision — each branch previews its risk profile, and
both pay identical enemy power at equal forward progress, so a fork is never a power discount.
Movement is always forward and stepwise; only the landing node resolves. **14 tile types**:
Enemy, Elite, Boss, Shrine, Cursed Ground, Treasure, Shop, Campfire, Minigame, Event, Portal,
Beast Cache, Dice Forge, Waypoint — weighted per chapter as data. 8 chapters × 3 difficulty
tiers (Normal / Heroic / Mythic), one multi-phase boss each.

**Dice.** One ordinary six-sided die, drawn uniformly from the run's seeded stream. No face
kinds, no rerolls, no luck smoothing — *the board* is what makes a roll interesting: a 6 is
better or worse depending on what sits six nodes ahead. A **fixed die** is the player's one
deterministic input: granted by the Dice Forge tile and the Campfire, the player names the
number 1..6 and it moves exactly that far, consuming no randomness.

**Combat.** Fixed-tick (20 ticks/second, 90-second cap) deterministic auto-battle — the player
never taps. Hero, up to 3 pets (untargetable ability modules) and a passive mount against 1–5
enemies. 14 stats with hard caps, a strict aggregation order (flat, then additive percent, then
multiplicative) and rounding at every accumulation point, so a seed and a build always replay
identically. Every effect in the game — perk, talent, curse, boss ability — is authored in one
declarative effect language rather than in code.

**Perks.** 98 run-scoped perks (90 standard plus 8 cursed) across 9 categories: Lightning,
Cold, Fire, Poison, Bleed, Defense, Offense, Crit, Sustain. Drafted 1-of-3 at the run opening
and after every won battle, 3 tiers each, so re-drafting a perk upgrades it — go tall or go
wide. Skipping pays gold and a reroll.

## Meta progression

- **Gear and the Forge.** 6 slots × 4 families = 24 base items × 5 rarities = 120 entries, plus
  4 Mythic sets. Families bias a build. Five operations, all free of real money: merge (3
  identical → next rarity), enhance (+0…+15), salvage, reforge and retune.
- **Talents.** Two branches, MIGHT and WARD, 20 nodes each across 4 point-gated tiers ending in
  keystones — specialisation is forced. Respec is free, unlimited and instant, and the tree is
  deliberately never maxable in v1.
- **Collection.** One hero, up to 3 equipped pets and 1 mount; both level on Beast Feed and
  ascend on duplicates. Pets carry a passive aura and an active ability; mounts are passive.
  Loadout changes are free outside a run, snapshotted at run start.
- **Economy.** 8 currencies — Gold (run-local, wiped at run end), Crowns, Soul Shards (the
  earned-only "premium", a pacing valve not a revenue lever), Energy, Enhance Stones, Merge
  Dust, Beast Feed and Honor. Energy gates runs: 120 max, 1 per 4 minutes, 20 per run, overflow
  banking into an Energy Reserve — the only thing in the game that accrues offline.

## Fairness

- **Luck protection.** Every random source has a deterministic floor, built from three
  primitives — hard pity (forced success after N misses), soft pity (rising weight) and mercy
  accrual (misses buy a deterministic substitute). Counters are server-owned, scoped per source
  class so they cannot be farmed cheaply, never decay, are never purchasable, and are **always
  shown as a real number**. The **Focus** system lets the player name a slot and family for
  weighted gear grants.
- **The ad contract.** Every rewarded benefit is capped and reachable free by watching; a
  subscriber receives exactly that capped amount automatically. An ad-watcher and a payer sit
  on an identical power curve; a player who watches nothing is never more than 45% behind at
  day 30. 29 rewarded placements, no banners, entitlement owned by the server, and nothing lost
  when a subscription lapses.

## Competition, social and live service

- **PvP — Ghost Duel.** One asynchronous mode. A server-built immutable snapshot of a build is
  the opponent, so a player can be duelled offline. Simplified Elo in which **only the
  attacker's rating moves**, a global ladder where everyone is ranked, seasons, and an Honor
  shop. Non-combat perk categories are banned from the duel loadout.
- **Resource Dungeons.** Three daily deterministic dungeons across 8 tiers, one per bottleneck
  material — Enhance Stones, Beast Feed, Crowns. Soul Shards stay unfarmable.
- **Live-ops.** A data-driven limited-time-event framework with three archetypes — a re-skinned
  chapter, a shared-seed score rush, a low-pressure collection event — each a content package
  with its own currency, reward track and shop, on a rolling calendar.
- **Guilds.** 30 players, unlocked mid-progression. Daily collective quests, a weekly Guild
  Boss, guild levels and non-combat perks only. **No resource transfers and no free text** —
  even the guild description is assembled from authored phrases.
- **Live-service essentials.** An inbox, account linking, the Energy Reserve, and 140 Feats in
  3 tiers feeding **Renown** — retroactive, never missable, never luck-based, and the number a
  content-complete player can still watch go up.

## Balance and grading

Combat power is one scalar, `K·√(EffectiveHP × DPS)`, evaluated against a fixed reference
opponent — geometric, so a build with vast health and no damage scores near zero under the
90-second fight cap. Three authored tables grade it: power needed per chapter and tier, power
expected at each Legend Level, and power expected per day per player profile. An **economy
simulator** plays 14 behavioural profiles against those curves and must pass before the live
service opens. Canonical pacing: content-complete around day 25–45 for an ad-watcher, mastery
around day 150+.

## Art style

Cartoonish fantasy-battle art with exaggerated, chunky proportions — oversized heads, small
bodies, expressive faces — paired with **painterly, semi-realistic rendering** of textures like
armour, fur and cloth. Bold, saturated warm colours and thick, clean silhouettes built to read
instantly even at small scale. Medieval-fantasy subject matter treated with a playful, humorous
tone rather than gritty realism. Soft, directional lighting gives scenes a glossy, tactile,
toy-like dimensionality. The mood is vibrant, energetic and lighthearted despite the combat
themes.

The game is **real 3D** — characters, props and boards are meshes lit and rendered in engine,
with the 2D interface overlaid above the 3D world. Specifics that follow: characters are 2.5–3
heads tall with oversized hands and feet; eyes are large, high-contrast and expressive; each
biome has its own locked palette; the key light holds one consistent direction across every
asset; actors sit on a fixed 3/4 low-angle camera so they read as heroic. **Silhouette is the
quality bar** — a character must stay identifiable filled black at 64 px, and detail that does
not survive that test is painted into the texture rather than modelled.

## Production scope

975 art assets · 106 audio assets · 39 portrait screens, EN and DE at launch, accessibility
required in v1 · content tables covering 30 event cards, 20 daily quests, 14 weekly modifiers,
curses, the Lucky Wheel, a 28-day login calendar and the FTUE · a running asset-licence
register.