using System.Diagnostics;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Determinism;

/// <summary>
/// One named permutation the committed table pins individually, and the property that named it.
/// </summary>
/// <param name="Id">The row id in <c>DslDeterminismBaseline.json</c>.</param>
/// <param name="Index">The permutation ordinal the property first holds at.</param>
internal sealed record NamedPermutation(string Id, int Index);

/// <summary>
/// The 10 000-permutation corpus, generated and resolved once for the whole suite.
/// </summary>
/// <remarks>
/// <para>
/// Built once in a static initialiser because every test in
/// <c>DslDeterminismBaselineTests</c> reads the same corpus and xUnit gives a class one instance per
/// test method. Regenerating it per method would multiply the suite's cost by its test count for no
/// added coverage: the corpus is a pure function of <c>BuildPermutationGenerator.BaselineSeed</c>,
/// and <c>The_corpus_is_re_runnable</c> is the test that says so.
/// </para>
/// <para>
/// 🔒 <b>The three phases are timed separately.</b> `18` §11 asks for 10 000 permutations in a suite
/// that has to stay fast, and "it is fast enough" is not a claim a report can make without the
/// number. <see cref="GenerationElapsed"/>, <see cref="ResolutionElapsed"/> and
/// <see cref="HashingElapsed"/> are what
/// <c>The_10000_permutation_pass_stays_inside_the_unit_tier_budget</c> asserts against and what the
/// completion report quotes.
/// </para>
/// </remarks>
internal sealed class ResolvedCorpus
{
    /// <summary>
    /// The named rows the committed table pins individually, in the order they appear in it. Each is
    /// a <em>property</em> plus the first permutation that has it, so the row survives a
    /// regeneration meaning the same thing.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Order matters, and this is the invariant.</b> <see cref="FirstUnclaimedIndexWhere"/>
    /// claims greedily down this list, so a selector satisfied by exactly <b>one</b> permutation
    /// (<c>corpus-first</c>, <c>corpus-last</c>) must come before any broad selector that could claim
    /// it first. Violating it does not produce a wrong answer — it produces an
    /// <see cref="InvalidOperationException"/> out of a static initialiser, which surfaces as a
    /// <c>TypeInitializationException</c> in every test in the suite at once. Loud, but a long way
    /// from its cause; hence the note.
    /// </remarks>
    private static readonly IReadOnlyList<(string Id, Func<BuildPermutation, PermutationOutcome, bool> Holds)> Selectors =
        new List<(string, Func<BuildPermutation, PermutationOutcome, bool>)>
        {
            ("corpus-first", static (permutation, _) => permutation.Index == 0),
            ("corpus-last", static (permutation, _) => permutation.Index == BuildPermutationGenerator.PermutationCount - 1),
            ("above-introsort-threshold", static (permutation, _) =>
                permutation.Effects.Count > BuildPermutationGenerator.IntrosortStabilityThreshold),
            ("at-or-below-introsort-threshold", static (permutation, _) =>
                permutation.Effects.Count <= BuildPermutationGenerator.IntrosortStabilityThreshold),
            // 🔒 Keyed on the duplicate-id ANCHOR, not on "some duplicate exists": the row is named
            //    for M2-02's blind spot, and the property it defends is that both halves of the
            //    deliberate pair survived step 2 in a build big enough to scramble.
            ("duplicate-ids-above-threshold", static (permutation, outcome) =>
                permutation.Effects.Count > BuildPermutationGenerator.IntrosortStabilityThreshold &&
                outcome.Active.Count(row =>
                    row.Id.Equals(BuildPermutationGenerator.DuplicateAnchorId, StringComparison.Ordinal)) == 2),
            ("pvp-context", static (permutation, _) => permutation.Context.IsPvp),
            ("enrage-context", static (permutation, _) => permutation.Context.EnrageAtSeconds is not null),
            ("stat-convert-present", static (permutation, _) =>
                permutation.Effects.Any(effect => effect.Op == EffectOp.STAT_CONVERT)),
            ("redirect-excess-present", static (permutation, _) =>
                permutation.Effects.Any(effect => effect.CapKind == StatCapKind.REDIRECT_EXCESS)),
            ("value-scale-status-argument", static (permutation, _) =>
                permutation.Effects.Any(effect => effect.ValueScale?.StatusId is not null)),
            ("non-combat-stat-skipped", static (_, outcome) => outcome.SkippedNonCombatStatEffects.Count > 0),
            ("condition-gated-an-effect-out", static (_, outcome) => outcome.GatedOut.Count > 0),
        };

    private ResolvedCorpus(
        IReadOnlyList<BuildPermutation> permutations,
        IReadOnlyList<PermutationOutcome> outcomes,
        IReadOnlyList<string> wires,
        IReadOnlyList<string> chunkWires,
        string aggregate,
        TimeSpan generationElapsed,
        TimeSpan resolutionElapsed,
        TimeSpan hashingElapsed)
    {
        Permutations = permutations;
        Outcomes = outcomes;
        Wires = wires;
        ChunkWires = chunkWires;
        Aggregate = aggregate;
        GenerationElapsed = generationElapsed;
        ResolutionElapsed = resolutionElapsed;
        HashingElapsed = hashingElapsed;
        // 🔒 Claimed as we go, so the twelve rows name twelve DIFFERENT builds. Without it,
        //    permutation 0 happens to carry six of the properties and the table would pin one
        //    permutation six times while reading as though it pinned six.
        var claimed = new HashSet<int>();
        Named = Selectors
            .Select(selector => new NamedPermutation(
                selector.Id,
                FirstUnclaimedIndexWhere(permutations, outcomes, claimed, selector.Id, selector.Holds)))
            .ToArray();
    }

    /// <summary>The corpus, built once for the process.</summary>
    internal static ResolvedCorpus Instance { get; } = Build();

    /// <summary>The 10 000 generated permutations, in ordinal order.</summary>
    internal IReadOnlyList<BuildPermutation> Permutations { get; }

    /// <summary>What `18` §8 made of each of them.</summary>
    internal IReadOnlyList<PermutationOutcome> Outcomes { get; }

    /// <summary>Each outcome's <c>"fnv1a:"</c> wire hash.</summary>
    internal IReadOnlyList<string> Wires { get; }

    /// <summary>The 100 chunk hashes, each over 100 permutation wire hashes.</summary>
    internal IReadOnlyList<string> ChunkWires { get; }

    /// <summary>The one hash over all 100 chunks — the headline of the committed table.</summary>
    internal string Aggregate { get; }

    /// <summary>The individually pinned rows, resolved to the permutations that carry their property.</summary>
    internal IReadOnlyList<NamedPermutation> Named { get; }

    /// <summary>How long generating the 10 000 permutations took.</summary>
    internal TimeSpan GenerationElapsed { get; }

    /// <summary>How long resolving them through `18` §8 took.</summary>
    internal TimeSpan ResolutionElapsed { get; }

    /// <summary>How long hashing the outcomes took.</summary>
    internal TimeSpan HashingElapsed { get; }

    /// <summary>Generation, resolution and hashing together.</summary>
    internal TimeSpan TotalElapsed => GenerationElapsed + ResolutionElapsed + HashingElapsed;

    /// <summary>The wire hash of one named row.</summary>
    internal string WireOf(NamedPermutation named)
    {
        ArgumentNullException.ThrowIfNull(named);

        return Wires[named.Index];
    }

    private static ResolvedCorpus Build()
    {
        var clock = Stopwatch.StartNew();
        var permutations = BuildPermutationGenerator.Corpus();
        var generation = clock.Elapsed;

        clock.Restart();
        var outcomes = new List<PermutationOutcome>(permutations.Count);
        foreach (var permutation in permutations)
        {
            outcomes.Add(PermutationResolution.Resolve(permutation));
        }

        var resolution = clock.Elapsed;

        clock.Restart();
        var wires = new List<string>(outcomes.Count);
        foreach (var outcome in outcomes)
        {
            wires.Add(PermutationResolution.Hash(outcome));
        }

        var chunkWires = new List<string>(
            (wires.Count + BuildPermutationGenerator.ChunkSize - 1) / BuildPermutationGenerator.ChunkSize);
        for (var first = 0; first < wires.Count; first += BuildPermutationGenerator.ChunkSize)
        {
            var span = wires.GetRange(first, Math.Min(BuildPermutationGenerator.ChunkSize, wires.Count - first));
            chunkWires.Add(PermutationResolution.HashChunk(first, span));
        }

        var aggregate = PermutationResolution.HashCorpus(chunkWires);
        var hashing = clock.Elapsed;

        return new ResolvedCorpus(
            permutations, outcomes, wires, chunkWires, aggregate, generation, resolution, hashing);
    }

    /// <summary>
    /// The lowest-numbered permutation that carries the named property and that no earlier row has
    /// already claimed — or a refusal naming the property.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The claim set is what makes the twelve rows twelve builds.</b> The rule is stated here
    /// once, and <c>Every_named_row_still_names_the_permutation_its_property_first_holds_at</c>
    /// re-derives it from the same code, so the committed ordinals mean what the ids say.
    /// </para>
    /// <para>
    /// 🔒 It throws rather than returning <c>-1</c>. A named row whose property no longer holds
    /// anywhere in the corpus is a generator that stopped emitting a shape, which is exactly the
    /// silent-coverage-loss failure the named rows exist to catch — and a sentinel index would be
    /// committed as a hash of permutation zero and pass forever.
    /// </para>
    /// </remarks>
    private static int FirstUnclaimedIndexWhere(
        IReadOnlyList<BuildPermutation> permutations,
        IReadOnlyList<PermutationOutcome> outcomes,
        HashSet<int> claimed,
        string id,
        Func<BuildPermutation, PermutationOutcome, bool> holds)
    {
        for (var index = 0; index < permutations.Count; index++)
        {
            if (!claimed.Contains(index) && holds(permutations[index], outcomes[index]))
            {
                claimed.Add(index);
                return index;
            }
        }

        throw new InvalidOperationException(
            $"No unclaimed permutation in the corpus carries the property '{id}', which the committed " +
            "baseline pins a row for. Either the generator has stopped emitting a shape it used to " +
            "emit — a loss of coverage, not a row to delete — or the property has become so rare that " +
            "the eleven other rows consumed every permutation carrying it. Restore the shape, or " +
            "retire the row deliberately and regenerate the table.");
    }
}
