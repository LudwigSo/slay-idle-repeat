using System.Linq;
using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// 🔴 `18` §2.4's <c>SURVIVE_LETHAL</c> and <c>REVIVE</c>, and `05` §3.1's anti-loop rule over them,
/// observed <b>in a fight</b> rather than over the two halves that meet there.
/// </summary>
/// <remarks>
/// 🔒 Both halves were built and both were tested — <c>CombatFlowOpTests</c> pins that the ops arm
/// the save, <c>CombatFlowStateTests</c> that <c>ConsumeDeathSave</c> honours <c>once</c> and the id
/// order. Nothing asked whether anything ever <em>called</em> it, and nothing did: every
/// survive-a-lethal-hit perk was inert and <c>ON_REVIVE</c> unreachable by construction. The defect
/// was the missing edge between the two things that were tested.
/// <para>
/// 🔴 <c>once</c> is admitted on <c>ON_LETHAL</c>/<c>ON_LOW_HP</c> only, and <c>ON_LETHAL</c> was
/// never fired — so `18` §7.4's <c>PK_UNBREAKABLE</c> could not be authored in its documented shape.
/// <c>AttackPipeline.ApplyToHp</c> now fires it between ward absorption and the HP write. The first
/// block below arms via <c>ON_BATTLE_START</c> and is unaffected by that.
/// </para>
/// </remarks>
public sealed class DeathSaveTests
{
    private const double MaxHp = 1000.0;

    /// <summary>
    /// 🔴 `18` §2.4 — a <c>SURVIVE_LETHAL</c> leaves the actor at its authored HP instead of at 0.
    /// </summary>
    /// <remarks>
    /// Two shapes, because `18` §10.1 E4's two units disagree about the same number: <c>FLAT 1</c> is
    /// `06`'s "survive at 1 HP", while the default reading of <c>0.25</c> is a quarter of Max HP.
    /// Running both stops the fix passing on a hard-wired 1. The blow is ten times the whole bar.
    /// </remarks>
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
                p.Pipeline.DealMaxHpPctDamage(p.Hero, MaxHp * 10.0, bypassesWards: true, "EFF_LETHAL");

                p.Hero.CurrentHp.ShouldBe(expectedHp);
                p.Hero.IsAlive.ShouldBeTrue();
            });

        // 🔒 `18` §3: the actor never died, so there is no death and no return to announce.
        probe.EventsOf(CombatEventType.ActorDeath).ShouldBeEmpty();

        // 🔒 The Hit carries what actually came off, not the lethal amount. `05` §8 makes the log the
        //    replay, and a Hit for 10 000 beside an actor standing at 1 HP is a frame nothing can draw.
        probe.EventsOf(CombatEventType.Hit).Single().Value.ShouldBe(MaxHp - expectedHp);
    }

    /// <summary>
    /// 🔒 The negative control — with no <c>SURVIVE_LETHAL</c> armed, the same blow kills.
    /// </summary>
    /// <remarks>
    /// Without this, a pipeline that simply refused to take an actor below 1 HP would pass the theory
    /// above on its <c>FLAT</c> row and look like a working death save.
    /// </remarks>
    [Fact]
    public void Without_a_save_the_same_blow_kills()
    {
        Fight(
            holding: null,
            p =>
            {
                p.Pipeline.DealMaxHpPctDamage(p.Hero, MaxHp * 10.0, bypassesWards: true, "EFF_LETHAL");

                p.Hero.CurrentHp.ShouldBe(0.0);
                p.Hero.IsAlive.ShouldBeFalse();
            });
    }

    /// <summary>
    /// 🔴 `18` §2.4 — a <c>REVIVE</c> returns the actor from 0 HP and fires <c>ON_REVIVE</c>.
    /// <c>SURVIVE_LETHAL</c> does neither.
    /// </summary>
    /// <remarks>
    /// Consumed in <c>ResolveDeaths</c>, not the pipeline: a <c>REVIVE</c> requires the actor to have
    /// <em>reached</em> 0, so it is read after <c>ON_DEATH</c> — and the actor is never logged as an
    /// <c>ActorDeath</c>, having come back before the body was removed. The <c>ON_REVIVE</c> is
    /// observed through a second holding rather than a recording double, because until this fix no
    /// fight could reach that edge.
    /// </remarks>
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
                p.Pipeline.DealMaxHpPctDamage(p.Hero, MaxHp * 10.0, bypassesWards: true, "EFF_LETHAL");
            },
            maxTicks: 1);

        probe.Result.HeroHpRemaining.ShouldBe(300.0, "0.30 x 1000 Max HP");
        probe.EventsOf(CombatEventType.ActorDeath).ShouldBeEmpty(
            "the actor came back before the body was removed");
        probe.EventsOf(CombatEventType.Shield).Count.ShouldBe(
            1, "`18` §3 fires ON_REVIVE, which is what the second holding is listening for");
    }

    /// <summary>
    /// 🔒 The negative control: a <c>SURVIVE_LETHAL</c> fires no <c>ON_REVIVE</c>, because `18` §3 is
    /// explicit that the actor never died.
    /// </summary>
    /// <remarks>
    /// Same probe holding, same lethal blow — the only difference is which op armed the save, so a
    /// consumer treating the two arms as one passes every other test here and fails this.
    /// </remarks>
    [Fact]
    public void A_SURVIVE_LETHAL_fires_no_ON_REVIVE()
    {
        var probe = Fight(
            new[]
            {
                SurviveLethal("PK_UNBREAKABLE", 1.0, ValueMode.FLAT),
                Holding("PK_Z_ON_REVIVE_PROBE", EffectOp.SHIELD, 7.0, TriggerKind.ON_REVIVE),
            },
            p => p.Pipeline.DealMaxHpPctDamage(p.Hero, MaxHp * 10.0, bypassesWards: true, "EFF_LETHAL"),
            maxTicks: 1);

        probe.EventsOf(CombatEventType.Shield).ShouldBeEmpty();
    }

    // ══════════════════════════════════════════ M2-R2: ON_LETHAL itself

    /// <summary>
    /// 🔴 `18` §7.4 — <c>PK_UNBREAKABLE</c> in the doc's own shape,
    /// <c>{"kind":"ON_LETHAL","once":true}</c> triggering <c>SURVIVE_LETHAL</c>, through the real
    /// attack pipeline rather than the <c>ON_BATTLE_START</c> workaround above.
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
                p.Pipeline.DealMaxHpPctDamage(p.Hero, MaxHp * 10.0, bypassesWards: true, "EFF_LETHAL");

                p.Hero.CurrentHp.ShouldBe(expectedHp);
                p.Hero.IsAlive.ShouldBeTrue();
            });

        probe.EventsOf(CombatEventType.ActorDeath).ShouldBeEmpty();
        probe.EventsOf(CombatEventType.Hit).Single().Value.ShouldBe(MaxHp - expectedHp);
    }

    /// <summary>
    /// 🔒 `05` §3.1's anti-loop rule, half one: <c>once</c> bounds an <c>ON_LETHAL</c>-armed save to a
    /// single firing per battle. The actor's first lethal hit is saved; its second, identical lethal
    /// hit actually kills — proving the bound is real and not merely "the save happened to fire once
    /// in this test".
    /// </summary>
    [Fact]
    public void Once_bounds_an_ON_LETHAL_save_to_one_hit_and_the_next_lethal_hit_kills()
    {
        Fight(
            new[] { SurviveLethalOnLethal("PK_UNBREAKABLE", 1.0, ValueMode.FLAT, once: true) },
            p =>
            {
                p.Pipeline.DealMaxHpPctDamage(p.Hero, MaxHp * 10.0, bypassesWards: true, "EFF_LETHAL_1");

                p.Hero.CurrentHp.ShouldBe(1.0, "the first lethal hit is the one authored once-save");
                p.Hero.IsAlive.ShouldBeTrue();

                p.Pipeline.DealMaxHpPctDamage(p.Hero, MaxHp * 10.0, bypassesWards: true, "EFF_LETHAL_2");

                p.Hero.CurrentHp.ShouldBe(0.0, "once is spent — nothing saves the second lethal hit");
                p.Hero.IsAlive.ShouldBeFalse();
            });
    }

    /// <summary>
    /// 🔒 `05` §3.1's anti-loop rule, both halves composed: two actors each holding an
    /// <c>ON_LETHAL</c>-armed <c>SURVIVE_LETHAL</c> <b>and</b> lethal <c>THORNS</c>. Without
    /// <c>once</c> bounding <em>and</em> thorns-never-retriggers-thorns, the exchange resaves both
    /// forever and the fight never resolves.
    /// </summary>
    /// <remarks>
    /// Two direct swings rather than two ticks, so the assertions are about the pipeline's own
    /// termination rather than turn order. Mitigation is exactly 0 and each side's output is
    /// individually lethal, so no outcome depends on rounding.
    /// </remarks>
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

                // Exchange 2: an IDENTICAL swing. If `once` did not bound either save, this would
                // resave both actors again — exactly the loop `05` §3.1 forbids. With it, both saves
                // are already spent, so this swing (and the thorns reflect it provokes) kills both.
                p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_ATTACK_2");

                p.Enemy().CurrentHp.ShouldBe(0.0, "once is spent — nothing saves the second exchange");
                p.Enemy().IsAlive.ShouldBeFalse();
                p.Hero.CurrentHp.ShouldBe(0.0, "once is spent on the hero's side too");
                p.Hero.IsAlive.ShouldBeFalse();
            });

        // 🔒 Termination, stated as a count: exactly four Hit events — the main swing and its thorns
        // reflect, twice over. A loop that kept resaving and re-reflecting would emit more; a pipeline
        // that stopped resolving early would emit fewer.
        probe.EventsOf(CombatEventType.Hit).Count.ShouldBe(4);
    }

    /// <summary>
    /// 🔒 Acceptance #4 — <c>ON_LETHAL</c> fires exactly once per lethal hit: not zero (the negative
    /// control, a survivable hit), and not twice (a <c>SHIELD</c> probe with no <c>once</c> reports
    /// every firing, so a double observation of the same hit would show as two <c>Shield</c> events
    /// rather than one).
    /// </summary>
    [Fact]
    public void ON_LETHAL_fires_exactly_once_per_lethal_hit_and_not_at_all_for_a_survivable_one()
    {
        var probe = Fight(
            new[] { Holding("Z_ON_LETHAL_PROBE", EffectOp.SHIELD, 7.0, TriggerKind.ON_LETHAL) },
            p =>
            {
                // A hit that leaves the actor standing never crosses `05` §4 step 9's "would take
                // fatal damage" — no occurrence, no Shield.
                p.Pipeline.DealMaxHpPctDamage(p.Hero, 10.0, bypassesWards: true, "EFF_SURVIVABLE");

                p.Hero.IsAlive.ShouldBeTrue();
                p.Services.Log.Events.Count(e => e.Type == CombatEventType.Shield).ShouldBe(0);

                // One lethal hit — one occurrence, one Shield. Not two: ApplyToHp is `05` §4's single
                // choke point for the attack, DAMAGE_MAXHP_PCT and the thorns reflect, and this hit
                // passes through exactly one of those three routes.
                p.Pipeline.DealMaxHpPctDamage(p.Hero, MaxHp * 10.0, bypassesWards: true, "EFF_LETHAL");

                p.Hero.IsAlive.ShouldBeFalse();
                p.Services.Log.Events.Count(e => e.Type == CombatEventType.Shield).ShouldBe(1);
            });

        probe.EventsOf(CombatEventType.Shield).Count.ShouldBe(
            1, "one occurrence total across the whole probe body — the survivable hit fired none");
    }

    // ══════════════════════════════════════════════════════ helpers

    /// <summary>
    /// A <c>SURVIVE_LETHAL</c> shaped like `06`'s <c>PK_UNBREAKABLE</c> — armed once in the pre-tick
    /// by <c>ON_BATTLE_START</c>, and carrying `18` §7.4's <c>"once": true</c>, which is what `05`
    /// §3.1's anti-loop rule counts.
    /// </summary>
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
    /// 🔴 M2-R2 — `18` §7.4's actual <c>PK_UNBREAKABLE</c> shape: a <c>SURVIVE_LETHAL</c> armed on
    /// <c>ON_LETHAL</c> itself, rather than on the <c>ON_BATTLE_START</c> workaround
    /// <see cref="SurviveLethal"/> uses. <paramref name="once"/> is `05` §3.1's anti-loop bound.
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

    /// <param name="once">
    /// `18` §7.4's <c>"once"</c>. The death-save holdings author it — an unauthored <c>once</c> is a
    /// save that re-arms every time its trigger fires, which is a different rule and not the one
    /// `05` §3.1 states. The <c>ON_REVIVE</c> probe leaves it unset so it can report every firing.
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
