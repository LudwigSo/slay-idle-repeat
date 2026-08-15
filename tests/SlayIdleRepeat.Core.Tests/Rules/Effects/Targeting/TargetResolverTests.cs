using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Targeting;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Targeting;

/// <summary>The eleven target tokens, each resolved relative to the effect's holder.</summary>
public sealed class TargetResolverTests
{
    // ------------------------------------------------------------------ holder-relative
    //
    // The ruling this whole task turns on, and the reason it is the first test in the file.

    /// <summary>
    /// The Volatile elite: an <c>ON_DEATH</c> <c>DAMAGE_MAXHP_PCT</c> targeting <c>ALL_ENEMIES</c>,
    /// whose prose is "explodes on death for 15% of hero Max HP". The effect sits on an enemy actor,
    /// so it only does what its own prose says if <c>ALL_ENEMIES</c> means the actors hostile to the
    /// holder — nowhere stated directly, and every boss, elite and summon in the game depends on it.
    /// Written so the naive reading fails: under "enemies always means the hero's enemies" the
    /// exploding elite would hit the grunts and itself and never the hero.
    /// </summary>
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
    /// …and the ruling covers every enemy token, not just <c>ALL_ENEMIES</c>. Without this,
    /// special-casing <c>ALL_ENEMIES</c> as holder-relative and computing the other four as
    /// <c>Side == BattleSide.ENEMY</c> passes the whole suite — and Sporequeen's sporelings and every
    /// boss <c>LOWEST_HP_ENEMY</c> would target their own side.
    /// </summary>
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

    /// <summary>An actor whose HP reaches 0 stops being targetable at that moment, and pets cannot be targeted or killed.</summary>
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

    /// <summary>Results come back in fixed actor-index order, not roster order.</summary>
    [Fact]
    public void An_enemy_set_is_returned_in_ascending_actor_index_order()
    {
        var hero = EffectTestBattle.Hero();
        var third = EffectTestBattle.Enemy("GRUNT_C", 3);
        var first = EffectTestBattle.Enemy("GRUNT_A", 1);
        var second = EffectTestBattle.Enemy("GRUNT_B", 2);

        // Handed in deliberately out of order: the resolver imposes its own fixed order, it does not
        // inherit whatever order the caller happened to build the roster in.
        var hit = TargetResolver.Resolve(
            EffectTarget.ALL_ENEMIES,
            EffectTestBattle.Context(hero, hero, third, first, second));

        hit.Select(a => a.Id).ShouldBe(["GRUNT_A", "GRUNT_B", "GRUNT_C"]);
    }

    /// <summary><c>PK_CLEAVE</c>: all enemies except the attack's primary target — the splash no longer double-hits its primary.</summary>
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
    /// <c>OTHER_ENEMIES</c> excludes the primary by actor index, not by id — so a pack spawned from
    /// one archetype does not vanish from the splash. Several units can share an archetype's id, and
    /// only the index is unique; an id-based exclusion would drop all three swarm units from
    /// <c>PK_CLEAVE</c>'s splash instead of only the primary.
    /// </summary>
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
    /// Valid only inside an attack context; elsewhere it degrades to <c>ALL_ENEMIES</c>. One of the
    /// two authored degradations.
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

    /// <summary>A pet's targeted ability selects the enemy with the highest current HP.</summary>
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
    /// No tie-break is authored, and one that varied would be a determinism break. Ties fall to the
    /// fixed actor index — the one actor ordering already established elsewhere, reused rather than
    /// invented.
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
    /// The draw protocol exactly: <c>candidates[rng.Range(0, candidates.Count)]</c> over the living
    /// enemies in index order, consuming exactly one draw. Asserted against an independently
    /// constructed stream at the same battle seed rather than a hard-coded index, so it states the
    /// protocol and cannot be satisfied by a resolver that draws a different number of times or walks
    /// the candidates differently.
    /// </summary>
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
    /// It genuinely draws — a resolver returning the first candidate satisfies the protocol test above
    /// by accident of one seed and fails this outright. Kept although the three seeds happen to reach
    /// all three candidates today: that is a property of those literals, not of the rule. Change one
    /// seed for an unrelated reason and the coverage silently collapses to a single candidate.
    /// </summary>
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

    /// <summary>An empty candidate set is empty, and takes no draw with it.</summary>
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

    /// <summary><c>SELF</c> is the holder, whichever side the holder is on.</summary>
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
    /// <c>SELF</c> still resolves on an actor at 0 HP: <c>ON_DEATH</c> fires before removal, and the
    /// Volatile explosion is that effect.
    /// </summary>
    [Fact]
    public void SELF_resolves_on_a_dying_holder()
    {
        var hero = EffectTestBattle.Hero();
        var dying = EffectTestBattle.Enemy("ELITE_VOLATILE", 1, currentHp: 0) with { IsAlive = false };

        TargetResolver.Resolve(EffectTarget.SELF, EffectTestBattle.Context(dying, hero, dying))
            .Select(a => a.Id).ShouldBe(["ELITE_VOLATILE"]);
    }

    /// <summary><c>PK_FLURRY</c>'s extra attack lands on the current target.</summary>
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

    // ------------------------------------------------------------------ CURRENT_TARGET on an enemy holder

    /// <summary>
    /// A boss <c>PERIODIC</c> carries no attack context, so <c>CURRENT_TARGET</c> used to throw for
    /// every faulting boss effect. "Enemies always target the Hero" makes an <c>ENEMY</c> holder's
    /// <c>CURRENT_TARGET</c> unambiguous in or out of an attack context.
    /// </summary>
    [Fact]
    public void CURRENT_TARGET_on_an_enemy_holder_with_no_attack_in_flight_resolves_to_the_hero()
    {
        var hero = EffectTestBattle.Hero();
        var boss = EffectTestBattle.Enemy("BOSS_RIMEHOLD", 1) with { IsBoss = true };

        var periodic = EffectTestBattle.Context(boss, hero, boss);
        periodic.CurrentTarget.ShouldBeNull("a PERIODIC trigger carries no attack context to begin with");

        TargetResolver.Resolve(EffectTarget.CURRENT_TARGET, periodic)
            .Select(a => a.Id).ShouldBe(["HERO"]);
    }

    /// <summary>
    /// The second shape: a hero pet and another enemy are on the roster too, so the resolution has to
    /// be picking the hero specifically — not merely "the only other actor", and not the holder's own
    /// side.
    /// </summary>
    [Fact]
    public void CURRENT_TARGET_on_an_enemy_holder_picks_the_hero_over_its_own_pet_and_side()
    {
        var hero = EffectTestBattle.Hero();
        var heroPet = EffectTestBattle.Pet("PET_STORMFANG", 1);
        var boss = EffectTestBattle.Enemy("BOSS_COGITATOR_PRIME", 2) with { IsBoss = true };
        var add = EffectTestBattle.Enemy("WARDEN_ADD", 3);

        var periodic = EffectTestBattle.Context(boss, hero, heroPet, boss, add);

        TargetResolver.Resolve(EffectTarget.CURRENT_TARGET, periodic)
            .Select(a => a.Id).ShouldBe(["HERO"]);
    }

    /// <summary>
    /// <c>STAT_COPY</c>'s dependency: Cogitator's Recalibrate names <c>CURRENT_TARGET</c> as its copy
    /// SOURCE, and the op reads exactly one actor. The naming-token contract — one actor, not a set —
    /// must hold for the enemy-holder fallback exactly as it does for the ordinary case.
    /// </summary>
    [Fact]
    public void CURRENT_TARGET_on_an_enemy_holder_resolves_to_exactly_one_actor()
    {
        var hero = EffectTestBattle.Hero();
        var boss = EffectTestBattle.Enemy("BOSS_COGITATOR_PRIME", 1) with { IsBoss = true };

        var resolved = TargetResolver.Resolve(
            EffectTarget.CURRENT_TARGET, EffectTestBattle.Context(boss, hero, boss));

        resolved.Count.ShouldBe(1, "STAT_COPY's copy source must be one actor, never a set");
    }

    /// <summary>
    /// The negative control the fix must NOT touch: a <c>HERO</c> holder's <c>CURRENT_TARGET</c>
    /// outside an attack context is genuinely ambiguous (target-priority machinery can pick a
    /// different living enemy from one basic attack to the next), so it still throws.
    /// </summary>
    [Fact]
    public void CURRENT_TARGET_on_a_HERO_holder_with_no_current_target_still_throws()
    {
        var hero = EffectTestBattle.Hero();
        var grunt = EffectTestBattle.Enemy("GRUNT_A", 1);

        var noAttack = EffectTestBattle.Context(hero, hero, grunt);
        noAttack.CurrentTarget.ShouldBeNull();

        var thrown = Should.Throw<EffectContextException>(
            () => TargetResolver.Resolve(EffectTarget.CURRENT_TARGET, noAttack));

        thrown.Token.ShouldBe(nameof(EffectTarget.CURRENT_TARGET));
    }

    /// <summary><c>PK_STALWART</c> reacts to whoever hit the holder.</summary>
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

    /// <summary>A sporeling's owner is Sporequeen.</summary>
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
    /// On an actor that is not a summon, the effect is skipped. The second of the two authored
    /// degradations, and — deliberately — a different failure mode from <c>OTHER_ENEMIES</c>'s.
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
    /// <c>ALL_PETS</c> is the holder's side's pets — <c>PK_PACK_LEADER</c> buffs its own pets, never
    /// the opposing hero's (a duel puts pets on both sides).
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
    /// The living-only filter governs selection, not naming: the five set tokens filter by liveness,
    /// the four naming tokens do not. <c>SELF</c>, <c>CURRENT_TARGET</c>, <c>ATTACKER</c> and
    /// <c>OWNER</c> pick nothing — each names an actor the caller already has. Filtering them breaks
    /// the cases that matter most: an <c>ON_DEATH</c> effect targeting <c>SELF</c>, and thorns or an
    /// <c>ON_HIT_TAKEN</c> reaction against an attacker that died in the same tick.
    /// </summary>
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
    /// <c>ALL_PETS</c> is a set token, so it filters by liveness with the rest. Pets are unkillable, so
    /// this is unreachable through the game — which is why it is pinned rather than left to whichever
    /// branch happens to be written. A set token that filtered inconsistently would be a rule with two
    /// spellings.
    /// </summary>
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
