using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Targeting;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Targeting;

/// <summary>
/// 🔒 `18` §5 — the eleven target tokens, each resolved relative to the effect's <b>holder</b>.
/// </summary>
public sealed class TargetResolverTests
{
    // ------------------------------------------------------------------ R10: holder-relative
    //
    // The ruling this whole task turns on, and the reason it is the first test in the file.

    /// <summary>
    /// 🔒 `18` §7.10 — the Volatile elite modifier, verbatim:
    /// <c>{"op":"DAMAGE_MAXHP_PCT","value":0.15,"valueMode":"TARGET_MAXHP_PCT",
    /// "trigger":{"kind":"ON_DEATH"},"target":"ALL_ENEMIES"}</c>, whose prose is
    /// <em>"explodes on death for 15% of <b>hero</b> Max HP"</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 The effect sits on an <b>enemy</b> actor and its authored target is <c>ALL_ENEMIES</c>. It
    /// only does what its own prose says if <c>ALL_ENEMIES</c> means <em>the actors hostile to the
    /// holder</em>. `18` §5 never states this, and the whole game's boss, elite and summon content
    /// depends on it.
    /// </para>
    /// <para>
    /// ⚠️ <b>This test is written so that the naive reading fails it.</b> Under "enemies always means
    /// the hero's enemies", the exploding elite would hit <c>GRUNT_A</c> and <c>GRUNT_B</c> — and
    /// itself — and never the hero. The assertion below is exactly the pair of claims that separates
    /// the two readings: the hero <em>is</em> selected, and the other enemies are <em>not</em>.
    /// </para>
    /// </remarks>
    [Fact]
    public void ALL_ENEMIES_on_the_Volatile_elite_selects_the_hero_not_the_other_enemies()
    {
        var hero = EffectTestBattle.Hero();
        var volatileElite = EffectTestBattle.Enemy("ELITE_VOLATILE", 1) with { IsElite = true };
        var gruntA = EffectTestBattle.Enemy("GRUNT_A", 2);
        var gruntB = EffectTestBattle.Enemy("GRUNT_B", 3);

        var exploding = EffectTestBattle.Context(volatileElite, hero, volatileElite, gruntA, gruntB);

        var hit = TargetResolver.Resolve(EffectTarget.ALL_ENEMIES, exploding);

        hit.Select(a => a.Id).ShouldBe(["HERO"]);
        hit.ShouldNotContain(gruntA, "18 §7.10's Volatile explosion damages the HERO, not its own side");
        hit.ShouldNotContain(gruntB);
        hit.ShouldNotContain(volatileElite, "an actor is never its own enemy");
    }

    /// <summary>
    /// The other half of the same ruling: the identical token on the hero selects the enemy side.
    /// One direction alone would pass for an implementation that always answered "the hero".
    /// </summary>
    [Fact]
    public void ALL_ENEMIES_on_the_hero_selects_the_enemy_side()
    {
        var hero = EffectTestBattle.Hero();
        var gruntA = EffectTestBattle.Enemy("GRUNT_A", 1);
        var gruntB = EffectTestBattle.Enemy("GRUNT_B", 2);

        var hit = TargetResolver.Resolve(
            EffectTarget.ALL_ENEMIES,
            EffectTestBattle.Context(hero, hero, gruntA, gruntB));

        hit.Select(a => a.Id).ShouldBe(["GRUNT_A", "GRUNT_B"]);
    }

    /// <summary>
    /// 🔒 And the ruling covers <b>every</b> enemy token, not just <c>ALL_ENEMIES</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ Without this, an implementation that special-cased <c>ALL_ENEMIES</c> as holder-relative and
    /// computed the other four as <c>Side == BattleSide.ENEMY</c> would pass the whole suite — and
    /// Sporequeen's sporelings, Bog Air and every boss <c>LOWEST_HP_ENEMY</c> would target their own
    /// side. The single-actor tokens are asserted rather than the sets so the expected answer is one
    /// id in every row: the hero is the enemy side's only living non-pet actor here.
    /// </remarks>
    [Theory]
    [InlineData(EffectTarget.ALL_ENEMIES)]
    [InlineData(EffectTarget.OTHER_ENEMIES)]
    [InlineData(EffectTarget.LOWEST_HP_ENEMY)]
    [InlineData(EffectTarget.HIGHEST_HP_ENEMY)]
    [InlineData(EffectTarget.RANDOM_ENEMY)]
    public void Every_enemy_token_on_an_enemy_holder_selects_the_hero_side(EffectTarget token)
    {
        var hero = EffectTestBattle.Hero();
        var heroPet = EffectTestBattle.Pet("PET_STORMFANG", 1);
        var elite = EffectTestBattle.Enemy("ELITE_VOLATILE", 2) with { IsElite = true };
        var grunt = EffectTestBattle.Enemy("GRUNT_A", 3);

        var fromTheElite = EffectTestBattle.Context(elite, hero, heroPet, elite, grunt) with
        {
            Rng = EffectTestBattle.CombatRng(1),
        };

        TargetResolver.Resolve(token, fromTheElite).Select(a => a.Id).ShouldBe(["HERO"]);
    }

    // ------------------------------------------------------------------ the set tokens

    /// <summary>
    /// `05` §3.1 step 6 — <em>"An actor whose HP reaches 0 stops acting and being targetable at that
    /// moment"</em> — and `05` §3.2 — <em>"Pets cannot be targeted or killed."</em>
    /// </summary>
    [Fact]
    public void An_enemy_set_holds_neither_the_dead_nor_any_pet()
    {
        var hero = EffectTestBattle.Hero();
        var heroPet = EffectTestBattle.Pet("PET_STORMFANG", 1);
        var alive = EffectTestBattle.Enemy("GRUNT_ALIVE", 2);
        var dead = EffectTestBattle.Enemy("GRUNT_DEAD", 3, currentHp: 0) with { IsAlive = false };
        var enemyPet = EffectTestBattle.Pet("PET_ENEMY", 4, BattleSide.ENEMY);

        var hit = TargetResolver.Resolve(
            EffectTarget.ALL_ENEMIES,
            EffectTestBattle.Context(hero, hero, heroPet, alive, dead, enemyPet));

        hit.Select(a => a.Id).ShouldBe(["GRUNT_ALIVE"]);
    }

    /// <summary>🔒 Results come back in `05` §3.1's fixed actor-index order, not roster order.</summary>
    [Fact]
    public void An_enemy_set_is_returned_in_ascending_actor_index_order()
    {
        var hero = EffectTestBattle.Hero();
        var third = EffectTestBattle.Enemy("GRUNT_C", 3);
        var first = EffectTestBattle.Enemy("GRUNT_A", 1);
        var second = EffectTestBattle.Enemy("GRUNT_B", 2);

        // Handed in deliberately out of order: the resolver imposes 05 §3.1's order, it does not
        // inherit whatever order the caller happened to build the roster in.
        var hit = TargetResolver.Resolve(
            EffectTarget.ALL_ENEMIES,
            EffectTestBattle.Context(hero, hero, third, first, second));

        hit.Select(a => a.Id).ShouldBe(["GRUNT_A", "GRUNT_B", "GRUNT_C"]);
    }

    /// <summary>
    /// `18` §5 / §7.10 — <c>PK_CLEAVE</c>: <em>"all enemies except the attack's primary target — the
    /// splash no longer double-hits its primary."</em>
    /// </summary>
    [Fact]
    public void OTHER_ENEMIES_excludes_the_attacks_primary_target()
    {
        var hero = EffectTestBattle.Hero();
        var primary = EffectTestBattle.Enemy("GRUNT_PRIMARY", 1);
        var splashed = EffectTestBattle.Enemy("GRUNT_SPLASH", 2);

        var attacking = EffectTestBattle.Context(hero, hero, primary, splashed) with
        {
            CurrentTarget = primary,
        };

        TargetResolver.Resolve(EffectTarget.OTHER_ENEMIES, attacking)
            .Select(a => a.Id)
            .ShouldBe(["GRUNT_SPLASH"]);
    }

    /// <summary>
    /// 🔒 <c>OTHER_ENEMIES</c> excludes the primary by <b>actor index</b>, not by id — so a pack
    /// spawned from one archetype does not vanish from the splash.
    /// </summary>
    /// <remarks>
    /// ⚠️ `05` §6.4 spawns several units from one archetype draw, and nothing in `05` or `18`
    /// promises a per-battle-unique id — only `05` §3.1's <b>index</b> is authorised as the actor's
    /// unique position. A roster that minted ids from content ids would give three swarm units the
    /// same string, and an id-based exclusion would then drop <b>all three</b> from
    /// <c>PK_CLEAVE</c>'s splash instead of only the primary. This is the roster that catches it.
    /// </remarks>
    [Fact]
    public void OTHER_ENEMIES_excludes_only_the_primary_when_a_pack_shares_one_id()
    {
        var hero = EffectTestBattle.Hero();
        var primary = EffectTestBattle.Enemy("GRUNT_SWARM", 1);
        var litter = EffectTestBattle.Enemy("GRUNT_SWARM", 2);
        var runt = EffectTestBattle.Enemy("GRUNT_SWARM", 3);

        var attacking = EffectTestBattle.Context(hero, hero, primary, litter, runt) with
        {
            CurrentTarget = primary,
        };

        TargetResolver.Resolve(EffectTarget.OTHER_ENEMIES, attacking)
            .Select(a => a.Index)
            .ShouldBe([2, 3]);
    }

    /// <summary>
    /// `18` §5 — <em>"Valid only inside an attack context; elsewhere it degrades to
    /// <c>ALL_ENEMIES</c>."</em> One of the two degradations the document actually authors.
    /// </summary>
    [Fact]
    public void OTHER_ENEMIES_degrades_to_ALL_ENEMIES_outside_an_attack_context()
    {
        var hero = EffectTestBattle.Hero();
        var first = EffectTestBattle.Enemy("GRUNT_A", 1);
        var second = EffectTestBattle.Enemy("GRUNT_B", 2);

        var outsideAnAttack = EffectTestBattle.Context(hero, hero, first, second);
        outsideAnAttack.CurrentTarget.ShouldBeNull("no primary target is what 'outside an attack' means");

        TargetResolver.Resolve(EffectTarget.OTHER_ENEMIES, outsideAnAttack)
            .Select(a => a.Id)
            .ShouldBe(["GRUNT_A", "GRUNT_B"]);
    }

    /// <summary>
    /// `05` §3.2 — <em>"a pet's targeted ability selects the enemy with the highest current HP"</em>.
    /// </summary>
    [Fact]
    public void HIGHEST_and_LOWEST_HP_ENEMY_pick_by_current_HP_not_by_fraction()
    {
        var hero = EffectTestBattle.Hero();

        // The tank has the most HP left and the SMALLEST remaining fraction; the runt the least HP
        // and the LARGEST. A resolver comparing fractions would answer these two exactly backwards.
        var tank = EffectTestBattle.Enemy("GRUNT_TANK", 1, currentHp: 400, maxHp: 2000);
        var runt = EffectTestBattle.Enemy("GRUNT_RUNT", 2, currentHp: 40, maxHp: 40);

        var battle = EffectTestBattle.Context(hero, hero, tank, runt);

        TargetResolver.Resolve(EffectTarget.HIGHEST_HP_ENEMY, battle)
            .Select(a => a.Id).ShouldBe(["GRUNT_TANK"]);

        TargetResolver.Resolve(EffectTarget.LOWEST_HP_ENEMY, battle)
            .Select(a => a.Id).ShouldBe(["GRUNT_RUNT"]);
    }

    /// <summary>
    /// 🔒 `18` §5 authors no tie-break, and a tie-break that varied would be a `14` §8.2 determinism
    /// break. Ties fall to `05` §3.1's fixed actor index — the one actor ordering the documents
    /// authorise (steering S6: reused, not invented).
    /// </summary>
    [Fact]
    public void An_HP_tie_breaks_by_ascending_actor_index_for_both_directions()
    {
        var hero = EffectTestBattle.Hero();
        var later = EffectTestBattle.Enemy("GRUNT_LATER", 5, currentHp: 50);
        var earlier = EffectTestBattle.Enemy("GRUNT_EARLIER", 2, currentHp: 50);

        // Handed in later-first so an implementation that simply takes "the first one found" is
        // distinguishable from one that applies the index rule.
        var tied = EffectTestBattle.Context(hero, hero, later, earlier);

        TargetResolver.Resolve(EffectTarget.LOWEST_HP_ENEMY, tied)
            .Select(a => a.Id).ShouldBe(["GRUNT_EARLIER"]);

        TargetResolver.Resolve(EffectTarget.HIGHEST_HP_ENEMY, tied)
            .Select(a => a.Id).ShouldBe(["GRUNT_EARLIER"]);
    }

    /// <summary>An HP-ordered token over no living enemy is an empty set, not a failure.</summary>
    [Theory]
    [InlineData(EffectTarget.LOWEST_HP_ENEMY)]
    [InlineData(EffectTarget.HIGHEST_HP_ENEMY)]
    [InlineData(EffectTarget.ALL_ENEMIES)]
    [InlineData(EffectTarget.OTHER_ENEMIES)]
    public void An_enemy_token_over_no_living_enemy_is_the_empty_set(EffectTarget target)
    {
        var hero = EffectTestBattle.Hero();
        var dead = EffectTestBattle.Enemy("GRUNT_DEAD", 1, currentHp: 0) with { IsAlive = false };

        TargetResolver.Resolve(target, EffectTestBattle.Context(hero, hero, dead)).ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ RANDOM_ENEMY

    /// <summary>
    /// 🔒 The draw protocol, pinned exactly: <c>RANDOM_ENEMY</c> is
    /// <c>candidates[rng.Range(0, candidates.Count)]</c> over the living enemies in `05` §3.1 index
    /// order, consuming exactly one draw.
    /// </summary>
    /// <remarks>
    /// Asserted against an <b>independently constructed</b> stream at the same battle seed rather
    /// than against a hard-coded index, so the test states the protocol rather than a magic number,
    /// and cannot be satisfied by a resolver that draws a different number of times or walks the
    /// candidates in a different order.
    /// </remarks>
    [Theory]
    [InlineData(1UL)]
    [InlineData(42UL)]
    [InlineData(9_007_199_254_740_993UL)]
    public void RANDOM_ENEMY_is_the_candidate_at_one_Range_draw_of_the_combat_stream(ulong battleSeed)
    {
        var hero = EffectTestBattle.Hero();
        var a = EffectTestBattle.Enemy("GRUNT_A", 1);
        var b = EffectTestBattle.Enemy("GRUNT_B", 2);
        var c = EffectTestBattle.Enemy("GRUNT_C", 3);

        var rng = EffectTestBattle.CombatRng(battleSeed);
        var battle = EffectTestBattle.Context(hero, hero, a, b, c) with { Rng = rng };

        var picked = TargetResolver.Resolve(EffectTarget.RANDOM_ENEMY, battle);

        var independent = EffectTestBattle.CombatRng(battleSeed);
        var expected = new[] { a, b, c }[independent.Range(0, 3)];

        picked.Select(x => x.Id).ShouldBe([expected.Id]);
        rng.Position.ShouldBe(1UL, "every RANDOM_ENEMY resolution consumes exactly one draw index");
    }

    /// <summary>
    /// It genuinely draws. A resolver that returned the first candidate would satisfy the protocol
    /// test above only by accident of one seed, and would fail this one outright.
    /// </summary>
    /// <remarks>
    /// ⚠️ Kept although the three seeds above happen to reach all three candidates today (they draw
    /// indices 2, 0 and 1). That is a property of those three literals, not of the rule: change one
    /// seed for an unrelated reason and the coverage silently collapses to a single candidate, with
    /// nothing going red. This is what stops that.
    /// </remarks>
    [Fact]
    public void RANDOM_ENEMY_reaches_every_candidate_across_seeds()
    {
        var hero = EffectTestBattle.Hero();
        var a = EffectTestBattle.Enemy("GRUNT_A", 1);
        var b = EffectTestBattle.Enemy("GRUNT_B", 2);
        var c = EffectTestBattle.Enemy("GRUNT_C", 3);

        var reached = new HashSet<string>(StringComparer.Ordinal);

        for (ulong seed = 0; seed < 200; seed++)
        {
            var battle = EffectTestBattle.Context(hero, hero, a, b, c) with
            {
                Rng = EffectTestBattle.CombatRng(seed),
            };

            reached.Add(TargetResolver.Resolve(EffectTarget.RANDOM_ENEMY, battle).Single().Id);
        }

        // Ordered before comparing: Shouldly's ShouldBe compares a HashSet as a SEQUENCE, in
        // enumeration order, so the assertion would otherwise depend on the order the seeds happened
        // to reach the three candidates in.
        reached.OrderBy(id => id, StringComparer.Ordinal).ShouldBe(["GRUNT_A", "GRUNT_B", "GRUNT_C"]);
    }

    /// <summary>An empty candidate set is empty, and — 🔒 — takes no draw with it.</summary>
    [Fact]
    public void RANDOM_ENEMY_over_no_living_enemy_is_empty_and_consumes_no_draw()
    {
        var hero = EffectTestBattle.Hero();
        var dead = EffectTestBattle.Enemy("GRUNT_DEAD", 1, currentHp: 0) with { IsAlive = false };
        var rng = EffectTestBattle.CombatRng(7);

        var battle = EffectTestBattle.Context(hero, hero, dead) with { Rng = rng };

        TargetResolver.Resolve(EffectTarget.RANDOM_ENEMY, battle).ShouldBeEmpty();
        rng.Position.ShouldBe(
            0UL,
            "DeterministicRng's contract is that a rejected call is not a call — a spent draw here " +
            "would shift every later draw of the battle");
    }

    // ------------------------------------------------------------------ the single-actor tokens

    /// <summary>`18` §5 — <c>SELF</c> is the holder, whichever side the holder is on.</summary>
    [Fact]
    public void SELF_is_the_holder()
    {
        var hero = EffectTestBattle.Hero();
        var boss = EffectTestBattle.Enemy("BOSS_THORNMAW", 1) with { IsBoss = true };

        TargetResolver.Resolve(EffectTarget.SELF, EffectTestBattle.Context(hero, hero, boss))
            .Select(a => a.Id).ShouldBe(["HERO"]);

        TargetResolver.Resolve(EffectTarget.SELF, EffectTestBattle.Context(boss, hero, boss))
            .Select(a => a.Id).ShouldBe(["BOSS_THORNMAW"]);
    }

    /// <summary>
    /// 🔒 <c>SELF</c> still resolves on an actor at 0 HP: `05` §3.1 step 6 fires <c>ON_DEATH</c>
    /// before removal, and `18` §7.10's Volatile explosion is that effect.
    /// </summary>
    [Fact]
    public void SELF_resolves_on_a_dying_holder()
    {
        var hero = EffectTestBattle.Hero();
        var dying = EffectTestBattle.Enemy("ELITE_VOLATILE", 1, currentHp: 0) with { IsAlive = false };

        TargetResolver.Resolve(EffectTarget.SELF, EffectTestBattle.Context(dying, hero, dying))
            .Select(a => a.Id).ShouldBe(["ELITE_VOLATILE"]);
    }

    /// <summary>`18` §7.3 — <c>PK_FLURRY</c>'s extra attack lands on the current target.</summary>
    [Fact]
    public void CURRENT_TARGET_is_the_holders_target()
    {
        var hero = EffectTestBattle.Hero();
        var primary = EffectTestBattle.Enemy("GRUNT_PRIMARY", 1);
        var other = EffectTestBattle.Enemy("GRUNT_OTHER", 2);

        var attacking = EffectTestBattle.Context(hero, hero, primary, other) with
        {
            CurrentTarget = primary,
        };

        TargetResolver.Resolve(EffectTarget.CURRENT_TARGET, attacking)
            .Select(a => a.Id).ShouldBe(["GRUNT_PRIMARY"]);
    }

    /// <summary>`18` §7.10 — <c>PK_STALWART</c> reacts to whoever hit the holder.</summary>
    [Fact]
    public void ATTACKER_is_the_actor_that_dealt_the_hit()
    {
        var hero = EffectTestBattle.Hero();
        var hitter = EffectTestBattle.Enemy("ELITE_HITTER", 1) with { IsElite = true };
        var bystander = EffectTestBattle.Enemy("GRUNT_BYSTANDER", 2);

        var reacting = EffectTestBattle.Context(hero, hero, hitter, bystander) with
        {
            Attacker = hitter,
        };

        TargetResolver.Resolve(EffectTarget.ATTACKER, reacting)
            .Select(a => a.Id).ShouldBe(["ELITE_HITTER"]);
    }

    /// <summary>`18` §5 — <em>"a sporeling's owner is Sporequeen"</em>.</summary>
    [Fact]
    public void OWNER_is_the_summoner_of_the_holder()
    {
        var hero = EffectTestBattle.Hero();
        var sporequeen = EffectTestBattle.Enemy("BOSS_SPOREQUEEN", 1) with { IsBoss = true };
        var sporeling = EffectTestBattle.Enemy("SUMMON_SPORELING", 2) with
        {
            IsSummon = true,
            OwnerId = "BOSS_SPOREQUEEN",
        };

        TargetResolver.Resolve(
                EffectTarget.OWNER,
                EffectTestBattle.Context(sporeling, hero, sporequeen, sporeling))
            .Select(a => a.Id).ShouldBe(["BOSS_SPOREQUEEN"]);
    }

    /// <summary>
    /// `18` §5 — <em>"On an actor that is not a summon, the effect is skipped."</em> The second of
    /// the document's two authored degradations, and — deliberately — a different failure mode from
    /// <c>OTHER_ENEMIES</c>'s.
    /// </summary>
    [Fact]
    public void OWNER_on_an_actor_that_is_not_a_summon_is_skipped()
    {
        var hero = EffectTestBattle.Hero();
        var grunt = EffectTestBattle.Enemy("GRUNT_A", 1);

        grunt.IsSummon.ShouldBeFalse();

        TargetResolver.Resolve(EffectTarget.OWNER, EffectTestBattle.Context(grunt, hero, grunt))
            .ShouldBeEmpty();
    }

    /// <summary>
    /// A summon whose owner has already left the roster is also skipped — the same empty set, not a
    /// failure. Sporequeen dying does not make every sporeling's effect a crash.
    /// </summary>
    [Fact]
    public void OWNER_of_a_summon_whose_summoner_is_gone_is_skipped()
    {
        var hero = EffectTestBattle.Hero();
        var orphan = EffectTestBattle.Enemy("SUMMON_SPORELING", 1) with
        {
            IsSummon = true,
            OwnerId = "BOSS_SPOREQUEEN",
        };

        TargetResolver.Resolve(EffectTarget.OWNER, EffectTestBattle.Context(orphan, hero, orphan))
            .ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ ALL_PETS

    /// <summary>
    /// <c>ALL_PETS</c> is the holder's side's pets — `PK_PACK_LEADER` buffs its own pets, never the
    /// opposing hero's (`05` §3.3 puts pets on both sides of a duel).
    /// </summary>
    [Fact]
    public void ALL_PETS_is_the_holders_own_side()
    {
        var duel = EffectTestBattle.Duel();

        TargetResolver.Resolve(EffectTarget.ALL_PETS, duel)
            .Select(a => a.Id).ShouldBe(["PET_ATTACKER"]);

        var defender = duel.Actors.Single(a => a.Id == "HERO_DEFENDER");

        TargetResolver.Resolve(EffectTarget.ALL_PETS, duel with { Holder = defender })
            .Select(a => a.Id).ShouldBe(["PET_DEFENDER"]);
    }

    /// <summary>A hero with no pets equipped is an empty set — an honest reading, not a failure.</summary>
    [Fact]
    public void ALL_PETS_on_a_hero_with_no_pets_is_the_empty_set()
    {
        var hero = EffectTestBattle.Hero();
        var grunt = EffectTestBattle.Enemy("GRUNT_A", 1);

        TargetResolver.Resolve(EffectTarget.ALL_PETS, EffectTestBattle.Context(hero, hero, grunt))
            .ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ selection vs. naming

    /// <summary>
    /// 🔒 The living-only filter of `05` §3.1 step 6 governs <b>selection</b>, not <b>naming</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// `05` §3.1: <em>"An actor whose HP reaches 0 stops acting and being <b>targetable</b> at that
    /// moment"</em> — a rule about who may be picked out of a set. <c>SELF</c>, <c>CURRENT_TARGET</c>,
    /// <c>ATTACKER</c> and <c>OWNER</c> pick nothing: each names one actor the caller already has.
    /// Filtering them would break the cases that matter most — an <c>ON_DEATH</c> effect targeting
    /// <c>SELF</c> (`18` §7.10's Volatile), and thorns or an <c>ON_HIT_TAKEN</c> reaction against an
    /// attacker that died in the same tick.
    /// </para>
    /// <para>
    /// So the rule is: the five set tokens filter by liveness; the four naming tokens do not.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_naming_tokens_still_resolve_a_subject_that_has_died()
    {
        var hero = EffectTestBattle.Hero();
        var deadElite = EffectTestBattle.Enemy("ELITE_HITTER", 1, currentHp: 0) with
        {
            IsAlive = false,
            IsElite = true,
        };
        var deadQueen = EffectTestBattle.Enemy("BOSS_SPOREQUEEN", 2, currentHp: 0) with
        {
            IsAlive = false,
            IsBoss = true,
        };
        var sporeling = EffectTestBattle.Enemy("SUMMON_SPORELING", 3) with
        {
            IsSummon = true,
            OwnerId = "BOSS_SPOREQUEEN",
        };

        var reacting = EffectTestBattle.Context(sporeling, hero, deadElite, deadQueen, sporeling) with
        {
            CurrentTarget = deadElite,
            Attacker = deadElite,
        };

        TargetResolver.Resolve(EffectTarget.CURRENT_TARGET, reacting)
            .Select(a => a.Id).ShouldBe(["ELITE_HITTER"]);
        TargetResolver.Resolve(EffectTarget.ATTACKER, reacting)
            .Select(a => a.Id).ShouldBe(["ELITE_HITTER"]);
        TargetResolver.Resolve(EffectTarget.OWNER, reacting)
            .Select(a => a.Id).ShouldBe(["BOSS_SPOREQUEEN"]);
    }

    /// <summary>
    /// <c>ALL_PETS</c> is a set token, so it filters by liveness with the rest of them.
    /// </summary>
    /// <remarks>
    /// `05` §3.2 makes pets unkillable, so this is unreachable through the game — which is exactly
    /// why it is pinned rather than left to whichever branch happens to be written. A set token that
    /// filtered inconsistently would be a rule with two spellings.
    /// </remarks>
    [Fact]
    public void ALL_PETS_filters_by_liveness_like_every_other_set_token()
    {
        var hero = EffectTestBattle.Hero();
        var living = EffectTestBattle.Pet("PET_STORMFANG", 1);
        var gone = EffectTestBattle.Pet("PET_GONE", 2) with { IsAlive = false };
        var grunt = EffectTestBattle.Enemy("GRUNT_A", 3);

        TargetResolver.Resolve(
                EffectTarget.ALL_PETS,
                EffectTestBattle.Context(hero, hero, living, gone, grunt))
            .Select(a => a.Id).ShouldBe(["PET_STORMFANG"]);
    }
}
