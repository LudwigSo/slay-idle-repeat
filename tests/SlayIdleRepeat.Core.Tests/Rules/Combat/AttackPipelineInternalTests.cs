using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// 🔒 The <b>residue</b> of `05` §4 — the claims no <see cref="SimulationResult"/> can report and no
/// public entry point can provoke.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Read <see cref="DamageResolutionTests"/> first.</b> That suite is where `05` §4's ten steps
/// are pinned: every case runs through <see cref="CombatSimulator.SimulateDuel"/> and asserts on the
/// combat log. This file holds only what is left over, in three groups, each here for a stated
/// reason:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Draw discipline.</b> <c>DeterministicRng.Position</c> is <em>"the entire persistable state
///     of this stream"</em>, and it appears in no <c>CombatEvent</c>. A spent or skipped draw
///     desynchronises a client from the server for the rest of the fight, so the counts are
///     load-bearing for `11` §6 — and only reachable through <c>BattleServices</c>.
///   </item>
///   <item>
///     <b>`05` §4.2's non-attack damage routes.</b> <c>DealTrueDamage</c> and
///     <c>DealMaxHpPctDamage</c> are pipeline methods; reaching them from outside would need an
///     authored effect whose <em>whole</em> observable difference is the ward pool's balance, which
///     the log does not carry either.
///   </item>
///   <item>
///     <b>The S6 refusals.</b> Both guard against a caller the public entry points cannot be:
///     a non-finite attack multiplier, and an <c>IEffectActorView</c> belonging to another battle.
///   </item>
/// </list>
/// <para>
/// ⚠️ These cases keep using <see cref="AttackPipelineBench"/>, which reaches through
/// <c>BattlePlan.Seams</c> to drive the internal <c>AttackPipeline</c> inside a real fight. That is
/// the sanctioned last resort, not the default — if one of these becomes observable from a log, it
/// belongs in <see cref="DamageResolutionTests"/>.
/// </para>
/// </remarks>
public sealed class AttackPipelineInternalTests
{
    private const double Atk = 100.0;
    private const double SanityCheckDef = 120.0;

    // ══════════════════════════════════════════════════════ the draw discipline

    /// <summary>
    /// 🔒 One resolved attack advances the combat stream by exactly <b>3</b>, and a dodged one by
    /// exactly <b>1</b>.
    /// </summary>
    /// <remarks>
    /// 🔒 Both rows are needed: a pipeline that always drew three would pass the first and fail the
    /// second, and one that drew lazily would pass the second and fail the first.
    /// </remarks>
    [Theory]
    [InlineData(0.0, 3UL)]
    [InlineData(1.0, 1UL)]
    public void A_resolved_attack_draws_three_times_and_a_dodged_one_draws_once(
        double dodge, ulong expectedDraws) =>
        Fight(
            attacker: Block(500.0, (StatId.ATK, Atk)),
            defender: Block(5000.0, (StatId.DEF, SanityCheckDef), (StatId.DODGE, dodge)),
            body: p =>
            {
                p.Services.Rng.Position.ShouldBe(
                    0UL, "nothing in this fight draws before the probe does");

                p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X");

                p.Services.Rng.Position.ShouldBe(expectedDraws);
            });

    /// <summary>
    /// 🔒 `18` §2.4's <c>FORCE_CRIT_NEXT</c> decides the <b>outcome</b> of step 4 and never its
    /// <b>draw</b>.
    /// </summary>
    /// <remarks>
    /// 🔒 If a forced crit skipped its draw, the stream's position would become a function of the
    /// attacker's flow state, and a client that had not observed the charge would diverge from the
    /// server on every draw thereafter. That the charge <em>crits</em> is
    /// <c>DamageResolutionTests.A_FORCE_CRIT_NEXT_charge_crits_a_swing_the_attackers_own_CRIT_never_would</c>;
    /// what is here is the half the log cannot show.
    /// </remarks>
    [Fact]
    public void A_forced_crit_crits_without_skipping_step_4s_draw() =>
        Fight(
            attacker: Block(500.0, (StatId.ATK, Atk), (StatId.CRIT, 0.0), (StatId.CDMG, 0.5)),
            defender: Block(5000.0, (StatId.DEF, SanityCheckDef)),
            body: p =>
            {
                p.Hero.Flow.GrantForcedCrits(1);

                p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X").Crit.ShouldBeTrue();
                p.Services.Rng.Position.ShouldBe(3UL, "step 4 drew even though the outcome was fixed");

                // The charge is spent: the next swing is an ordinary one, and still draws three.
                p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X").Crit.ShouldBeFalse();
                p.Services.Rng.Position.ShouldBe(6UL);
            });

    // ══════════════════════════════════════════════════════ 05 §4.2's other two routes

    /// <summary>
    /// 🔒 `05` §4.2 — <c>DAMAGE_TRUE</c> <em>"bypasses everything … no <c>DR%</c>,
    /// <c>DAMAGE_TAKEN_MULT</c>, floor or wards"</em>, and `05` §4.1's bypass class (a).
    /// </summary>
    [Fact]
    public void DAMAGE_TRUE_bypasses_DR_the_damage_taken_multiplier_and_the_ward_pool() =>
        Fight(
            defender: Block(5000.0, (StatId.DEF, SanityCheckDef), (StatId.DR_PCT, 0.5)),
            body: p =>
            {
                p.Enemy().Flow.AddDamageTakenMultiplier(0.5, "EFF_A");
                p.Pipeline.GrantWard(p.Enemy(), 1000.0, null, "EFF_W");

                p.Pipeline.DealTrueDamage(p.Enemy(), 100.0, "EFF_TRUE");

                p.Enemy().CurrentHp.ShouldBe(4900.0, "none of the three touched it");
                p.Enemy().Wards.Total.ShouldBe(1000.0, "the pool is untouched, not merely bypassed");
                p.Services.Rng.Position.ShouldBe(0UL, "a non-attack damage event draws nothing");
            });

    /// <summary>
    /// 🔒 `05` §4.2 — <c>DAMAGE_MAXHP_PCT</c>: <em>"<c>DR%</c> and <c>DAMAGE_TAKEN_MULT</c>
    /// <b>do</b> apply; wards absorb"</em> — unless `05` §4.1's bypass class <b>(b)</b> says
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// 🔒 R12 — the bypass flag is <c>EffectTagging.IsSelfInflictedCost</c>'s answer, read once at
    /// the op and reported here, never re-derived. The two rows are the two answers, over the same
    /// ward: <em>"wards must not silently delete perk drawbacks"</em> (<c>CP_BLOOD_PRICE</c>).
    /// </remarks>
    [Theory]
    [InlineData(false, 5000.0, 950.0)]   // absorbed: the pool pays the 50, HP is untouched
    [InlineData(true, 4950.0, 1000.0)]   // `05` §4.1 (b): the drawback reaches HP, the pool is untouched
    public void DAMAGE_MAXHP_PCT_applies_DR_and_is_absorbed_unless_it_is_a_self_inflicted_cost(
        bool bypassesWards, double expectedHp, double expectedWard) =>
        Fight(
            defender: Block(5000.0, (StatId.DEF, SanityCheckDef), (StatId.DR_PCT, 0.5)),
            body: p =>
            {
                p.Pipeline.GrantWard(p.Enemy(), 1000.0, null, "EFF_W");

                // 100, halved by DR% to 50.
                p.Pipeline.DealMaxHpPctDamage(p.Enemy(), 100.0, bypassesWards, "CP_BLOOD_PRICE");

                p.Enemy().CurrentHp.ShouldBe(expectedHp);
                p.Enemy().Wards.Total.ShouldBe(expectedWard);
                p.Services.Rng.Position.ShouldBe(0UL, "no dodge, no crit, no block — so no draws");
            });

    // ══════════════════════════════════════════════════════ the S6 refusals

    /// <summary>
    /// 🔒 A non-finite number is refused by name rather than carried through the ten steps.
    /// </summary>
    /// <remarks>
    /// A NaN compares <c>false</c> against every bound in `05` §4 — the dodge test, the floor, the
    /// ward cap — so it passes through all of them and is refused by <c>CombatLog</c> three layers
    /// later, naming the serialiser rather than the effect. The message is the deliverable
    /// (steering S2), so the effect id is asserted and not merely the type.
    /// </remarks>
    [Fact]
    public void A_non_finite_number_is_refused_naming_the_effect_that_produced_it() =>
        Fight(body: p =>
        {
            var refused = Should.Throw<EffectContextException>(
                () => p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), double.NaN, "PK_GAMBLER"));

            refused.Message.ShouldContain("PK_GAMBLER", Case.Sensitive);
            refused.Message.ShouldContain("AttackMultiplier", Case.Sensitive);

            Should.Throw<EffectContextException>(
                () => p.Pipeline.Heal(p.Enemy(), double.PositiveInfinity, "PK_TRANSFUSION"))
                .Message.ShouldContain("PK_TRANSFUSION", Case.Sensitive);
        });

    /// <summary>
    /// 🔒 A view that is not this battle's actor is refused — <c>BattleFlowSink</c>'s guard and its
    /// reason: a battle has one roster and one view of it.
    /// </summary>
    [Fact]
    public void A_foreign_actor_view_is_refused() =>
        Fight(body: p =>
            Should.Throw<InvalidOperationException>(
                    () => p.Pipeline.Heal(new ForeignView(), 1.0, "EFF_X"))
                .Message.ShouldContain("one roster", Case.Sensitive));

    /// <summary>An <c>IEffectActorView</c> that is not a <c>BattleActor</c>.</summary>
    private sealed class ForeignView : IEffectActorView
    {
        public string Id => "FOREIGN";

        public int Index => 99;

        public BattleSide Side => BattleSide.ENEMY;

        public EffectActorKind Kind => EffectActorKind.ENEMY;

        public bool IsAlive => true;

        public double CurrentHp => 1.0;

        public double MaxHp => 1.0;

        public bool IsElite => false;

        public bool IsBoss => false;

        public bool IsSummon => false;

        public string? OwnerId => null;

        public int StatusStacks(string statusId) => 0;
    }

    // ══════════════════════════════════════════════════════ helpers

    /// <summary>`05` §1's block — see <see cref="AttackPipelineBench.Stats"/> for the two defaults.</summary>
    private static ActorStats Block(double maxHp, params (StatId Stat, double Value)[] rest) =>
        AttackPipelineBench.Stats(maxHp, rest);

    /// <summary>One probe fight: a hero, one enemy, nobody swinging, the probe in slot 1.</summary>
    private static AttackProbe Fight(
        Action<AttackProbe> body,
        ActorStats? attacker = null,
        ActorStats? defender = null,
        int attackerLevel = 1) =>
        AttackPipelineBench.Run(
            new[]
            {
                BattleTestBench.Hero(attacker ?? Block(500.0, (StatId.ATK, Atk)), attackerLevel),
                BattleTestBench.Enemy(0, defender ?? Block(5000.0, (StatId.DEF, SanityCheckDef))),
            },
            body,
            battleSeed: 1UL);
}
