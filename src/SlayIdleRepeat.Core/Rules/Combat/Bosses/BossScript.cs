namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// One boss's power-to-stats row: the four coefficients that replace a shared archetype's.
/// </summary>
/// <remarks>
/// Only the four coefficients are here. The secondaries — CRIT, CDMG, DODGE, LIFESTEAL — are the
/// baseline every boss shares, so they are authored once and handed to
/// <see cref="BossEncounterRequest.Baseline"/> rather than repeated on every row.
/// </remarks>
/// <param name="Hp">The HP coefficient.</param>
/// <param name="Atk">The ATK coefficient.</param>
/// <param name="Def">The DEF coefficient.</param>
/// <param name="Aspd">The ASPD coefficient.</param>
internal readonly record struct BossCoefficients(double Hp, double Atk, double Def, double Aspd);

/// <summary>
/// One mechanic inside a phase block — an effect named by id, plus the wind-up a damaging one needs.
/// </summary>
/// <param name="EffectId">
/// The id of the authored effect — a sibling of this script, resolved against
/// <see cref="BossEncounterRequest.Effects"/>. A reference rather than an embedded effect object: the
/// mechanic has to be on <c>ActorPlan.Effects</c> to be registered, telegraphable and
/// index-resolvable in the battle's effect table, so an embedded copy would be a second identity for
/// one mechanic.
/// </param>
/// <param name="TelegraphSeconds">
/// The visible wind-up, in seconds, or <c>null</c> where the mechanic needs none. Boss-script data,
/// not a DSL key and not an engine constant — see <see cref="BossTelegraphs"/> for the rules a lead
/// has to satisfy.
/// </param>
internal readonly record struct BossMechanic(string EffectId, double? TelegraphSeconds = null);

/// <summary>One of a boss's three phase blocks — the mechanics live while that phase does.</summary>
/// <remarks>
/// Every mechanic in every block goes onto <see cref="ActorPlan.Effects"/>, phases 2 and 3 included,
/// and <see cref="BossPhaseController.EnterInitialPhase"/> then de-anchors the later two — otherwise
/// phase 2's and phase 3's effects would be missing from the battle's effect table, which is built
/// once from the opening roster.
/// </remarks>
internal sealed record BossPhaseBlock
{
    /// <summary>Which phase this block is — <c>1</c>, <c>2</c> or <c>3</c>.</summary>
    public required int Phase { get; init; }

    /// <summary>The block's mechanics. May be empty.</summary>
    public required IReadOnlyList<BossMechanic> Mechanics { get; init; }
}

/// <summary>
/// The authoring contract each boss script is written against — every boss expressed purely in the
/// effect DSL, with zero bespoke boss code.
/// </summary>
/// <remarks>
/// The two universal built-ins are not authored on a script: <c>SYS_ENRAGE</c> and the phase-3
/// STUN/FREEZE immunities are implemented once and attached to every boss by
/// <see cref="BossEncounterBuilder"/>, and no script may name them.
/// </remarks>
internal sealed record BossScript
{
    /// <summary>The three phases every boss has, no more and no fewer.</summary>
    internal const int PhaseCount = 3;

    /// <summary>The boss's content id — <c>BOSS_THORNMAW</c>. Also its actor id.</summary>
    public required string Id { get; init; }

    /// <summary>The per-boss coefficient row.</summary>
    public required BossCoefficients Coefficients { get; init; }

    /// <summary>
    /// The fraction of boss power this boss's summons are derived at, or <c>null</c> where it
    /// authors no <c>SUMMON</c>.
    /// </summary>
    /// <remarks>
    /// Per boss rather than per mechanic: every summoning boss summons exactly one archetype, and
    /// several re-summon the same adds as top-ups, so a per-mechanic fraction could let one named
    /// add stand on the field at two different powers inside one fight.
    /// <see cref="BossSummonSource"/> still refuses a fraction outside the band.
    /// </remarks>
    public double? AddsPowerFraction { get; init; }

    /// <summary>
    /// Exactly <see cref="PhaseCount"/> blocks, numbered <c>1</c>, <c>2</c>, <c>3</c>, in that
    /// order. <see cref="BossEncounterBuilder"/> refuses anything else.
    /// </summary>
    public required IReadOnlyList<BossPhaseBlock> Phases { get; init; }
}
