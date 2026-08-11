# 19 — Content Tables: Events, Quests, Modifiers, FTUE, Curses, Wheel

Resolves open items P1 #10 (30 event cards), P1 #11 (daily quest pool), P1 #12 (weekly modifiers) and P3 #35 (FTUE copy) — plus three gaps found in consistency audits: the **curse catalogue** (Part E), the **Lucky Wheel** (Part F) and the **28-day login calendar** (Part G), all referenced across several documents but never specified.

All strings shown here are **English source strings**. Every one is a localisation key in `data/loc/en.json`, with a `de.json` counterpart (EN + DE only at launch — `16` decision D20).

🔒 Narrative scope: **flavour text only.** No plot, no recurring characters, no cutscenes. The voice throughout is dry, terse and slightly wry — never whimsical, never epic.

---

# PART A — Event Cards (30)

Schema is defined in `03_BOARD_AND_TILES.md` §5. `w` = outcome weight. Costs and rewards scale with `chapterScalar` unless marked flat.

## A1. Chapters 1–3 (available early)

| # | ID | Title | Body | Options |
|---|---|---|---|---|
| 1 | `EVT_WELL` | The Wishing Well | *Coins glitter under black water.* | **Toss 100 Gold** → 60% random perk / 40% +40 Crowns · **Reach in** (−10% HP) → 70% +3 Enhance Stones / 30% curse `Slippery` · **Walk away** |
| 2 | `EVT_SIGNPOST` | A Rotted Signpost | *Three arms. Two have fallen off.* | **Follow the standing arm** → move forward 3 nodes · **Search the fallen ones** → +150 Gold, +1 curse chance 25% · **Ignore it** |
| 3 | `EVT_TRAVELLER` | The Tired Traveller | *He offers his pack. He does not offer his name.* | **Trade 200 Gold** → a random B-or-better gear item · **Rob him** → +400 Gold, curse `Marked` (enemies +10% ATK this stage) · **Share your rations** (−5% HP) → +2 Reroll Charges |
| 4 | `EVT_SHRINE_CRACKED` | A Cracked Shrine | *Something used to live in here.* | **Pray** → 50% +15% Max HP / 50% −10% DEF, both for the run · **Repair it** (150 Gold) → +12% Max HP guaranteed · **Leave** |
| 5 | `EVT_BEEHIVE` | Sunlit Hive | *The buzzing is louder than it should be.* | **Take the honey** (−8% HP) → heal 30% HP after the next battle, +2 Beast Feed ×20 · **Smoke it out** (100 Gold) → +40% of the above, no HP cost · **Leave** |
| 6 | `EVT_LOST_PUP` | A Lost Whelp | *It follows you three paces, then stops.* | **Feed it** (−80 Gold) → 35% Pet Egg / 65% +60 Beast Feed · **Leave it** → nothing · **Take it with you** → +5% pet aura power this run |
| 7 | `EVT_MERCHANT_CART` | Overturned Cart | *The wheels are still spinning.* | **Loot it** → +250 Gold, 20% chance of an ambush battle · **Right the cart** (−5% HP) → the next Shop tile has 6 slots · **Move on** |
| 8 | `EVT_OLD_SOLDIER` | The Old Soldier | *"Tried this road once. Didn't like it."* | **Ask about the road** → reveal all tiles in this stage · **Ask about his armour** (250 Gold) → +15% DEF for the run · **Ask nothing** → +1 Reroll Charge |
| 9 | `EVT_MUSHROOM_RING` | A Ring of Mushrooms | *Fairy circle. Or just fungus.* | **Step inside** → 40% teleport forward 5 nodes / 40% +200 Crowns / 20% curse `Dizzy` (−1 to Pip rolls, 3 rolls) · **Harvest** → +25 Beast Feed · **Step around** |
| 10 | `EVT_STONE_FACE` | The Weeping Stone | *Water runs from its eyes. It has been doing this a while.* | **Drink** → heal 25% Max HP · **Chisel it** (−10% HP) → +1 Enhance Stone ×5 · **Sit with it** → +8% Healing Received for the run |

## A2. Chapters 3–6 (mid-game)

| # | ID | Title | Body | Options |
|---|---|---|---|---|
| 11 | `EVT_GAMBLER` | The Roadside Gambler | *"One roll. Your die, my stakes."* | **Roll high (4+)** stake 300 Gold → win ×3 Gold, lose it all · **Roll for a perk** stake 500 Gold → win a random Epic perk, lose the gold · **Decline** |
| 12 | `EVT_FORGE_COLD` | A Cold Forge | *The coals are out but the tools are good.* | **Reforge a weapon** (400 Gold) → +20% ATK for the run · **Strip it for parts** → +8 Enhance Stones · **Light it** (−12% HP) → both, at half value |
| 13 | `EVT_CAGED` | Something in a Cage | *You cannot tell what it was.* | **Open it** → 45% it fights you (elite battle, double drops) / 55% it follows you (+10% all stats, run) · **Leave it** → +100 Crowns from the cage's lock · **Feed it** (−60 Beast Feed) → +10% all stats, no risk |
| 14 | `EVT_TWO_DOORS` | Two Doors, One Key | *The key is warm.* | **Left door** → a Treasure tile's contents, doubled · **Right door** → a free perk draft with upgraded rarity · **Melt the key** → +6 Enhance Stones |
| 15 | `EVT_BATTLEFIELD` | An Old Battlefield | *Nobody buried them.* | **Loot the dead** → 2 random gear items, C–B rarity · **Bury them** (costs 20 s of run time, no mechanical cost) → +15% Max HP for the run · **Salt the ground** (−200 Gold) → next 3 enemies have −20% ATK |
| 16 | `EVT_MIRROR` | A Standing Mirror | *Your reflection is a half-second late.* | **Touch it** → copy your highest stat bonus onto your lowest stat · **Break it** → +500 Gold, curse `Fractured` (−8% DEF, run) · **Turn it around** → nothing |
| 17 | `EVT_TAX` | The Toll Collector | *He has no authority. He does have a large friend.* | **Pay** (15% of current Gold) → pass, +1 Reroll Charge · **Refuse** → elite battle, keep everything · **Haggle** (roll 4+) → pay half, else pay double |
| 18 | `EVT_SPRING` | A Hot Spring | *Steam, and no one else for miles.* | **Bathe** → heal 50% Max HP, +5% Max HP for the run · **Fill your flask** → gain a Health Draught consumable ×2 · **Both** (−250 Gold) |
| 19 | `EVT_ARCHIVE` | A Buried Archive | *Wet paper. Some of it legible.* | **Read the combat notes** → +1 tier on one owned perk · **Read the maps** → reveal all tiles for the rest of this stage · **Sell the lot** → +400 Crowns |
| 20 | `EVT_STORM` | Gathering Storm | *You have maybe two minutes.* | **Run for it** → move forward 4 nodes, skip their content · **Shelter** (lose 1 Reroll Charge) → heal 20% HP · **Walk through it** (−15% HP) → +25% ATK for the rest of the stage |

## A3. Chapters 6–8 (late)

| # | ID | Title | Body | Options |
|---|---|---|---|---|
| 21 | `EVT_ORACLE` | The Blind Oracle | *"You will roll a four." You have not rolled yet.* | **Ask about the boss** → the boss starts at 90% HP · **Ask about yourself** → +1 tier on two owned perks · **Ask about the die** → one die face upgraded for the run |
| 22 | `EVT_BARGAIN` | An Even Bargain | *Nothing is written down.* | **Give 25% Max HP** → +45% ATK for the run · **Give 30% ATK** → +60% Max HP for the run · **Give nothing** → +150 Crowns |
| 23 | `EVT_SPORE_FIELD` | A Field of Caps | *They lean toward you.* | **Walk through** → 3 `SPORE` stacks, +600 Crowns · **Burn a path** (−300 Gold) → safe passage, +2 Beast Feed ×50 · **Go around** → lose 2 nodes of progress |
| 24 | `EVT_AUTOMATON` | A Stopped Automaton | *One gear short.* | **Give it a gear** (−6 Enhance Stones) → it fights alongside you for 3 battles · **Strip it** → +10 Enhance Stones, +300 Crowns · **Wind it up** → 50% it helps / 50% it attacks (elite battle) |
| 25 | `EVT_STARFALL` | Starfall | *Something lands two nodes ahead.* | **Investigate** → move to that node, guaranteed S-rarity gear · **Avoid it** → +1 Reroll Charge · **Watch from here** → +8% all stats for the run |
| 26 | `EVT_DEBT` | A Collector's Ledger | *Your name is in it. You have never been here.* | **Pay in Gold** (all of it) → +30% all stats for the run · **Pay in blood** (−35% current HP) → 2 S-rarity gear items · **Tear out the page** → curse `Hunted` (all elites gain a modifier) |
| 27 | `EVT_TWIN` | Someone Wearing Your Face | *They are also surprised.* | **Fight** → duel a copy of your own build (elite-tier rewards ×2) · **Trade** → swap your lowest-value gear for a random A item · **Walk past** → +12% Dodge for the run |
| 28 | `EVT_CLOCKTOWER` | The Clock That Runs Back | *The hands move the wrong way. Slowly.* | **Wind it forward** → skip to the next stage immediately, keep all rewards banked so far · **Wind it back** → replay the last 3 tiles with new contents · **Smash it** → +800 Crowns, curse `Untimely` (−15% ASPD, run) |
| 29 | `EVT_LAST_LAMP` | The Last Lamp | *It is the only light for a long way.* | **Take it** → tile preview +6 for the run, all `TILE_CURSE` in this stage are revealed · **Leave it lit** → heal 40% Max HP · **Extinguish it** (−10% HP) → +40% Crit Damage for the run |
| 30 | `EVT_DICELORD_OFFER` | An Offer, Unsigned | *A single golden die on a flat stone.* | **Take it** → one die face becomes `Star` for the run, and the boss gains +15% ATK · **Roll it** → 50% both above / 50% neither · **Leave it** → +1,000 Crowns |

⚠️ **NEEDS DETAIL:** Exact outcome weights and value scalars for events 11–30 are indicative. They must be passed through the economy simulator (doc 21) before they are treated as final, because several (26, 28, 30) can swing a run's reward total by more than 50%.

---

# PART B — Daily Quest Pool (20)

3 quests are drawn per day. 1 free reroll. Difficulty is balanced so all 3 are completable in ~40 minutes of normal play.

| # | ID | Objective | Tier |
|---|---|---|---|
| 1 | `DQ_RUNS_3` | Complete 3 runs | Easy |
| 2 | `DQ_ENEMIES_40` | Defeat 40 enemies | Easy |
| 3 | `DQ_STAGE3` | Reach Stage 3 in any chapter | Easy |
| 4 | `DQ_TREASURE_5` | Land on 5 Treasure tiles | Easy |
| 5 | `DQ_PERKS_15` | Draft 15 perks | Easy |
| 6 | `DQ_SHOP_BUY_3` | Buy 3 things from Shop tiles | Easy |
| 7 | `DQ_ELITES_5` | Defeat 5 Elites | Medium |
| 8 | `DQ_BOSS_1` | Defeat any chapter boss | Medium |
| 9 | `DQ_MERGE_2` | Merge 2 items | Medium |
| 10 | `DQ_ENHANCE_8` | Enhance any item to +8 or higher | Medium |
| 11 | `DQ_DUEL_WIN` | Win a Ghost Duel | Medium |
| 12 | `DQ_PET_LEVEL_2` | Level a pet twice | Medium |
| 13 | `DQ_TALENT_SPEND_3` | Spend 3 Talent Points | Medium |
| 14 | `DQ_MINIGAMES_4` | Complete 4 minigames | Medium |
| 15 | `DQ_NO_DEATH` | Complete a run without dying | Hard |
| 16 | `DQ_LEGENDARY_PERK` | Draft a Legendary perk | Hard |
| 17 | `DQ_TIER3_PERK` | Take any perk to Tier III | Hard |
| 18 | `DQ_HEROIC_CLEAR` | Clear any chapter on Heroic or above | Hard |
| 19 | `DQ_DUEL_STREAK_3` | Win 3 Ghost Duels in a day | Hard |
| 20 | `DQ_SS_ITEM` | Obtain or merge an SS-rarity item | Hard |

**Draw rule:** always 1 Easy + 1 Medium + 1 Easy-or-Medium. **Hard quests only appear as the reroll result**, and pay ×2. This means a player never opens the app to three chores, but a player who wants a challenge can reroll into one.

**Rewards per quest:** 500 Crowns, +20 Energy, and one of {Enhance Stones ×15, Beast Feed ×30, Soul Shards ×20}. All 3 complete → bonus chest (1 gear item at chapter-appropriate rarity + 100 Soul Shards).

Quests 11, 19 require PvP and are excluded before Legend Level 10. Quests 9, 10, 20 are excluded before the Forge unlocks at Legend Level 8.

---

# PART C — Weekly Chapter Challenge Modifiers (14)

One challenge per week: a fixed `(chapter, tier, seed, modifier set)` shared by all players, with a leaderboard on run score. 2–3 modifiers are combined. Reward: 400 Soul Shards, a gear chest, and Talent Points for top percentiles.

| # | ID | Name | Effect | Class |
|---|---|---|---|---|
| 1 | `MOD_GLASS` | Brittle | Hero Max HP −50%, ATK +50% | Risk |
| 2 | `MOD_SWARM` | Overrun | All enemy encounters use `SWARM` archetypes at +30% count | Combat |
| 3 | `MOD_NO_SHOPS` | Barren | No Shop tiles generate | Denial |
| 4 | `MOD_NO_HEAL` | Bloodless | All healing reduced by 70% | Denial |
| 5 | `MOD_DOUBLE_ELITES` | Gauntlet | Elite count doubled, Elite drops doubled | Risk/Reward |
| 6 | `MOD_FIXED_DIE` | Fixed Fate | Every roll is exactly 3 | Twist |
| 7 | `MOD_CHAOS_DIE` | Wild Dice | All six faces are `Star` (you always choose), but rerolls are disabled | Twist |
| 8 | `MOD_RICH` | Gilded | Gold and Crowns ×3, enemy DEF +40% | Risk/Reward |
| 9 | `MOD_SPEEDRUN` | Against the Clock | Score is based on real time to clear; enemies −20% HP | Scoring |
| 10 | `MOD_ONE_LIFE` | Ironclad | No revives of any kind, including the ad revive | Risk |
| 11 | `MOD_PERKLESS` | Unadorned | No perk drafts. Gear and talents only. | Denial |
| 12 | `MOD_CURSED` | Haunted | Start the run with 3 random curses; drop rates +60% | Risk/Reward |
| 13 | `MOD_MIRROR` | Reflections | All enemies have 25% Thorns | Combat |
| 14 | `MOD_LONG_ROAD` | The Long Road | Board length +50%, all rewards +50% | Endurance |

**Combination rule:** never pair two `Denial` modifiers. Never pair `MOD_PERKLESS` with `MOD_ONE_LIFE`. Always include at least one Risk/Reward or Twist so the week has an identity rather than just being harder.

✅ **The Weekly Chapter Challenge is now an `EVENT_SCORE_RUSH` package** in the live-ops framework — see `26_LIVE_OPS_AND_EVENTS.md` §3.2. The 14 modifiers above are unchanged and remain its content; what changes is that the challenge no longer has a bespoke system or a bespoke surface. It runs on the event scheduler, appears on the Events Hub (S30), and can be retuned weekly by editing a JSON package.

✅ This structurally resolves open item **O3**: the scoring formula becomes a `leaderboard.formula` field on the event package rather than one hardcoded formula that must be right forever. The default remains `tilesCleared × 100 + enemiesKilled × 25 + bossKilled × 2000 − secondsElapsed`, and the concern that it may simply reward the strongest account still stands — but it is now correctable on live evidence within a week instead of within an app release.

Rewards are paid by **percentile band, not absolute rank**, so the reward experience is identical at 10,000 players and at 1,000,000.

---

# PART D — FTUE Script

🔒 Total FTUE ≤ 5 minutes. No ads. The Plus offer is not shown before Legend Level 8. Every tooltip is dismissible and the whole tutorial is skippable from the pause menu after beat 2.

**Narrator:** none. There is no tutorial character. Instructions appear as short diegetic captions on the board itself, in the same dry voice as the event cards. This was chosen over the previously-placeholdered "Keeper of the Die" narrator because a talking guide would be the only recurring character in a game with no story, and would set an expectation the rest of the game does not meet.

| Beat | Trigger | On-screen copy | Interaction |
|---|---|---|---|
| 0 | First launch | *"Name yourself."* | Name entry, 12 chars, default "Wanderer". Skippable. |
| 1 | Board loads | *"Roll to move."* — with a pulsing ring on the die | Tap the die. Nothing else is tappable. |
| 2 | Land on enemy | *"Fights resolve themselves. Watch."* | Battle auto-plays at ×1. Speed controls appear but are not highlighted. |
| 3 | Battle won | *"Choose one. It lasts until this run ends."* | The three perk cards fan in one at a time. Timer disabled. |
| 4 | Roll 3 → Treasure | *"Yours. All of it, even if you die."* | Auto-resolves; the reward flies to the HUD. |
| 5 | Roll 5 → Shop | *"Gold is for now. It does not follow you home."* | Player must buy one thing. Prices are set so anything is affordable. |
| 6 | Roll 7 → Elite (scripted, drops hero to ~25% HP) | *"That was close. You can ask for a different number."* | The reroll button pulses. Player uses their first Reroll Charge. |
| 7 | Roll 9–11 → mini-boss | *"Last one."* | Scripted to be winnable. Boss has phase 1 only. |
| 8 | Victory | *"Take it back with you."* | Run Results screen, all rewards positive, no ad offer. |
| 9 | Home screen | *"You kept the gear. Put it on."* | Forced single gear equip on the Hero screen. |
| 10 | After equip | *"And this is permanent."* | Forced single Talent Point spend. Then FTUE ends. |

**Post-FTUE:** the player is released with Energy full, 2 daily quests pre-assigned, and Chapter 1 available. The ATT prompt (iOS) fires here, not before. The first interstitial is 72 hours away.

⚠️ **NEEDS DETAIL:** beat 6 requires the elite fight to be scripted to leave the player at ~25% HP regardless of their build. In a deterministic simulator this is straightforward (fix the seed and the enemy stats), but it must be explicitly implemented as a **tutorial-only enemy definition**, not as a hack in the combat loop.

🔒 **The tutorial run guarantees a level-up.** Beat 10 forces a Talent Point spend, and points come only from level-ups (`09` §2) — so the tutorial run pays a **fixed, scripted 300 Legend XP** (Level 2 needs 120, per `07` §1.1), guaranteeing at least one Talent Point exists before beat 10 regardless of play. Not subject to any multiplier.

---

# PART E — Curse Catalogue (12)

Curses are run-scoped negative effects applied by `TILE_CURSE`, by certain event outcomes, and by the `Cursed` elite modifier. They were referenced across `03`, `07`, `12` and Part A above but never defined; this is the catalogue.

| Rule | Specification |
|---|---|
| Scope | `RUN` — a curse lasts until the run ends |
| Stacking | Same curse never stacks. Re-application refreshes nothing; it is simply ignored. |
| Cleansing | `TILE_SHRINE` may offer "Cleanse a curse" as one of its two options. `AD_SKIP_CURSE` prevents one before it lands (1/run). `MNT_IRONSHELL` makes the player immune to `TILE_CURSE` entirely. |
| Compensation | Every curse from `TILE_CURSE` comes with a reward attached — curses are a **trade**, never a pure punishment. Event-inflicted curses are the player's own choice. |
| Visibility | Active curses appear as red icons in the board HUD, tappable for the full description. |

| ID | Name | Effect | Typical paired reward |
|---|---|---|---|
| `CUR_SLIPPERY` | Slippery | −1 to all Pip rolls (minimum 1) | +250 Gold |
| `CUR_MARKED` | Marked | Enemies +10% ATK for the rest of the stage | +2 Enhance Stones |
| `CUR_DIZZY` | Dizzy | The next 3 rolls cannot be rerolled | +180 Gold |
| `CUR_FRACTURED` | Fractured | −8% DEF | +500 Gold |
| `CUR_HUNTED` | Hunted | Every Elite for the rest of the run gains an extra modifier | +1 gear drop per Elite |
| `CUR_UNTIMELY` | Untimely | −15% Attack Speed | +300 Crowns |
| `CUR_FAMISHED` | Famished | −40% Healing Received | +12% ATK |
| `CUR_BRITTLE_BONES` | Brittle Bones | −12% Max HP | +8% Crit Chance |
| `CUR_MISERLY` | Miserly | Shop prices +50% | +600 Gold |
| `CUR_BLIND` | Blind | Tile preview reduced to 2 | +2 Reroll Charges |
| `CUR_LEADFOOT` | Leadfoot | `Chain` and `Surge` faces behave as plain Pip 3 | +15% Gold |
| `CUR_TITHE` | Tithe | 20% of all Gold gained is lost | +20% gear drop chance |

📐 TUNABLE: all values and pairings. `CUR_HUNTED` and `CUR_TITHE` are the two most likely to need rebalancing — both change the run's economy rather than a single stat.

⚠️ **NEEDS DETAIL:** which curses can appear in which chapters is unspecified. Suggested: `CUR_SLIPPERY`, `CUR_MARKED`, `CUR_DIZZY`, `CUR_FRACTURED` from Chapter 1; the rest gated from Chapter 3 onward, with `CUR_HUNTED` from Chapter 5.

---

# PART F — The Lucky Wheel

Referenced as `AD_LUCKY_WHEEL` (`12` §4.2), screen S25 (`13` §1) and a Home widget, but never specified. This is it.

| Property | Value |
|---|---|
| Free spins | 1 per day, at 05:00 UTC |
| Ad spins | `AD_LUCKY_WHEEL`, +2 per day |
| Plus subscribers | 3 spins/day, auto-granted (identical to a full ad-watcher) |
| Segments | 8 |
| Animation | 2.5 s ratcheting spin with a deceleration curve, skippable after 1 s |
| Authority | The result is **rolled server-side** and sent to the client, which animates to the predetermined segment. The wheel is presentation, not randomness. |

### Segment table

| # | Reward | Weight |
|---|---|---|
| 1 | Crowns ×(400 × chapterScalar) | 24 |
| 2 | Beast Feed ×60 | 18 |
| 3 | Enhance Stones ×20 | 16 |
| 4 | Energy +40 | 14 |
| 5 | Merge Dust ×(300 × chapterScalar) | 12 |
| 6 | Soul Shards ×80 | 9 |
| 7 | One gear item, A-rarity or better | 5 |
| 8 | **Jackpot:** one Pet Egg | 2 |

`chapterScalar = 1 + 0.35 × highestChapterCleared`, matching the ad bundle scaling in `12` §5.

**Pity:** the jackpot segment is guaranteed at least once every 60 spins, with soft pity from spin 40 (slope `0.06`), and the same segment may never be rolled three times consecutively. `24_LUCK_PROTECTION.md` §4.8 is the authority. 📐 TUNABLE

🔒 The wheel is **not** a gacha and cannot be bought. It is a small daily gift with variance, and every segment is a positive outcome — there are no blanks and no "better luck next time". A wheel that can land on nothing is a slot machine; a wheel that always gives something is a present.

---

# PART G — The 28-Day Login Calendar

Referenced in `02` §9 ("28-day cycle, day 7/14/21/28 give pets or S-tier gear chests") but never authored. This is it.

| Rule | Specification |
|---|---|
| Advancement | The calendar advances **on login, not by date**. A missed day pauses the calendar; nothing is skipped or lost. |
| Cycle | After day 28 it restarts at day 1. Every cycle pays identically — nothing is first-cycle-exclusive (`11` §5.3). |
| Scaling | Crown and Merge Dust values are Chapter-1 base and scale by `chapterScalar = 1 + 0.35 × highestChapterCleared`, matching Part F and `12` §5. |
| Claiming | One tap on S25; auto-highlighted when unclaimed. Never a popup. |
| Luck classes | Pet Eggs are `EGG_PET`; the day-14/28 S-tier chests are **`CHEST_PREMIUM`**; any other gear chest is `CHEST_STANDARD` (`24` §3, amended accordingly). |

| Day | Reward | Day | Reward |
|---|---|---|---|
| 1 | 300 Crowns | 15 | 800 Crowns |
| 2 | 20 Enhance Stones | 16 | 40 Enhance Stones |
| 3 | 40 Beast Feed | 17 | 90 Beast Feed |
| 4 | 100 Soul Shards | 18 | 200 Soul Shards |
| 5 | +30 Energy | 19 | +60 Energy |
| 6 | 200 Merge Dust | 20 | 400 Merge Dust |
| **7** | **1 Pet Egg** | **21** | **1 Pet Egg + 200 Soul Shards** |
| 8 | 500 Crowns | 22 | 1,200 Crowns |
| 9 | 30 Enhance Stones | 23 | 60 Enhance Stones |
| 10 | 60 Beast Feed | 24 | 120 Beast Feed |
| 11 | 150 Soul Shards | 25 | 300 Soul Shards |
| 12 | +40 Energy | 26 | Full Energy refill |
| 13 | 300 Merge Dust | 27 | 600 Merge Dust |
| **14** | **1 S-tier Gear Chest** | **28** | **1 S-tier Gear Chest + 1 Pet Egg** |

📐 TUNABLE — all values, in `data/tuning/currencies.json`. The simulator models the calendar at each profile's `LoginCalendar` engagement rate (`21` §5.2).
