using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Services.Persistence;

/// <summary>
/// The two keys this layer stores state under, and the only place their spelling is decided.
/// </summary>
/// <remarks>
/// <para>
/// One committed row per player (<c>player.&lt;playerId&gt;</c>) holding the player and whatever run
/// the player is in, plus one archived row per finished run (<c>run.&lt;runId&gt;</c>). The player
/// row is the commit point: player and run move under one key, in one write, so a crash can never
/// tear one from the other.
/// </para>
/// <para>
/// Both methods refuse an id whose text would leave <see cref="ILocalCachePort"/>'s closed key space
/// rather than escaping or truncating it. Two different ids that mangled to the same key would read
/// each other's state, and an id that mangled to a key the file-backed store rejects would work in
/// memory and fail on a device.
/// </para>
/// </remarks>
public static class SliceKeys
{
    /// <summary>The key a player's committed slice is stored under.</summary>
    /// <param name="player">The player. Its text must lie inside the cache's key space.</param>
    /// <returns><c>"player."</c> followed by the id's text.</returns>
    /// <exception cref="ArgumentException">
    /// The id's text is empty, or holds a character outside <c>A-Z</c>, <c>a-z</c>, <c>0-9</c>,
    /// <c>.</c>, <c>_</c> and <c>-</c>.
    /// </exception>
    public static string ForPlayer(PlayerId player) =>
        "player." + InsideKeySpace(player.Value, nameof(player), "player");

    /// <summary>The key a finished run is archived under.</summary>
    /// <param name="run">The run. Its text must lie inside the cache's key space.</param>
    /// <returns><c>"run."</c> followed by the id's text.</returns>
    /// <exception cref="ArgumentException">
    /// The id's text is empty, or holds a character outside <c>A-Z</c>, <c>a-z</c>, <c>0-9</c>,
    /// <c>.</c>, <c>_</c> and <c>-</c>.
    /// </exception>
    public static string ForRun(RunId run) =>
        "run." + InsideKeySpace(run.Value, nameof(run), "run");

    /// <summary>The id's text, once it is known to be a key the cache will accept.</summary>
    /// <param name="text">The id's text. Null on an id whose constructor never ran.</param>
    /// <param name="parameterName">The refused parameter, so the failure points at the caller's argument.</param>
    /// <param name="subject">What the id names, in the failure's own words.</param>
    /// <remarks>
    /// Indexed rather than a LINQ pass: a key is built two or three times per command, and enumerating
    /// a <c>string</c> as a sequence allocates on every one of them.
    /// </remarks>
    private static string InsideKeySpace(string? text, string parameterName, string subject)
    {
        if (IsKeySpelling(text))
        {
            return text!;
        }

        throw new ArgumentException(
            "This " + subject + " id cannot be stored: '" + (text ?? "<no text at all>") + "' is outside " +
            "the cache's key space, which is non-empty and made only of A-Z, a-z, 0-9, '.', '_' and " +
            "'-'. Escaping or trimming it here would let two different " + subject + " ids mangle to " +
            "one key and read each other's state, and a key the file-backed store rejects would work " +
            "in memory and fail on a device. Issue ids inside the key space instead.",
            parameterName);
    }

    private static bool IsKeySpelling(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var character in text.AsSpan())
        {
            if (!IsKeyCharacter(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsKeyCharacter(char character) =>
        character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')
                     or '.' or '_' or '-';
}
