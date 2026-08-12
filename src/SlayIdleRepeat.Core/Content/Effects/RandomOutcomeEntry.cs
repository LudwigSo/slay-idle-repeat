namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// 🔒 One row of <see cref="EffectOp.RANDOM_OUTCOME"/>'s <c>outcomes</c> table — an effect named by
/// <b>id</b>, and the weight it is drawn with.
/// </summary>
/// <param name="EffectId">
/// 🔒 The `18` §8 id of the effect this row fires. A <b>reference</b>, never an embedded effect
/// (R19): every other place in the DSL that reaches another effect does so by id, and an effect
/// nested inside an effect would sit outside `18` §8's ascending-effect-id ordering, outside the
/// schema's op-to-key partition, and outside every id-keyed lookup `05` §3.1 and `05` §4 make.
/// </param>
/// <param name="Weight">
/// The row's share of the draw, in `14` §8.0's <c>WeightedPick</c> terms: a finite, non-negative
/// number. 🔒 <b>Zero disables the row</b> — the walk's comparison is strict, so a zero-weight row is
/// unreachable wherever it sits in the table, and that is how authored content turns an outcome off
/// without renumbering the ones around it.
/// </param>
/// <remarks>
/// ⚠️ The weights are <b>relative</b>, not probabilities: `17` §9's phase 1 <em>Roll of Fate</em>
/// writes <c>2 / 2 / 2</c> over three outcomes and its phase 2 writes <c>4 / 2</c> over two. Both are
/// legal tables for the same op with no branch anywhere — which is the point of the key.
/// </remarks>
public readonly record struct RandomOutcomeEntry(string EffectId, double Weight);
