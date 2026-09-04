namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>Which minigame a Minigame tile offers.</summary>
/// <remarks>
/// <para>
/// 🔴 <b>This is a CLIENT-SIDE, UNENFORCED pick, and the seam is documented here because nothing
/// else says so.</b> A run records that a tile is a Minigame tile and nothing about which minigame it
/// is; <c>MINIGAME_SUBMIT</c> accepts any of the four ids at any Minigame tile. So a modified client
/// could open the most generous arm every time. Moving the decision to the server needs a new
/// <c>RunSnapshot</c> field and a <c>SchemaVersion</c> bump, which is a migration rather than a
/// screen — until then this is the honest description of what the choice is worth.
/// </para>
/// <para>
/// 🔒 <b>Deterministic all the same.</b> An honest client must offer the same game every time the
/// same tile is opened, or a resume would re-roll the arm and a player could reload until the arm
/// they wanted came up — the unenforced pick would become an enforced advantage. The run seed and the
/// tile's own linear index are what make it stable, hashed through the project's one pinned hash so
/// the derivation cannot fork.
/// </para>
/// </remarks>
public static class MinigameChoice
{
    /// <summary>The minigame this tile offers.</summary>
    /// <param name="runSeed">The run's committed seed.</param>
    /// <param name="tileLinearIndex">The tile's linear node index — its identity within the board.</param>
    /// <param name="built">The arms a screen exists for, as <see cref="MinigameArms.Built"/> lists them.</param>
    /// <returns>One of <paramref name="built"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="built"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="built"/> is empty.</exception>
    public static string For(ulong runSeed, int tileLinearIndex, IReadOnlyList<string> built) =>
        throw new NotImplementedException(NotBuiltYet);

    private const string NotBuiltYet =
        "MinigameChoice is a signature-only stub: the Hash64-backed pick lands with the tests " +
        "written against it.";
}
