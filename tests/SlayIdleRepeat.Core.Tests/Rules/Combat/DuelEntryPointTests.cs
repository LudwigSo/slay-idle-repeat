using Shouldly;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// <see cref="CombatSimulator.SimulateDuel"/>, the public entry point for the Ghost Duel, run
/// through the real engine against the shipped content files.
/// </summary>
/// <remarks>
/// <c>PvpDuelTests</c> pins initiative ordering and the exact-tie rule against the internal
/// <c>Simulate(BattlePlan)</c>; this suite is that method's public half and does not re-assert them.
/// </remarks>
public sealed class DuelEntryPointTests
{
    private const ulong BattleSeed = 0xE4_C0_1D_E4_0000_0002UL;

    private static readonly Lazy<ContentSnapshot> LazyContent = new(GameDataLoader.Load);

    private static ContentSnapshot Content => LazyContent.Value;

    private static ActorStats Build(double atk, double def, double maxHp) => ActorStats.From(
        new Dictionary<StatId, double>
        {
            [StatId.MAX_HP] = maxHp,
            [StatId.ATK] = atk,
            [StatId.ASPD] = 1.0,
            [StatId.DEF] = def,
            [StatId.CRIT] = 0.0,
            [StatId.CDMG] = 0.5,
            [StatId.LIFESTEAL] = 0.0,
            [StatId.DODGE] = 0.0,
            [StatId.BLOCK] = 0.0,
            [StatId.PEN] = 0.0,
            [StatId.DMG_PCT] = 1.0,
            [StatId.DR_PCT] = 1.0,
            [StatId.HEAL_PCT] = 1.0,
            [StatId.THORNS] = 0.0,
        });

    /// <summary>Two hero builds can fight through a real entry point outside <c>Core.Rules</c>.</summary>
    [Fact]
    public void Two_hero_builds_can_fight_a_real_duel()
    {
        var result = CombatSimulator.SimulateDuel(
            BattleSeed,
            attacker: Build(atk: 300.0, def: 50.0, maxHp: 2_000.0), attackerLevel: 10,
            defender: Build(atk: 100.0, def: 20.0, maxHp: 1_000.0), defenderLevel: 10,
            durationSeconds: 60.0, Content);

        result.ShouldNotBeNull();
        result.Log.Count.ShouldBeGreaterThan(0, "a real duel logs at least BattleStart plus a swing");
        result.Log.ShouldContain(e => e.Type == CombatEventType.Attack, "both hero-shaped sides swing");
        result.HeroWon.ShouldBeTrue("the attacker heavily outguns the Ghost on every stat");
    }

    /// <summary>
    /// The duel cap, converted from seconds to ticks, actually bounds the fight when run through the
    /// public entry point.
    /// </summary>
    [Fact]
    public void The_duration_cap_the_caller_passes_actually_bounds_the_fight()
    {
        // Neither side can kill the other: ATK 0 leaves only the damage floor, and DEF/HP are large
        // enough that the floor cannot clear either side inside the cap.
        var tank = Build(atk: 0.0, def: 100.0, maxHp: 1_000_000.0);

        var result = CombatSimulator.SimulateDuel(
            BattleSeed, tank, attackerLevel: 1, tank, defenderLevel: 1, durationSeconds: 5.0, Content);

        result.DurationTicks.ShouldBe(100, "5.0 s at the 20 Hz clock is exactly 100 ticks");
    }

    /// <summary>
    /// A duration whose tick count exceeds the log's addressable range is refused at the door
    /// rather than truncated to a fight the caller did not ask for.
    /// </summary>
    [Fact]
    public void A_duration_beyond_the_logs_tick_range_is_refused()
    {
        var tank = Build(atk: 0.0, def: 100.0, maxHp: 1_000_000.0);

        Should.Throw<ArgumentOutOfRangeException>(() => CombatSimulator.SimulateDuel(
            BattleSeed, tank, attackerLevel: 1, tank, defenderLevel: 1,
            durationSeconds: 200.0, Content));
    }

    /// <summary>An attacker-held <c>APPLY_STATUS</c> effect resolves onto the Ghost mid-duel.</summary>
    [Fact]
    public void An_attached_APPLY_STATUS_effect_actually_resolves_during_a_duel()
    {
        var proof = new EffectDefinition
        {
            Id = "TEST_DUEL_PROOF_APPLY_STATUS",
            Op = EffectOp.APPLY_STATUS,
            StatusId = "POISON",
            Value = 0.02,
            Target = EffectTarget.CURRENT_TARGET,
            Trigger = new EffectTrigger { Kind = TriggerKind.ON_HIT },
            Duration = new EffectDuration { Scope = DurationScope.BATTLE, Seconds = 5.0 },
        };

        var withEffect = CombatSimulator.SimulateDuel(
            BattleSeed,
            attacker: Build(300.0, 50.0, 2_000.0), attackerLevel: 10,
            defender: Build(100.0, 20.0, 1_000.0), defenderLevel: 10,
            durationSeconds: 60.0, Content, attackerEffects: new[] { proof });

        var withoutEffect = CombatSimulator.SimulateDuel(
            BattleSeed,
            attacker: Build(300.0, 50.0, 2_000.0), attackerLevel: 10,
            defender: Build(100.0, 20.0, 1_000.0), defenderLevel: 10,
            durationSeconds: 60.0, Content);

        withEffect.Log.ShouldContain(
            e => e.Type == CombatEventType.StatusApplied,
            "the attacker's attached APPLY_STATUS effect should have resolved onto the Ghost");

        withoutEffect.Log.ShouldNotContain(
            e => e.Type == CombatEventType.StatusApplied,
            "the negative control: the identical duel with no attached effect applies no status");
    }
}
