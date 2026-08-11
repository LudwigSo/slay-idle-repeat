# 09 — Talent Tree

The talent tree is the **permanent power spine**. Gear fluctuates, perks vanish at run end, pets rotate — talents only ever go up. This is the system that guarantees pillar P1: *the player is always a little stronger than yesterday.*

---

## 1. Structure

**3 branches × 20 nodes = 60 nodes.**

| Branch | Theme | Colour | Unlocked at |
|---|---|---|---|
| **MIGHT** | Offense, crit, penetration, damage conversion | red | Legend Level 1 |
| **WARD** | HP, defense, mitigation, sustain, survivability | blue | Legend Level 1 |
| **FORTUNE** | Dice faces, rerolls, drop rates, gold, board control | gold | Legend Level 40 |

Each branch is a **4-tier gated ladder**:

```
Tier 1 (nodes 1-6)   — no gate
Tier 2 (nodes 7-12)  — requires 6 points spent in this branch
Tier 3 (nodes 13-17) — requires 14 points spent in this branch
Tier 4 (nodes 18-20) — requires 24 points spent in this branch  [Keystones]
```

The gating means a player cannot cherry-pick the three keystones. They must commit. **Specialisation is the point.**

---

## 2. Talent Points

| Source | Amount |
|---|---|
| Legend Level up | +1 per level-up (199 total at the level 200 cap) |
| Chapter first clear (Normal) | +2 each (16 total) |
| Chapter first clear (Heroic) | +3 each (24 total) |
| Chapter first clear (Mythic) | +5 each (40 total) |
| PvP season rank rewards | +1 to +4 per season |
| Codex milestones | +10 total |
| **Renown milestones** (`28` Part D) | **+30 total** — +2 per 1,000 Renown, plus +6 at full Feat completion |

**Maximum obtainable in v1: ~324 points** (199 from levels + 80 from first clears + ~5 from seasons + 10 from Codex + 30 from Renown) against a tree that costs **633 points to fully max**. The tree is deliberately never completable in v1 — a maxed player has bought less than half of it. This preserves meaningful choice and gives the post-launch level-cap raise somewhere to go.

### 2.1 Respec

- **Free, unlimited, instant.** 🔒
- There is no respec cost in currency, no cooldown, no ad.
- Rationale: respec fees are a monetisation artefact. Without money in the game they serve only to punish experimentation, which is the opposite of what we want.
- Because respec is free, **talent presets are a core free feature: 3 saved preset slots from the start.** There is no ad placement attached to presets.

🔒 **3 free preset slots** covers the three loadouts a player actually maintains: a PvE push build, a farm build, and a PvP build. Slay Plus grants unlimited slots as a convenience perk (`12` §2). Presets store talents **and** gear, pets and mount together, so one tap fully reconfigures the character.

📐 If playtest telemetry shows median preset usage hitting the cap, raise the free allowance. Never gate it further.

---

### 2.2 Node ID convention

```
{BRANCH}_{NODE_NAME}     branch prefix: MT (Might) · WD (Ward) · FT (Fortune)

Examples:  MT_WHETSTONE · MT_PRECISION · WD_CONSTITUTION · FT_COINPURSE
Keystones: MT_KS_BLOODMOON · WD_KS_UNYIELDING · FT_KS_SIXTH_STAR
```

All 60 node IDs follow this pattern. They appear in save data, PvP ghost snapshots (`11` §2) and the effect DSL.

---

## 3. Node costs and scaling

Each node has **5 ranks**. Cost per rank rises within the node:

```
RankCost = [1, 2, 2, 3, 3]     // explicit table, 11 points to max a five-rank node
Keystone nodes: single rank, cost 8 points, no ranks.
```

Per-branch totals: 17 five-rank nodes (17 × 11 = 187) + 3 keystones (3 × 8 = 24) = **211 points to max one branch**.
Whole tree: 3 × 211 = **633 points to max everything**.

---

## 4. MIGHT branch (20 nodes)

### Tier 1
| Node | Per rank (×5) |
|---|---|
| Whetstone | +2% ATK |
| Precision | +1% Crit Chance |
| Cruelty | +5% Crit Damage |
| Swiftness | +1.5% Attack Speed |
| Piercer | +1.5% Armor Penetration |
| Aggression | +1.5% damage dealt |

### Tier 2 (needs 6 in branch)
| Node | Per rank (×5) |
|---|---|
| Bloodthirst | +1.2% Lifesteal |
| Executioner's Edge | +3% damage to enemies below 35% HP |
| Giantbane | +3% damage to Elites and Bosses |
| Escalation | +1% ATK per battle won this run, cap +5% per rank |
| Kindling | +4% DoT damage |
| Opening Blow | +8% damage on the first attack of a battle |

### Tier 3 (needs 14 in branch)
| Node | Per rank (×5) |
|---|---|
| Overwhelm | +2% ATK, +1% Crit Chance |
| Savage Rhythm | Every 6th attack deals +40% damage (+8% per rank) |
| Bonecrusher | Crits apply `SUNDER` −3% DEF per rank |
| Warlust | +2% ATK per rank while above 80% HP |
| Relentless | −3% per rank to all pet ability cooldowns |

### Tier 4 — Keystones (needs 24 in branch, 8 pts each, pick freely)
| Keystone | Effect |
|---|---|
| **Bloodmoon Ascendant** | Lifesteal is doubled, but Max HP −20% |
| **Perfect Strike** | Crit Chance above the 75% cap converts to Crit Damage at 1:4 |
| **Avatar of War** | ×1.20 multiplicative ATK; you can no longer be healed above 80% Max HP |

---

## 5. WARD branch (20 nodes)

### Tier 1
| Node | Per rank (×5) |
|---|---|
| Constitution | +3% Max HP |
| Plating | +3% DEF |
| Footwork | +1% Dodge |
| Guard | +1.5% Block |
| Resilience | +1% Damage Reduction |
| Mending | +6% Healing Received |

### Tier 2 (needs 6)
| Node | Per rank (×5) |
|---|---|
| Second Wind | Heal +2% Max HP after each battle |
| Barbs | +3% Thorns |
| Bulwark Training | +2% Max HP, +2% DEF |
| Steadfast | −4% duration of all debuffs on you |
| Warded Soul | +2% shield strength from all sources |
| Slow Burn | Heal 0.2%/rank Max HP per second in battle |

### Tier 3 (needs 14)
| Node | Per rank (×5) |
|---|---|
| Fortified | +3% DEF, +1% Damage Reduction |
| Grit | +4% Max HP; below 30% HP, +4% Damage Reduction |
| Counterpoise | Dodging or blocking grants +6% ATK for 3 s |
| Iron Will | Immune to `STUN` for the first 8 s of a battle (+2 s per rank) |
| Vital Reserve | +2% Max HP; overheal becomes a shield up to 4%/rank Max HP |

### Tier 4 — Keystones
| Keystone | Effect |
|---|---|
| **Unyielding** | Once per battle, survive lethal damage at 15% HP |
| **Aegis Eternal** | ×1.25 multiplicative Max HP; −15% ATK |
| **Reversal** | 30% of damage you take is dealt back as true damage to the attacker |

---

## 6. FORTUNE branch (20 nodes) — unlocked at Legend Level 40

This is the branch that makes the die *yours*. It is deliberately gated late so that the die's evolution is a mid-game reveal rather than a day-1 checkbox.

### Tier 1
| Node | Per rank (×5) |
|---|---|
| Coinpurse | +5% Gold |
| Prospector | +4% Crowns from all sources |
| Sharp Eyes | +2% gear drop chance |
| Forager | +5% Beast Feed |
| Stone Sense | +4% Enhance Stones |
| Vigor | +3% Energy regeneration rate |

### Tier 2 (needs 6)
| Node | Per rank (×5) |
|---|---|
| Steady Hand | +1 Reroll Charge at rank 3 and rank 5 (max +2) |
| Nudge | 1 per stage: adjust a Pip result by ±1 (+1 use per 2 ranks) |
| Haggler | −3% shop prices |
| Scavenger's Luck | +3% chance for Treasure tiles to double |
| Wanderer | +2% chance a `TILE_EMPTY` becomes a `TILE_TREASURE` |
| Cartography | Reveal +1 upcoming tile per rank |

### Tier 3 (needs 14)
| Node | Per rank (×5) |
|---|---|
| **Weighted Faces** | Your `1` face becomes a `2` at rank 1, a `3` at rank 3, a `4` at rank 5 |
| **Surging Fate** | Your `2` face becomes `Surge` at rank 3; `Surge` heals +3%/rank |
| Rich Veins | +4% rarity weight shift on all drops |
| Draft Insight | +6% chance an owned-perk upgrade appears in a draft |
| Lucky Streak | +2% Legendary perk weight |

### Tier 4 — Keystones
| Keystone | Effect |
|---|---|
| **The Sixth Star** | Your `6` face becomes a `Star` face permanently |
| **Chainweaver** | Your `4` face becomes a `Chain` face; `Chain` may chain up to 5 times |
| **Golden Fate** | Your `5` face becomes a `Fortune` face; `Fortune` also grants +1 free perk draft reroll |

**Note:** a player who takes all three Fortune keystones ends with a die of `[Weighted, Surge, 3, Chain, Fortune, Star]` — a fundamentally different game feel. That transformation is the branch's whole promise and should be showcased in marketing.

---

## 7. UI requirements

- Vertical scrolling tree per branch, tabs to switch branch. Portrait-friendly: nodes in a 3-column zigzag.
- Locked tiers are visible but greyed, with the requirement stated ("14 points in MIGHT").
- Every node shows current rank `2/5` and the **exact delta** the next rank gives, in real numbers, not percentages alone: *"+3% DEF (+42 DEF)"*.
- A persistent header shows total points, unspent points, and a **RESPEC** button (free, one confirm tap).
- A "Preview build" toggle shows the hero's resulting stat block before committing.

---

## 8. Balance guardrail

⚠️ **This table is now `TalentFactor(L)` in `29_POWER_MODEL.md` §5**, one of four factor curves that together define `ExpectedPower(L)`. It is subject to assertion **A13**: no single factor column may exceed 60% of the total power multiplier at any level. Talents currently sit well under that; **gear is the column at risk.**

Across the whole tree, a fully-invested player should gain roughly:

| Investment | Approx. power gain |
|---|---|
| 50 points | ×1.6 PlayerPower |
| 120 points | ×2.8 |
| 200 points | ×4.5 |
| 324 points (v1 max) | **×6.9** ⚠️ re-derive |

Combined with gear (up to ×20 across chapters) and pets/mounts (up to ×2.5), total attainable power growth from Legend Level 1 to v1 endgame is roughly **×320**, against the ×128 enemy power ramp across 8 chapters plus ×16 for Mythic. That leaves a comfortable but not trivial margin at the top end. 📐 Verify with the balance harness (`05` §9).

⚠️ The v1 maximum rose from 294 to 324 when Renown milestones were added (`28` D3.1). The ×6.9 figure is extrapolated, not derived — it is **assertion E22** in `21` §7a and must be recomputed rather than assumed. The +30 is deliberately small precisely so that this extrapolation stays safe; if the harness disagrees, cut the Renown Talent Point grant before touching the tree.
