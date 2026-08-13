using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// 🔒 `29` §2 — <c>PlayerPower = K_POWER × sqrt(EffectiveHP × DPS)</c>, evaluated against `29` §2.2's
/// fixed reference opponent.
/// </summary>
/// <remarks>
/// <para>
/// ═══ 🔒 <b>WHY THIS TYPE IS PUBLIC, AND WHY IT LANDED HERE</b> ═══
/// </para>
/// <para>
/// `30` §11.2 names <c>CombatSimulator</c> and <c>PowerCalculator</c> as <em>"the only two
/// <c>Rules</c> types that are public"</em>, with a named external consumer each — the client's local
/// battle simulation (`14` §2.4) and the Hero screen's power readout (`29` §1).
/// <c>Domain.PublicRuleTypes</c> has carried the name since M2-08; this is the type arriving under it,
/// so nothing in that transcription changes.
/// </para>
/// <para>
/// 🔒 <b>M2-16a landed it because `05` §9 cannot be measured without it.</b> Two of the six balance
/// guardrails are defined over this number and not over anything else: guardrail 1 is
/// <em>"a player at exactly <c>ParPower(c,t)</c>"</em>, which `29` §2.5.3's scaling rule reaches by
/// bisecting this function, and guardrail 6 is <em>"top-3 by <b>marginal power</b>"</em>, which is its
/// partial derivative. A copy of the formula inside <c>tools/BalanceHarness</c> would have made
/// guardrail 6 grade the harness's arithmetic rather than the game's — the precise failure R30 exists
/// to prevent one directory over, where `05` §4's and `29` §2.3's mitigation dials are mirrored
/// against each other so that <em>"the power model predicts a mitigation the fight does not
/// produce"</em> cannot happen silently.
/// </para>
/// <para>
/// ⚠️ <b>The player-aggregate form is not this.</b> `30` §11.2 spells the call
/// <c>PowerCalculator.Compute(player, content)</c>; no player aggregate exists yet (M1/M3), so what
/// ships is the <b>stat-block</b> form the simulator and the harness both already speak. Whoever
/// lands the aggregate adds an overload that aggregates to an <see cref="ActorStats"/> and calls this
/// one — it does not replace it.
/// </para>
/// <para>
/// ⚠️ <b>Two inputs of `29` §2.3 are absent from the data and are left absent (steering S6).</b>
/// </para>
/// <list type="bullet">
///   <item><b><c>PetDpsShare</c> is 0.</b> `29` §2.4 computes it from pet ability definitions and
///   weights them through <c>tuning/power_model.json#/petAbilityWeights</c>, which is authored
///   <c>{}</c> — there are no pets in the repository until M4-07. It is not defaulted to a plausible
///   share; the term is absent, and a build whose damage is mostly its pets' therefore reads low.
///   Greppable as <see cref="AbsentPetDpsShare"/>.</item>
///   <item><b><c>Regen_per_sec</c> is 0.</b> `05` §5's <c>REGEN</c> is a status, not one of `05`
///   §1's fourteen stats, so a bare stat block cannot carry one. Greppable as
///   <see cref="AbsentRegenPerSecond"/>.</item>
/// </list>
/// <para>
/// 🔒 <b><c>kPower</c> is <c>null</c> in the shipped data, and <see cref="Compute"/> throws rather
/// than defaulting it.</b> `29` §2.1: <em>"K_POWER is a pure calibration constant … fixed once, by
/// solving for <c>PlayerPower = 1000</c> on the reference par build … the harness derives the exact
/// value and writes it into <c>power_model.json</c>."</em> Until someone does, the honest reading is
/// <see cref="PowerIndex"/> — the same quantity without the constant. Every ratio the guardrails take
/// (the scaling rule, marginal power, A10's ±12%) is invariant under <c>K_POWER</c>, so the constant
/// cancels and no caller is blocked by its absence.
/// </para>
/// </remarks>
public static class PowerCalculator
{
    /// <summary>🔒 `29` §2.3's <c>PetDpsShare</c>, absent because no pet exists until M4-07.</summary>
    /// <remarks>Named rather than inlined so that <c>grep PetDpsShare</c> finds the hole.</remarks>
    internal const double AbsentPetDpsShare = 0.0;

    /// <summary>🔒 `29` §2.3's <c>Regen_per_sec</c>, absent because `05` §1 has no REGEN stat.</summary>
    internal const double AbsentRegenPerSecond = 0.0;

    /// <summary>`29` §2.5.3 — the level a loadout is evaluated at when it targets no content.</summary>
    internal const int DefaultEvaluationLevel = 40;

    /// <summary>`29` §7 — the document holding every weight below.</summary>
    internal const string Document = "tuning/power_model.json";

    /// <summary>`29` §2.1 — <c>"geometric"</c> or <c>"additive_legacy"</c>.</summary>
    internal const string ModelPointer = Document + "#/model";

    /// <summary>`29` §2.1 — the calibration constant. Authored <c>null</c>; see the remarks.</summary>
    internal const string KPowerPointer = Document + "#/kPower";

    /// <summary>`29` §2.3 — <c>Block × BLOCK_WEIGHT</c>, the halved-hit weighting of `05` §4 step 5.</summary>
    internal const string BlockWeightPointer = Document + "#/effectiveHp/blockWeight";

    /// <summary>`29` §2.3 — <c>LS_WEIGHT</c>.</summary>
    internal const string LifestealWeightPointer = Document + "#/effectiveHp/lifestealWeight";

    /// <summary>`29` §2.3 — <c>REGEN_WEIGHT</c>.</summary>
    internal const string RegenWeightPointer = Document + "#/effectiveHp/regenWeight";

    /// <summary>`29` §2.3 / `05` §4 — the mitigation denominator's flat term. Mirrored by R30.</summary>
    internal const string MitigationFlatPointer = Document + "#/mitigation/flatConstant";

    /// <summary>`29` §2.3 / `05` §4 — the mitigation denominator's per-level term. Mirrored by R30.</summary>
    internal const string MitigationPerLevelPointer = Document + "#/mitigation/perLevelConstant";

    /// <summary>`29` §2.2 — the reference opponent's level, the attacker level of <c>EffectiveHP</c>.</summary>
    internal const string ReferenceLevelPointer = Document + "#/referenceOpponent/level";

    /// <summary>`29` §2.2 — the reference opponent's DEF, what the hero's PEN is measured against.</summary>
    internal const string ReferenceDefPointer = Document + "#/referenceOpponent/def";

    /// <summary>`29` §2.2 — the reference opponent's PEN, which `05` §4 step 3 applies to hero DEF.</summary>
    internal const string ReferencePenPointer = Document + "#/referenceOpponent/pen";

    /// <summary>🔒 `29` §2.1's default and the form every table in that document is authored against.</summary>
    internal const string GeometricModel = "geometric";

    /// <summary>🔒 `02` §4.4's superseded additive form, retained in data so the change is reversible.</summary>
    internal const string AdditiveLegacyModel = "additive_legacy";

    /// <summary>
    /// 🔒 `29` §2.3's <c>EffectiveHP</c> — how much of `29` §2.2's reference opponent's output the
    /// block absorbs before dying.
    /// </summary>
    /// <param name="stats">The `05` §1 block. Caps are applied first, per `05` §1.1.</param>
    /// <param name="content">The loaded content snapshot; every weight is read from it.</param>
    /// <exception cref="MissingContentException">A pointer is absent.</exception>
    /// <exception cref="UnauthorisedTunableException">A weight is authored <c>null</c>.</exception>
    public static double EffectiveHp(ActorStats stats, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(content);

        var model = Read(content);

        // 05 §4 step 3, with the reference opponent as the ATTACKER: its level drives the
        // denominator and its pen (authored 0) reduces the defender's DEF.
        var effDef = Capped(stats, StatId.DEF, model) * (1.0 - model.ReferencePen);
        var mitigationVsReference = Mitigation(effDef, model.ReferenceLevel, model);

        var survivability = Capped(stats, StatId.MAX_HP, model)
            / (1.0 - mitigationVsReference)
            / (1.0 - Capped(stats, StatId.DR_PCT, model))
            / (1.0 - Capped(stats, StatId.DODGE, model))
            / (1.0 - (Capped(stats, StatId.BLOCK, model) * model.BlockWeight))
            * (1.0 + (Capped(stats, StatId.LIFESTEAL, model) * model.LifestealWeight))
            * (1.0 + (AbsentRegenPerSecond * model.RegenWeight));

        return DeterminismRounding.Round(survivability);
    }

    /// <summary>
    /// 🔒 `29` §2.3's <c>DPS</c> — sustained damage per second against `29` §2.2's reference opponent.
    /// </summary>
    /// <param name="stats">The `05` §1 block. Caps are applied first, per `05` §1.1.</param>
    /// <param name="level">
    /// The attacker's level — `05` §4's <c>20 × attacker.Level</c> term, which is what makes a
    /// stat-identical hero mitigated less at Legend Level 200 than at 10. `29` §2.5.3 sets it to
    /// <c>EnemyLevel(c,t)</c> when a loadout targets content and to
    /// <see cref="DefaultEvaluationLevel"/> otherwise.
    /// </param>
    /// <param name="content">The loaded content snapshot.</param>
    public static double Dps(ActorStats stats, int level, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(content);

        var model = Read(content);

        // 05 §4 step 3 with the HERO as the attacker: the hero's PEN against the reference's DEF,
        // and the hero's own level in the denominator.
        var effDef = model.ReferenceDef * (1.0 - Capped(stats, StatId.PEN, model));
        var mitigationOfReference = Mitigation(effDef, level, model);

        var dps = Capped(stats, StatId.ATK, model)
            * Capped(stats, StatId.ASPD, model)
            * (1.0 + (Capped(stats, StatId.CRIT, model) * Capped(stats, StatId.CDMG, model)))
            * (1.0 + Capped(stats, StatId.DMG_PCT, model))
            * (1.0 - mitigationOfReference)
            * (1.0 + AbsentPetDpsShare);

        return DeterminismRounding.Round(dps);
    }

    /// <summary>
    /// 🔒 <c>sqrt(EffectiveHP × DPS)</c> — `29` §2.1's power <b>without</b> <c>K_POWER</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the member every ratio should use, and it exists because <c>kPower</c> is authored
    /// <c>null</c>. `29` §2.1 defines the constant by <c>PlayerPower(referenceParBuild) := 1000</c>,
    /// so a caller that needs the absolute scale derives
    /// <c>K_POWER = 1000 / PowerIndex(referenceParBuild)</c> from
    /// <c>tuning/calibration_builds.json</c> itself. Every guardrail in `05` §9 compares one power to
    /// another and is therefore invariant under the constant.
    /// </para>
    /// <para>
    /// 🔒 <b>The geometric mean is not interchangeable with the additive form</b> (`29` §2.1): the
    /// additive one is not monotone in both terms, so a build with vast EffectiveHP and no damage
    /// scores highly and then loses every fight under `05` §3's 90 s cap. This member refuses to
    /// evaluate an <c>additive_legacy</c> document rather than silently answering in the other shape.
    /// </para>
    /// </remarks>
    /// <exception cref="ContentTypeMismatchException">
    /// <c>power_model.json#/model</c> is <c>additive_legacy</c> — see the remarks — or is neither
    /// authored form.
    /// </exception>
    public static double PowerIndex(ActorStats stats, int level, ContentSnapshot content) =>
        DeterminismRounding.Round(Math.Sqrt(EffectiveHp(stats, content) * Dps(stats, level, content)));

    /// <summary>
    /// 🔒 `29` §2.1 — <c>PlayerPower = K_POWER × sqrt(EffectiveHP × DPS)</c>, the absolute scalar.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Throws on the shipped data today</b>, and deliberately:
    /// <c>tuning/power_model.json#/kPower</c> is authored <c>null</c> and steering S6 forbids
    /// coercing a hole to a default at read time. Use <see cref="PowerIndex"/>, or derive the
    /// constant from the reference par build as `29` §2.1 specifies.
    /// </remarks>
    /// <exception cref="UnauthorisedTunableException"><c>kPower</c> is <c>null</c>.</exception>
    public static double Compute(ActorStats stats, int level, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var kPower = content.ReadDouble(KPowerPointer);
        return DeterminismRounding.Round(kPower * PowerIndex(stats, level, content));
    }

    /// <summary>
    /// 🔒 `05` §1.1 — <em>"caps are applied <b>after</b> all aggregation"</em>, and `29` §2.3
    /// restates it for this evaluation: <em>"a player at the 0.75 crit cap gains nothing from more
    /// crit, and <c>PlayerPower</c> must reflect that or it will recommend gear that does nothing."</em>
    /// </summary>
    private static double Capped(ActorStats stats, StatId stat, PowerModel model) =>
        model.Caps.Apply(stat, stats[stat]);

    /// <summary>
    /// 🔒 `05` §4 step 3 — the one mitigation curve, read from the dials R30 mirrors against
    /// <c>content/combat_caps.json</c>.
    /// </summary>
    private static double Mitigation(double effectiveDef, int attackerLevel, PowerModel model) =>
        effectiveDef / (effectiveDef + model.MitigationFlat + (model.MitigationPerLevel * attackerLevel));

    private static PowerModel Read(ContentSnapshot content)
    {
        var model = content.ReadText(ModelPointer);

        if (!model.Equals(GeometricModel, StringComparison.Ordinal))
        {
            throw new ContentTypeMismatchException(
                ModelPointer,
                ContentValueKind.Text,
                model.Equals(AdditiveLegacyModel, StringComparison.Ordinal)
                    ? $"'{GeometricModel}'. `29` §2.1 replaced '{AdditiveLegacyModel}' because it is not " +
                      "monotone in both terms — a build with enormous EffectiveHP and no damage scores " +
                      "highly and then loses every fight under `05` §3's 90 s cap. Every table in `29` is " +
                      "authored against the geometric form, so answering in the other one would grade the " +
                      "game against numbers nobody wrote"
                    : $"'{GeometricModel}' or '{AdditiveLegacyModel}', the two forms `29` §2.1 ships");
        }

        return new PowerModel(
            Caps: CombatCaps.Read(content).Caps,
            BlockWeight: content.ReadDouble(BlockWeightPointer),
            LifestealWeight: content.ReadDouble(LifestealWeightPointer),
            RegenWeight: content.ReadDouble(RegenWeightPointer),
            MitigationFlat: content.ReadDouble(MitigationFlatPointer),
            MitigationPerLevel: content.ReadDouble(MitigationPerLevelPointer),
            ReferenceLevel: content.ReadInt32(ReferenceLevelPointer),
            ReferenceDef: content.ReadDouble(ReferenceDefPointer),
            ReferencePen: content.ReadDouble(ReferencePenPointer));
    }

    /// <summary>`29` §2.2-2.3's weights, read once per evaluation.</summary>
    private sealed record PowerModel(
        StatCaps Caps,
        double BlockWeight,
        double LifestealWeight,
        double RegenWeight,
        double MitigationFlat,
        double MitigationPerLevel,
        int ReferenceLevel,
        double ReferenceDef,
        double ReferencePen);
}
