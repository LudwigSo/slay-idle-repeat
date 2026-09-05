using SlayIdleRepeat.Core.Rng;

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
    public static string For(ulong runSeed, int tileLinearIndex, IReadOnlyList<string> built)
    {
        ArgumentNullException.ThrowIfNull(built);

        if (built.Count == 0)
        {
            throw new ArgumentException(
                "There is no minigame to offer. A pick over nothing would have to invent an id or " +
                "answer an empty one, and both reach MINIGAME_SUBMIT as an unknown minigame — a " +
                "refusal the player reads as the game having broken rather than as a client with " +
                "no screens.",
                nameof(built));
        }

        // The draw shape the whole game uses: seed, a named stream, and the index of the draw. The
        // tile's linear index IS the draw index here, so every tile of a run takes its own draw off
        // one stream and re-opening a tile takes the same one again.
        var draw = Hash64.Of(runSeed, ChoiceStream, unchecked((ulong)tileLinearIndex));

        return built[(int)(draw % (ulong)built.Count)];
    }

    /// <summary>The stream this pick's draws are taken from.</summary>
    /// <remarks>
    /// Named rather than unnamed, and named for this decision alone: a stream shared with another
    /// draw would move this pick whenever that draw's count moved, and the tile a player resumed
    /// onto would offer a different game.
    /// </remarks>
    private const string ChoiceStream = "minigame.choice";
}
