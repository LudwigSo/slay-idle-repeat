namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>One authored effect — the whole vocabulary of the effect DSL, in one shape.</summary>
/// <remarks>
/// <para>
/// Every effect is the same eight-part shape: op · trigger · condition · target · value ·
/// valueScale · duration · stacking. Every perk, talent, gear affix, pet aura, mount bonus, status
/// effect, event outcome, shrine buff, curse and boss mechanic is one or more of these — there is
/// no per-perk, per-talent or per-boss code.
/// </para>
/// <para>
/// <see cref="Id"/> is required and authored, never derived, defaulted or positional: effect
/// ordering is the ascending lexicographic order of effect ids, and deriving the id from array
/// position or a content hash would reintroduce exactly the order-dependence that ordering exists
/// to remove. Ordering goes through <see cref="EffectOrder"/>, which is ordinal.
/// </para>
/// <para>
/// Only <see cref="Id"/> and <see cref="Op"/> are required, including <see cref="Trigger"/> and
/// <see cref="Target"/>, which the eight-part shape names — some authored effects genuinely carry
/// neither, and no default is manufactured for either here.
/// </para>
/// <para>
/// The keys after the spine are op-specific. Which of them each op admits is stated once, in
/// <c>game-data/schema/effect.schema.json</c>, as a closed partition of the 44 ops into seventeen
/// key shapes — so an op paired with a key it does not admit is a validation failure rather than a
/// field that silently means nothing.
/// </para>
/// <para>
/// The schema is the only enforcement of which keys go with which op: this record admits
/// combinations the schema rejects (<see cref="Stat"/> is nullable although every <c>STAT_*</c>
/// branch requires it), so anything that builds an effect in code rather than loading authored
/// JSON is responsible for building shapes the schema would accept.
/// </para>
/// <para>
/// Nothing here resolves, fires, evaluates or stacks — this type is the vocabulary alone. The
/// interpreter belongs under <c>SlayIdleRepeat.Core.Rules.Effects</c>; the vocabulary is here, in
/// <c>Content</c>, because <c>Content</c> holds every definition type and may not be named by
/// <c>Rules</c>, so the perk, pet, curse and boss definitions could not otherwise reach it.
/// </para>
/// </remarks>
public sealed record EffectDefinition
{
    /// <summary>The authored, repository-unique effect id — the key effect ordering sorts by.</summary>
    /// <remarks>Uniqueness is the content pipeline's, not a second mechanism here.</remarks>
    public required string Id { get; init; }

    /// <summary>Which of the 44 operations this effect performs.</summary>
    public required EffectOp Op { get; init; }

    /// <summary>When the effect fires. <c>null</c> where the author wrote none — see the type remarks.</summary>
    public EffectTrigger? Trigger { get; init; }

    /// <summary>The gate, or <c>null</c> for ungated.</summary>
    public EffectCondition? Condition { get; init; }

    /// <summary>Who the effect applies to. <c>null</c> where the author wrote none — see the type remarks.</summary>
    public EffectTarget? Target { get; init; }

    /// <summary>The authored magnitude, before <see cref="ValueScale"/>.</summary>
    public double? Value { get; init; }

    /// <summary>State scaling. <c>null</c> — the default — means <c>effectiveValue = value</c>.</summary>
    public ValueScale? ValueScale { get; init; }

    /// <summary>How long the effect lasts.</summary>
    public EffectDuration? Duration { get; init; }

    /// <summary>How repeat applications combine.</summary>
    public EffectStacking? Stacking { get; init; }

    /// <summary>Free-form author tags — <c>["offense"]</c>, <c>["drawback"]</c>.</summary>
    /// <remarks>
    /// Not what <see cref="EffectOp.REMOVE_STATUS"/> means by "a tag group": that labels a status;
    /// this array labels the effect. Two vocabularies, spelled the same way — see
    /// <see cref="AuthorTag"/> and <see cref="StatusTag"/> for why they are kept apart at the type
    /// level.
    /// </remarks>
    public IReadOnlyList<string> Tags { get; init; } = [];

    // ------------------------------------------------------------------ op-specific keys

    /// <summary>The stat a stat op or <see cref="EffectOp.STAT_COPY"/> names.</summary>
    public StatSelector? Stat { get; init; }

    /// <summary>
    /// The destination stat of <see cref="EffectOp.STAT_CONVERT"/> and of a
    /// <see cref="StatCapKind.REDIRECT_EXCESS"/> <see cref="EffectOp.STAT_CAP_OVERRIDE"/>.
    /// <see cref="Stat"/> is the source.
    /// </summary>
    /// <remarks>
    /// <c>STAT_CONVERT</c> converts a percentage of stat A into stat B, but the eight-part shape
    /// carries only one <c>stat</c> key, so which side is which was ambiguous. The pre-existing
    /// <c>stat</c> key is the source; this key names where the amount goes.
    /// </remarks>
    public StatId? ToStat { get; init; }

    /// <summary>
    /// The <c>N</c> of <see cref="EffectOp.ATTACK_MULT_NEXT"/>'s "multiply the damage of the next N
    /// attacks" and <see cref="EffectOp.FORCE_CRIT_NEXT"/>'s "the next N attacks always crit".
    /// </summary>
    /// <remarks>
    /// Both ops need a count and — for the first — a multiplier, and the shape carries one
    /// <see cref="Value"/>, which is spent on the multiplier; this key supplies the count.
    /// <c>FORCE_CRIT_NEXT</c> has no multiplier at all and carries only this.
    /// </remarks>
    public int? Charges { get; init; }

    /// <summary>The status tag group <see cref="EffectOp.REMOVE_STATUS"/> clears.</summary>
    /// <remarks>A different vocabulary from <see cref="Tags"/>, and a different C# type.</remarks>
    public StatusTag? StatusTag { get; init; }

    /// <summary>What <see cref="Value"/> is a multiple of.</summary>
    public ValueMode? ValueMode { get; init; }

    /// <summary>
    /// <see cref="EffectOp.SHIELD"/> only: the total unbroken ward contributed by that effect
    /// instance is clamped at <c>sourceCapPct × Max HP</c>.
    /// </summary>
    public double? SourceCapPct { get; init; }

    /// <summary>The status a status op names — one of twelve.</summary>
    /// <remarks>
    /// A string, not an enum: the closed set of twelve is a different milestone's vocabulary to
    /// declare, and restating it here would be two enums that must agree with nothing making them.
    /// The schema still closes the set, enumerating all twelve.
    /// </remarks>
    public string? StatusId { get; init; }

    /// <summary><see cref="EffectOp.STAT_CAP_OVERRIDE"/> only.</summary>
    public StatCapKind? CapKind { get; init; }

    /// <summary>
    /// <see cref="EffectOp.SUMMON"/> only — the enemy archetype spawned, e.g. <c>SWARM</c>.
    /// Resolves against <c>content/enemies/</c>.
    /// </summary>
    public string? Archetype { get; init; }

    /// <summary><see cref="EffectOp.SUMMON"/> only — the ceiling on simultaneously living summons.</summary>
    public int? MaxAlive { get; init; }

    /// <summary>
    /// <see cref="EffectOp.RANDOM_OUTCOME"/> only — the weighted table of mutually exclusive
    /// effects, exactly one of which the op's single draw picks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A new key, not <see cref="Value"/>: there is no collection-shaped <c>value</c> anywhere in
    /// the DSL, so this op carries no <see cref="Value"/> at all.
    /// </para>
    /// <para>
    /// Each row names a sibling effect id — one declared by the same owning content, never an
    /// embedded effect object and never a global registry lookup (see
    /// <see cref="RandomOutcomeEntry"/>). The weights are relative. <c>null</c> on every other op;
    /// <c>EffectOpValidation</c> refuses a borrowed one.
    /// </para>
    /// </remarks>
    public IReadOnlyList<RandomOutcomeEntry>? Outcomes { get; init; }

    /// <summary><see cref="EffectOp.MODIFY_DIE_FACE"/> only — which face is replaced.</summary>
    public DieFaceIndex? FaceIndex { get; init; }

    /// <summary><see cref="EffectOp.MODIFY_DIE_FACE"/> only — the replacement face.</summary>
    public DieFaceSpec? NewFace { get; init; }

    /// <summary>
    /// <see cref="EffectOp.MODIFY_DIE_FACE"/> only — its own <c>scope</c> key. See
    /// <see cref="DieFaceScope"/> for why this is not <see cref="Duration"/>'s scope.
    /// </summary>
    public DieFaceScope? Scope { get; init; }

    /// <summary>The family <see cref="Op"/> belongs to.</summary>
    public EffectOpFamily Family => EffectOps.FamilyOf(Op);
}
