using System.Globalization;

namespace SlayIdleRepeat.Core.Rng;

/// <summary>The complete RNG stream registry. A system that needs randomness draws from one of these streams or gets a new row here.</summary>
/// <remarks>
/// One run seed spawns independent named streams so that consuming randomness in one system never
/// shifts another. The names are wire values, appearing in <c>rngStreamStates</c> on every outcome
/// the server sends, so changing one is a protocol change, not a rename.
/// <para>
/// They live here as constants rather than literals at each call site because a typo in a literal
/// does not fail — it silently opens a different, perfectly valid sequence, corrupting the run in a
/// way that is by definition unreproducible. <see cref="DeterministicRng"/> validates against
/// <see cref="IsRegistered"/> so even a name arriving as data cannot slip through.
/// </para>
/// </remarks>
public static class RngStreams
{
    /// <summary>Board layout generation; Portal jump draws.</summary>
    public const string Board = "board";

    /// <summary>Die rolls and the fair-bag weights.</summary>
    public const string Dice = "dice";

    /// <summary>Perk draft options.</summary>
    public const string Draft = "draft";

    /// <summary>Gear, currency and material drops.</summary>
    public const string Drops = "drops";

    /// <summary>Treasure-tile payout profile draws.</summary>
    public const string Treasure = "treasure";

    /// <summary>Shrine option draws.</summary>
    public const string Shrine = "shrine";

    /// <summary>Per battle, re-rooted at <c>battleSeed</c> — see <see cref="SeedDerivation.BattleSeed"/>, the sole derivation.</summary>
    public const string Combat = "combat";

    /// <summary>Event card outcomes.</summary>
    public const string Events = "events";

    /// <summary>Forge draws: a fusion's affix re-roll and an enhancement attempt.</summary>
    /// <remarks>
    /// A row of its own rather than borrowing <see cref="Drops"/>, which is what a drop draws from.
    /// A fusion and an enhancement are the opposite of a drop — the deterministic route to an item a
    /// player builds towards rather than one the game hands them — and reusing the drop stream's name
    /// would make a replay of either read as a drop that never happened.
    /// <para>
    /// Both draws are out-of-run, so this name never appears in a run's <c>rngStreamStates</c>: a
    /// meta draw starts at index 0 each command and persists no counter.
    /// </para>
    /// </remarks>
    public const string Forge = "forge";

    /// <summary>The prefix of the one parameterised row, <c>minigame:{index}</c>.</summary>
    public const string MinigamePrefix = "minigame:";

    /// <summary>The nine fixed rows of the registry. The tenth row is parameterised and cannot be enumerated — build it with <see cref="Minigame"/>.</summary>
    public static IReadOnlyList<string> FixedNames { get; } = Array.AsReadOnly(new[]
    {
        Board, Dice, Draft, Drops, Treasure, Shrine, Combat, Events, Forge,
    });

    /// <summary>The minigame stream for the given index — the parameterised row of the registry.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is negative.</exception>
    public static string Minigame(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return MinigamePrefix + index.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Whether the name is a row of the registry: one of the <see cref="FixedNames"/>, or
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
