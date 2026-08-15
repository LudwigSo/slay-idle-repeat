using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// <c>PlayerPower = K_POWER × sqrt(EffectiveHP × DPS)</c>, evaluated against a fixed reference
/// opponent.
/// </summary>
/// <remarks>
/// One of only two public <c>Rules</c> types, alongside the combat simulator — this one backs the
/// Hero screen's power readout and the balance guardrails, which are defined directly over this
/// number (not a reimplementation of it) so a guardrail can't end up grading a copy's arithmetic
/// instead of the game's. The stat-block form here, not a player-aggregate form, is what ships,
/// since no player aggregate exists yet; an aggregate-taking overload can be added later without
/// replacing this one. Two inputs are absent from the data rather than defaulted to a plausible
/// value: pet DPS share (no pets exist yet) and passive regen per second (not a stat a bare stat
/// block can carry) — see <see cref="AbsentPetDpsShare"/> and <see cref="AbsentRegenPerSecond"/>.
/// <c>kPower</c> is <c>null</c> in the shipped data (not yet calibrated), so <see cref="Compute"/>
/// throws rather than defaulting it; use <see cref="PowerIndex"/>, the same quantity without the
/// constant, since every ratio the guardrails take is invariant under it.
/// </remarks>
public static class PowerCalculator
{
    /// <summary>Pet DPS share, absent because no pet content exists yet.</summary>
    /// <remarks>Named rather than inlined so a search for the constant name finds the hole.</remarks>
    internal const double AbsentPetDpsShare = 0.0;

    /// <summary>Passive regen per second, absent because REGEN isn't one of the fourteen stats.</summary>
    internal const double AbsentRegenPerSecond = 0.0;

    /// <summary>The level a loadout is evaluated at when it targets no content.</summary>
    internal const int DefaultEvaluationLevel = 40;

    /// <summary>The document holding every weight below.</summary>
    internal const string Document = "tuning/power_model.json";

    /// <summary><c>"geometric"</c> or <c>"additive_legacy"</c>.</summary>
    internal const string ModelPointer = Document + "#/model";

    /// <summary>The calibration constant. Authored <c>null</c>; see the remarks.</summary>
    internal const string KPowerPointer = Document + "#/kPower";

    /// <summary><c>Block × BLOCK_WEIGHT</c>, the halved-hit weighting of the mitigation formula.</summary>
    internal const string BlockWeightPointer = Document + "#/effectiveHp/blockWeight";

    /// <summary><c>LS_WEIGHT</c>.</summary>
    internal const string LifestealWeightPointer = Document + "#/effectiveHp/lifestealWeight";

    /// <summary><c>REGEN_WEIGHT</c>.</summary>
    internal const string RegenWeightPointer = Document + "#/effectiveHp/regenWeight";

    /// <summary>The mitigation denominator's flat term. Mirrored against the combat caps document.</summary>
    internal const string MitigationFlatPointer = Document + "#/mitigation/flatConstant";

    /// <summary>The mitigation denominator's per-level term. Mirrored against the combat caps document.</summary>
    internal const string MitigationPerLevelPointer = Document + "#/mitigation/perLevelConstant";

    /// <summary>The reference opponent's level, the attacker level of <c>EffectiveHP</c>.</summary>
    internal const string ReferenceLevelPointer = Document + "#/referenceOpponent/level";

    /// <summary>The reference opponent's DEF, what the hero's PEN is measured against.</summary>
    internal const string ReferenceDefPointer = Document + "#/referenceOpponent/def";

    /// <summary>The reference opponent's PEN, applied to hero DEF.</summary>
    internal const string ReferencePenPointer = Document + "#/referenceOpponent/pen";

    /// <summary>The default form, and the form every table in the document is authored against.</summary>
    internal const string GeometricModel = "geometric";

    /// <summary>The superseded additive form, retained in data so the change is reversible.</summary>
    internal const string AdditiveLegacyModel = "additive_legacy";

    /// <summary>
    /// <c>EffectiveHP</c> — how much of the reference opponent's output the block absorbs before
    /// dying.
    /// </summary>
    /// <param name="stats">The stat block. Caps are applied first.</param>
    /// <param name="content">The loaded content snapshot; every weight is read from it.</param>
    /// <exception cref="MissingContentException">A pointer is absent.</exception>
    /// <exception cref="UnauthorisedTunableException">A weight is authored <c>null</c>.</exception>
    public static double EffectiveHp(ActorStats stats, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(content);

        var model = Read(content);

        // The reference opponent as the attacker: its level drives the mitigation denominator and
        // its pen (authored 0) reduces the defender's DEF.
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
    /// <c>DPS</c> — sustained damage per second against the reference opponent.
    /// </summary>
    /// <param name="stats">The stat block. Caps are applied first.</param>
    /// <param name="level">
    /// The attacker's level, which drives the per-level term of the mitigation denominator — a
    /// stat-identical hero is mitigated less at Legend Level 200 than at 10. Callers pass the
    /// content's enemy level when a loadout targets content, and
    /// <see cref="DefaultEvaluationLevel"/> otherwise.
    /// </param>
    /// <param name="content">The loaded content snapshot.</param>
    public static double Dps(ActorStats stats, int level, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(content);

        var model = Read(content);

        // The hero as the attacker: the hero's PEN against the reference's DEF, and the hero's own
        // level in the mitigation denominator.
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
    /// <c>sqrt(EffectiveHP × DPS)</c> — power without <c>K_POWER</c>.
    /// </summary>
    /// <remarks>
    /// The member every ratio should use, since <c>kPower</c> is authored <c>null</c> and every
    /// guardrail compares one power to another, making the constant cancel out. Refuses to evaluate
    /// an <c>additive_legacy</c> document rather than silently answering in that shape: the additive
    /// form isn't monotone in both terms, so a build with vast EffectiveHP and no damage would score
    /// highly and then lose every fight.
    /// </remarks>
    /// <exception cref="ContentTypeMismatchException">
    /// <c>power_model.json#/model</c> is <c>additive_legacy</c> — see the remarks — or is neither
    /// authored form.
    /// </exception>
    public static double PowerIndex(ActorStats stats, int level, ContentSnapshot content) =>
        DeterminismRounding.Round(Math.Sqrt(EffectiveHp(stats, content) * Dps(stats, level, content)));

    /// <summary>
    /// <c>PlayerPower = K_POWER × sqrt(EffectiveHP × DPS)</c>, the absolute scalar.
    /// </summary>
    /// <remarks>
    /// Throws on the shipped data today, deliberately: <c>kPower</c> is authored <c>null</c>, and a
    /// hole is not coerced to a default at read time. Use <see cref="PowerIndex"/> instead, or derive
    /// the constant from the reference par build.
    /// </remarks>
    /// <exception cref="UnauthorisedTunableException"><c>kPower</c> is <c>null</c>.</exception>
    public static double Compute(ActorStats stats, int level, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var kPower = content.ReadDouble(KPowerPointer);
        return DeterminismRounding.Round(kPower * PowerIndex(stats, level, content));
    }

    /// <summary>
    /// Caps applied after aggregation: a player at the crit cap gains nothing from more crit, and
    /// power must reflect that or it recommends gear that does nothing.
    /// </summary>
    private static double Capped(ActorStats stats, StatId stat, PowerModel model) =>
        model.Caps.Apply(stat, stats[stat]);

    /// <summary>The mitigation curve, read from the dials mirrored against the combat caps document.</summary>
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

    /// <summary>The reference-opponent and mitigation weights, read once per evaluation.</summary>
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
