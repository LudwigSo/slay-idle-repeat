# 27 — Guilds

🔒 **Decision D26: guilds ship in v1.** This reverses the "no guilds, chat, alliances, friend lists, social graph" exclusion in `00_README_INDEX.md` §3, which is amended accordingly.

Guilds are the strongest retention system in this game's reference set, and the one thing a purely solo design cannot replicate: a reason to open the app that is about somebody else. They are also the single largest source of ongoing operational cost and reputational risk in the project, so this document is built around containment as much as around design.

---

## 1. The three rules that shape everything below

| # | Rule | Why |
|---|---|---|
| **R1** | 🔒 **No guild benefit may be combat power.** Guild perks affect Crowns, Beast Feed, Energy regeneration and material yield **only**. Nothing a guild grants may change ATK, HP, DEF, drop rarity or any stat that enters a battle. | PvP is a scoreboard for the grind (`11`). If a guild grants combat power, the ladder measures guild membership, and a solo player is permanently second-class in the only competitive mode. This rule is what lets guilds exist without breaking the game's central promise. |
| **R2** | 🔒 **No resource may be transferred between players.** No vault, no gifting, no trading, no donations that another player withdraws. | Every player-to-player transfer system in a free game becomes an alt-account farm within a week. There is no cap, ratio or account-age rule that survives a determined player with ten devices, and the enforcement cost is unbounded. Guild rewards are **generated**, never **moved**. |
| **R3** | 🔒 **No free-text chat in v1.** Communication is a fixed phrase board plus an activity log. | Free-text chat in a game with a global audience is a moderation obligation, a CSAM-reporting obligation, a localisation obligation and a 24/7 on-call obligation. None of those are affordable at this project's size, and shipping chat badly is worse than not shipping it. See §7 for the upgrade path. |

Everything in this document is a consequence of those three rules.

---

## 2. Structure

| Property | Value |
|---|---|
| Members | **30** 📐 |
| Unlock | Legend Level **15** — after Forge (8), PvP (10) and Dungeons (8), so guilds are the fourth system a player meets, not the first |
| Creation cost | **20,000 Crowns** 📐 — enough to be a decision, cheap enough that any committed player can found one |
| Join policy | Founder picks **Open** · **Request** · **Invite only**, and may set a minimum Legend Level |
| Identity | Name (16 chars) and a 4-character tag, both profanity-filtered in EN and DE at creation and on every edit. **No guild emblem, banner or crest** — there are no cosmetic assets in v1 (D14). |
| Description | One line, 80 chars, from a **fixed set of 40 authored phrases** the founder assembles (§6.1b) — never free text (R3) |
| Roles | **Leader** (1) · **Officer** (up to 4) · **Member** |
| Leaving | Free, instant. **24-hour cooldown** before joining another guild. |
| Kick | Leader and Officers may kick. A kicked player carries the same 24-hour cooldown. |
| Inactivity | A member with no run in **14 days** is flagged in the roster. After **30 days** the server auto-removes them. A Leader inactive for **21 days** is auto-demoted and the longest-tenured active Officer is promoted, so a guild can never be permanently orphaned. |
| Disband | Leader-only, with a typed confirmation. Immediate. |

### 2.1 The 24-hour join cooldown 🔒

Without it, the optimal play is to join a guild the day its Guild Boss rewards pay out, collect, leave, and repeat — thirty guilds a month, contributing to none. The cooldown makes guild-hopping strictly worse than staying, which is the only enforcement that does not require a moderator.

---

## 3. Guild Quests — the daily loop

The everyday reason to open the guild tab. Collective, generated, never transferred (R2).

| Property | Value |
|---|---|
| Quests per day | **3**, drawn server-side at 05:00 UTC from a pool of **15** authored guild quests |
| Shape | Collective counters — *"The guild defeats 3,000 enemies"*, *"The guild clears 40 dungeon runs"*, *"The guild wins 60 Ghost Duels"* |
| Scaling | Targets scale with the guild's **active member count** (members with a run in the last 7 days), not its roster size. A guild of 8 actives is never given a 30-player target. 📐 |
| Contribution cap | A single member's contribution to one quest caps at **15%** of its target. One dedicated player cannot carry the guild, and thirty casual players always can. |
| Reward | Completing all 3 grants every member who contributed **anything** a **Guild Chest**. Contribution, not presence, is the gate — but the bar is one enemy killed. |
| Guild Chest | Crowns + Enhance Stones + Beast Feed, scaled by guild level and the member's own highest chapter. **No gear, no eggs, no crates, no Soul Shards, no pity progress.** It is source class `CHEST_NONE` — it grants no randomised item at all, so `24_LUCK_PROTECTION.md` does not apply to it. |
| Streak | A guild completing all 3 quests on **7 consecutive days** unlocks a one-off bonus chest for every active member. The streak resets on a miss and is displayed on the guild home. |

### 3.1 The 15 authored guild quests 🔒

Targets are for **30 active members** and scale linearly with the active count (see the Scaling row above). All 📐 TUNABLE in `data/tuning/guilds.json`.

| # | ID | Objective (at 30 actives) |
|---|---|---|
| 1 | `GQ_ENEMIES` | The guild defeats 3,000 enemies |
| 2 | `GQ_ELITES` | The guild defeats 200 Elites |
| 3 | `GQ_BOSSES` | The guild defeats 40 chapter bosses |
| 4 | `GQ_RUNS` | The guild completes 60 runs |
| 5 | `GQ_VICTORIES` | The guild wins 35 runs |
| 6 | `GQ_DUNGEONS` | The guild clears 40 dungeon runs |
| 7 | `GQ_DUELS` | The guild wins 60 Ghost Duels |
| 8 | `GQ_PERKS` | The guild drafts 700 perks |
| 9 | `GQ_TILES` | The guild travels 2,500 tiles |
| 10 | `GQ_TREASURE` | The guild opens 150 Treasure tiles |
| 11 | `GQ_MERGES` | The guild performs 50 merges |
| 12 | `GQ_ENHANCE` | The guild makes 150 enhancement attempts |
| 13 | `GQ_PETS` | The guild levels pets 80 times |
| 14 | `GQ_MINIGAMES` | The guild completes 100 minigames |
| 15 | `GQ_STARS` | The guild rolls 120 `Star` faces |

Draw rule: 3 per day, never the same quest twice in one day, and at least one of the three must be satisfiable by ordinary chapter runs alone (`GQ_ENEMIES`, `GQ_RUNS`, `GQ_VICTORIES`, `GQ_PERKS`, `GQ_TILES`, `GQ_TREASURE`) so a runs-only player can always contribute. Quests 7 and 6 are excluded from the draw until the guild's median member has unlocked PvP / Dungeons respectively.

The 15% contribution cap is the design's centre of gravity. It converts the guild from *"find one strong player and coast"* into *"we need people who show up"*, which is exactly the social pressure that makes guilds retain — applied at a level a casual player can meet in ten minutes.

---

## 4. The Guild Boss — the weekly beat

| Property | Value |
|---|---|
| Cadence | One boss per week, **Monday 05:00 UTC** to Sunday 05:00 UTC |
| Boss identity | One of the **8 existing chapter bosses** (`17_BOSS_DESIGNS.md`), rotating weekly. **No new boss art, no new mechanics.** |
| HP pool | Shared across the guild. `GuildBossHp = BaseHp × activeMembers × difficultyTier` 📐 |
| Difficulty tiers | 8, unlocked by the guild's **median** member highest-chapter-cleared. Median, not maximum — one whale must not drag a guild into a tier it cannot clear. |
| Attempts | **3 per member per week.** No ad placement, no Plus grant, no purchase. 🔒 |
| Cost | **Free.** No Energy. The Guild Boss must never compete with runs or dungeons for Energy, or a player is taxed for participating. |
| Resolution | A standard deterministic fight (`05`) against a boss with a very large HP pool and the **70-second universal enrage removed**, capped at 120 s of simulated time. The member's damage dealt in that window is added to the pool. Watchable and skippable like any battle. |
| Deaths | A member who dies still contributes the damage they dealt. There is no failure state — only a smaller number. |
| Rewards | Two components, both **generated**: a **personal** bracket reward by the member's own damage percentile within the guild, and a **guild** reward by total pool damage. Paid at week end. |
| Reward contents | Soul Shards, Crowns, Enhance Stones, Beast Feed, Set Tokens and — at the top guild bracket only — one `CHEST_PREMIUM` per member. **No SS-exclusive drops, no guild-only items** (C2 of `26` applies here too). |
| Leaderboard | Guild damage totals within your **own tier band only**, top 100. There is no global guild ladder in v1 — see §8. |

### 4.1 Why damage brackets rather than a kill

A "the guild kills the boss" model produces a binary week: either the guild clears and everyone celebrates, or it does not and the week was wasted, and the guild fractures over who did not log in. Percentile brackets mean **every** week pays, scaled to effort, and a quiet week is a smaller reward rather than nothing. This is the same principle as death still paying out in a run (`02` §5.2), applied socially.

### 4.2 Solo-player fairness

A player in no guild loses access to the Guild Chest and Guild Boss rewards. That is a real gap and it must be bounded. **Assertion for the economy simulator (§9, E17): a guildless player reaches every `01` §7 milestone within 1.20× the time of a player in an average-activity guild.** If guild membership is worth more than 20%, it has stopped being a social feature and become a requirement.

---

## 5. Guild Level and perks

| Property | Value |
|---|---|
| Source | Guild XP from completed Guild Quests and Guild Boss damage. Never from currency spent. |
| Levels | **1 → 20** 📐 |
| Unlocks by level | Member cap (30 → 50 across the ladder), Officer slots, Guild Boss tier access, and the perks below |

🔒 **All guild perks are non-combat (R1).** The complete list — there are no others, and none may be added without amending R1:

| Perk | Max value at Guild Level 20 |
|---|---|
| **Full Coffers** | +8% Crowns from all sources 📐 |
| **Well Fed** | +8% Beast Feed from all sources 📐 |
| **Second Wind** | +6% Energy regeneration rate 📐 |
| **Quarried** | +6% Enhance Stones from all sources 📐 |

Every one is a material-income modifier. None touches a stat, a drop rarity, a pity counter or anything that enters a combat simulation or a Ghost Duel snapshot (`11` §2). A guild snapshot field does **not** exist on the Ghost, and the architecture tests must assert that guild state is unreachable from `SlayIdleRepeat.Core`'s combat path.

---

## 6. Communication (R3)

No free text. Two surfaces, both fully authored.

### 6.1 The Phrase Board

| Property | Value |
|---|---|
| What | A member posts one of **60 authored phrases**, optionally targeting one Guild Quest or the Guild Boss |
| Categories | Greeting · Rally · Thanks · Apology · Coordination · Congratulation |
| Catalogue | All 60 phrases are authored in §6.1a |
| Rate limit | 5 posts per member per day |
| Retention | Board holds the last 50 posts, 7-day TTL |
| Localisation | Phrases are localisation keys, so a German member's post reads in German to an English member. **This is a genuine advantage of the fixed-phrase design and should be said in the store copy.** |

### 6.1a The 60 phrases 🔒

English source strings; every one is a localisation key (EN + DE).

| Category | Phrases (10 each) |
|---|---|
| **Greeting** | "Welcome." · "Good to see you." · "Hello, all." · "New here — hello." · "Back again." · "Morning." · "Evening, all." · "Glad to be here." · "Room for one more?" · "The door was open." |
| **Rally** | "Boss attempts still open." · "Quest needs a push." · "Almost there — keep going." · "Every run counts today." · "Let's finish the streak." · "Boss falls tonight." · "One more push." · "Don't leave the quest at 90%." · "Dungeon quest needs runners." · "Save an attempt for the boss." |
| **Thanks** | "Thanks, everyone." · "Nice work." · "Nice damage." · "That helped." · "Good week, all." · "Couldn't have done it alone." · "Appreciated." · "You carried that one." · "Well earned." · "Good hustle." |
| **Apology** | "Away this week." · "Back tomorrow." · "Missed my attempts — sorry." · "Busy days, less play." · "Sorry, forgot the quest." · "Life first, dice later." · "Slow week from me." · "Will make it up next week." · "Traveling — sporadic." · "Sorry for the silence." |
| **Coordination** | "Need help on the dungeon quest." · "Focus the enemy quest first." · "Save attempts for Sunday." · "Use attempts early." · "Tier up next week?" · "Stay this tier for now." · "Check the quest board." · "Boss resets Monday." · "Two quests done, one to go." · "Attempts expire tonight." |
| **Congratulation** | "Congratulations!" · "Big clear — well done." · "New personal best!" · "Nice rank." · "That's a proper hit." · "Look at that streak." · "Guild level up!" · "Welcome to the top bracket." · "Mythic! Well done." · "You've earned it." |

### 6.1b The 40 description phrases 🔒

The one-line guild description (§2) is assembled from this fixed set — never free text (R3). Founders pick up to 2, joined by a space.

"Casual and friendly." · "Daily players." · "No pressure, just dice." · "We clear our quests." · "Boss-focused." · "Streak protectors." · "All levels welcome." · "Veterans preferred." · "New players welcome." · "Quiet and consistent." · "Active daily." · "Weekend warriors." · "Early birds." · "Night owls." · "EU hours." · "NA hours." · "Asia hours." · "German-speaking." · "English-speaking." · "Bilingual EN/DE." · "Boss attempts required." · "Quests optional." · "Contribution expected." · "Play how you like." · "Climbing the tiers." · "Top bracket or bust." · "Here for the chest." · "Collectors." · "Ladder grinders." · "Dungeon runners." · "Mythic chasers." · "Building slowly." · "Founded by friends." · "Strangers becoming regulars." · "The kettle is on." · "We roll sixes here." · "Bad luck welcome." · "Retired heroes." · "Just here to slay." · "Idle? Never."

### 6.2 The Guild Log

Server-generated, not player-authored: joins, leaves, kicks, promotions, quest completions, boss milestones, level-ups, streak breaks. Read-only. 30-day retention.

### 6.3 Moderation surface, minimised but not zero

Even without free text there is a moderation obligation, and it must be staffed:

| Vector | Control |
|---|---|
| Guild name and tag | Profanity filter (EN + DE) at creation and on edit, plus a report action on every guild profile |
| Player display name | Already filtered at creation (`07` §1); add a report action on every roster row and duel candidate card |
| Harassment via kick/demote cycling | Rate-limited: a Leader may perform at most 10 role actions per day |
| Reports | Route to a review queue, not to automatic action. Sanctions ladder matches `14` §9: shadow action first, account action only on repeated confirmed abuse. |

⚠️ **This still requires a human review queue and a documented response SLA.** It is smaller than a chat moderation obligation by roughly two orders of magnitude, but it is not nothing, and it must be owned by a named person before launch.

---

## 7. The chat upgrade path

R3 is a v1 decision, not a permanent one. If retention data shows guilds working and players asking for chat, the sequence is:

1. **Phrase Board first** (v1) — measures whether guilds retain at all, at near-zero risk.
2. **Guild-only free text, EN + DE, with a third-party moderation API and a hard age gate**, as a post-launch update — only if there is a person on call.
3. **Never global or cross-guild chat.** The blast radius of a global channel is the entire player base; a 30-person guild channel is thirty people.

Recorded as a standing recommendation in `16_DECISION_LOG.md`, not as a plan.

---

## 8. Explicitly out of scope for v1

- ❌ **Guild vs Guild PvP.** It needs matchmaking, a season structure, tie-breaking, and it creates exactly the "recruit whales or lose" dynamic R1 exists to prevent.
- ❌ **Guild Vault / donations / gifting / trading.** R2.
- ❌ **Global guild leaderboard.** Tier-band boards only (§4). A global board turns guild recruitment into a spreadsheet and drives the kick-the-underperformer behaviour that makes guilds miserable.
- ❌ **Guild-exclusive gear, pets, mounts, perks or currency.** `26` C2 applies.
- ❌ **Guild cosmetics** — emblems, banners, crests. There are no cosmetics in v1 (D14).
- ❌ **Friend lists, direct messages, cross-guild social.** The social graph stays exactly 30 people wide.
- ❌ **Paying real money for anything guild-related.** `12` §8.

---

## 9. Impact on the economy simulator

| # | Requirement |
|---|---|
| **E16** | Add two profiles: *guildless* and *average-activity guild member* (a guild at level 10 completing ~5 of 7 quest days and reaching the median boss bracket). |
| **E17** | New assertion: **a guildless player reaches every `01` §7 milestone within 1.20× the time of an average guild member.** §4.2. |
| **E18** | New assertion: **guild income supplies no more than 20% of any single material's total income** at p50. Above that, guild membership is compulsory in practice. |
| **E19** | Model the interaction of guild perks with dungeon yield (`25`) and event income (`26`). The three systems all pay materials and they compound multiplicatively on Crowns and Beast Feed — this is the most likely place the economy breaks. |

⚠️ **E19 is the highest-risk assertion in the whole simulator.** Docs 25, 26 and 27 each add a material income stream, and each was sized independently. Together they may double a mid-game player's Crown and Beast Feed income, which would collapse the merge bottleneck that `10` §4 identifies as the game's most exciting sink. **Run the simulator with all three enabled before tuning any of them individually.**

---

## 10. Screens

| # | Screen | Purpose |
|---|---|---|
| **S33** | **Guild Home** | Guild name/tag/level, Guild Quest progress ×3 with the member's own contribution, streak counter, Guild Boss card with attempts remaining, Phrase Board, Guild Log |
| **S34** | **Guild Roster** | 30 rows: name, Legend Level, role, last-active, weekly contribution. Leader/Officer actions inline. Report action on every row. |
| **S35** | **Guild Browser** | Search by name/tag, filter by join policy and minimum level, sorted by activity. Create-guild entry point. |
| **S36** | **Guild Boss** | Boss portrait, shared HP bar, damage contribution list, attempts remaining, FIGHT button, reward bracket preview |

**Entry point:** a sixth item in the bottom navigation is not available — the nav is full at five (`13` §2). Guilds enter via a **card on the Home screen** below the daily-quest strip, badged when a Guild Quest is claimable or Guild Boss attempts are unused.

For a player in no guild, that card reads *"Find a guild"* and routes to S35. It never nags, never re-appears after dismissal on the same day, and never blocks anything.

---

## 11. Technical notes

| Concern | Requirement |
|---|---|
| Storage | Postgres: `guilds`, `guild_members`, `guild_quests`, `guild_boss_state`, `guild_log`, `phrase_posts`. Reached only via `IGuildRepository` (D22). |
| Concurrency 🔒 | Guild Boss damage and Guild Quest counters are the only genuinely contended writes in the game — 30 players may submit simultaneously. **The domain never returns a mutated guild.** A player command emits a `GuildContribution { guildId, counterId, delta }` domain event, and the Application layer applies it as a single-row **atomic increment** — never read-modify-write. See `30_DOMAIN_MODEL.md` §5, which records guilds as the one place the pure single-player state machine does not hold. |
| Weekly settlement | `GuildRules.SettleWeek(guildState, memberLedger, context) → grants[]` — a **separate pure function** run once by a hosted service at week end, not a player command. Deterministic and testable in memory like everything else. `30` §5. |
| Authority | Damage is computed server-side from the member's stored snapshot and a server-issued seed, exactly as in a Ghost Duel (`11` §6). The client submits a `LogHash`; the server has already computed the fight. |
| Push | Two notifications, both opt-in and inside the existing 1/day cap: *"Guild Boss attempts expire tonight"* and *"Your guild completed its quests"*. Nothing else, ever. |
| GDPR | Guild membership, contribution history and phrase posts are personal data. Account deletion (`14` §7.3) must remove or anonymise all of it, and a deleted member's contributions must not orphan a guild's totals. |
| Kill switch | `guilds.enabled` remote-config flag, matching every other major feature (`14` §14). |
| Telemetry | `guild_created`, `guild_joined`, `guild_left`, `guild_kicked`, `guild_quest_contributed`, `guild_quest_completed`, `guild_boss_attempt`, `guild_chest_claimed`, `phrase_posted`, `guild_reported`. The retention metric that matters: **D7 and D30 retention for guilded vs guildless players**, which is the number that justifies this entire document.
| Scope | ⚠️ Guilds are the largest single addition in this documentation set: ~6 new tables, 4 screens, a contended-write path, a moderation queue and a permanent operational obligation. **Budget 3–4 engineering weeks plus ongoing ops.** If the schedule slips, this is the correct feature to move to the first post-launch update — it is additive and nothing else depends on it. |
