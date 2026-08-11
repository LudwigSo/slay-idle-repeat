namespace SlayIdleRepeat.Core.Rng;

/// <summary>
/// The named seed derivations of `14` §8.1 — the places where one seed spawns another.
/// </summary>
/// <remarks>
/// They live here as functions rather than as an expression callers write out because each is a
/// contract between two machines. A caller who re-derived <c>battleSeed</c> inline would sooner
/// or later write <c>battleIndex + 1</c>, or the wrong stream name, and the client and server
/// would then disagree about a battle that nobody could reproduce afterwards.
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
