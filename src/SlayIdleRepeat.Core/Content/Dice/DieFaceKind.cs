namespace SlayIdleRepeat.Core.Content.Dice;

/// <summary>The six faces a die can carry.</summary>
/// <remarks>
/// <para>
/// Lives under <c>Content/Dice</c>, not <c>Rules/Dice</c>, even though it is the dice system's
/// vocabulary: the layering forbids <c>Events</c> from naming <c>Rules</c>, and the
/// <c>DiceRolled</c> event carries a face — so a face type under <c>Rules</c> could never be
/// carried by the event that names it. <c>Content</c> is the layer every other one may see.
/// </para>
/// <para>
/// No zero member, matching every other closed DSL enum in this codebase: a forgotten assignment
/// reads as <c>default</c>, which <see cref="Enum.IsDefined(Type,object)"/> rejects rather than
/// silently naming <see cref="Pip"/>.
/// </para>
/// </remarks>
public enum DieFaceKind
{
    /// <summary>Move that many nodes. The only kind <see cref="DieFace.Value"/> means anything for.</summary>
    Pip = 1,

    /// <summary>★ — choose your own movement, 1-6. "The most valuable face in the game."</summary>
    Star,

    /// <summary>⚡ — move 3, then heal a Max-HP percentage that scales with <see cref="DieFace.Tier"/>.</summary>
    Surge,

    /// <summary>✦ — move 4; the landed tile pays double (Gold/Crowns/drops, never perks).</summary>
    Fortune,

    /// <summary>○ — move 0, stay, immediately re-resolve the current tile at 50% reward. Curse-inflicted only; never an upgrade target.</summary>
    Void,

    /// <summary>⛓ — move 2, then roll again (chains up to 3x, then a forced stop).</summary>
    Chain,
}
