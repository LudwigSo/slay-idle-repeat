namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>
/// `03` §3.1 — a fork branch's preview: its label and up to 3 icons. "The preview must be honest
/// — it lists real contents": <see cref="Icons"/> are the branch's actually-generated tiles, in
/// walk order, never a description of the bias table alone.
/// </summary>
/// <param name="Label">The branch's drawn bias label.</param>
/// <param name="Icons">The first <c>min(3, branch length)</c> tiles of the branch, in walk order.</param>
internal sealed record ForkPreview(ForkLabel Label, IReadOnlyList<TileKind> Icons);
