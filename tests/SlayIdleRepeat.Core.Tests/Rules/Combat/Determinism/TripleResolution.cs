using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Tests.BalanceHarness;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Determinism;

/// <summary>What one simulated triple produced, in the shape <c>CanonicalStateWriter</c> encodes.</summary>
/// <param name="Triple">The triple's ordinal.</param>
/// <param name="LogHash">
/// `14` §8.2's subject: the fight's battle log hash, taken straight off
/// <see cref="SimulationResult.LogHash"/>. This is the value the two architectures compare.
/// </param>
/// <param name="HeroWon">Who won — the coarsest thing a divergence can move, and the first a reader looks at.</param>
/// <param name="DurationTicks">How long it ran, which separates a drift that changed the outcome from one that only changed its timing.</param>
/// <param name="HeroHpRemaining">
/// The hero's remaining HP. Carried alongside the log hash rather than instead of it: a
/// <c>LogHash</c> is 64 bits over an event list, and a run that diverged only in a value the log
/// rounds away would move this column first and give the failure message something to say.
/// </param>
/// <param name="EventCount">How many events the fight logged, before the terminating BattleEnd.</param>
internal sealed record TripleOutcome(
    int Triple,
    ulong LogHash,
    bool HeroWon,
    int DurationTicks,
    double HeroHpRemaining,
    int EventCount);

/// <summary>One chunk of the corpus: the triple wire hashes it covers, in ordinal order.</summary>
internal sealed record TripleChunk(int Chunk, int First, int Last, IReadOnlyList<string> Triples);

/// <summary>The whole corpus: its chunk wire hashes, in ordinal order.</summary>
internal sealed record TripleCorpus(int Triples, int ChunkSize, IReadOnlyList<string> Chunks);

/// <summary>
/// Simulating one triple and hashing what came out — the subject of the cross-architecture
/// comparison.
/// </summary>
/// <remarks>
/// The fight runs through <see cref="CombatSimulator.SimulateEncounter"/> against the real shipped
/// <c>game-data/</c> tree, not a hand-built fixture: the enemy derivation, the mitigation dials and
/// the stat caps a divergence would show up in are all content, and a corpus over invented content
/// would compare arithmetic the shipped game never runs.
/// <para>
/// Hashing goes through <c>CanonicalStateWriter</c>'s public door only. A second byte assembler is a
/// second thing that has to be kept in step with the field-order pin, and the day the two drift is
/// the day a table goes red for a reason nobody can localise.
/// </para>
/// </remarks>
internal static class TripleResolution
{
    /// <summary>Simulates one triple.</summary>
    internal static TripleOutcome Resolve(BattleTriple triple)
    {
        ArgumentNullException.ThrowIfNull(triple);

        var result = CombatSimulator.SimulateEncounter(
            triple.BattleSeed,
            BattleTripleGenerator.StatsOf(triple),
            triple.HeroLevel,
            triple.Chapter,
            triple.TierOrdinal,
            triple.EnemyPowers,
            ShippedHarness.Content,
            triple.EliteIndex);

        return new TripleOutcome(
            triple.Index,
            result.LogHash,
            result.HeroWon,
            result.DurationTicks,
            result.HeroHpRemaining,
            result.Log.Count);
    }

    /// <summary>The <c>"fnv1a:"</c> wire hash of one simulated triple.</summary>
    internal static string Hash(TripleOutcome outcome) =>
        CanonicalStateWriter.HashMetaCommandState(outcome);

    /// <summary>The wire hash of one chunk of triple wire hashes.</summary>
    /// <param name="first">The chunk's first triple ordinal.</param>
    /// <param name="tripleWires">The chunk's triple wire hashes, in ordinal order.</param>
    internal static string HashChunk(int first, IReadOnlyList<string> tripleWires)
    {
        ArgumentNullException.ThrowIfNull(tripleWires);

        return CanonicalStateWriter.HashMetaCommandState(new TripleChunk(
            first / BattleTripleGenerator.ChunkSize,
            first,
            first + tripleWires.Count - 1,
            tripleWires));
    }

    /// <summary>The wire hash of the whole corpus — the headline row of the committed table.</summary>
    internal static string HashCorpus(IReadOnlyList<string> chunkWires)
    {
        ArgumentNullException.ThrowIfNull(chunkWires);

        return CanonicalStateWriter.HashMetaCommandState(new TripleCorpus(
            BattleTripleGenerator.TripleCount, BattleTripleGenerator.ChunkSize, chunkWires));
    }
}
