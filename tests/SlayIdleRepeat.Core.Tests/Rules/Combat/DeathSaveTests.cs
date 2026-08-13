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
/// <para>
/// 🔒 <b>Why this file exists.</b> Both halves of these two ops were built, and both were tested.
/// <c>CombatFlowOpTests</c> pins that the ops <em>arm</em> the right save
/// (<c>bench.OnlyAmount("ArmSurviveLethal")</c>), and <c>CombatFlowStateTests</c> pins that
/// <c>ConsumeDeathSave</c> honours the <c>once</c> count and the ascending effect-id order. Nothing
/// asked whether anything ever <em>called</em> <c>ConsumeDeathSave</c> — and nothing did.
/// Cross-task review found it with no production caller at all, which made every "survive a lethal
/// hit" perk in the game inert, made <c>REVIVE</c> never return anyone, and left <c>ON_REVIVE</c>
/// — one of `18` §11's 23 triggers — unreachable by construction.
/// </para>
/// <para>
/// So every test here drives a <b>real fight</b> and reads the actor's HP and the log. A test
/// written against <c>CombatFlowState</c> could not have caught the defect, because the defect was
/// precisely the absence of the edge between the two things that were tested.
/// </para>
/// <para>
/// 🔒 The saves are armed by an <c>ON_BATTLE_START</c> holding: the op runs once in the pre-tick and
/// the save then sits on <c>CombatFlowState</c> waiting for a lethal blow that may never come.
/// </para>
/// <para>
/// ⚠️ <b>`05` §3.1's <c>once</c> half is NOT covered here, and cannot be yet.</b>
/// <c>ConsumeDeathSave</c> honours it and <c>CombatFlowStateTests</c> pins that it does — but the
/// count reaches the save from the holding's <c>once</c>, and `18` §3 admits <c>once</c> on
/// <c>ON_LETHAL</c> and <c>ON_LOW_HP</c> only. <c>ON_BATTLE_START</c> is refused outright
/// (<em>"it carries once, which `18` §3 does not give it"</em>), so a <c>once</c> death save can
/// only be authored on <c>ON_LETHAL</c> — and <b><c>ON_LETHAL</c> is never fired by the engine</b>.
/// <c>TriggerRegistry</c> lists it as a moment (<em>"inside `05` §4, when the hit would be fatal,
/// before slot 6"</em>) and nothing in <c>BattleSimulation</c> or <c>AttackPipeline</c> raises it,
/// which also means `18` §7.4's <c>PK_UNBREAKABLE</c> cannot be authored in its documented shape at
/// all. Recorded by cross-task review as an open finding rather than approximated here: an
/// end-to-end <c>once</c> test would have to arm the save by a route no content can take, and would
/// then pass while the perk it stands for still could not be written.
/// </para>
/// </remarks>
public sealed class DeathSaveTests
{
    private const double MaxHp = 1000.0;

    /// <summary>
    /// 🔴 `18` §2.4 — a <c>SURVIVE_LETHAL</c> leaves the actor at its authored HP instead of at 0.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Two shapes.</b> `18` §10.1 E4 gives the op two units and they disagree about what the
    /// same number means: <c>FLAT 1</c> is `06`'s <em>"survive a lethal hit at 1 HP"</em>, while the
    /// default reading of <c>0.25</c> is a quarter of Max HP. Running both is what stops the fix
    /// passing on a hard-wired 1.
    /// </para>
    /// <para>
    /// The blow is ten times the actor's whole bar, so nothing but a save can leave it standing.
    /// </para>
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
    /// 🔴 `18` §2.4 — a <c>REVIVE</c> returns the actor from 0 HP, and `18` §3 makes it fire
    /// <c>ON_REVIVE</c>. <c>SURVIVE_LETHAL</c> does neither.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 Consumed in <c>ResolveDeaths</c> and not in the pipeline, which is the whole difference
    /// between the two ops: a <c>REVIVE</c> requires the actor to have <em>reached</em> 0, so it is
    /// read after <c>ON_DEATH</c> has fired. The actor is therefore never logged as an
    /// <c>ActorDeath</c> — it came back before the body was removed.
    /// </para>
    /// <para>
    /// 🔒 The <c>ON_REVIVE</c> is observed through a second holding whose only job is to be fired by
    /// it, rather than through a recording double: `18` §11 counts <c>ON_REVIVE</c> among the 23
    /// triggers, and until this fix no fight could reach it, so a double would have been asserting
    /// against an edge that did not exist.
    /// </para>
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
    /// 🔒 The negative control for the trigger half — a <c>SURVIVE_LETHAL</c> fires no
    /// <c>ON_REVIVE</c>, because `18` §3 is explicit that the actor never died.
    /// </summary>
    /// <remarks>
    /// The same probe holding as the <c>REVIVE</c> case, over the same lethal blow, so the only
    /// difference between the two tests is which op armed the save. A consumer that treated the two
    /// arms as one would pass every other test in this file and fail this.
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
