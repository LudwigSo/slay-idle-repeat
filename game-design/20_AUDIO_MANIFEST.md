# 20 — Audio Direction & AI Generation Manifest

Resolves open item P1 #16. 🔒 **Audio is AI-generated**, mirroring the art pipeline in doc 15.

---

## 1. Audio direction

> Warm, chunky, slightly toy-like fantasy. Every sound is short, punchy and unmistakable at phone-speaker volume with the music playing under it. Nothing atmospheric, nothing subtle, nothing that requires headphones to read.

| Rule | Specification |
|---|---|
| **Palette** | Acoustic and orchestral-lite: plucked strings, hand percussion, marimba, soft brass, choir pads. Avoid synth leads and EDM — they fight the chibi art. |
| **Music mood** | Adventurous and light in early biomes, tenser and lower in later ones. Never dark or oppressive; this is a cosy grind, not a horror game. |
| **Loop length** | 90–150 s, seamlessly loopable |
| **SFX length** | 60–400 ms for combat and UI, up to 1.2 s for celebratory stingers |
| **Frequency discipline** | Combat SFX live in 200 Hz–6 kHz where phone speakers actually reproduce. Nothing important below 150 Hz. |
| **Loudness** | Music −18 LUFS integrated, SFX peaks at −6 dBFS. Music ducks 6 dB under the crit and level-up stingers. |
| **Silence** | The board has no music bed during the roll wind-up — 0.4 s of near-silence before the die lands makes the landing hit. |

**Reference vocabulary (direction only, never named in prompts):** the warmth of *Slay the Spire*'s map music, the punchiness of *Vampire Survivors*' pickups, the toy-orchestral feel of casual mobile RPG UI.

---

## 2. Generation method

| Asset class | Tool class | Notes |
|---|---|---|
| **Music loops** | Suno / Udio class text-to-music | Generate 3 candidates per track, pick one, edit to a seamless loop in Audacity/Reaper. Generation gives you ~2–4 min; you cut a clean loop from the best 100 s. |
| **SFX** | ElevenLabs Sound Effects or equivalent text-to-SFX | Generate 4–6 candidates per sound, pick, then normalise and trim. |
| **UI clicks / small hits** | Same, or a free CC0 library | These are the sounds AI generation is weakest at. Falling back to a CC0 UI pack for ~20 of them is acceptable and probably better. |

### 2.1 ⚠️ Licensing — check before generating anything

Confirm the commercial licence terms of whichever music and SFX tools are used, and keep provenance records (tool, version, prompt, date) for every generated file, exactly as for art (doc 15 §G). Music generation tools vary widely in what they permit for commercial game use, and some restrict it to paid tiers only. **This is a legal prerequisite, not a formality.**

### 2.2 Consistency workflow

1. Generate the **Home theme first** and iterate until it is right. It defines the instrument palette for everything else.
2. For each subsequent track, include the palette explicitly in the prompt (see §3) so the set holds together.
3. Generate music **in one session per batch**; tool behaviour drifts between sessions.
4. Master the whole set together at the end (same EQ curve, same limiter) — this does more for cohesion than prompt discipline.
5. For SFX, generate a family at a time (all impacts, then all pickups) so their character matches.

---

## 3. Music manifest (12 tracks)

Prompt scaffold:
```
{MOOD}, fantasy mobile game background music, light orchestral with
{INSTRUMENTS}, warm and melodic, medium tempo, loopable, no vocals,
no drums-heavy EDM, clean mix, cheerful chunky character,
suitable for a cute chibi adventure game
```

| ID | Use | Mood + instrument prompt | Length |
|---|---|---|---|
| `mus_home` | Home / Camp | *calm, cosy, evening campfire; acoustic guitar, soft marimba, light strings, gentle hand percussion* | 120 s |
| `mus_ch1_greenwood` | Chapter 1 board | *bright, adventurous, sunlit; plucked strings, flute, tambourine* | 110 s |
| `mus_ch2_mire` | Chapter 2 board | *murky, curious, slightly off-kilter; low woodwinds, muted marimba, sparse percussion* | 110 s |
| `mus_ch3_crypt` | Chapter 3 board | *hushed, echoing, gently eerie; choir pad, pizzicato strings, soft bells* | 110 s |
| `mus_ch4_ember` | Chapter 4 board | *urgent, hot, driving; low brass, taiko-style percussion, tense strings* | 110 s |
| `mus_ch5_frost` | Chapter 5 board | *crystalline, spacious, cold but pretty; glockenspiel, high strings, airy pad* | 110 s |
| `mus_ch6_clockwork` | Chapter 6 board | *mechanical, rhythmic, precise; pizzicato, woodblock, ticking percussion, brass stabs* | 110 s |
| `mus_ch7_bloom` | Chapter 7 board | *woozy, organic, slightly psychedelic; detuned marimba, breathy pad, soft choir* | 110 s |
| `mus_ch8_astral` | Chapter 8 board | *grand, weightless, awed; full strings, choir, celesta, distant brass* | 130 s |
| `mus_boss` | All bosses | *driving, heroic, high stakes; full orchestra, choir, heavy percussion* — with a 4-bar intro stinger | 100 s |
| `mus_boss_final` | The Dicelord only | *epic, cosmic, triumphant-then-ominous; full orchestra, big choir, timpani* | 130 s |
| `mus_arena` | Arena / PvP | *competitive, tight, confident; snare-forward percussion, brass, driving strings* | 100 s |

---

## 4. SFX manifest (94)

Prompt scaffold:
```
{DESCRIPTION}, short game sound effect, punchy and clean, cartoon fantasy
game audio, dry, no reverb tail, mono, no music
```

### 4.1 Dice (11)
`sfx_die_pickup` *wooden die lifted* · `sfx_die_tumble` *die tumbling on wood, 0.8 s* · `sfx_die_land` *die landing with a solid clack*

⚠️ **Three rows, not eleven.** ⚠️ Removed with the die's special faces and the reroll (`04` §5, `16` D41). The eight removed are the five per-face landings (`sfx_die_land_star` / `_surge` / `_fortune` / `_void` / `_chain`), `sfx_reroll`, `sfx_nudge` and `sfx_face_upgrade`. The register's SFX total moves 94 → 86 and its combined total 106 → 98.

### 4.2 Board & movement (12)
`sfx_hop` *light footstep hop on stone* · `sfx_hop_mount` *heavier hoof-step* · `sfx_stage_gate` *rising fanfare, 1.2 s* · `sfx_tile_reveal` *soft paper flip* · `sfx_fork_choose` *decisive wooden thunk* · `sfx_portal_enter` *swirling whoosh* · `sfx_shrine` *warm ascending chime* · `sfx_curse` *low dissonant sting with a whisper* · `sfx_campfire` *crackling fire, 1 s* · `sfx_treasure_open` *chest lid creak then sparkle burst* · `sfx_shop_enter` *small shopkeeper bell* · `sfx_cache_open` *wooden crate pop with a creature chirp*

### 4.3 Combat (26)
`sfx_battle_start` *short drum-and-brass sting* · `sfx_hit_light` · `sfx_hit_medium` · `sfx_hit_heavy` *escalating blunt impacts* · `sfx_hit_slash` · `sfx_hit_pierce` · `sfx_hit_magic` *whoosh with a crystalline pop* · `sfx_crit` *sharp impact with a metallic ring and a brief pitch rise* · `sfx_block` *shield clang* · `sfx_dodge` *quick cloth whoosh* · `sfx_miss` *soft air swipe* · `sfx_heal` *warm rising shimmer* · `sfx_shield_form` *glassy bubble forming* · `sfx_shield_break` *glass shattering, small* · `sfx_burn_loop` *soft crackling flame loop* · `sfx_poison_tick` *wet bubbling blip* · `sfx_bleed_tick` *short wet cut* · `sfx_freeze` *ice crystallising crackle* · `sfx_stun` *dizzy cartoon chime with birds* · `sfx_rage` *low growling swell* · `sfx_thorns` *sharp spiny click* · `sfx_enemy_death_small` *comic squeak-poof* · `sfx_enemy_death_large` *heavy collapse with a dust thud* · `sfx_boss_phase` *dramatic descending sting with a low brass hit* · `sfx_boss_telegraph` *rising warning tone, 1.2 s* · `sfx_victory` *triumphant 1.5 s fanfare*

### 4.4 Pets & mounts (8)
`sfx_pet_ability_generic` *small magical pop* · `sfx_pet_zap` · `sfx_pet_heal` · `sfx_pet_roar` *tiny cute roar* · `sfx_pet_summon` *sparkle materialise* · `sfx_pet_levelup` *cheerful two-note chirp* · `sfx_mount_summon` *hoofbeats approaching, 0.8 s* · `sfx_mount_dash` *fast whoosh with hoofbeats*

### 4.5 Meta & progression (18)
`sfx_levelup` *big warm ascending fanfare with a choir hit, 1.2 s* · `sfx_talent_spend` *solid magical click with a stone grind* · `sfx_talent_keystone` *deep resonant unlock with a choir swell* · `sfx_equip` *cloth and leather shift* · `sfx_unequip` *reverse of above* · `sfx_merge_charge` *rising energy build, 0.9 s* · `sfx_merge_success` *bright crystalline burst with a metallic ring* · `sfx_enhance_success` *hammer strike on an anvil with a magical ping* · `sfx_enhance_fail` *dull hammer thud with a descending tone* · `sfx_salvage` *item crumbling into dust* · `sfx_gear_drop_common` *small metallic clink* · `sfx_gear_drop_rare` *clink with a soft chime* · `sfx_gear_drop_legendary` *clink with a bright rising arpeggio and a whoosh* · `sfx_currency_gold` *coin jingle* · `sfx_currency_crown` *warmer coin chime* · `sfx_currency_shard` *crystal ting* · `sfx_egg_hatch` *shell crack then a happy chirp* · `sfx_energy_full` *soft electric charge complete*

### 4.6 UI (19)
`sfx_ui_tap` *soft wooden click* · `sfx_ui_tap_primary` *fuller, more confident click* · `sfx_ui_back` *lower reverse click* · `sfx_ui_open_panel` *paper slide* · `sfx_ui_close_panel` *reverse paper slide* · `sfx_ui_tab` *light tick* · `sfx_ui_error` *soft dull buzz, never harsh* · `sfx_ui_confirm` *two-note affirmative* · `sfx_ui_toggle_on` / `sfx_ui_toggle_off` · `sfx_perk_card_in` *card whoosh* · `sfx_perk_select` *satisfying stamp with a magical ring* · `sfx_perk_upgrade` *stamp with an ascending flourish* · `sfx_quest_complete` *bright three-note chime* · `sfx_chest_reward` *sparkle cascade* · `sfx_wheel_spin_loop` *ratcheting click loop* · `sfx_wheel_stop` *final ratchet with a chime* · `sfx_notification` *gentle two-note ping* · `sfx_reconnect` *soft descending-then-rising pair, non-alarming*

**Total SFX: 94.** Combined with 12 music tracks: **106 audio assets.**

---

## 5. Technical spec

| Property | Value |
|---|---|
| Music format | OGG Vorbis, q6, 44.1 kHz stereo |
| SFX format | WAV 16-bit 44.1 kHz mono in source, converted to OGG q4 for shipping |
| Loop points | Music loops must be sample-accurate. Verify by looping 20× and listening for a click. |
| Naming | `mus_*` and `sfx_*` per the manifest, `snake_case` |
| Bus structure | Master → Music / SFX / UI, each with its own volume setting |
| Ducking | Music −6 dB for 0.5 s under `sfx_crit`, `sfx_levelup`, `sfx_boss_phase`, `sfx_merge_success` |
| Ad handling | **Full audio duck to silence before any ad, restore on return.** Mandatory — nothing is worse than game music fighting an ad. |
| Polyphony caps | Combat hits capped at 6 simultaneous voices with a 30 ms retrigger guard, or a fast build becomes a wall of noise |
| Memory | Music streamed, SFX fully loaded. Total audio budget < 40 MB. |

---

## 6. QA checklist

- [ ] Every loop is seamless at 20 repetitions
- [ ] Every SFX is audible over music at 50% device volume on a phone speaker
- [ ] No SFX exceeds −6 dBFS peak
- [ ] Nothing important sits below 150 Hz
- [ ] Combat at ×3 speed with 6 enemies does not clip or turn to mush
- [ ] Audio ducks fully before every ad and restores cleanly
- [ ] All three volume sliders behave independently and persist
- [ ] Provenance record exists for every generated file
- [ ] Commercial licence for each tool confirmed in writing

---

## 7. Generation order

1. `mus_home` — iterate until the palette is right; it defines everything else
2. Core combat SFX (§4.3) — these are heard thousands of times per session and matter most
3. Dice SFX (§4.1) — the signature sound of the game
4. UI SFX (§4.6) — unblocks all screen implementation
5. `mus_ch1_greenwood` + `mus_boss` — unblocks a fully playable vertical slice
6. Meta and progression SFX (§4.5)
7. Board, pets and mounts (§4.2, §4.4)
8. Remaining 7 biome tracks, in one session
9. `mus_boss_final`, `mus_arena`
10. Full-set mastering pass
