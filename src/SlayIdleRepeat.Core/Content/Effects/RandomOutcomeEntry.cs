namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// One row of <see cref="EffectOp.RANDOM_OUTCOME"/>'s <c>outcomes</c> table — an effect named by
/// id, and the weight it is drawn with.
/// </summary>
/// <param name="EffectId">
/// The id of the effect this row fires — a sibling reference: an id declared by the same owning
/// content as the <c>RANDOM_OUTCOME</c> itself. Never an embedded effect, and never a global
/// registry lookup. See the remarks.
/// </param>
/// <param name="Weight">
/// The row's share of the draw: a finite, non-negative number. Zero disables the row, since the
/// weighted-pick walk's comparison is strict, which is how authored content turns an outcome off
/// without renumbering the ones around it.
/// </param>
/// <remarks>
/// <para>
/// Why a row names an id rather than carrying an effect: there is no effect registry, and this
/// shape does not want one. An effect is never a file of its own — it is embedded in the content
/// that owns it (a perk, an enemy row, a boss script) — so <c>effectId</c> resolves within the same
/// owning content, against the effect set that content already declares.
/// </para>
/// <para>
/// The outcomes are ordinary mechanics and have to exist anyway on the owning content's own effect
/// list to be registered, telegraphed and given a position in the battle's effect table; inlining
/// them here would author each one twice. Nothing else in the DSL nests an effect inside an effect,
/// and a same-content reference needs no registry to resolve.
/// </para>
/// <para>
/// The weights are relative, not probabilities: two outcome tables can use entirely different
/// totals for the same op, with no branch anywhere — that is the point of the key.
/// </para>
/// </remarks>
public readonly record struct RandomOutcomeEntry(string EffectId, double Weight);
