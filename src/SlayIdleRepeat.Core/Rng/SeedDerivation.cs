using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rng;

/// <summary>
/// Every named seed derivation in the game — the places where a seed is born (`02` §2) or where
/// one seed spawns another (`14` §8.1). This is their only home.
/// </summary>
/// <remarks>
/// <para>
/// They live here as functions rather than as an expression callers write out because each is a
/// contract between two machines. A caller who re-derived <c>battleSeed</c> inline would sooner
/// or later write <c>battleIndex + 1</c>, or the wrong stream name, and the client and server
/// would then disagree about a battle that nobody could reproduce afterwards.
/// </para>
/// <para>
/// 🔒 <b>The seam M1 needs is here, not in a handler.</b> <c>02</c> §2 has <c>START_RUN</c> compute
/// <c>runSeed = Hash64(playerId, chapterId, tierId, utcUnixSeconds, runCounter)</c> — with
/// <c>utcUnixSeconds</c> taken from <c>GameContext.NowUtc</c> and <c>runCounter</c> from the
/// player's lifetime runs-started counter — <i>inside</i> <c>GameRules.Apply</c>. That derivation
/// belongs in this class as a <c>RunSeed(...)</c> function for exactly the reason above: the run
/// seed is a five-argument expression whose argument order <b>is</b> the encoding, and a
/// <c>START_RUN</c> handler that spells it out inline is one reordered argument away from a
/// silently different, perfectly stable, unreproducible universe. The handler calls it; it does
/// not restate it.
/// </para>
/// <para>
/// The third rule of `14` §8.1 — meta-command draws, <c>Hash64(CommandSeed, s, i)</c> with no
/// persisted counter — needs no function here: <c>CommandSeed</c> is issued by the server host
/// rather than derived (`30` §3), so the draw is just
/// <c>new DeterministicRng(commandSeed, streamName)</c>.
/// </para>
/// </remarks>
public static class SeedDerivation
{
    /// <summary>
    /// 🔒 `02` §2 — <c>runSeed = Hash64(playerId, chapterId, tierId, utcUnixSeconds, runCounter)</c>,
    /// the seed a run is born with and the root of every stream in <c>RngStreams</c>.
    /// </summary>
    /// <param name="playerId">The player starting the run. Its <c>Value</c> is the first hashed argument.</param>
    /// <param name="chapterId">The chapter (`02` §1, <c>chapter.schema.json</c>'s <c>"minimum": 1</c>).</param>
    /// <param name="tier">
    /// `02` §2's <c>tierId</c>. Widened through <see cref="DifficultyTier"/>'s underlying
    /// <see cref="int"/>, which is why renumbering that enum re-seeds every run in existence.
    /// </param>
    /// <param name="nowUtc">
    /// 🔒 <c>GameContext.NowUtc</c>. It takes a <see cref="DateTimeOffset"/> rather than
    /// <c>long utcUnixSeconds</c> <b>deliberately</b>, and for two reasons.
    /// <see cref="DateTimeOffset.ToUnixTimeSeconds"/> <em>is</em> `02` §2's
    /// <c>floor(NowUtc as Unix seconds)</c>, so the flooring rule lives in the one place instead of
    /// at every call site that could round instead. And it means <b>no two adjacent parameters share
    /// a type</b>, which makes the transposition this whole class exists to prevent unrepresentable:
    /// a caller who swapped <c>chapterId</c> and <c>runCounter</c> would not compile.
    /// <para>
    /// ⚠️ There is deliberately <b>no zero-offset guard</b>, unlike the aggregates.
    /// <c>ToUnixTimeSeconds</c> <em>converts</em>, so an instant expressed with any offset yields the
    /// same seconds as the same instant expressed in UTC — the ambiguity the aggregates refuse
    /// (a <c>DateTimeOffset</c> that hashes identically while comparing unequal) cannot arise here,
    /// because only the converted number is hashed.
    /// </para>
    /// </param>
    /// <param name="runCounter">
    /// `02` §2's <c>runCounter</c>: <c>Player.BeginRun()</c>'s return, which is the lifetime
    /// runs-started counter <b>after</b> the increment. It is what makes two runs started in the same
    /// second on the same chapter and tier draw different boards. ⚠️ Zero is <b>not</b> refused:
    /// nothing in `02` §2 says the counter's first value is 1, and refusing 0 would be a claim the
    /// documents do not authorise (steering S6). Only a negative counter is refused, because a count
    /// that has gone below zero is not a count.
    /// </param>
    /// <returns>The run's committed seed. <c>Run.RunSeed</c> holds it and never recomputes it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="chapterId"/> is below 1, <paramref name="tier"/> is not a defined
    /// <see cref="DifficultyTier"/>, or <paramref name="runCounter"/> is negative.
    /// </exception>
    public static ulong RunSeed(
        PlayerId playerId, int chapterId, DifficultyTier tier, DateTimeOffset nowUtc, long runCounter)
    {
        // The three guards say WHICH rule refused and cite it, rather than leaning on the framework's
        // "must be greater than or equal to '1'" — a seed derivation that failed with an ordinary
        // bounds message is one a reader fixes by widening the bound (steering S2).
        if (chapterId < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chapterId),
                chapterId,
                "02 §1 runs chapters from 1 and chapter.schema.json sets \"minimum\": 1. There is " +
                "deliberately no upper bound here — content/chapters/ is empty (M3-14) — but a " +
                "chapter below 1 names no chapter at all, and hashing it would produce a perfectly " +
                "stable runSeed for a run that cannot be played.");
        }

        if (!Enum.IsDefined(tier))
        {
            throw new ArgumentOutOfRangeException(
                nameof(tier),
                tier,
                "10 §7 runs three tiers — NORMAL, HEROIC, MYTHIC — and DifficultyTier has no zero " +
                "member on purpose. Widening an undefined tier into the hash would produce a " +
                "perfectly stable runSeed for a difficulty the game does not have.");
        }

        if (runCounter < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(runCounter),
                runCounter,
                "02 §2's runCounter is the player's lifetime runs-started counter — Player.BeginRun's " +
                "return, after the increment — so it counts upwards from zero and is never reset. " +
                "Zero is accepted (nothing in 02 §2 says the first value is 1, and refusing it would " +
                "be a claim the documents do not authorise); a NEGATIVE counter is not a count, and " +
                "seeding from one would produce a run no play could have produced.");
        }

        // 🔒 Argument for argument, in 02 §2's order. playerId.Value rather than playerId because
        // 14 §8.0's canonical encoding has exactly two shapes and a PlayerId is the string it wraps;
        // a default(PlayerId) therefore arrives here as a null string and Hash64Argument refuses it,
        // which is the loud failure a stable seed for no player would not be.
        return Hash64.Of(
            playerId.Value, chapterId, tier, nowUtc.ToUnixTimeSeconds(), runCounter);
    }

    /// <summary>
    /// 🔒 `14` §8.1 — <c>battleSeed = Hash64(runSeed, "combat", battleIndex)</c>. Combat draw
    /// <c>i</c> of that battle is then <c>Hash64(battleSeed, "combat", i)</c>, which is
    /// <c>new DeterministicRng(battleSeed, RngStreams.Combat)</c>.
    /// </summary>
    /// <remarks>
    /// The client receives the <c>battleSeed</c> and simulates the identical fight (`14` §2.4)
    /// <b>without ever holding <c>runSeed</c></b> — which never leaves the server (`02` §2). A
    /// client that held the run seed could read tomorrow's draft options and drops.
    /// <para>
    /// A revived battle restarts from draw 0 of the same battle stream, which needs no support
    /// beyond constructing the stream again: reproducible by construction.
    /// </para>
    /// </remarks>
    /// <param name="runSeed">The run's committed seed (`02` §2). Server-side state.</param>
    /// <param name="battleIndex">The battle's index within the run, from 0.</param>
    /// <exception cref="ArgumentOutOfRangeException">The battle index is negative.</exception>
    public static ulong BattleSeed(ulong runSeed, int battleIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(battleIndex);
        return Hash64.Of(runSeed, RngStreams.Combat, (ulong)battleIndex);
    }
}
