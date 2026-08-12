namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// 🔒 One authored effect — the whole vocabulary of `18`, in one shape.
/// </summary>
/// <remarks>
/// <para>
/// `18` §1: <em>"Every effect is the same eight-part shape: op · trigger · condition · target ·
/// value · valueScale · duration · stacking. Everything in the game is a combination of those."</em>
/// Every perk, talent, gear affix, pet aura, mount bonus, status effect, event outcome, shrine buff,
/// curse and boss mechanic is one or more of these — <em>"there is no per-perk, per-talent or
/// per-boss code"</em>.
/// </para>
/// <para>
/// 🔒 <b><see cref="Id"/> is required and authored.</b> `18` §8 closes with <em>"effect-id order
/// means the ascending lexicographic order of effect IDs, not draft order. This removes the last
/// source of order-dependence between client and server"</em>, and `05` §3.1 keys on that order in
/// five places. Not one JSON example in `18` shows the field, so the ruling is recorded here: the id
/// is <b>never derived, never defaulted, never positional</b>. Deriving it from array position or a
/// content hash would put back exactly the order-dependence §8 exists to remove. Ordering goes
/// through <see cref="EffectOrder"/>, which is ordinal.
/// </para>
/// <para>
/// ⚠️ <b>Only <see cref="Id"/> and <see cref="Op"/> are required</b>, including
/// <see cref="Trigger"/> and <see cref="Target"/>, which the eight-part shape names. That is not
/// laxity, it is what `18` authors: §9.1's <c>CP_GLASS_HEART</c> and §7.7's <c>PET_STORMFANG</c>
/// <c>active</c> effects carry no <c>trigger</c> (the pet's ability cooldown is the wrapper's, not
/// the effect's), and §7.4, §7.5, §7.6 and §7.8 carry no <c>target</c>. `18` states no default for
/// either, so none is manufactured here — <c>game-data/README.md</c>: <em>"a hole that is null is
/// greppable, and a hole filled with a plausible-looking number is invisible"</em>. Recorded as
/// errata for the milestone conductor; M2-02 rules on the defaults when it resolves.
/// </para>
/// <para>
/// The keys after the spine are op-specific and are harvested from `18`'s own worked examples
/// (§2.2, §2.4, §7.6, §7.8, §7.9, §9.1, §9.2). Which of them each op admits is stated once, in
/// <c>game-data/schema/effect.schema.json</c>, as a closed partition of the 43 ops into thirteen
/// key shapes — so <c>{"op":"STAT_ADD_PCT","archetype":"SWARM"}</c> is a validation failure rather
/// than a field that silently means nothing.
/// </para>
/// <para>
/// ⚠️ <b>The schema is the only enforcement of which keys go with which op.</b> This record admits
/// combinations the schema rejects — <see cref="Stat"/> is nullable although every <c>STAT_*</c>
/// branch requires it, and <see cref="StatSelector.AllCombat"/> is assignable to
/// <see cref="EffectOp.STAT_SET"/>. That is deliberate (the partition is stated once, not twice),
/// and it has a consequence worth stating: anything that builds an effect <em>in code</em> rather
/// than loading authored JSON — the balance harness (`05` §9), a test, <c>InMemoryGame</c> — is
/// outside that enforcement, and is responsible for building shapes the schema would accept.
/// </para>
/// <para>
/// 🔒 Nothing here resolves, fires, evaluates or stacks. M2-02 (resolution order), M2-03 (op
/// behaviour), M2-04 (trigger firing), M2-05 (condition evaluation) and M2-06 (duration and
/// stacking) all key on this vocabulary; M2-01 is the vocabulary alone. The <b>interpreter</b> they
/// build belongs under <c>SlayIdleRepeat.Core.Rules.Effects</c> and is <c>internal</c> there
/// (`30` §11.2/§11.4); the vocabulary is here, in <c>Content</c>, because `30` §11.4 puts <em>"every
/// definition type"</em> in <c>Content</c> and forbids <c>Content</c> from naming <c>Rules</c> — so
/// the perk, pet, curse and boss definitions M2-07 and M3 author could not reach an
/// <c>EffectDefinition</c> that lived under <c>Rules</c>.
/// </para>
/// <para>
/// ⚠️ <b>Flagged for a doc fix</b>, in the house style of <c>game-data/README.md</c>'s count-
/// discrepancy box: `18` §5 and §10 step 2 name <c>SlayIdleRepeat.Core/Effects</c> and
/// <c>SlayIdleRepeat.Core/Effects/Ops</c>, and `14` §4's tree shows the same flat directory. Neither
/// exists, and neither can: `30` §11.4 — the tree the architecture tests are written against —
/// places <c>Effects/</c> under <c>Rules/</c>, and <c>Domain.PermittedCoreNamespaces</c> is a closed
/// list. `18` §10 is the procedure a future author follows to extend the DSL, so its path is the
/// costly one: the two real homes are <b>this namespace</b> for the vocabulary and
/// <c>Core/Rules/Effects/</c> for the resolver.
/// </para>
/// </remarks>
public sealed record EffectDefinition
{
    /// <summary>
    /// 🔒 The authored, repository-unique effect id — the key `18` §8 and `05` §3.1 order by.
    /// </summary>
    /// <remarks>
    /// Uniqueness is the content pipeline's (`14` §6's duplicate-id failure class, M0-09), not a
    /// second mechanism here.
    /// </remarks>
    public required string Id { get; init; }

    /// <summary>Which of `18` §2's 43 operations this effect performs.</summary>
    public required EffectOp Op { get; init; }

    /// <summary>When the effect fires. <c>null</c> where the author wrote none — see the type remarks.</summary>
    public EffectTrigger? Trigger { get; init; }

    /// <summary>The `18` §4 gate, or <c>null</c> for ungated.</summary>
    public EffectCondition? Condition { get; init; }

    /// <summary>Who the effect applies to. <c>null</c> where the author wrote none — see the type remarks.</summary>
    public EffectTarget? Target { get; init; }

    /// <summary>The authored magnitude, before <see cref="ValueScale"/>.</summary>
    public double? Value { get; init; }

    /// <summary>`18` §1.1's state scaling. <c>null</c> — the default — means <c>effectiveValue = value</c>.</summary>
    public ValueScale? ValueScale { get; init; }

    /// <summary>How long the effect lasts.</summary>
    public EffectDuration? Duration { get; init; }

    /// <summary>How repeat applications combine.</summary>
    public EffectStacking? Stacking { get; init; }

    /// <summary>
    /// Free-form author tags — <c>["offense"]</c> in §1, <c>["drawback"]</c> in §7.5. Also what
    /// <see cref="EffectOp.REMOVE_STATUS"/> means by <em>"a tag group"</em>.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    // ------------------------------------------------------------------ op-specific keys

    /// <summary>The stat a §2.1 stat op or <see cref="EffectOp.STAT_COPY"/> names.</summary>
    public StatSelector? Stat { get; init; }

    /// <summary>What <see cref="Value"/> is a multiple of (`18` §2.2).</summary>
    public ValueMode? ValueMode { get; init; }

    /// <summary>
    /// <see cref="EffectOp.SHIELD"/> only: <em>"the total unbroken ward contributed by that effect
    /// instance is clamped at <c>sourceCapPct × Max HP</c>"</em> (`18` §2.2, <c>PK_TRANSFUSION</c>).
    /// </summary>
    public double? SourceCapPct { get; init; }

    /// <summary>
    /// The status a §2.3 status op names — one of `05` §5's twelve.
    /// </summary>
    /// <remarks>
    /// ⚠️ A string, not an enum. The closed set of twelve is `05` §5's, which is the combat
    /// document's vocabulary and the status milestone's type to declare; restating it here would be
    /// two enums that must agree with nothing making them.
    /// <c>game-data/schema/effect.schema.json</c> still closes the set, enumerating all twelve.
    /// </remarks>
    public string? StatusId { get; init; }

    /// <summary><see cref="EffectOp.STAT_CAP_OVERRIDE"/> only (`18` §7.6).</summary>
    public StatCapKind? CapKind { get; init; }

    /// <summary>
    /// <see cref="EffectOp.SUMMON"/> only — the enemy archetype spawned, e.g. <c>SWARM</c>
    /// (`18` §7.8). Resolves against <c>content/enemies/</c>, which M2 authors.
    /// </summary>
    public string? Archetype { get; init; }

    /// <summary><see cref="EffectOp.SUMMON"/> only — the ceiling on simultaneously living summons.</summary>
    public int? MaxAlive { get; init; }

    /// <summary><see cref="EffectOp.MODIFY_DIE_FACE"/> only — which face is replaced.</summary>
    public DieFaceIndex? FaceIndex { get; init; }

    /// <summary><see cref="EffectOp.MODIFY_DIE_FACE"/> only — the replacement face.</summary>
    public DieFaceSpec? NewFace { get; init; }

    /// <summary>
    /// <see cref="EffectOp.MODIFY_DIE_FACE"/> only — `18` §9.2's own <c>scope</c> key. See
    /// <see cref="DieFaceScope"/> for why this is not <see cref="Duration"/>'s scope.
    /// </summary>
    public DieFaceScope? Scope { get; init; }

    /// <summary>The `18` §2 family <see cref="Op"/> belongs to.</summary>
    public EffectOpFamily Family => EffectOps.FamilyOf(Op);
}
