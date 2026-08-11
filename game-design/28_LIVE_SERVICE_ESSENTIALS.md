# 28 — Live Service Essentials

🔒 **Decision D29: the inbox, account linking, the Energy Reserve and Feats all ship in v1.**

Closes open items **O19**, **O20**, **O21** and **O22** from `16_DECISION_LOG.md` §B1a.

These four are not features anyone puts on a store page. They are the difference between an app and a service, and three of the four are things whose absence is only discovered at the worst possible moment: after an outage with no way to apologise, after a lost phone with no way to recover, after a weekend away with two days of Energy discarded.

They are grouped in one document because they are small individually and because three of them touch the same surface — the Home screen and the profile.

---

# PART A — The Inbox (O19)

## A1. Why it is not optional

`14` §2 makes the server authoritative for everything. `26` makes events a rolling live-ops calendar with mid-flight kill switches and currency reconciliation. `27` adds a moderation queue that must be able to tell a player what happened to their report.

Every one of those creates a message the game currently has no way to deliver. The first server incident will need compensation delivered to a specific cohort, and building an inbox under incident pressure is how you ship a bad one.

## A2. Shape

| Property | Value |
|---|---|
| Direction | **Server → player only.** 🔒 No player-to-player messaging of any kind, ever. This is the same containment reasoning as `27` §1 R3. |
| Composition | Messages are **authored templates with typed parameters**, not free text. 🔒 A free-text message cannot be localised, cannot be reviewed before it reaches 300,000 people, and cannot be tested. |
| Languages | Every template exists in EN and DE (`13` §10). A message with no translation **does not send**. |
| Capacity | 50 messages per player; oldest non-attachment messages are pruned first |
| Retention | 30 days |
| Attachments | Currencies, materials, gear, chests, Energy, Set Tokens, Beast Marks. Granted server-side, idempotent by `messageId`. |
| Expiry 🔒 | **Unclaimed attachments are auto-granted when the message expires, never destroyed.** The message disappears; the reward does not. This game does not have expiring gifts. |
| Marketing | ❌ **None.** No Plus promotion, no event advertising, no "come back" nudges, no ad offers. The inbox is a utility, not a channel. |

### A2.1 Message categories (6)

| Category | Sender | Example | Attachment |
|---|---|---|---|
| `ANNOUNCEMENT` | Ops | Maintenance window, known issue, resolution | Rarely |
| `COMPENSATION` | Ops | *"Servers were unavailable for 3 hours on 14 March. Here is something for the trouble."* | Always |
| `RECONCILIATION` | System | Event closed: unspent event currency converted (`26` C4); season rewards paid (`11` §5.3); guild disbanded | Usually |
| `MODERATION` | System | The outcome of a report the player filed, or a sanction applied to them (`27` §6.3) | Never |
| `ACCOUNT` | System | Linking succeeded, a new device signed in, Plus lapsed (`12` §2.2) | Never |
| `MILESTONE` | System | A Feat tier or Renown threshold reached while offline (Part D) | Sometimes |

🔒 `MODERATION` and `ACCOUNT` messages are **never** auto-deleted and never pruned by the capacity rule. They are the record.

## A3. Schema

```json
{
  "messageId": "msg_7c31...",
  "category": "COMPENSATION",
  "templateId": "loc.mail.compensation.outage",
  "params": { "hours": 3, "dateUtc": "2026-03-14" },
  "attachments": [
    { "type": "SOUL_SHARDS", "amount": 500 },
    { "type": "ENERGY",      "amount": 120 },
    { "type": "CHEST",       "chestClass": "CHEST_STANDARD", "count": 1 }
  ],
  "createdAtUtc": "2026-03-14T18:00:00Z",
  "expiresAtUtc": "2026-04-13T18:00:00Z",
  "readAtUtc": null,
  "claimedAtUtc": null
}
```

### A3.1 Targeting

| Target | Use |
|---|---|
| `ALL` | Announcements |
| `SEGMENT` | A predicate over the profile: region, client version, highest chapter, Plus status, last-active window, guild membership, whether they were in an affected run window |
| `PLAYER` | Moderation outcomes, account events, individual support resolutions |

Segment sends are **rate-limited and require a dry run** that reports the recipient count before dispatch. Sending 500 Soul Shards to the wrong predicate is an economy incident, and the tooling should make it hard.

## A4. Claiming

- **CLAIM ALL** is the primary button — a single `CLAIM_INBOX` command with the `messageIds` filter omitted; tapping one message sends `CLAIM_INBOX` with that `messageId` (`14` §2.3, ruled in `16` A7). Nobody wants to tap 40 messages.
- Claims are idempotent on `messageId`; a duplicate claim replays the stored outcome, exactly like a run command (`14` §3.2).
- Attachments that would exceed a cap (Energy above max) route to the **Energy Reserve** (Part C) rather than being discarded.
- `CHEST` attachments (and any egg or crate attachment) claim as **unopened containers onto the shelf** (`24` §4.0), never as pre-opened contents — pity and Focus are read when the player opens them. Inventory capacity is therefore checked at open, not at claim; the capacity-held rule below applies only to attachments that grant items directly.
- A claim that would exceed inventory capacity (`08` §5) is **held**, not lost: the message stays claimable and states *"Not enough inventory space."*

## A5. Surfaces

| Where | Behaviour |
|---|---|
| **S37 Inbox** | New screen. List grouped by category, unread dot, attachment icons, CLAIM ALL. |
| Home (S03) | Envelope icon in the header with an unread count badge. **No popup, no auto-open.** |
| Push | Only for `COMPENSATION` and only when the attachment is non-trivial, inside the existing 1/day cap (`14` §12). Never for announcements. |

## A6. Operational requirements

| Concern | Requirement |
|---|---|
| Storage | Postgres `player_messages`, indexed by `(playerId, expiresAtUtc)`. Reached only via `IMessageRepository` (D22). |
| Expiry job | A hosted service that, nightly, auto-grants attachments on expiring messages and then deletes them. Auto-grants emit a `MILESTONE` message only if the value is material. |
| Audit | Every segment send is logged with the predicate, the dry-run count, the actual count, and the operator. This is economy-affecting action and must be attributable. |
| Telemetry | `mail_received`, `mail_read`, `mail_claimed`, `mail_expired_autogranted`, `mail_segment_sent` |
| Kill switch | `mail.enabled` — the inbox degrades to hidden, and the expiry job still auto-grants |

---

# PART B — Account Linking (O20)

## B1. The gap

`14` §7.3 issues an anonymous device-bound account on first launch and offers an upgrade to Google or Apple sign-in. Nothing in the design ever **asks**. And because D11 makes the server authoritative and `14` §7.2 makes the local file a disposable cache, there is no save to restore — a wiped or lost device is total, silent, unrecoverable account loss.

That is a 1-star review and a support burden with no resolution path, and it will happen to a player who has been playing for ninety days.

## B2. Prompt schedule 🔒

| When | Form | Dismissible |
|---|---|---|
| Legend Level **10** (with the PvP unlock) | One full-card prompt, shown once | Yes — a single tap, no confirmation |
| Legend Level **30** | One full-card prompt, shown once | Yes |
| Every **14 days** thereafter while unlinked | A one-line non-modal banner on Home | Yes, dismissed for 14 days |
| Settings → Account | Persistent status row, always available | n/a |
| Profile (S27) | Link status shown as a plain line | n/a |

🔒 **Linking never blocks play, never gates a feature, and never interrupts a run.** Two prompts and a fortnightly banner is the entire pressure budget. This is the same restraint the Plus offer operates under (`12` §2.4) and for the same reason.

## B3. The linking reward

**One-time, on first successful link: 500 Soul Shards + 1 `CHEST_STANDARD`.** 📐

- Available to every player, free or Plus. Not purchasable, not an ad reward.
- **Not farmable:** the server enforces uniqueness on the identity provider's subject ID, so one Google or Apple identity can link exactly one account, ever.
- The reward exists because linking is a chore with a benefit the player cannot see until the day they need it. Paying for it converts a 20% link rate into an 80% one, and every unlinked account is a future support ticket with no answer.

## B4. Recovery, and the conflict case

The dangerous path is not linking — it is signing in on a new device that already has progress.

```
Player installs on a new phone
  → anonymous account B is created automatically, Legend Level 1
  → player signs in with Google
  → server finds identity already bound to account A (Legend Level 74)
  → CONFLICT
```

🔒 **The client must never resolve this silently, in either direction.** It shows a comparison card and requires an explicit choice:

| | Account A (signed in) | Account B (this device) |
|---|---|---|
| Legend Level | 74 | 3 |
| Chapters cleared | 6 | 1 |
| Gear items | 214 | 9 |
| Last played | 2 days ago | just now |

- The choice is **irreversible** and says so, with a typed confirmation on the discarding side.
- The discarded account is **soft-deleted with a 30-day recovery window**, not hard-deleted. Support can restore it. After 30 days it is purged.
- If account B has *no* meaningful progress (Legend Level < 5, no purchases), skip the dialog and adopt account A silently. Nobody wants a modal about discarding nothing.

## B5. Rules

| Rule | Specification |
|---|---|
| Multiple providers | One account may link **both** Google and Apple. This is the cross-platform path and it costs nothing to allow. |
| Unlinking | ❌ **Not offered.** It exists only to enable account selling and to generate unanswerable support tickets. Players who want to leave use account deletion. |
| Deletion | Unchanged from `14` §7.3: GDPR endpoint, hard delete within 30 days. Deletion also releases the provider subject ID for reuse. |
| Plus entitlement | Bound to the **account**, not the device or the store identity (`12` §2.1). It follows the account through any link or recovery. |
| Abandoned anonymous accounts | An unlinked account with no session for **180 days** is purged. There is no way to warn them — no contact channel exists, which is precisely the problem this part solves. Documented, not fixable. |
| Notification | A successful link, and any subsequent sign-in from a new device, generates an `ACCOUNT` inbox message (Part A). |

---

# PART C — The Energy Reserve (O21)

## C1. The gap

`10` §3 caps Energy at 200 with **no overflow banking** — *"Energy stops at max; no overflow banking"*. That same section states the design intent plainly: *"a returning lapsed player should always find a full tank."*

They contradict each other. A player away for two days regenerates 720 Energy and keeps 200. The other 520 — twenty-six runs — is discarded. That is a returning-player tax in a game whose Energy design is explicitly meant to be a soft pacing tool rather than a wall.

## C2. Specification

| Property | Value |
|---|---|
| What | A second Energy bank that receives **overflow only** |
| Capacity | **1× Max Energy** (200 at cap) 📐 |
| Fills | Only while the main bar is at maximum. Regeneration, daily refills, quest grants, ad grants, level-up refills and inbox attachments all overflow into it. |
| Spends | Automatically. A run or dungeon draws from the main bar first, then from the Reserve for any shortfall. There is no button and no decision. |
| Regenerates | ❌ Never on its own. The Reserve only ever receives what the main bar could not hold. |
| Purchasable | ❌ Never, by money or ad. `AD_ENERGY` (`12` §4.2) grants +40 to the main bar and overflows into the Reserve exactly like any other source — capped, and identical for Plus. |
| Talents | `FT_VIGOR` (`09` §6) raises the regeneration rate and therefore fills the Reserve faster. No new talent node. |
| UI | A second, thinner segment drawn behind the main Energy bar in a desaturated tint, with the numbers read as `138/200 (+200)`. Not a separate widget. |

**Maximum banked value: 400 Energy = 20 runs ≈ 2.5–3 hours of play.** That is a generous weekend, and it is deliberately not more — a player who returns after three weeks should not find a month of content stacked up, because the whole point of a run-based design (D2) is that the session is an active choice.

## C3. Why this does not break D2

D2 locks the game to run-based play with **no idle income**, and `10` §3 already carves out Energy regeneration as the single exception. The Reserve does not add a second exception — it changes what happens to Energy that the existing exception already generated and then threw away. Nothing new accrues; less is discarded.

📐 The 1× cap is the dial. If telemetry shows returning players still hitting the ceiling, raise it. If it shows daily players banking a permanent surplus they never spend, the regeneration rate is too high and that is the number to change, not this one.

---

# PART D — Feats and Renown (O22)

## D1. The gap

The Codex (`06` §6, `13` S24) records what the player has **collected** — perks, enemies, gear, pets. Nothing records what the player has **done**. There is no first-Mythic-clear, no thousand-elites, no ten-duel-streak, no `+15` item.

That matters more now than it did, because **D28 accepts the content cliff**. A player who has cleared Chapter 8 Mythic has no list left. Feats are a list.

## D2. Structure

| Property | Value |
|---|---|
| Name | **Feats**. The meta-level they feed is **Renown**. |
| Count in v1 | **140 feats** 📐 across 9 categories |
| Tiering | Most feats have **3 tiers** (e.g. defeat 1,000 / 10,000 / 100,000 enemies). Each tier claims separately and pays separately. |
| Evaluation | **Server-side**, from lifetime counters on the `Player` aggregate, incremented inside `GameRules.Apply` on the same state changes that emit the events in `14` §10.1. ⚠️ **Counters are aggregate state, not a projection over the event stream** — see `30` §12.7. Treating the event log as their source of truth would be event sourcing through the back door, for one feature. Almost no new telemetry is required either way. |
| Retroactivity 🔒 | Feats evaluate against **lifetime profile counters**, so all existing progress counts the moment the feature ships. A player who has already killed 40,000 enemies claims tiers 1 and 2 immediately. |
| Missable | ❌ **None.** No time-limited feats, no event-exclusive feats, no seasonal feats. Consistent with `11` §5.3 and `26` C2: nothing in this game is permanently unobtainable. |
| Difficulty | No feat may require a specific random outcome (e.g. "roll six sixes"). Feats measure persistence and mastery, never luck — which would be an odd thing to add to a game in the same release as `24_LUCK_PROTECTION.md`. |

### D2.1 Categories

| Category | Feats | Examples |
|---|---|---|
| **Slaughter** | 20 | Enemies, elites, bosses defeated; crit totals; overkill |
| **The Road** | 18 | Tiles travelled, runs completed, chapters cleared per tier, deathless runs, forks taken |
| **The Die** | 14 | `Star` faces rolled, rerolls used, Chain lengths, faces permanently upgraded |
| **The Draft** | 16 | Perks drafted, Tier III perks, distinct perks seen, Legendary drafts, `PK_SINGULARITY` survived |
| **The Forge** | 18 | Merges, `+15` items, SS items owned, Reforges, a full 6-piece set |
| **The Menagerie** | 14 | Pets owned, ★5 pets, mounts owned, pet levels |
| **The Arena** | 14 | Duels won, streaks, peak tier, seasons completed |
| **The Dungeons** | 10 | Dungeon clears, tier-8 clears, Guardian kills (`25`) |
| **Fellowship** | 16 | Guild quest contributions, Guild Boss damage, streak weeks (`27`) |

📐 All 140 are authored as data in `game-data/feats.json`. Adding more post-launch is a content drop, not a patch.

### D2.2 The 140-feat catalogue 🔒

Format: `Feat — measure — tier thresholds (1 / 2 / 3)`. Feats marked **(single)** have one tier and pay the tier-3 Renown value (60). All thresholds 📐 TUNABLE. Names are localisation keys; the display names below are the EN source.

**Slaughter (20)**

| # | Feat | Measure | Tiers |
|---|---|---|---|
| 1 | First Blood, Then More | Enemies defeated | 1,000 / 10,000 / 100,000 |
| 2 | Elite Opinions | Elites defeated | 100 / 1,000 / 10,000 |
| 3 | Boss Material | Bosses defeated | 25 / 250 / 2,500 |
| 4 | Critical Acclaim | Critical hits landed | 5,000 / 50,000 / 500,000 |
| 5 | Overkill Is Underrated | Kills with a hit ≥ 200% of the target's Max HP | 50 / 500 / 5,000 |
| 6 | Crowd Control | `SWARM` units defeated | 500 / 5,000 / 50,000 |
| 7 | A Slow Poison | Enemies finished by a DoT tick | 200 / 2,000 / 20,000 |
| 8 | Prickly | Enemies killed by Thorns | 50 / 500 / 5,000 |
| 9 | Good Help | Enemies killed by pet abilities | 100 / 1,000 / 10,000 |
| 10 | Short Conversations | Battles won in under 10 s | 100 / 1,000 / 10,000 |
| 11 | Untouched | Battles won without taking damage | 50 / 500 / 5,000 |
| 12 | Modifier Collector | Elites defeated per modifier, all 8 | 1 each / 25 each / 250 each |
| 13 | Field Guide | Distinct enemy visual variants defeated | 16 / 40 / 64 |
| 14 | Big Numbers | Total damage dealt | 10M / 1B / 100B |
| 15 | The One Hit | Largest single hit | 10k / 100k / 1M |
| 16 | Applied Chemistry | Status effects applied | 5,000 / 50,000 / 500,000 |
| 17 | From the Brink | Battles won after dropping below 10% HP | 25 / 250 / 2,500 |
| 18 | Beat the Clock | Bosses defeated after their enrage began | 5 / 50 / 500 |
| 19 | No Safety Net | Bosses defeated without using a revive | 10 / 100 / 1,000 |
| 20 | The Full Set | All 8 chapter bosses defeated on a tier | Normal / Heroic / Mythic |

**The Road (18)**

| # | Feat | Measure | Tiers |
|---|---|---|---|
| 21 | Mileage | Tiles travelled | 1,000 / 10,000 / 100,000 |
| 22 | Repeat Offender | Runs completed | 50 / 500 / 5,000 |
| 23 | Finisher | Runs won | 25 / 250 / 2,500 |
| 24 | Deathless | Victories without dying (no revive) | 10 / 100 / 1,000 |
| 25 | Forks Taken | Forks chosen | 100 / 1,000 / 10,000 |
| 26 | The Hard Way | Perilous branches taken | 50 / 500 / 5,000 |
| 27 | Chapters: Normal | Chapters cleared on Normal | 2 / 5 / 8 |
| 28 | Chapters: Heroic | Chapters cleared on Heroic | 2 / 5 / 8 |
| 29 | Chapters: Mythic | Chapters cleared on Mythic | 2 / 5 / 8 |
| 30 | Finders Keepers | Treasure tiles opened | 100 / 1,000 / 10,000 |
| 31 | Decisions, Decisions | Event cards resolved | 50 / 500 / 5,000 |
| 32 | Seen It All | Distinct event cards seen | 10 / 20 / 30 |
| 33 | Devout | Shrine buffs taken | 100 / 1,000 / 10,000 |
| 34 | Cursed and Thriving | Runs won while cursed | 10 / 100 / 1,000 |
| 35 | Shortcut Enthusiast | Portals taken | 25 / 250 / 2,500 |
| 36 | Warm Hands | Campfire choices made | 50 / 500 / 5,000 |
| 37 | Retail Therapy | In-run shop purchases | 100 / 1,000 / 10,000 |
| 38 | The Ascetic | Runs won with zero shop purchases | 10 / 100 / 1,000 |

**The Die (14)**

| # | Feat | Measure | Tiers |
|---|---|---|---|
| 39 | Roller | Dice rolled | 1,000 / 10,000 / 100,000 |
| 40 | Star Struck | `Star` faces rolled | 100 / 1,000 / 10,000 |
| 41 | Second Opinions | Rerolls used | 100 / 1,000 / 10,000 |
| 42 | Chain Reaction | `Chain` faces triggered | 50 / 500 / 5,000 |
| 43 | Full Chain | Maximum-length chains completed | 10 / 100 / 1,000 |
| 44 | Surge Protector | `Surge` heals triggered | 50 / 500 / 5,000 |
| 45 | Double Down | `Fortune` doubles landed | 50 / 500 / 5,000 |
| 46 | Gentle Persuasion | Nudges used | 25 / 250 / 2,500 |
| 47 | Forge Ahead | Dice Forge upgrades taken | 10 / 100 / 1,000 |
| 48 | A Die of One's Own | Die faces permanently upgraded | 1 / 3 / 6 |
| 49 | Boxcars | Sixes rolled | 200 / 2,000 / 20,000 |
| 50 | Snake Eyes | Ones rolled | 200 / 2,000 / 20,000 |
| 51 | House Rules | Dice Duel minigames won | 10 / 100 / 1,000 |
| 52 | The Whole Repertoire | Roll every face kind once (Pip, Star, Surge, Fortune, Chain, Void) | **(single)** |

**The Draft (16)**

| # | Feat | Measure | Tiers |
|---|---|---|---|
| 53 | Drafted | Perks drafted | 100 / 1,000 / 10,000 |
| 54 | Maxed Out | Perks taken to Tier III | 10 / 100 / 1,000 |
| 55 | Variety Hour | Distinct perks drafted | 30 / 60 / 90 |
| 56 | Golden Picks | Legendary perks drafted | 10 / 100 / 1,000 |
| 57 | Purple Prose | Epic perks drafted | 50 / 500 / 5,000 |
| 58 | Above It All | Drafts skipped | 25 / 250 / 2,500 |
| 59 | Ask Again | Draft rerolls used | 50 / 500 / 5,000 |
| 60 | Deal With It | Cursed perks accepted | 5 / 50 / 500 |
| 61 | Singularity Survivor | Runs won with `PK_SINGULARITY` active | 1 / 10 / 100 |
| 62 | Renaissance Build | Runs won owning perks from all 6 categories | 5 / 50 / 500 |
| 63 | Going Tall | Runs won with 3 Tier-III perks | 5 / 50 / 500 |
| 64 | Going Wide | Runs won with 15+ distinct perks | 5 / 50 / 500 |
| 65 | One Health Point | Win a run with `CP_GLASS_HEART` | **(single)** |
| 66 | Perk Scholar | Codex perk entries discovered | 49 / 74 / 98 |
| 67 | Sharpening | Owned-perk upgrades taken in drafts | 50 / 500 / 5,000 |
| 68 | Impulse Buyer | Perks bought from shop tiles | 25 / 250 / 2,500 |

**The Forge (18)**

| # | Feat | Measure | Tiers |
|---|---|---|---|
| 69 | Three Into One | Merges performed | 25 / 250 / 2,500 |
| 70 | Red-Violet | SS items created or found | 1 / 5 / 24 |
| 71 | Plus Fifteen | Items enhanced to +15 | 1 / 6 / 24 |
| 72 | Hammer Time | Enhancement attempts | 100 / 1,000 / 10,000 |
| 73 | Stone Mason | Enhance Stones spent | 1,000 / 10,000 / 100,000 |
| 74 | Recycler | Items salvaged | 250 / 2,500 / 25,000 |
| 75 | Dust to Dust | Merge Dust earned | 5,000 / 50,000 / 500,000 |
| 76 | Better of Two | Reforges performed | 10 / 100 / 1,000 |
| 77 | Fine Tuning | Retunes performed | 10 / 100 / 1,000 |
| 78 | Near Perfect | Items owned at ≥95% quality | 1 / 6 / 24 |
| 79 | Matching Pair | Wear a 2-piece SS set bonus | **(single)** |
| 80 | The Full Regalia | Wear a full 6-piece SS set | **(single)** |
| 81 | Token Effort | Set Tokens earned | 4 / 12 / 48 |
| 82 | Gold Standard | S-or-better items acquired | 10 / 100 / 1,000 |
| 83 | Dressed for the Occasion | Every slot at a rarity floor | all S+ / all SS |
| 84 | Under Lock | Affixes locked during Retunes | 5 / 50 / 500 |
| 85 | Wish Granted | Wishlist affix hits | 5 / 50 / 500 |
| 86 | Against the Odds | Enhancements succeeded at +11 or higher | 10 / 100 / 1,000 |

**The Menagerie (14)**

| # | Feat | Measure | Tiers |
|---|---|---|---|
| 87 | Menagerie Keeper | Distinct pets owned | 8 / 16 / 24 |
| 88 | Five Stars | Pets at ★5 | 1 / 5 / 12 |
| 89 | Growth Spurt | Pet level-ups | 100 / 1,000 / 5,000 |
| 90 | Fully Grown | Pets at level 60 | 1 / 3 / 12 |
| 91 | Egg Collection | Pet Eggs opened | 25 / 250 / 2,500 |
| 92 | The Rare Ones | SS pets owned | 1 / 2 / 4 |
| 93 | Marked Progress | Beast Marks earned | 60 / 200 / 600 |
| 94 | Chosen, Not Rolled | Pets bought with Beast Marks | 1 / 2 / 5 |
| 95 | Well Fed | Beast Feed spent | 5,000 / 50,000 / 500,000 |
| 96 | Stable Hand | Distinct mounts owned | 4 / 8 / 12 |
| 97 | Thoroughbreds | SS mounts owned | 1 / 2 / 3 |
| 98 | Broken In | Mounts at level 30 | 1 / 4 / 12 |
| 99 | Crate Expectations | Mount Crates opened | 10 / 100 / 1,000 |
| 100 | The Dream Team | Runs won with 3 SS pets equipped | 1 / 10 / 100 |

**The Arena (14)**

| # | Feat | Measure | Tiers |
|---|---|---|---|
| 101 | Duellist | Duels won | 10 / 100 / 1,000 |
| 102 | Regular | Duels fought | 25 / 250 / 2,500 |
| 103 | On a Roll | Best win streak | 3 / 7 / 15 |
| 104 | Home Defence | Successful defences | 10 / 100 / 1,000 |
| 105 | Climbing | Peak tier reached | Gold / Diamond / Legend |
| 106 | Season Ticket | Seasons completed (≥10 duels) | 1 / 5 / 15 |
| 107 | Honorable | Honor earned | 5,000 / 50,000 / 500,000 |
| 108 | Spoils of War | Honor Shop purchases | 5 / 50 / 250 |
| 109 | Punching Up | Wins vs higher-rated opponents | 10 / 100 / 1,000 |
| 110 | Swift Justice | Duels won in under 20 s | 10 / 100 / 1,000 |
| 111 | War of Attrition | Duels won on the 60 s timeout | 5 / 50 / 500 |
| 112 | Full House | Duels won with all 5 perk slots filled | 10 / 100 / 1,000 |
| 113 | Two Big Ideas | Duels won with exactly 2 perks equipped | 10 / 100 / 1,000 |
| 114 | Rated | Peak rating reached | 1,400 / 1,900 / 2,400 |

**The Dungeons (10)**

| # | Feat | Measure | Tiers |
|---|---|---|---|
| 115 | Full Sweep | Dungeon full clears (Guardian killed) | 25 / 250 / 2,500 |
| 116 | Errand Runner | Dungeon entries | 50 / 500 / 5,000 |
| 117 | Top Shelf | Tier-8 clears | 1 / 25 / 250 |
| 118 | Quarry | Enhance Stones from dungeons | 1,000 / 10,000 / 100,000 |
| 119 | Provisioner | Beast Feed from dungeons | 2,000 / 20,000 / 200,000 |
| 120 | Minted | Crowns from dungeons | 25k / 250k / 2.5M |
| 121 | Clean Sweep Days | Days with all 9 entries used | 5 / 25 / 100 |
| 122 | Three of Three | Clear each of the three dungeons | **(single)** |
| 123 | In and Out | Clears in under 2 minutes | 10 / 100 / 1,000 |
| 124 | Guardian Scholar | Guardians defeated with all 8 elite modifiers | **(single)** |

**Fellowship (16)**

| # | Feat | Measure | Tiers |
|---|---|---|---|
| 125 | Signed Up | Join a guild | **(single)** |
| 126 | Pulling Weight | Guild quests contributed to | 25 / 250 / 2,500 |
| 127 | Quest Days | Days your guild completed all 3 quests | 10 / 100 / 500 |
| 128 | Streak Keeper | 7-day guild streaks achieved | 1 / 10 / 50 |
| 129 | Chest Day | Guild Chests claimed | 10 / 100 / 500 |
| 130 | Showing Up | Guild Boss attempts made | 10 / 100 / 1,000 |
| 131 | Guild Artillery | Total Guild Boss damage | 10M / 1B / 100B |
| 132 | Top Bracket | Weeks in your guild's top damage bracket | 1 / 10 / 50 |
| 133 | The Long Haul | Guild Boss weeks participated | 4 / 25 / 100 |
| 134 | A Few Words | Phrases posted | 10 / 100 / 1,000 |
| 135 | Loyalty | Days in the same guild | 30 / 180 / 365 |
| 136 | Rising Together | Guild level reached while a member | 5 / 10 / 20 |
| 137 | Founder | Found a guild | **(single)** |
| 138 | The Responsible One | Hold Officer or Leader for 30 days | **(single)** |
| 139 | Peak Performance | Participate in a tier-8 Guild Boss week | **(single)** |
| 140 | Jack of All Quests | Contribute to all 15 quest types | **(single)** |

**Renown accounting:** 128 three-tier feats × 95 + 11 single-tier × 60 + one two-tier (feat 83, [25 + 60]) ≈ **12,905 Renown**, against the ~12,000 target in D3 — close enough that the milestone ladder (D3.1) is unchanged. Feats 20, 27–29, 105 and 114 use qualitative tiers and pay the same [10, 25, 60] per tier.

## D3. Rewards

Each feat tier pays **Renown Points** plus a material reward.

```
RenownPoints(tier) = [10, 25, 60]
Total v1 Renown available ≈ 12,000 📐
```

| Reward type | Where used |
|---|---|
| Crowns, Enhance Stones, Beast Feed | Every tier |
| Soul Shards | Tier 2 and 3 |
| Set Tokens, Beast Marks (`24` §5.1, §4.4) | Tier 3 of the Forge and Menagerie categories only |
| **Talent Points** | Renown milestones only — see D3.1 |

### D3.1 Renown milestones and the Talent Point budget ⚠️

Renown milestones at every **1,000 points** pay **+2 Talent Points**, for **+24 total**, plus a final +6 at full completion.

**This changes the number in `09` §2.** The v1 Talent Point maximum rises from **~294 to ~324** against a tree costing 633 to max. The tree is still not completable in v1, which is the property `09` §2 exists to preserve, and the balance guardrail in `09` §8 must be re-derived for 324 points rather than 294 — expect roughly ×6.9 PlayerPower rather than ×6.5.

📐 The +30 is the dial. It is deliberately small: Feats are meant to give the post-content player a **list**, not a power spike that invalidates the enemy ramp.

## D4. Surfaces

| Where | Behaviour |
|---|---|
| **S38 Feats** | New screen. Category tabs, per-feat progress bars with real numbers (`4,187 / 10,000`), tier pips, CLAIM and CLAIM ALL. A Renown header with the next milestone. |
| Codex (S24) | Gains a sibling tab. Feats and Codex are the two halves of "what have I done here" and belong next to each other. |
| Profile (S27) | Renown total shown beside Legend Level. This is the post-cap number a player can still watch go up. |
| Home (S03) | A dot on the Codex/Feats nav entry when something is claimable. Nothing more. |
| Inbox | A `MILESTONE` message when a Renown milestone is crossed while the player is offline (Part A) |

## D5. Why Renown is a number and not a rank

There are no cosmetics (D14) and PvP tier is already a text label (`11` §5.2). Renown is deliberately **just an integer on the profile** — no title, no badge, no frame. It is the honest version of what a trophy case would be, and it costs zero art.

⚠️ This is the same exposure as risk **R3**: a long-horizon achievement system with no visual payoff is untested. If Renown telemetry shows players stop claiming, that is the signal that R3 was real and the cheapest fix remains die skins.

---

# PART E — Cross-cutting

## E1. New screens

| # | Screen | Part |
|---|---|---|
| **S37** | **Inbox** | A |
| **S38** | **Feats** | D |

**39 screens.** The bottom navigation stays at five (`13` §2): the Inbox is a header icon on Home, Feats are a tab on the Codex.

## E2. Impact on the economy simulator

| # | Requirement |
|---|---|
| **E20** | Model the **Energy Reserve** for the lapsed-and-returning profile specifically. The relevant question is not average income but whether a player returning after 48 hours can spend what they banked in one sitting — if they cannot, the cap is too high and the Reserve is decoration. |
| **E21** | Model **Feat rewards as a one-off retroactive grant** at the moment of first launch after release, then as a slow trickle. The retroactive lump for an existing account is the largest single currency injection in the game and must not break the Crown curve. |
| **E22** | Re-derive the `09` §8 talent power guardrail for **324 points**, not 294. |
| **E23** | **Assertion:** compensation and inbox grants are excluded from all fairness assertions. Ops grants are outside the economy by definition and must not be allowed to mask a broken curve. |

Assertion count: **28 → 32**. Profiles: **11 → 12** (adding *lapsed-and-returning*).

## E3. Build order

These slot in at step 15 of `16` Part D, with one exception: 🔒 **the inbox should be built earlier, alongside the server skeleton (step 4).** It is small, it has no dependencies, and the first time it is genuinely needed will be an incident — at which point it is far too late to start.

## E4. What none of these do

- ❌ No inbox message ever advertises Plus, an event, or an ad.
- ❌ No prompt to link an account ever blocks, gates or interrupts.
- ❌ The Energy Reserve is never purchasable, never an ad reward, and never a Plus benefit.
- ❌ No Feat is missable, time-limited, luck-dependent, or grants combat power beyond the capped Talent Points in D3.1.
- ❌ None of the four is a Plus benefit in any form. Per `12` §2.5, Plus grants time and convenience — and every one of these is a baseline right.
