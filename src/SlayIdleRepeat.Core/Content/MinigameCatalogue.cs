namespace SlayIdleRepeat.Core.Content;

/// <summary>The four <c>MG_*</c> minigame ids and the server-authority split between them.</summary>
/// <remarks>
/// <c>MG_CHEST_PICK</c> and <c>MG_DICE_DUEL</c> are server-rolled like everything else.
/// <c>MG_TIMING_BAR</c> and <c>MG_MEMORY_RUNE</c> are genuine skill inputs, so their outcomes are
/// client-asserted instead — a deliberate, documented exception to server authority.
/// </remarks>
internal static class MinigameCatalogue
{
    /// <summary>Three Chests — server-rolled.</summary>
    internal const string ChestPick = "MG_CHEST_PICK";

    /// <summary>Strike the Anvil — client-asserted, legality-validated only.</summary>
    internal const string TimingBar = "MG_TIMING_BAR";

    /// <summary>Dice Duel — server-rolled.</summary>
    internal const string DiceDuel = "MG_DICE_DUEL";

    /// <summary>Rune Recall — client-asserted, legality-validated only.</summary>
    internal const string MemoryRune = "MG_MEMORY_RUNE";

    /// <summary>Whether this is one of the four known minigame ids.</summary>
    internal static bool IsKnown(string minigameId) =>
        minigameId is ChestPick or TimingBar or DiceDuel or MemoryRune;

    /// <summary>
    /// The server draws this minigame's outcome itself; a client-supplied
    /// <see cref="Commands.MinigameSubmitCommand.Result"/> is ignored.
    /// </summary>
    internal static bool IsServerRolled(string minigameId) =>
        minigameId is ChestPick or DiceDuel;

    /// <summary>
    /// The client's claimed outcome is trusted once it passes the legality check (a valid tier, one
    /// submission per tile).
    /// </summary>
    internal static bool IsClientAsserted(string minigameId) =>
        minigameId is TimingBar or MemoryRune;
}
