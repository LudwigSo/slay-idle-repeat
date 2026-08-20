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
| 3 | `EVT_TRAVELLER` | The Tired Traveller | *He offers his pack. He does not offer his name.* | **Trade 200 Gold** → a random B-or-better gear item · **Rob him** → +400 Gold, curse `Marked` (enemies +10% ATK this stage) · **Share your rations** (−5% HP) → ~~+2 Reroll Charges~~ ⚠️ *Reroll Charge* outcomes are unpayable — the reroll is removed (`04` §5) — and each is owed a replacement reward. |
| 4 | `EVT_SHRINE_CRACKED` | A Cracked Shrine | *Something used to live in here.* | **Pray** → 50% +15% Max HP / 50% −10% DEF, both for the run · **Repair it** (150 Gold) → +12% Max HP guaranteed · **Leave** |
| 5 | `EVT_BEEHIVE` | Sunlit Hive | *The buzzing is louder than it should be.* | **Take the honey** (−8% HP) → heal 30% HP after the next battle, +2 Beast Feed ×20 · **Smoke it out** (100 Gold) → +40% of the above, no HP cost · **Leave** |
| 6 | `EVT_LOST_PUP` | A Lost Whelp | *It follows you three paces, then stops.* | **Feed it** (−80 Gold) → 35% Pet Egg / 65% +60 Beast Feed · **Leave it** → nothing · **Take it with you** → +5% pet aura power this run |
| 7 | `EVT_MERCHANT_CART` | Overturned Cart | *The wheels are still spinning.* | **Loot it** → +250 Gold, 20% chance of an ambush battle · **Right the cart** (−5% HP) → the next Shop tile has 6 slots · **Move on** |
⚠️ **Three of Part A's outcomes granted Reroll Charges and now grant fixed dice** (`04` §6): `EVT_TRAVELLER`'s *Share your rations* (+2), `EVT_OLD_SOLDIER`'s *Ask nothing* (+1) and `EVT_STARFALL`'s *Avoid it* (+1). Two more are NOT converted: `EVT_TAX`'s toll needs a proportional cost the flat cost field cannot express, and `EVT_STORM`'s *lose a charge to heal* needs a way to take a die back, which a card cannot have — it does not know which of the player's dice it gave them. 🔴 **Three outcomes are dead outright**: every *reveal the tiles* outcome — `EVT_OLD_SOLDIER`'s *Ask about the road*, `EVT_ARCHIVE`'s *Read the maps* and `EVT_LAST_LAMP`'s *Take it* — pays nothing, because the board is completely visible at all times (`04` §4, `16` D42). All three are owed replacement rewards, which is a design decision rather than a transcription's to make.

| 8 | `EVT_OLD_SOLDIER` | The Old Soldier | *"Tried this road once. Didn't like it."* | **Ask about the road** → reveal all tiles in this stage · **Ask about his armour** (250 Gold) → +15% DEF for the run · **Ask nothing** → ~~+1 Reroll Charge~~ ⚠️ *Reroll Charge* outcomes are unpayable — the reroll is removed (`04` §5) — and each is owed a replacement reward. |
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
| 17 | `EVT_TAX` | The Toll Collector | *He has no authority. He does have a large friend.* | **Pay** (15% of current Gold) → pass, ~~+1 Reroll Charge~~ ⚠️ *Reroll Charge* outcomes are unpayable — the reroll is removed (`04` §5) — and each is owed a replacement reward. · **Refuse** → elite battle, keep everything · **Haggle** (roll 4+) → pay half, else pay double |
| 18 | `EVT_SPRING` | A Hot Spring | *Steam, and no one else for miles.* | **Bathe** → heal 50% Max HP, +5% Max HP for the run · **Fill your flask** → gain a Health Draught consumable ×2 · **Both** (−250 Gold) |
| 19 | `EVT_ARCHIVE` | A Buried Archive | *Wet paper. Some of it legible.* | **Read the combat notes** → +1 tier on one owned perk · **Read the maps** → reveal all tiles for the rest of this stage · **Sell the lot** → +400 Crowns |
| 20 | `EVT_STORM` | Gathering Storm | *You have maybe two minutes.* | **Run for it** → move forward 4 nodes, skip their content · **Shelter** (~~lose 1 Reroll Charge~~) → heal 20% HP · ⚠️ *Reroll Charge* outcomes are unpayable — the reroll is removed (`04` §5) — and each is owed a replacement reward. **Walk through it** (−15% HP) → +25% ATK for the rest of the stage |

## A3. Chapters 6–8 (late)

| # | ID | Title | Body | Options |
|---|---|---|---|---|
| 21 | `EVT_ORACLE` | The Blind Oracle | *"You will roll a four." You have not rolled yet.* | **Ask about the boss** → the boss starts at 90% HP · **Ask about yourself** → +1 tier on two owned perks · **Ask about the die** → ~~one die face upgraded for the run~~ ⚠️ the die has no faces (`04` §5); owed a replacement |
| 22 | `EVT_BARGAIN` | An Even Bargain | *Nothing is written down.* | **Give 25% Max HP** → +45% ATK for the run · **Give 30% ATK** → +60% Max HP for the run · **Give nothing** → +150 Crowns |
| 23 | `EVT_SPORE_FIELD` | A Field of Caps | *They lean toward you.* | **Walk through** → 3 `SPORE` stacks, +600 Crowns · **Burn a path** (−300 Gold) → safe passage, +2 Beast Feed ×50 · **Go around** → lose 2 nodes of progress |
| 24 | `EVT_AUTOMATON` | A Stopped Automaton | *One gear short.* | **Give it a gear** (−6 Enhance Stones) → it fights alongside you for 3 battles · **Strip it** → +10 Enhance Stones, +300 Crowns · **Wind it up** → 50% it helps / 50% it attacks (elite battle) |
| 25 | `EVT_STARFALL` | Starfall | *Something lands two nodes ahead.* | **Investigate** → move to that node, guaranteed S-rarity gear · **Avoid it** → ~~+1 Reroll Charge~~ ⚠️ *Reroll Charge* outcomes are unpayable — the reroll is removed (`04` §5) — and each is owed a replacement reward. · **Watch from here** → +8% all stats for the run |
| 26 | `EVT_DEBT` | A Collector's Ledger | *Your name is in it. You have never been here.* | **Pay in Gold** (all of it) → +30% all stats for the run · **Pay in blood** (−35% current HP) → 2 S-rarity gear items · **Tear out the page** → curse `Hunted` (all elites gain a modifier) |
| 27 | `EVT_TWIN` | Someone Wearing Your Face | *They are also surprised.* | **Fight** → duel a copy of your own build (elite-tier rewards ×2) · **Trade** → swap your lowest-value gear for a random A item · **Walk past** → +12% Dodge for the run |
| 28 | `EVT_CLOCKTOWER` | The Clock That Runs Back | *The hands move the wrong way. Slowly.* | **Wind it forward** → skip to the next stage immediately, keep all rewards banked so far · **Wind it back** → replay the last 3 tiles with new contents · **Smash it** → +800 Crowns, curse `Untimely` (−15% ASPD, run) |
| 29 | `EVT_LAST_LAMP` | The Last Lamp | *It is the only light for a long way.* | **Take it** → tile preview +6 for the run, all `TILE_CURSE` in this stage are revealed · **Leave it lit** → heal 40% Max HP · **Extinguish it** (−10% HP) → +40% Crit Damage for the run |
| 30 | `EVT_DICELORD_OFFER` | An Offer, Unsigned | *A single golden die on a flat stone.* | **Take it** → ~~one die face becomes `Star` for the run~~, and the boss gains +15% ATK · **Roll it** → 50% both above / 50% neither · **Leave it** → +1,000 Crowns ⚠️ **The card's whole premise is a face grant** (`04` §5), so `EVT_DICELORD_OFFER` is now a pure downside on *Take it*. Owed a redesign or a removal. |

⚠️ **NEEDS DETAIL:** Exact outcome weights and value scalars for events 11–30 are indicative. They must be passed through the economy simulator (doc 21) before they are treated as final, because several (26, 28, 30) can swing a run's reward total by more than 50%.

---

# PART B — Daily Quest Pool (20)

3 quests are drawn per day — by the game day's first `BEGIN_SESSION`, using that command's server-issued seed (`14` §2.3, `30` §2.3). 1 free reroll per day, via the `REROLL_QUEST` command; the replacement is drawn from the reroll command's own seed. Difficulty is balanced so all 3 are completable in ~40 minutes of normal play. *(Command wiring ruled in `16` A7.)*

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

**Rewards per quest:** 500 Crowns, +20 Energy, and one of {Enhance Stones ×15, Beast Feed ×30, Soul Shards ×20}. All 3 complete → bonus chest (a `CHEST_STANDARD`, granted **unopened** onto the shelf — contents and the exact "chapter-appropriate" definition are `24` §4.0–4.0a) + 100 Soul Shards (a fixed rider of the quest system, not part of the chest).

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
| ~~7~~ | ~~`MOD_CHAOS_DIE`~~ | ~~Wild Dice~~ | ⚠️ **Unbuildable and unreplaced.** *All six faces are `Star`, but rerolls are disabled* — both halves are gone (`04` §5). The modifier pool is one row short. | Twist |
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

The "Roll *n*" triggers below are the **forced results of the rigged die sequence in D3** — the tutorial die is not random (ruled in `16` A7), so every landing below is guaranteed, not hoped for.

| Beat | Trigger | On-screen copy | Interaction |
|---|---|---|---|
| 0 | First launch | *"Name yourself."* | Name entry, 12 chars, default "Wanderer". Skippable. |
| 1 | Board loads | *"Roll to move."* — with a pulsing ring on the die | Tap the die. Nothing else is tappable. |
| 2 | Land on enemy | *"Fights resolve themselves. Watch."* | Battle auto-plays at ×1. Speed controls appear but are not highlighted. |
| 3 | Battle won | *"Choose one. It lasts until this run ends."* | The three perk cards fan in one at a time (fixed options — D4). Timer disabled. |
| 4 | Roll 3 → Treasure | *"Yours. All of it, even if you die."* | Auto-resolves; the authored payout (D5) flies to the HUD. |
| 5 | Roll 5 → Shop | *"Gold is for now. It does not follow you home."* | Player must buy exactly one thing (fixed offers and authored prices — D4.2). Every offer costs less than the Gold held, so anything is affordable. |
| 6 | Roll 7 → Elite (`FTUE_ELITE`, leaves the hero at ~25% HP — D4) | *"That was close."* | Battle auto-plays. Afterwards: the second perk draft (fixed options — D4). |
| ~~6b~~ | ⚠️ **Removed with the reroll** (`04` §5). It showed a `2` pointing at the Cursed Ground tile, pulsed the reroll button, and taught that rerolls exist and dodge trouble. | — | 🔴 The FTUE is one beat short and the tile it threatened is now unthreatening — see beat 10 below. The replacement lesson is owed. |
| 7 | Roll 9 → mini-boss (`BOSS_FTUE` — D4) | *"Last one."* | The hero heals to full on landing (tutorial-only 📐). The forced `6` clamps onto the boss node — the boss is always reached exactly (`03` §1.1). Phase 1 only. No draft afterwards (D4). |
| 8 | Victory | *"Take it back with you."* | Run Results screen, all rewards positive, no ad offer. |
| 9 | Home screen | *"You kept the gear. Put it on."* | Forced single gear equip on the Hero screen. |
| 10 | After equip | *"And this is permanent."* | Forced single Talent Point spend. Then FTUE ends. |

**Post-FTUE:** the player is released with Energy full, 2 daily quests pre-assigned, and Chapter 1 available. The ATT prompt (iOS) fires here, not before. The first interstitial is 72 hours away.

✅ **Resolved (ruled in `16` A7): the FTUE is an authored data package — `data/content/ftue.json`.** Everything above is data in that file, nothing is scripted in code. The elite near-death is a tutorial-only enemy definition plus fixed choice sets (D4), never a hack in the combat loop (O9 closed). Sections D1–D8 below are the package spec.

## D1. Principle

The FTUE run is an ordinary run through the ordinary rules engine — same commands, same combat, same board states — driven by authored content: an authored board (`03` §3 authored-board mode), a forced die stream, and tutorial-only definitions. The only tutorial-only *rules* are the seven flags listed in D4.3, each of which exists to make the script safe, not to change what the game is.

## D2. Board layout — 12 nodes, walk order

No stage gates, no forks, no campfire. Node 11 is the final node and holds the mini-boss.

| Node | Tile | Role |
|---|---|---|
| 0 | `TILE_ENEMY` | Beat 1–3: first fight, first draft |
| 1 | `TILE_EMPTY` | spacing |
| 2 | `TILE_EMPTY` | spacing |
| 3 | `TILE_EMPTY` | spacing |
| 4 | `TILE_TREASURE` | Beat 4: authored payout (D5) |
| 5 | `TILE_EMPTY` | spacing |
| 6 | `TILE_SHOP` | Beat 5: tutorial shop (D4.2) |
| 7 | `TILE_EMPTY` | spacing |
| 8 | `TILE_ELITE` | Beat 6: `FTUE_ELITE` |
| 9 | `TILE_EMPTY` | ⚠️ Was where beat 6b's reroll landed; nothing lands here on purpose now |
| 10 | `TILE_CURSE` (`CUR_SLIPPERY`) | ⚠️ **Designed never to be landed on** — it existed as the reroll lesson's visible threat, and that lesson is gone (`04` §5). It is now a tile the forced sequence passes over for no stated reason. Inspectable (tooltip works); passing never resolves (`03` §1.1). |
| 11 | `TILE_MINIBOSS` (`BOSS_FTUE`) | Beat 7–8 |

The board deliberately violates generator constraint C7 (≥2 treasure, ≥1 cache) — authored boards bypass constraints (`03` §3), and the day-1 payout is scripted (D5), so C7's protection is not needed here.

## D3. Forced die sequence

The hero starts at the trailhead before node 0, like every run (`03` §1.1). The FTUE's die stream is **rigged**: results come from this ordered list, not from the RNG (tutorial-only flag, D4.3). ⚠️ *and the Fair-Dice bag is not consulted* — there is no bag any more (`04` §2). All results are numbers the die can show, 1..6.

| Roll | Forced result | Cumulative | Lands on | Beat |
|---|---|---|---|---|
| 1 | `1` | 1 | node 0 — ENEMY | 1–3 |
| 2 | `2` | 3 | node 2 — empty | — |
| 3 | `2` | 5 | node 4 — TREASURE | 4 |
| 4 | `1` | 6 | node 5 — empty | — |
| 5 | `1` | 7 | node 6 — SHOP | 5 |
| 6 | `1` | 8 | node 7 — empty | — |
| 7 | `1` | 9 | node 8 — ELITE | 6 |
| 8 | ⚠️ was `2` → forced reroll → `1` | 10 | 🔴 **This row has no rigged value now.** The reroll it demonstrated is gone (`04` §5), so the roll has to become an ordinary one — and whatever number it takes must not land on node 10's curse, which beat 6b's reroll used to be what avoided. Owed with the replacement beat. | ~~6b~~ |
| 9 | `6` (clamped) | 12 | node 11 — MINIBOSS. The clamp teaches the boss-reached-exactly rule (`03` §1.1). | 7 |

⚠️ *Roll 8 is the only roll where the reroll prompt is interactive; on every other roll the reroll button is hidden* — there is no reroll prompt at all (`04` §4). The small forced values are deliberate: nine rolls across twelve tiles keeps every beat visible.

## D4. Tutorial-only definitions

### D4.1 Enemies

Three tutorial-only entries. Powers are **final, authored values** — no chapter scaling, no ×2.2 elite multiplier, no `StageMult` — and all three fights run at **Level = 1**. Grunt and elite stats derive from Power through the standard `05` §6 derivation; the mini-boss statblock is authored **once**, in `17` §1.2's `BOSS_FTUE` row (power 900, Thornmaw-shaped coefficients, phase 1 only — ruled in `16` A7).

| ID | Base | Power | Notes |
|---|---|---|---|
| `FTUE_GRUNT` | `GRUNT` archetype | **320** 📐 | Beat 1. A watchable, safe first fight. |
| `FTUE_ELITE` | `BRUTE` archetype | **700** 📐 | Beat 6. Carries **no elite modifier** (tutorial-only exception — the banner reads plain "ELITE" and teaches that the banner exists). Tuned to leave the hero at ~25% HP. |
| `BOSS_FTUE` | Statblock: `17` §1.2 (single authority). Skin: Thornmaw, **phase 1 only** (`17` §2 — Basking: basic attacks, ASPD 0.7) | **900** 📐 (authored in `17` §1.2) | Beat 7. ⚠️ **The mini-boss identity — Thornmaw phase 1 — is the default; final confirmation is tracked as O36** (`16` B4). Never enters phases 2–3 regardless of HP. |

These numbers are placeholders with teeth: the binding constraints are the acceptance bands in D8, verified over every tutorial choice combination. If a band fails, the Powers move; the bands do not.

### D4.2 Drafts and the shop — fixed choice sets

Perk drafts in the FTUE use **fixed option sets**, not seeded draws, so the beat-6 near-death holds under every combination:

| Draft | After | Options (all Common, disjoint sets) |
|---|---|---|
| 1 | Beat 3 (first fight) | `PK_SHARP_EDGE` (+12% ATK) · `PK_TOUGH_HIDE` (+15% Max HP) · `PK_LEECH` (+6% Lifesteal) 📐 |
| 2 | Beat 6 (elite) | `PK_KEEN_EYE` (+6% Crit) · `PK_IRON_SKIN` (+18% DEF) · `PK_REGEN` (1% Max HP/s) 📐 |

There is **no draft after the mini-boss** (the run ends into beat 8; a draft there would teach nothing and pad the ≤ 5-minute budget). FTUE drafts have **no reroll and no skip** — beat 3's "Choose one" is literal.

The **tutorial shop** (beat 5) has four fixed offers in the standard §7 slot shape: `PK_BRUTALITY` (Common perk) · **Health Draught** (`03` §7.1 — the board-use consumable, introduced here) · Run Buff **+8% ATK** 📐 · Heal 35% Max HP. Prices are the authored **tutorial price row** in `ftue.json` (`16` A7) — the one FTUE exception to the `03` §7 price formula; the canonical Draught row (**100 Gold** 📐) is fixed below, after the XP lock. Constraint the row must satisfy: every offer ≤ **140 Gold** 📐 — the hero holds 150 Gold at beat 5 (D5), and *"anything is affordable"* is a locked beat. The player must buy exactly one thing; the shop then closes (no refresh — refresh is partly an ad affordance and no ads run in the FTUE).

### D4.3 Tutorial-only rule flags (complete list)

1. `riggedDieStream` — die results come from D3's list. ⚠️ *the Fair-Dice bag is bypassed* — there is no bag to bypass (`04` §2).
2. `noAds` — no ad placements anywhere (existing lock, now a package flag).
3. `heroLethalClampAt1Hp` — the hero cannot die; lethal damage clamps to 1 HP. The fights are authored so this never fires; if it does, emit `ftue_clamp_fired` telemetry — it means tuning drifted, and it must be investigated, not shipped around.
4. `fixedDrafts` / `noDraftReroll` / `noBossDraft` — D4.2.
5. `fullHealAtMiniboss` — heal to 100% Max HP on landing on node 11 📐.
6. `zeroEnergyCost` — the FTUE run costs 0 Energy 📐 and is not a `(Chapter, Tier)` attempt: no first-clear bonus, no completion multiplier (payout is D5's fixed table).
7. `noRunTtl` — the FTUE run is exempt from the 48 h run TTL. A player who installs, plays two beats and returns in a week resumes their tutorial, not a cold start.

## D5. The scripted payout — the single day-1 state

Everything the tutorial banks, granted **identically on completion and on skip**. This table is the single source of truth for the post-FTUE account state; nothing else in the tutorial grants meta-currency.

| Grant | Amount | Moment (completers) |
|---|---|---|
| Legend XP | **300** — 🔒 fixed (the lock below this section), guarantees the level-up beat 10 needs | run end |
| Crowns | **60** 📐 | Treasure tile, beat 4 |
| Enhance Stones | **5** 📐 | Treasure tile, beat 4 |
| Soul Shards | **30** 📐 | Mini-boss victory |
| Gear | `GEAR_WEAPON_BLADE`, rarity **B**, `chapterOrigin` 1, `q = 0.70`, affix `AFX_CRIT_CHANCE +3%`, enhance +0 📐 — the beat-9 forced-equip item | Mini-boss victory (shown in the beat-8 tally) |

In-run Gold is scripted too — first kill **150** 📐, elite kill **200** 📐 — and vanishes at run end like all Gold, so shop choices cannot diverge the day-1 state. Kills pay no per-kill Legend XP (the fixed 300 is the whole grant, per the lock below). No ad-double exists (no ads).

**Convergence statement:** after beat 10, a skipper and a completer hold exactly: this payout, the blade equipped, one Talent Point spent (the node is the player's own choice — the one accepted divergence), Energy full, 2 daily quests pre-assigned, `ftueProgress = complete`. Perks, consumables and Gold were run-scoped and are gone either way.

## D6. Skip semantics

Skip is available from the pause menu **after beat 2** (existing lock), sent as the `SKIP_FTUE` meta command (`14` §2.3). On skip: grant the full D5 payout, mark beats up to 8 complete, and jump directly to **beat 9** (Home, forced equip) then **beat 10** (forced Talent Point spend). Skippers and completers converge on the one day-1 state above. Skipping is one confirm ("Skip the tutorial? You keep everything it pays.") — never a nag, never re-offered after completion.

## D7. Persistence and resume

FTUE progress persists **per beat**: the Player aggregate carries `ftueProgress { completedAtUtc | null, beatId }` (`30` §4), where `beatId` ∈ B0…B10 plus B6b, advanced server-side as each beat's interaction completes. Resume rules:

| Killed at | Resumes to |
|---|---|
| Beat 0 (name entry) | Re-prompt beat 0. |
| Beats 1–8 (in-run) | The run resumes through standard run persistence (`02` §1, `14` §3) — exempt from the TTL per D4.3 — and the client re-displays the current beat's caption. A kill mid-battle restarts that battle, per the standard rule (`02` §6). |
| Beats 9–10 (post-run, forced steps) | The forced step re-presents on next launch. The payout is already banked (idempotent — granted once, keyed on the run). |

The FTUE is complete when beat 10's spend commits; `completedAtUtc` is set and no FTUE surface ever appears again.

## D8. Validation and acceptance tests (binding)

The content schema validator must check `ftue.json` structurally: 12 nodes, mini-boss in final position, D3's sequence walkable (each cumulative total lands the beat it claims, the roll-9 clamp included), draft sets disjoint, shop offers resolvable, payout table complete.

The balance harness (`05` §9) must enumerate **every tutorial choice combination** — 3 draft-1 picks × 4 shop purchases × 3 draft-2 picks × Draught use/not-use where legal — and assert:

| # | Assertion |
|---|---|
| T1 | Beat-1 fight: victory, 6–15 s at ×1 speed, hero ≥ 85% HP after 📐 |
| T2 | Beat-6 elite: victory, hero at **15–35%** Max HP after (target ~25%) 📐 |
| T3 | Mini-boss: victory, hero ≥ 25% HP at end, fight 15–40 s 📐 |
| T4 | `heroLethalClampAt1Hp` never fires in any combination |
| T5 | Total FTUE critical path ≤ 5 minutes at default speeds (locked budget) |

These tests are the real spec for D4.1's Power values. Tune Powers until they pass; never widen the bands to make them pass.

🔒 **The tutorial run guarantees a level-up.** Beat 10 forces a Talent Point spend, and points come only from level-ups (`09` §2) — so the tutorial run pays a **fixed, scripted 300 Legend XP** (Level 2 needs 120, per `07` §1.1), guaranteeing at least one Talent Point exists before beat 10 regardless of play. Not subject to any multiplier.

🔒 **Tutorial shop price row (ruled in `16` A7):** the beat-5 shop's canonical offer is a **Health Draught at 100 Gold** 📐, authored in `ftue.json` — *not* derived from the `03` §7 price formula. The FTUE script banks **150 Gold** before beat 5 (the scripted first-kill Gold, D5) and every offer costs at most 140 Gold 📐 (D4.2), so the beat-5 purchase — and its "anything is affordable" promise — can never fail. The remaining tutorial-shop slots and their prices belong to the `ftue.json` package (`16` A7's FTUE ruling); this row is fixed here because the live-shop pricing in `03` §7 references it as its FTUE exception.

---

# PART E — Curse Catalogue (12)

Curses are run-scoped negative effects applied by `TILE_CURSE`, by certain event outcomes, and by the `Cursed` elite modifier. They were referenced across `03`, `07`, `12` and Part A above but never defined; this is the catalogue.

| Rule | Specification |
|---|---|
| Scope | `RUN` — a curse lasts until the run ends |
| Stacking | Same curse never stacks. Re-application refreshes nothing; it is simply ignored. |
| Cleansing | 🔒 If ≥1 cleansable curse is active, a shrine's second option is **always** a Cleanse (player picks which curse; paired rewards are kept) — the full rule is `03` §7a.5 (ruled in `16` A7). `AD_SKIP_CURSE` prevents one before it lands (1/run). `MNT_IRONSHELL` makes the player immune to `TILE_CURSE` entirely. |
| Compensation | Every curse from `TILE_CURSE` comes with a reward attached — curses are a **trade**, never a pure punishment. Event-inflicted curses are the player's own choice. |
| Visibility | Active curses appear as red icons in the board HUD, tappable for the full description. |

| ID | Name | Effect | Typical paired reward |
|---|---|---|---|
| `CUR_SLIPPERY` | Slippery | −1 to all Pip rolls (minimum 1) | +250 Gold |
| `CUR_MARKED` | Marked | Enemies +10% ATK for the rest of the stage | +2 Enhance Stones |
| `CUR_FRACTURED` | Fractured | −8% DEF | +500 Gold |
| `CUR_HUNTED` | Hunted | Every Elite for the rest of the run gains an extra modifier | +1 gear drop per Elite |
| `CUR_UNTIMELY` | Untimely | −15% Attack Speed | +300 Crowns |
| `CUR_FAMISHED` | Famished | −40% Healing Received | +12% ATK |
| `CUR_BRITTLE_BONES` | Brittle Bones | −12% Max HP | +8% Crit Chance |
| `CUR_MISERLY` | Miserly | Shop prices +50% | +600 Gold |
| `CUR_TITHE` | Tithe | 20% of all Gold gained is lost | +20% gear drop chance |

⚠️ **Nine curses, not twelve.** Three are removed and none replaced, so the pool a `TILE_CURSE` draws from is three rows short:

| Removed | Was | With |
|---|---|---|
| `CUR_DIZZY` | *the next 3 rolls cannot be rerolled*, +180 Gold | the reroll (`04` §5) |
| `CUR_LEADFOOT` | *`Chain` and `Surge` faces behave as plain Pip 3*, +15% Gold | the die's special faces (`04` §5) |
| `CUR_BLIND` | *Tile preview reduced to 2*, +15% Gold | the tile preview, when the board became permanently visible (`04` §4, `16` D42) |

⚠️ `CUR_BLIND` briefly held `CUR_LEADFOOT`'s freed +15% Gold, rather than have a reward number invented for it. Both rows are gone now, so that reward is unclaimed again.

📐 TUNABLE: all values and pairings. `CUR_HUNTED` and `CUR_TITHE` are the two most likely to need rebalancing — both change the run's economy rather than a single stat.

⚠️ **NEEDS DETAIL:** which curses can appear in which chapters is unspecified. Suggested: `CUR_SLIPPERY`, `CUR_MARKED`, `CUR_FRACTURED` from Chapter 1; the rest gated from Chapter 3 onward, with `CUR_HUNTED` from Chapter 5.

---

# PART F — The Lucky Wheel

Referenced as `AD_LUCKY_WHEEL` (`12` §4.2), screen S25 (`13` §1) and a Home widget, but never specified. This is it.

| Property | Value |
|---|---|
| Free spins | 1 per day; the counter resets lazily at the 05:00 UTC boundary (`30` §2.3) |
| Ad spins | `AD_LUCKY_WHEEL`, +2 per day |
| Plus subscribers | 3 spins/day, auto-granted (identical to a full ad-watcher) |
| Segments | 8 |
| Animation | 2.5 s ratcheting spin with a deceleration curve, skippable after 1 s |
| Authority | A spin is the `SPIN_WHEEL` meta command (`14` §2.3); the result is **rolled inside `Apply`** from the command's server-issued seed (`30` §3) and sent to the client, which animates to the predetermined segment. Ad spins are charges granted by `CLAIM_AD_REWARD` and consumed by the same command, oldest first. The wheel is presentation, not randomness. |

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
| 8 | **Jackpot:** one Pet Egg — granted as an **unopened container** on the shelf (`24` §4.0, ruled in `16` A7) | 2 |

`chapterScalar = 1 + 0.35 × highestChapterCleared`, matching the ad bundle scaling in `12` §5.

**Pity:** the jackpot segment is guaranteed at least once every 60 spins, with soft pity from spin 40 (slope `0.06`), and the same segment may never be rolled three times consecutively. `24_LUCK_PROTECTION.md` §4.8 is the authority. 📐 TUNABLE

🔒 The wheel is **not** a gacha and cannot be bought. It is a small daily gift with variance, and every segment is a positive outcome — there are no blanks and no "better luck next time". A wheel that can land on nothing is a slot machine; a wheel that always gives something is a present.

---

# PART G — The 28-Day Login Calendar

Referenced in `02` §9 ("28-day cycle, day 7/14/21/28 give pets or S-tier gear chests") but never authored. This is it.

| Rule | Specification |
|---|---|
| Advancement | The calendar advances at **`BEGIN_SESSION` — the first server contact of the game day — not by date** (`14` §2.3, `30` §2.3, ruled in `16` A7). It advances at most once per game day, and only when the currently open day has been claimed; a missed day — or an unclaimed one — pauses the calendar. Nothing is skipped or lost. |
| Cycle | After day 28 it restarts at day 1. Every cycle pays identically — nothing is first-cycle-exclusive (`11` §5.3). |
| Scaling | Crown and Merge Dust values are Chapter-1 base and scale by `chapterScalar = 1 + 0.35 × highestChapterCleared`, matching Part F and `12` §5. |
| Claiming | One tap on S25 — the `CLAIM_CALENDAR` command (`14` §2.3); auto-highlighted when unclaimed. Never a popup. |
| Luck classes | Pet Eggs are `EGG_PET`; the day-14/28 S-tier chests are **`CHEST_PREMIUM`**; any other gear chest is `CHEST_STANDARD` (`24` §3). All are granted as **unopened containers** on the shelf — pity and Focus are read at open, not at grant (`24` §4.0, ruled in `16` A7). |

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
