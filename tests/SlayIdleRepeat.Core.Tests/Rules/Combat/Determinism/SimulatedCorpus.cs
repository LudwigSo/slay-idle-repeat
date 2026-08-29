using System.Diagnostics;
using SlayIdleRepeat.Core.Rules.Combat;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Determinism;

/// <summary>One named triple the committed table pins individually, and the property that named it.</summary>
/// <param name="Id">The row id in <c>BattleLogHashBaseline.json</c>.</param>
/// <param name="Index">The triple ordinal the property first holds at.</param>
internal sealed record NamedTriple(string Id, int Index);

/// <summary>The 10 000-triple corpus, generated, simulated and hashed once for the whole suite.</summary>
/// <remarks>
/// Built in a static initialiser because xUnit gives a class one instance per test method, and the
/// corpus is a pure function of <see cref="BattleTripleGenerator.BaselineSeed"/> — re-simulating it
/// per method would multiply the suite's cost by its test count for no added coverage. The three
/// phases are timed separately: "it is fast enough" is not a claim a report can make without the
/// number.
/// </remarks>
internal sealed class SimulatedCorpus
{
    /// <summary>
    /// The named rows the committed table pins individually, in the order they appear in it.
    /// </summary>
    /// <remarks>
    /// Each row is a <em>property of the corpus</em> plus the first triple that has it, so a row
    /// survives a re-baseline still meaning the same thing. Order matters:
    /// <see cref="FirstUnclaimedIndexWhere"/> claims greedily down this list, so a selector satisfied
    /// by exactly one triple must come before any broad selector that could claim it first.
    /// </remarks>
    private static readonly IReadOnlyList<(string Id, Func<BattleTriple, TripleOutcome, bool> Holds)> Selectors =
        new List<(string, Func<BattleTriple, TripleOutcome, bool>)>
        {
            ("corpus-first", static (triple, _) => triple.Index == 0),
            ("corpus-last", static (triple, _) => triple.Index == BattleTripleGenerator.TripleCount - 1),
            ("hero-won", static (_, outcome) => outcome.HeroWon),
            ("hero-lost", static (_, outcome) => !outcome.HeroWon),
            // The one shape in which every tick of the fight is compared rather than the fight ending
            // early: an accumulation drift too small to change the winner still moves this row.
            ("ran-to-the-tick-cap", static (_, outcome) => outcome.DurationTicks == CombatLog.MaxTicks),
            ("single-body-roster", static (triple, _) => triple.EnemyPowers.Count == 1),
            ("full-roster", static (triple, _) => triple.EnemyPowers.Count == BattleTripleGenerator.MaxRoster),
            ("elite-in-the-roster", static (triple, _) => triple.EliteIndex >= 0),
            ("no-elite-in-the-roster", static (triple, _) => triple.EliteIndex < 0),
            ("mythic-tier", static (triple, _) => triple.TierOrdinal == 2),
            ("final-chapter", static (triple, _) =>
                triple.Chapter == BattleTripleGenerator.FirstChapter + BattleTripleGenerator.ChapterSpan - 1),
            // A build drawn above a shipped ceiling, so the cap step is pinned by a row rather than
            // only by whichever triples happen to reach it. Per stat against the read ceiling, not
            // one literal across the block: MAX_HP alone clears any ratio-sized threshold on every
            // triple in the corpus, which would make this row resolve to an arbitrary fight.
            ("build-over-a-shipped-cap", static (triple, _) => BattleTripleGenerator.OverAShippedCap(triple)),
        };

    private SimulatedCorpus(
        IReadOnlyList<BattleTriple> triples,
        IReadOnlyList<TripleOutcome> outcomes,
        IReadOnlyList<string> wires,
        IReadOnlyList<string> chunkWires,
        string aggregate,
        TimeSpan generationElapsed,
        TimeSpan simulationElapsed,
        TimeSpan hashingElapsed)
    {
        Triples = triples;
        Outcomes = outcomes;
        Wires = wires;
        ChunkWires = chunkWires;
        Aggregate = aggregate;
        GenerationElapsed = generationElapsed;
        SimulationElapsed = simulationElapsed;
        HashingElapsed = hashingElapsed;

        // Claimed as we go, so the twelve rows name twelve DIFFERENT triples rather than pinning one
        // triple twelve times while reading as though it pinned twelve.
        var claimed = new HashSet<int>();
        Named = Selectors
            .Select(selector => new NamedTriple(
                selector.Id,
                FirstUnclaimedIndexWhere(triples, outcomes, claimed, selector.Id, selector.Holds)))
            .ToArray();
    }

    /// <summary>The corpus, built once for the process.</summary>
    internal static SimulatedCorpus Instance { get; } = Build();

    /// <summary>The 10 000 generated triples, in ordinal order.</summary>
    internal IReadOnlyList<BattleTriple> Triples { get; }

    /// <summary>What simulating each of them produced.</summary>
    internal IReadOnlyList<TripleOutcome> Outcomes { get; }

    /// <summary>Each outcome's <c>"fnv1a:"</c> wire hash.</summary>
    internal IReadOnlyList<string> Wires { get; }

    /// <summary>The 100 chunk hashes, each over 100 triple wire hashes.</summary>
    internal IReadOnlyList<string> ChunkWires { get; }

    /// <summary>The one hash over all 100 chunks — the headline of the committed table.</summary>
    internal string Aggregate { get; }

    /// <summary>The individually pinned rows, resolved to the triples that carry their property.</summary>
    internal IReadOnlyList<NamedTriple> Named { get; }

    /// <summary>How long generating the 10 000 triples took.</summary>
    internal TimeSpan GenerationElapsed { get; }

    /// <summary>How long simulating them took.</summary>
    internal TimeSpan SimulationElapsed { get; }

    /// <summary>How long hashing the outcomes took.</summary>
    internal TimeSpan HashingElapsed { get; }

    /// <summary>Generation, simulation and hashing together.</summary>
    internal TimeSpan TotalElapsed => GenerationElapsed + SimulationElapsed + HashingElapsed;

    /// <summary>The wire hash of one named row.</summary>
    internal string WireOf(NamedTriple named)
    {
        ArgumentNullException.ThrowIfNull(named);

        return Wires[named.Index];
    }

    private static SimulatedCorpus Build()
    {
        var clock = Stopwatch.StartNew();
        var triples = BattleTripleGenerator.Corpus();
        var generation = clock.Elapsed;

        clock.Restart();
        var outcomes = new List<TripleOutcome>(triples.Count);
        foreach (var triple in triples)
        {
            outcomes.Add(TripleResolution.Resolve(triple));
        }

        var simulation = clock.Elapsed;

        clock.Restart();
        var wires = new List<string>(outcomes.Count);
        foreach (var outcome in outcomes)
        {
            wires.Add(TripleResolution.Hash(outcome));
        }

        var chunkWires = new List<string>(
            (wires.Count + BattleTripleGenerator.ChunkSize - 1) / BattleTripleGenerator.ChunkSize);
        for (var first = 0; first < wires.Count; first += BattleTripleGenerator.ChunkSize)
        {
            var span = wires.GetRange(first, Math.Min(BattleTripleGenerator.ChunkSize, wires.Count - first));
            chunkWires.Add(TripleResolution.HashChunk(first, span));
        }

        var aggregate = TripleResolution.HashCorpus(chunkWires);
        var hashing = clock.Elapsed;

        return new SimulatedCorpus(
            triples, outcomes, wires, chunkWires, aggregate, generation, simulation, hashing);
    }

    /// <summary>
    /// The lowest-numbered triple that carries the named property and that no earlier row has already
    /// claimed — or a refusal naming the property.
    /// </summary>
    /// <remarks>
    /// It throws rather than returning a sentinel: a named row whose property no longer holds anywhere
    /// is a generator that stopped emitting a shape, which is exactly the silent-coverage-loss failure
    /// the named rows exist to catch — and a sentinel would be committed as a hash of triple zero and
    /// pass forever.
    /// </remarks>
    private static int FirstUnclaimedIndexWhere(
        IReadOnlyList<BattleTriple> triples,
        IReadOnlyList<TripleOutcome> outcomes,
        HashSet<int> claimed,
        string id,
        Func<BattleTriple, TripleOutcome, bool> holds)
    {
        for (var index = 0; index < triples.Count; index++)
        {
            if (!claimed.Contains(index) && holds(triples[index], outcomes[index]))
            {
                claimed.Add(index);

                return index;
            }
        }

        throw new InvalidOperationException(
            $"No unclaimed triple in the corpus carries the property '{id}', which the committed " +
            "baseline pins a row for. Either the generator has stopped emitting a shape it used to " +
            "emit — a loss of coverage, not a row to delete — or the property has become so rare " +
            "that the other rows consumed every triple carrying it. Restore the shape, or retire the " +
            "row deliberately and regenerate the table.");
    }
}
