namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// 🔒 `03` §6's four <c>MG_*</c> minigame ids and `03` §6.2's server-authority split between them.
/// </summary>
/// <remarks>
/// <para>
/// §6.2, quoted in full: <em>"<c>MG_CHEST_PICK</c> and <c>MG_DICE_DUEL</c> are server-rolled like
/// everything else. <c>MG_TIMING_BAR</c> and <c>MG_MEMORY_RUNE</c> are genuine skill inputs, and
/// their outcomes are client-asserted — a deliberate, documented exception to `14` §2.1."</em> The
/// split is a fact about the <em>id</em>, not about the command, so it lives here rather than being
/// re-derived inline at the one call site that needs it (<see cref="Handlers.MinigameSubmit"/>).
/// </para>
/// </remarks>
internal static class MinigameCatalogue
{
    /// <summary>Three Chests — server-rolled (`03` §6.2).</summary>
    internal const string ChestPick = "MG_CHEST_PICK";

    /// <summary>Strike the Anvil — client-asserted, legality-validated only (`03` §6.2).</summary>
    internal const string TimingBar = "MG_TIMING_BAR";

    /// <summary>Dice Duel — server-rolled (`03` §6.2).</summary>
    internal const string DiceDuel = "MG_DICE_DUEL";

    /// <summary>Rune Recall — client-asserted, legality-validated only (`03` §6.2).</summary>
    internal const string MemoryRune = "MG_MEMORY_RUNE";

    /// <summary>Every `03` §6 minigame id.</summary>
    internal static bool IsKnown(string minigameId) =>
        minigameId is ChestPick or TimingBar or DiceDuel or MemoryRune;

    /// <summary>
    /// 🔒 `03` §6.2 — the server draws this minigame's outcome itself; a client-supplied
    /// <see cref="Commands.MinigameSubmitCommand.Result"/> is ignored.
    /// </summary>
    internal static bool IsServerRolled(string minigameId) =>
        minigameId is ChestPick or DiceDuel;

    /// <summary>
    /// 🔒 `03` §6.2 — the client's claimed outcome is trusted once it passes the legality check (a
    /// valid tier, one submission per tile). The documented exception to `14` §2.1.
    /// </summary>
    internal static bool IsClientAsserted(string minigameId) =>
        minigameId is TimingBar or MemoryRune;
}
