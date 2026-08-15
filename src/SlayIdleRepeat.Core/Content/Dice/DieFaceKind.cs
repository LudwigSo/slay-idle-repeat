namespace SlayIdleRepeat.Core.Content.Dice;

/// <summary>
/// 🔒 `04` §1 — the six faces a die can carry. This is the real enum
/// <see cref="Content.Effects.DieFaceSpec"/>'s own remarks point to: "the dice system's vocabulary
/// and the dice milestone's type to declare" (M3-04).
/// </summary>
/// <remarks>
/// <para>
/// Lives under <c>Content/Dice</c>, not <c>Rules/Dice</c>, even though it is the dice
/// <em>system's</em> vocabulary. `30` §11.4's layering forbids <c>Events</c> from naming
/// <c>Rules</c>, and `30` §7 writes <c>DiceRolled</c> as <c>(int Sequence, DieFace Face)</c> — so a
/// face type under <c>Rules</c> could never be carried by the event that names it. <c>Content</c>
/// is the layer every other one may see, which is exactly what a shared vocabulary needs.
/// </para>
/// <para>
/// No zero member, matching every other closed DSL enum in this codebase (<see cref="ValueMode"/>,
/// <see cref="StatCapKind"/>...): a forgotten assignment reads as <c>default</c>, which
/// <see cref="Enum.IsDefined(Type,object)"/> rejects rather than silently naming <see cref="Pip"/>.
/// </para>
/// </remarks>
public enum DieFaceKind
{
    /// <summary>Move that many nodes (`04` §1). The only kind <see cref="DieFace.Value"/> means anything for.</summary>
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
