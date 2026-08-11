using System.Globalization;

namespace SlayIdleRepeat.Core.Rng;

/// <summary>
/// 🔒 `14` §8.1 — the complete stream registry. "A system that needs randomness draws from one
/// of these streams or gets a new row here."
/// </summary>
/// <remarks>
/// <para>
/// One run seed spawns independent named streams so that consuming randomness in one system
/// never shifts another. The names are wire values: they appear in <c>rngStreamStates</c> on
/// every outcome the server sends (`14` §2.3), so changing one is a protocol change, not a
/// rename.
/// </para>
/// <para>
/// They live here as constants because a stream name written as a literal at a call site has a
/// failure mode nothing catches: a typo does not fail, it silently opens a different — perfectly
/// valid — sequence, and the run it corrupts is by definition unreproducible.
/// <see cref="DeterministicRng"/> validates against <see cref="IsRegistered"/> so that even a
/// name arriving as data cannot slip through.
/// </para>
/// </remarks>
public static class RngStreams
{
    /// <summary>Board layout generation; Portal jump draws (`03` §1.1).</summary>
    public const string Board = "board";

    /// <summary>Die rolls and the fair-bag weights.</summary>
    public const string Dice = "dice";

    /// <summary>Perk draft options.</summary>
    public const string Draft = "draft";

    /// <summary>Gear, currency and material drops.</summary>
    public const string Drops = "drops";

    /// <summary>Treasure-tile payout profile draws (`03` §7a.3).</summary>
    public const string Treasure = "treasure";

    /// <summary>Shrine option draws (`03` §7a.5).</summary>
    public const string Shrine = "shrine";

    /// <summary>
    /// Per battle, re-rooted at <c>battleSeed</c> — see <see cref="SeedDerivation.BattleSeed"/>,
    /// which is the sole derivation.
    /// </summary>
    public const string Combat = "combat";

    /// <summary>Event card outcomes.</summary>
    public const string Events = "events";

    /// <summary>The prefix of the one parameterised row, <c>minigame:{index}</c>.</summary>
    public const string MinigamePrefix = "minigame:";

    /// <summary>
    /// The eight fixed rows of `14` §8.1, in the order the specification lists them. The ninth
    /// row is parameterised and cannot be enumerated — build it with <see cref="Minigame"/>.
    /// </summary>
    public static IReadOnlyList<string> FixedNames { get; } = Array.AsReadOnly(new[]
    {
        Board, Dice, Draft, Drops, Treasure, Shrine, Combat, Events,
    });

    /// <summary>The minigame stream for the given index — the parameterised row of `14` §8.1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is negative.</exception>
    public static string Minigame(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return MinigamePrefix + index.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Whether the name is a row of the registry: one of the eight fixed names, or
    /// <c>minigame:</c> followed by a non-negative <see cref="int"/> in canonical decimal form.
    /// </summary>
    /// <remarks>
    /// Ordinal and case-sensitive, and the minigame index must be canonical — <c>minigame:03</c>
    /// is rejected. It is a different string from <c>minigame:3</c> and would therefore be a
    /// different sequence for what every human reading it would call the same minigame.
    /// <para>
    /// Null is not a row, so it answers false rather than throwing: this is a membership
    /// question. A caller for whom null is a bug says so itself — <see cref="DeterministicRng"/>
    /// rejects it before ever asking.
    /// </para>
    /// </remarks>
    public static bool IsRegistered(string? streamName)
    {
        if (streamName is null)
        {
            return false;
        }

        for (var i = 0; i < FixedNames.Count; i++)
        {
            if (string.Equals(FixedNames[i], streamName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return IsMinigame(streamName);
    }

    private static bool IsMinigame(string streamName)
    {
        if (!streamName.StartsWith(MinigamePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var index = streamName.AsSpan(MinigamePrefix.Length);
        if (index.Length == 0 || (index.Length > 1 && index[0] == '0'))
        {
            return false;
        }

        foreach (var character in index)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        // Rejects an index no int can hold, which Minigame could never have produced.
        return int.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }
}
