using System.Linq;
using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// <c>SURVIVE_LETHAL</c>, <c>REVIVE</c>, the <c>ON_LETHAL</c>/<c>ON_REVIVE</c> triggers, and the
/// anti-loop rules over them.
/// </summary>
/// <remarks>
/// Internal bench seam: each case needs an exactly-sized, ward-bypassing lethal blow at a chosen
/// instant — twice, for the once-bound — plus HP readings between blows; <c>SimulateDuel</c> can
/// deliver damage only on the swing schedule, through the full damage formula.
/// </remarks>
public sealed class DeathSaveTests
{
    private const double MaxHp = 1000.0;

    /// <summary>
    /// Two shapes, because <c>FLAT</c> and the default percentage reading disagree about the same
    /// number — running both stops the fix passing on a hard-wired 1.
    /// </summary>
    [Theory]
    [InlineData(1.0, ValueMode.FLAT, 1.0)]
    [InlineData(0.25, null, 250.0)]
    public void A_SURVIVE_LETHAL_leaves_the_actor_at_its_authored_HP(
        double value, ValueMode? mode, double expectedHp)
    {
        var probe = Fight(
            new[] { SurviveLethal("PK_UNBREAKABLE", value, mode) },
            p =>
            {
                p.Pipeline.DealMaxHpPctDamage(p.Hero, MaxHp * 10.0, bypassesWards: true, "EFF_LETHAL", source: null);

                p.Hero.CurrentHp.ShouldBe(expectedHp);
                p.Hero.IsAlive.ShouldBeTrue();
            });

        // The actor never died, so there is no death and no return to announce.
        probe.EventsOf(CombatEventType.ActorDeath).ShouldBeEmpty();

        // The Hit carries what actually came off, not the lethal amount — otherwise a Hit for 10 000
        // beside an actor standing at 1 HP would be a frame nothing can draw.
        probe.EventsOf(CombatEventType.Hit).Single().Value.ShouldBe(MaxHp - expectedHp);
    }

    /// <summary>
    /// Without this, a pipeline that simply refused to take an actor below 1 HP would pass the
    /// theory above.
    /// </summary>
    [Fact]
    public void Without_a_save_the_same_blow_kills()
    {
        Fight(
            holding: null,
            p =>
            {
                p.Pipeline.DealMaxHpPctDamage(p.Hero, MaxHp * 10.0, bypassesWards: true, "EFF_LETHAL", source: null);

                p.Hero.CurrentHp.ShouldBe(0.0);
                p.Hero.IsAlive.ShouldBeFalse();
            });
    }

    /// <summary>
    /// A <c>REVIVE</c> is consumed in <c>ResolveDeaths</c>, not the pipeline: it requires the actor
    /// to have reached 0, so it is read after <c>ON_DEATH</c>, and the actor is never logged as an
    /// <c>ActorDeath</c>, having come back before the body was removed.
    /// </summary>
    [Fact]
    public void A_REVIVE_returns_the_actor_from_0_HP_and_fires_ON_REVIVE()
    {
        var probe = Fight(
            new[]
            {
                Holding("PK_SECOND_WIND", EffectOp.REVIVE, 0.30, TriggerKind.ON_BATTLE_START),
                Holding("PK_Z_ON_REVIVE_PROBE", EffectOp.SHIELD, 7.0, TriggerKind.ON_REVIVE),
            },
            p =>
            {
                p.Pipeline.DealMaxHpPctDamage(p.Hero, MaxHp * 10.0, bypassesWards: true, "EFF_LETHAL", source: null);
            },
            maxTicks: 1);

        probe.Result.HeroHpRemaining.ShouldBe(300.0, "0.30 x 1000 Max HP");
        probe.EventsOf(CombatEventType.ActorDeath).ShouldBeEmpty(
            "the actor came back before the body was removed");
        probe.EventsOf(CombatEventType.Shield).Count.ShouldBe(
            1, "`18` §3 fires ON_REVIVE, which is what the second holding is listening for");
    }

    /// <summary>Same probe holding and blow as the case above — only the arming op differs.</summary>
    [Fact]
    public void A_SURVIVE_LETHAL_fires_no_ON_REVIVE()
    {
        var probe = Fight(
            new[]
            {
                SurviveLethal("PK_UNBREAKABLE", 1.0, ValueMode.FLAT),
                Holding("PK_Z_ON_REVIVE_PROBE", EffectOp.SHIELD, 7.0, TriggerKind.ON_REVIVE),
            },
            p => p.Pipeline.DealMaxHpPctDamage(
                p.Hero, MaxHp * 10.0, bypassesWards: true, "EFF_LETHAL", source: null),
            maxTicks: 1);

        probe.EventsOf(CombatEventType.Shield).ShouldBeEmpty();
    }

    /// <summary>
    /// <c>PK_UNBREAKABLE</c> armed <c>ON_LETHAL</c>, through the real attack pipeline rather than the
    /// <c>ON_BATTLE_START</c> workaround above.
    /// </summary>
    [Theory]
    [InlineData(1.0, ValueMode.FLAT, 1.0)]
    [InlineData(0.25, null, 250.0)]
    public void An_ON_LETHAL_armed_PK_UNBREAKABLE_survives_the_documented_shape(
        double value, ValueMode? mode, double expectedHp)
    {
        var probe = Fight(
            new[] { SurviveLethalOnLethal("PK_UNBREAKABLE", value, mode, once: true) },
            p =>
            {
                p.Pipeline.DealMaxHpPctDamage(p.Hero, MaxHp * 10.0, bypassesWards: true, "EFF_LETHAL", source: null);

                p.Hero.CurrentHp.ShouldBe(expectedHp);
                p.Hero.IsAlive.ShouldBeTrue();
            });

        probe.EventsOf(CombatEventType.ActorDeath).ShouldBeEmpty();
        probe.EventsOf(CombatEventType.Hit).Single().Value.ShouldBe(MaxHp - expectedHp);
    }

    [Fact]
    public void Once_bounds_an_ON_LETHAL_save_to_one_hit_and_the_next_lethal_hit_kills()
    {
        Fight(
            new[] { SurviveLethalOnLethal("PK_UNBREAKABLE", 1.0, ValueMode.FLAT, once: true) },
            p =>
            {
                p.Pipeline.DealMaxHpPctDamage(p.Hero, MaxHp * 10.0, bypassesWards: true, "EFF_LETHAL_1", source: null);

                p.Hero.CurrentHp.ShouldBe(1.0, "the first lethal hit is the one authored once-save");
                p.Hero.IsAlive.ShouldBeTrue();

                p.Pipeline.DealMaxHpPctDamage(p.Hero, MaxHp * 10.0, bypassesWards: true, "EFF_LETHAL_2", source: null);

                p.Hero.CurrentHp.ShouldBe(0.0, "once is spent — nothing saves the second lethal hit");
                p.Hero.IsAlive.ShouldBeFalse();
            });
    }

    /// <summary>
    /// 16 D49: <c>valueMode: NEGATE</c> voids the lethal hit — HP exactly unchanged, not "survive
    /// at an HP the effect names". The chip hit first puts the hero at 900, so an implementation
    /// that restored to Max HP, survived at 1, or survived at a fraction all read differently from
    /// the voided hit; it also proves a non-lethal hit neither consumes the save nor is itself
    /// negated.
    /// </summary>
    [Fact]
    public void A_NEGATE_save_voids_the_lethal_hit_and_leaves_HP_exactly_unchanged()
    {
        var probe = Fight(
            new[] { NegateLethal("SET_BONUS_HEAVY_6") },
            p =>
            {
                p.Pipeline.DealMaxHpPctDamage(p.Hero, 100.0, bypassesWards: true, "EFF_CHIP", source: null);

                p.Hero.CurrentHp.ShouldBe(900.0, "a survivable hit lands in full — NEGATE guards only a lethal one");

                p.Pipeline.DealMaxHpPctDamage(p.Hero, MaxHp * 10.0, bypassesWards: true, "EFF_LETHAL", source: null);

                p.Hero.CurrentHp.ShouldBe(900.0, "the lethal hit is voided outright — the chip did not spend the save");
                p.Hero.IsAlive.ShouldBeTrue();
            });

        probe.EventsOf(CombatEventType.ActorDeath).ShouldBeEmpty();

        var hits = probe.EventsOf(CombatEventType.Hit);
        hits.Count.ShouldBe(2, "the chip and the voided blow — a swing with no event would draw as a miss");
        hits[^1].Value.ShouldBe(
            0.0, "nothing came off HP, and the Hit event says so rather than reporting the lethal amount");
    }

    [Fact]
    public void Once_bounds_a_NEGATE_save_and_the_second_lethal_hit_kills()
    {
        Fight(
            new[] { NegateLethal("SET_BONUS_HEAVY_6") },
            p =>
            {
                p.Pipeline.DealMaxHpPctDamage(p.Hero, 100.0, bypassesWards: true, "EFF_CHIP", source: null);
                p.Pipeline.DealMaxHpPctDamage(p.Hero, MaxHp * 10.0, bypassesWards: true, "EFF_LETHAL_1", source: null);

                p.Hero.CurrentHp.ShouldBe(900.0, "the first lethal hit is the once-per-battle negate");
                p.Hero.IsAlive.ShouldBeTrue();

                p.Pipeline.DealMaxHpPctDamage(p.Hero, MaxHp * 10.0, bypassesWards: true, "EFF_LETHAL_2", source: null);

                p.Hero.CurrentHp.ShouldBe(0.0, "once is spent — nothing negates the second lethal hit");
                p.Hero.IsAlive.ShouldBeFalse();
            });
    }

    /// <summary>
    /// The order pin: an ON_LETHAL save is consumed BEFORE death, a REVIVE only after
    /// <c>ON_DEATH</c>. Lethal hit 1 is negated with the held REVIVE untouched; lethal hit 2 kills
    /// past the spent negate and the REVIVE then returns the actor, firing <c>ON_REVIVE</c> —
    /// D49 moves neither half of that order.
    /// </summary>
    [Fact]
    public void A_NEGATE_save_is_consumed_before_death_and_a_held_REVIVE_only_after_it()
    {
        var probe = Fight(
            new[]
            {
                NegateLethal("SET_BONUS_HEAVY_6"),
                Holding("PK_SECOND_WIND", EffectOp.REVIVE, 0.30, TriggerKind.ON_BATTLE_START),
                Holding("PK_Z_ON_REVIVE_PROBE", EffectOp.SHIELD, 7.0, TriggerKind.ON_REVIVE),
            },
            p =>
            {
                p.Pipeline.DealMaxHpPctDamage(p.Hero, MaxHp * 10.0, bypassesWards: true, "EFF_LETHAL_1", source: null);

                p.Hero.CurrentHp.ShouldBe(MaxHp, "the negate fires before death, so the REVIVE has nothing to answer");
                p.Services.Log.Events.Count(e => e.Type == CombatEventType.Shield).ShouldBe(
                    0, "no death, no return, no ON_REVIVE");

                p.Pipeline.DealMaxHpPctDamage(p.Hero, MaxHp * 10.0, bypassesWards: true, "EFF_LETHAL_2", source: null);
            },
            maxTicks: 1);

        probe.Result.HeroHpRemaining.ShouldBe(300.0, "0.30 x 1000 Max HP — the REVIVE answered the second hit");
        probe.EventsOf(CombatEventType.ActorDeath).ShouldBeEmpty("the actor came back before the body was removed");
        probe.EventsOf(CombatEventType.Shield).Count.ShouldBe(1, "ON_REVIVE fired for the REVIVE and only then");
    }

    /// <summary>
    /// Both anti-loop halves composed: two actors each holding an <c>ON_LETHAL</c>-armed
    /// <c>SURVIVE_LETHAL</c> and lethal <c>THORNS</c>. Without <c>once</c> bounding and
    /// thorns-never-retriggers-thorns, the exchange resaves both forever and the fight never resolves.
    /// </summary>
    [Fact]
    public void Two_actors_with_ON_LETHAL_saves_and_thorns_terminate_instead_of_looping()
    {
        var probe = AttackPipelineBench.Run(
            new[]
            {
                BattleTestBench.Hero(
                    AttackPipelineBench.Stats(MaxHp, (StatId.ATK, 100_000.0)),
                    1,
                    effects: new[]
                    {
                        SurviveLethalOnLethal("PK_UNBREAKABLE_HERO", 1.0, ValueMode.FLAT, once: true),
                    }),
                BattleTestBench.Enemy(
                    0,
                    AttackPipelineBench.Stats(MaxHp, (StatId.THORNS, 0.5)),
                    effects: new[]
                    {
                        SurviveLethalOnLethal("PK_UNBREAKABLE_ENEMY", 1.0, ValueMode.FLAT, once: true),
                    }),
            },
            p =>
            {
                // Exchange 1: both saves are unspent — attacker's hit and the thorns reflect it
                // provokes are each individually lethal, and both actors come out of it at 1 HP.
                p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_ATTACK_1");

                p.Enemy().CurrentHp.ShouldBe(1.0, "PK_UNBREAKABLE_ENEMY's once-save, spent here");
                p.Enemy().IsAlive.ShouldBeTrue();
                p.Hero.CurrentHp.ShouldBe(1.0, "PK_UNBREAKABLE_HERO's once-save, off the thorns reflect");
                p.Hero.IsAlive.ShouldBeTrue();

                // Exchange 2: an identical swing. If `once` did not bound either save, this would
                // resave both actors again, an infinite loop. With it, both saves are already spent,
                // so this swing (and the thorns reflect it provokes) kills both.
                p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_ATTACK_2");

                p.Enemy().CurrentHp.ShouldBe(0.0, "once is spent — nothing saves the second exchange");
                p.Enemy().IsAlive.ShouldBeFalse();
                p.Hero.CurrentHp.ShouldBe(0.0, "once is spent on the hero's side too");
                p.Hero.IsAlive.ShouldBeFalse();
            });

        // Termination as a count: exactly four Hit events (the main swing and its thorns reflect,
        // twice over). A loop that kept resaving would emit more; stopping early would emit fewer.
        probe.EventsOf(CombatEventType.Hit).Count.ShouldBe(4);
    }

    [Fact]
    public void ON_LETHAL_fires_exactly_once_per_lethal_hit_and_not_at_all_for_a_survivable_one()
    {
        var probe = Fight(
            new[] { Holding("Z_ON_LETHAL_PROBE", EffectOp.SHIELD, 7.0, TriggerKind.ON_LETHAL) },
            p =>
            {
                // A hit that leaves the actor standing never crosses the "would take fatal damage"
                // check — no occurrence, no Shield.
                p.Pipeline.DealMaxHpPctDamage(p.Hero, 10.0, bypassesWards: true, "EFF_SURVIVABLE", source: null);

                p.Hero.IsAlive.ShouldBeTrue();
                p.Services.Log.Events.Count(e => e.Type == CombatEventType.Shield).ShouldBe(0);

                // One lethal hit — one occurrence, one Shield, not two.
                p.Pipeline.DealMaxHpPctDamage(p.Hero, MaxHp * 10.0, bypassesWards: true, "EFF_LETHAL", source: null);

                p.Hero.IsAlive.ShouldBeFalse();
                p.Services.Log.Events.Count(e => e.Type == CombatEventType.Shield).ShouldBe(1);
            });

        probe.EventsOf(CombatEventType.Shield).Count.ShouldBe(
            1, "one occurrence total across the whole probe body — the survivable hit fired none");
    }

    /// <summary>A <c>SURVIVE_LETHAL</c> armed once in the pre-tick by <c>ON_BATTLE_START</c>.</summary>
    private static HeldEffect SurviveLethal(string id, double value, ValueMode? mode) =>
        new(new EffectDefinition
        {
            Id = id,
            Op = EffectOp.SURVIVE_LETHAL,
            Target = EffectTarget.SELF,
            Value = value,
            ValueMode = mode,
            Trigger = new EffectTrigger { Kind = TriggerKind.ON_BATTLE_START, },
        });

    /// <summary>
    /// A <c>SURVIVE_LETHAL</c> armed on <c>ON_LETHAL</c> itself, rather than the
    /// <c>ON_BATTLE_START</c> workaround <see cref="SurviveLethal"/> uses.
    /// </summary>
    private static HeldEffect SurviveLethalOnLethal(string id, double value, ValueMode? mode, bool once) =>
        new(new EffectDefinition
        {
            Id = id,
            Op = EffectOp.SURVIVE_LETHAL,
            Target = EffectTarget.SELF,
            Value = value,
            ValueMode = mode,
            Trigger = new EffectTrigger { Kind = TriggerKind.ON_LETHAL, Once = once },
        });

    /// <summary>Ironvow's six-piece shape (16 D49): a value-less <c>NEGATE</c> save on <c>ON_LETHAL</c>, once.</summary>
    private static HeldEffect NegateLethal(string id) =>
        new(new EffectDefinition
        {
            Id = id,
            Op = EffectOp.SURVIVE_LETHAL,
            Target = EffectTarget.SELF,
            ValueMode = ValueMode.NEGATE,
            Trigger = new EffectTrigger { Kind = TriggerKind.ON_LETHAL, Once = true },
        });

    /// <param name="once">
    /// An unauthored <c>once</c> is a save that re-arms every time its trigger fires. The
    /// <c>ON_REVIVE</c> probe leaves it unset so it can report every firing.
    /// </param>
    private static HeldEffect Holding(
        string id, EffectOp op, double value, TriggerKind trigger, bool? once = null) =>
        new(new EffectDefinition
        {
            Id = id,
            Op = op,
            Target = EffectTarget.SELF,
            Value = value,
            Trigger = new EffectTrigger { Kind = trigger, Once = once },
        });

    private static AttackProbe Fight(
        HeldEffect[]? holding, Action<AttackProbe> body, int maxTicks = 1) =>
        AttackPipelineBench.Run(
            new[]
            {
                BattleTestBench.Hero(
                    AttackPipelineBench.Stats(MaxHp, (StatId.ATK, 10.0)),
                    1,
                    effects: holding ?? Array.Empty<HeldEffect>()),
                BattleTestBench.Enemy(0, AttackPipelineBench.Stats(5000.0)),
            },
            body,
            maxTicks: maxTicks);
}
