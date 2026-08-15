using System.Globalization;

namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>
/// Opaque identity of one node in a <see cref="Board"/>'s graph. Distinct from
/// <see cref="BoardNode.LinearIndex"/>: a branch node shares its linear index with the spine
/// node at the same forward distance from its junction (`03` §1.1), so the linear index alone
/// cannot address a single node — <see cref="NodeId"/> can.
/// </summary>
public readonly record struct NodeId(int Value)
{
    public override string ToString() => "N" + Value.ToString(CultureInfo.InvariantCulture);
}
