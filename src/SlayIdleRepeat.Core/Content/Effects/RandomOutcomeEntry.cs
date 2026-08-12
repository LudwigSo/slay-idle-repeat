namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// 🔒 One row of <see cref="EffectOp.RANDOM_OUTCOME"/>'s <c>outcomes</c> table — an effect named by
/// <b>id</b>, and the weight it is drawn with.
/// </summary>
/// <param name="EffectId">
/// 🔒 The `18` §8 id of the effect this row fires — a <b>sibling reference</b>: an id declared by the
/// <b>same owning content</b> as the <c>RANDOM_OUTCOME</c> itself. Never an embedded effect, and
/// never a global registry lookup. See the remarks.
/// </param>
/// <param name="Weight">
/// The row's share of the draw, in `14` §8.0's <c>WeightedPick</c> terms: a finite, non-negative
/// number. 🔒 <b>Zero disables the row</b> — the walk's comparison is strict, so a zero-weight row is
/// unreachable wherever it sits in the table, and that is how authored content turns an outcome off
/// without renumbering the ones around it.
/// </param>
/// <remarks>
/// <para>
/// ═══ 🔒 <b>WHY A ROW NAMES AN ID RATHER THAN CARRYING AN EFFECT — THE SIBLING-SCOPE ARGUMENT</b> ═══
/// </para>
/// <para>
/// 🔴 <b>There is no effect registry, and this shape does not want one.</b> An effect is never a file
/// of its own: it is <b>embedded in the content that owns it</b> — a perk, an enemy row, a boss
/// script. So <c>effectId</c> here is resolved <b>within the same owning content</b>, against the
/// effect set that content already declares. The boss encounter builder holds exactly that set while
/// it is building a boss, which is why the reference resolves with nothing global in sight, and it
/// refuses a row naming an id the same script does not declare.
/// </para>
/// <para>
/// The three reasons the row is a reference rather than a nested effect object:
/// </para>
/// <list type="number">
///   <item>
///   🔒 <b>The outcomes are ordinary phase mechanics and have to exist anyway.</b> `17` §9's three
///   <em>Roll of Fate</em> results must be on <c>ActorPlan.Effects</c> to be registered by
///   <c>RegisterHoldings</c>, to be telegraphable, and to have a position in the battle's effect
///   table — the table `05` §7's <c>Telegraph</c> and <c>RunEffectQueued</c> index into. Inlining
///   them here would author each one <b>twice</b> and give the log two identities for one mechanic.
///   </item>
///   <item>
///   🔒 <b>Nothing else in the DSL nests an effect inside an effect.</b>
///   <c>effect.schema.json</c>'s <c>oneOf</c> is a closed partition over the op-to-key mapping and
///   has no branch shaped like an effect object inside an <c>outcomes</c> row; adding one would put
///   a second effect outside `18` §8's ascending-effect-id ordering and outside every id-keyed
///   lookup `05` §3.1 and `05` §4 make.
///   </item>
///   <item>
///   🔒 <b>A same-content reference needs no registry to resolve.</b> That is what makes the shape
///   independent of whether the repository ever grows one — the resolver is the owner of the
///   content, and it is holding the sibling set at the moment it has to answer.
///   </item>
/// </list>
/// <para>
/// ⚠️ The weights are <b>relative</b>, not probabilities: `17` §9's phase 1 <em>Roll of Fate</em>
/// writes <c>2 / 2 / 2</c> over three outcomes and its phase 2 writes <c>4 / 2</c> over two. Both are
/// legal tables for the same op with no branch anywhere — which is the point of the key.
/// </para>
/// </remarks>
public readonly record struct RandomOutcomeEntry(string EffectId, double Weight);
