using System.Globalization;
using SlayIdleRepeat.Core.Content.Dice;

namespace SlayIdleRepeat.Core.Rules.Dice;

/// <summary>
/// What a rolled <see cref="DieFace"/> actually does: how far the hero moves, and any non-movement
/// effect that rides along. Pure: no <c>Run</c>, no RNG, no clock.
/// </summary>
/// <remarks>
/// <see cref="DieFaceKind.Star"/>'s player-chosen movement is a genuine gap: no command payload
/// exists yet for a client to send that choice, so <see cref="Resolve"/> takes an optional
/// <paramref name="playerChosenMovement"/> and returns a <see cref="FaceOutcome"/> whose
/// <see cref="FaceOutcome.RequiresPlayerChoice"/> is true when none was supplied, rather than
/// picking a plausible 1-6 on the caller's behalf. The starting die is all
/// <see cref="DieFaceKind.Pip"/>, so this path is unreachable today and is here for when a Star face
/// becomes reachable.
/// </remarks>
internal static class FaceEffectResolver
{
    /// <summary>Surge heals 12/15/18/21% Max HP at tier 0/1/2/3.</summary>
    private static readonly IReadOnlyList<double> SurgeHealPctByTier = new[] { 0.12, 0.15, 0.18, 0.21 };

    /// <summary>Fortune's landed-tile reward multiplier (Gold/Crowns/drops, never perks).</summary>
    public const double FortuneRewardMultiplier = 2.0;

    /// <summary>Void's re-resolve reward multiplier.</summary>
    public const double VoidReResolveRewardMultiplier = 0.5;

    /// <summary>Chain's roll-again ceiling before a forced stop.</summary>
    public const int ChainMaxLinks = 3;

    /// <summary>
    /// Resolves a rolled face into how far the hero moves and any side effect. Pure.
    /// </summary>
    /// <param name="face">The face that was rolled — after every upgrade source has already applied.</param>
    /// <param name="chainLinksSoFar">
    /// How many <see cref="DieFaceKind.Chain"/> links this roll sequence has already resolved (0 for
    /// the first roll of a turn). Used only to decide whether a further Chain forces a stop.
    /// </param>
    /// <param name="playerChosenMovement">
    /// The player's chosen 1-6 for a <see cref="DieFaceKind.Star"/> face — see the type remarks for
    /// why this is not yet wired to a real command payload.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="face"/> is <see cref="DieFace.IsUnset"/>, <paramref name="chainLinksSoFar"/> is
    /// negative, or <paramref name="playerChosenMovement"/> is supplied outside 1..6.
    /// </exception>
    public static FaceOutcome Resolve(DieFace face, int chainLinksSoFar = 0, int? playerChosenMovement = null)
    {
        if (face.IsUnset)
        {
            throw new ArgumentOutOfRangeException(
                nameof(face), face, "A default DieFace names no face; build one with DieFace.Pip or " +
                "DieFace.Special.");
        }

        if (chainLinksSoFar < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chainLinksSoFar), chainLinksSoFar, "A link count is never negative.");
        }

        if (playerChosenMovement is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerChosenMovement), playerChosenMovement,
                "04 §1's Star face chooses movement 1-6; " + Text(playerChosenMovement!.Value) +
                " is outside that range.");
        }

        return face.Kind switch
        {
            DieFaceKind.Pip => FaceOutcome.Move(face.Value),

            DieFaceKind.Star => playerChosenMovement is { } chosen
                ? FaceOutcome.Move(chosen)
                : FaceOutcome.NeedsPlayerChoice(),

            DieFaceKind.Surge => FaceOutcome.MoveAndHeal(3, SurgeHealPctByTier[face.Tier]),

            DieFaceKind.Fortune => FaceOutcome.MoveAndDoubleReward(4),

            DieFaceKind.Void => FaceOutcome.VoidReResolve(),

            DieFaceKind.Chain => chainLinksSoFar < ChainMaxLinks
                ? FaceOutcome.MoveAndChain(2)
                : FaceOutcome.MoveAndForceStop(2),

            _ => throw new ArgumentOutOfRangeException(
                nameof(face), face.Kind, "04 §1 fixes DieFaceKind at six named members."),
        };
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>What resolving one <see cref="DieFace"/> produces: movement plus at most one side effect.</summary>
/// <remarks>
/// Exactly one of the boolean/nullable members below is meaningful per outcome — the shape mirrors
/// the six face kinds one-to-one rather than a generic "effect bag" nothing asks for.
/// </remarks>
internal readonly record struct FaceOutcome
{
    private FaceOutcome(
        int movement,
        bool requiresPlayerChoice,
        double? healPct,
        bool doubleReward,
        bool reResolveAtHalfReward,
        bool rollAgain,
        bool forcedStop)
    {
        Movement = movement;
        RequiresPlayerChoice = requiresPlayerChoice;
        HealPct = healPct;
        DoubleReward = doubleReward;
        ReResolveAtHalfReward = reResolveAtHalfReward;
        RollAgain = rollAgain;
        ForcedStop = forcedStop;
    }

    /// <summary>Nodes to move. 0 for <see cref="DieFaceKind.Void"/> and for an unresolved <see cref="RequiresPlayerChoice"/> outcome.</summary>
    public int Movement { get; }

    /// <summary>True for a <see cref="DieFaceKind.Star"/> roll with no player choice supplied — see <see cref="FaceEffectResolver"/>'s remarks.</summary>
    public bool RequiresPlayerChoice { get; }

    /// <summary><see cref="DieFaceKind.Surge"/> only — the Max-HP percentage to heal, or <c>null</c>.</summary>
    public double? HealPct { get; }

    /// <summary><see cref="DieFaceKind.Fortune"/> only — the landed tile pays <see cref="FaceEffectResolver.FortuneRewardMultiplier"/>.</summary>
    public bool DoubleReward { get; }

    /// <summary><see cref="DieFaceKind.Void"/> only — re-resolve the current tile at half reward, no movement.</summary>
    public bool ReResolveAtHalfReward { get; }

    /// <summary><see cref="DieFaceKind.Chain"/> only, under the link cap — roll again after this movement.</summary>
    public bool RollAgain { get; }

    /// <summary><see cref="DieFaceKind.Chain"/> only, at the link cap — this movement is the last of the chain.</summary>
    public bool ForcedStop { get; }

    internal static FaceOutcome Move(int nodes) => new(nodes, false, null, false, false, false, false);

    internal static FaceOutcome NeedsPlayerChoice() => new(0, true, null, false, false, false, false);

    internal static FaceOutcome MoveAndHeal(int nodes, double healPct) =>
        new(nodes, false, healPct, false, false, false, false);

    internal static FaceOutcome MoveAndDoubleReward(int nodes) =>
        new(nodes, false, null, true, false, false, false);

    internal static FaceOutcome VoidReResolve() => new(0, false, null, false, true, false, false);

    internal static FaceOutcome MoveAndChain(int nodes) => new(nodes, false, null, false, false, true, false);

    internal static FaceOutcome MoveAndForceStop(int nodes) => new(nodes, false, null, false, false, false, true);
}
