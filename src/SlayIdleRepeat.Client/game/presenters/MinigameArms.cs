using SlayIdleRepeat.Core.Rules.Board;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>Which of the rules layer's minigames this client has actually built a screen for.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>Derived from <see cref="MinigameView.Ids"/> by subtraction, not written out.</b> Rune Recall
/// is the one arm with no screen, and stating the built set as three literals would leave the day it
/// lands looking exactly like the day before it. Taking the catalogue and removing the unbuilt arm
/// means the count is a fact about both lists, and a fourth arm arriving is a failure that says so.
/// </para>
/// <para>
/// ⚠️ The rules layer accepts a submission for the unbuilt arm perfectly happily — a tile could open
/// on it — so this list is what stops a screen offering a game it cannot draw.
/// </para>
/// </remarks>
public static class MinigameArms
{
    /// <summary>The arm the rules layer knows and this client cannot draw.</summary>
    public const string Unbuilt = "MG_MEMORY_RUNE";

    /// <summary>The minigames a tile may actually open, in the order a screen offers them.</summary>
    public static IReadOnlyList<string> Built => throw new NotImplementedException(NotBuiltYet);

    private const string NotBuiltYet =
        "MinigameArms is a signature-only stub: the built list is derived from MinigameView.Ids, " +
        "which is itself still a stub.";
}
