namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>
/// A fork branch's preview: its label and up to 3 icons. <see cref="Icons"/> are the branch's
/// actually-generated tiles, in walk order — never a description of the bias table alone.
/// </summary>
/// <param name="Label">The branch's drawn bias label.</param>
/// <param name="Icons">The first <c>min(3, branch length)</c> tiles of the branch, in walk order.</param>
internal sealed record ForkPreview(ForkLabel Label, IReadOnlyList<TileKind> Icons);
