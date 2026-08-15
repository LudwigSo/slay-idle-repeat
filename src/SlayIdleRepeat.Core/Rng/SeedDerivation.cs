using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rng;

/// <summary>Every named seed derivation in the game — the places where a seed is born, or where one seed spawns another. This is their only home.</summary>
/// <remarks>
/// They live here as functions rather than as an expression callers write out, because each is a
/// contract between two machines: a caller who re-derived <c>battleSeed</c> inline would sooner or
/// later write <c>battleIndex + 1</c> or the wrong stream name, and client and server would
/// disagree about a battle that nobody could reproduce afterwards. A <c>START_RUN</c> handler that
/// spelled the run seed's five-argument expression out inline is one reordered argument away from
/// a silently different, unreproducible universe — the handler calls this class, it does not
/// restate it.
/// <para>
/// Meta-command draws need no function here: <c>CommandSeed</c> is issued by the server host
/// rather than derived, so the draw is just <c>new DeterministicRng(commandSeed, streamName)</c>.
/// </para>
/// </remarks>
public static class SeedDerivation
{
    /// <summary><c>runSeed = Hash64(playerId, chapterId, tierId, utcUnixSeconds, runCounter)</c> — the seed a run is born with and the root of every stream in <c>RngStreams</c>.</summary>
    /// <param name="playerId">The player starting the run. Its <c>Value</c> is the first hashed argument.</param>
    /// <param name="chapterId">The chapter. Minimum 1.</param>
    /// <param name="tier">
    /// The difficulty tier. Widened through <see cref="DifficultyTier"/>'s underlying
    /// <see cref="int"/>, which is why renumbering that enum re-seeds every run in existence.
    /// </param>
    /// <param name="nowUtc">
    /// <c>GameContext.NowUtc</c>. Takes a <see cref="DateTimeOffset"/> rather than a raw
    /// <c>long utcUnixSeconds</c> so the flooring rule (<see cref="DateTimeOffset.ToUnixTimeSeconds"/>)
    /// lives in one place, and so no two adjacent parameters share a type — the transposition bug
    /// this class exists to prevent (swapping <c>chapterId</c> and <c>runCounter</c>) would not compile.
    /// </param>
    /// <param name="runCounter">
    /// <c>Player.BeginRun()</c>'s return — the lifetime runs-started counter after the increment —
    /// which is what makes two runs started in the same second on the same chapter and tier draw
    /// different boards. Zero is accepted; only a negative counter is refused.
    /// </param>
    /// <returns>The run's committed seed. <c>Run.RunSeed</c> holds it and never recomputes it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="chapterId"/> is below 1, <paramref name="tier"/> is not a defined
    /// <see cref="DifficultyTier"/>, or <paramref name="runCounter"/> is negative.
    /// </exception>
    public static ulong RunSeed(
        PlayerId playerId, int chapterId, DifficultyTier tier, DateTimeOffset nowUtc, long runCounter)
    {
        if (chapterId < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chapterId),
                chapterId,
                "Chapters run from 1; a chapter below that names no chapter at all, and hashing it " +
                "would produce a perfectly stable runSeed for a run that cannot be played.");
        }

        if (!Enum.IsDefined(tier))
        {
            throw new ArgumentOutOfRangeException(
                nameof(tier),
                tier,
                "DifficultyTier has no zero member on purpose. Widening an undefined tier into the " +
                "hash would produce a perfectly stable runSeed for a difficulty the game does not have.");
        }

        if (runCounter < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(runCounter),
                runCounter,
                "runCounter is the player's lifetime runs-started counter, after the increment, so " +
                "it counts upwards from zero and is never reset. A negative counter is not a count, " +
                "and seeding from one would produce a run no play could have produced.");
        }

        // playerId.Value rather than playerId: the canonical encoding has exactly two shapes and a
        // PlayerId is the string it wraps, so a default(PlayerId) arrives here as a null string and
        // Hash64Argument refuses it — a loud failure rather than a stable seed for no player.
        return Hash64.Of(
            playerId.Value, chapterId, tier, nowUtc.ToUnixTimeSeconds(), runCounter);
    }

    /// <summary><c>battleSeed = Hash64(runSeed, "combat", battleIndex)</c>. Combat draw <c>i</c> of that battle is then <c>Hash64(battleSeed, "combat", i)</c>, i.e. <c>new DeterministicRng(battleSeed, RngStreams.Combat)</c>.</summary>
    /// <remarks>
    /// The client receives the <c>battleSeed</c> and simulates the identical fight without ever
    /// holding <c>runSeed</c>, which never leaves the server — a client holding it could read
    /// tomorrow's draft options and drops.
    /// <para>A revived battle restarts from draw 0 of the same battle stream: reproducible by construction.</para>
    /// </remarks>
    /// <param name="runSeed">The run's committed seed. Server-side state.</param>
    /// <param name="battleIndex">The battle's index within the run, from 0.</param>
    /// <exception cref="ArgumentOutOfRangeException">The battle index is negative.</exception>
    public static ulong BattleSeed(ulong runSeed, int battleIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(battleIndex);
        return Hash64.Of(runSeed, RngStreams.Combat, (ulong)battleIndex);
    }
}
