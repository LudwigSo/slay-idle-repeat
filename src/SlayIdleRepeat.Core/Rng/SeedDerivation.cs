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
