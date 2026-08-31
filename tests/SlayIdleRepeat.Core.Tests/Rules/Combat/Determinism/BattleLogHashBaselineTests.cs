using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Determinism;

/// <summary>
/// `14` §8.2's determinism corpus: 10 000 fixed <c>(seed, build, enemy)</c> triples simulated to their
/// <c>LogHash</c>, compared against the committed table.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 On ONE machine these cases prove <b>stability</b> — the combat pipeline is a pure function of
/// its triple and cannot drift across a refactor or a compiler without a chunk going red. The claim
/// `14` §8.2 actually makes, that <b>Linux x64 and Android ARM64 agree</b>, is made by running this
/// same suite on both: that is <c>ci.yml</c>'s <c>determinism</c> job, which M5-12 turned on. The
/// iOS ARM64 leg is authored and gated off with iOS itself.
/// </para>
/// <para>
/// ⚠️ D45 (HP persists across a run) and D46 (DMG%/DR% become multiplier stats) both move every
/// number in the committed table. Re-baseline after both, never between them.
/// </para>
/// <para>
/// 🔴 <b>What this corpus adds to the 4-decimal-place rounding rule, and what it does not.</b> The
/// existing audit is two classes and they check two different things:
/// <c>DeterminismRoundingRuleTests</c> is an IL scan asserting the rule is STATED in exactly one
/// place, and <c>DeterminismRoundingTests</c> pins what that one place COMPUTES. Neither is a
/// coverage rule — nothing requires an accumulation point to round at all, and a new
/// double-returning method that forgets to is caught only if its value reaches
/// <c>CanonicalStateWriter</c> or <c>CombatLog</c>, both of which refuse an unrounded double
/// outright. This corpus widens that net and does not replace it: ten thousand fights push real
/// values through every arm of the attack pipeline into <c>CombatLog</c>, so an unrounded
/// accumulation on any reached path throws rather than hashing. It still says nothing about
/// transient doubles that reach neither guard, about the <c>Content</c> layer's separate
/// <c>AwayFromZero</c> rounding to integers, or about <c>Math.Pow</c>, which `14` §8.2 asks combat
/// code to avoid and which no rule enforces.
/// </para>
/// </remarks>
public sealed class BattleLogHashBaselineTests
{
    /// <summary>
    /// How many of the 10 000 fights must produce a <c>LogHash</c> nothing else in the corpus
    /// produced.
    /// </summary>
    /// <remarks>
    /// The non-vacuity floor for the whole table. A corpus in which every fight logged the same events
    /// would satisfy every chunk hash and every aggregate below while comparing one battle ten
    /// thousand times, and no assertion phrased over hashes could tell the difference.
    /// </remarks>
    private const int DistinctLogHashFloor = 9_900;

    private static SimulatedCorpus Corpus => SimulatedCorpus.Instance;

    // ═══════════════════════════════════════════════════════ the committed table

    [Theory]
    [MemberData(nameof(ChunkIds))]
    public void Every_committed_chunk_hash_still_covers_its_hundred_triples(int chunk)
    {
        var first = chunk * BattleLogHashBaseline.ChunkSize;

        Corpus.ChunkWires[chunk].ShouldBe(
            BattleLogHashBaseline.Chunks[chunk],
            $"triples {first.ToString(CultureInfo.InvariantCulture)}–" +
            $"{(first + BattleLogHashBaseline.ChunkSize - 1).ToString(CultureInfo.InvariantCulture)} " +
            "no longer hash to the committed value. This is a determinism break in the combat " +
            "pipeline, not a stale test: an accumulation point stopped rounding, an enemy derivation " +
            "moved, or the content this corpus runs against was retuned. Establish which, write it " +
            "down, and only then regenerate through the documented command.");
    }

    [Fact]
    public void The_committed_aggregate_still_covers_all_hundred_chunks()
    {
        BattleLogHashBaseline.Chunks.Count.ShouldBe(
            BattleTripleGenerator.TripleCount / BattleTripleGenerator.ChunkSize,
            "the aggregate is a hash over the chunk list; a table with no chunks would make the " +
            "comparison below true of an empty corpus.");

        Corpus.Aggregate.ShouldBe(
            BattleLogHashBaseline.Aggregate,
            "the one hash over all 100 chunks. It cannot move without a chunk moving, so a red " +
            "aggregate with green chunks would mean the chunk list itself was reordered or resized.");
    }

    [Theory]
    [MemberData(nameof(NamedRowIds))]
    public void Every_named_row_still_pins_the_triple_its_property_names(string id)
    {
        var committed = BattleLogHashBaseline.Row(id);
        var resolved = Corpus.Named.Single(named => named.Id.Equals(id, StringComparison.Ordinal));

        resolved.Index.ShouldBe(
            committed.Triple,
            $"'{id}' names a property of the corpus, and the first triple carrying it has moved. The " +
            "row is still meaningful, but it is pinning a different fight than the one reviewed.");

        HexOf(Corpus.Outcomes[resolved.Index].LogHash).ShouldBe(
            committed.LogHash,
            $"'{id}' pins `14` §8.2's own subject — this fight's battle log hash — in hexadecimal, so " +
            "the value the two architectures compare is legible in the diff.");

        Corpus.WireOf(resolved).ShouldBe(
            committed.Hash,
            $"'{id}' pins the whole simulated outcome: winner, duration, remaining HP, event count " +
            "and log hash. A row that moved while its chunk did not is impossible; a row that moved " +
            "with its chunk localises the break to one readable fight.");
    }

    [Fact]
    public void The_committed_table_is_the_corpus_the_generator_describes()
    {
        BattleLogHashBaseline.Triples.ShouldBe(
            BattleTripleGenerator.TripleCount,
            "`14` §8.2 asks for 10 000 fixed triples. A table built over fewer would pass every hash " +
            "comparison above while comparing less than the document requires.");

        BattleTripleGenerator.TripleCount.ShouldBe(
            10_000, "transcribed from `14` §8.2 rather than read back off the generator.");

        BattleLogHashBaseline.ChunkSize.ShouldBe(BattleTripleGenerator.ChunkSize);

        BattleLogHashBaseline.BaselineSeed.ShouldBe(
            "0x" + BattleTripleGenerator.BaselineSeed.ToString("x16", CultureInfo.InvariantCulture),
            "the seed is the corpus. Ten thousand rows are not committed; this one number plus the " +
            "generator reproduces every one of them, which is what lets a second architecture build " +
            "the identical corpus without shipping it.");
    }

    /// <summary>
    /// The table is found through the assembly's embedded resources, not through a path.
    /// </summary>
    /// <remarks>
    /// The Android ARM64 leg runs somewhere whose working directory is not ours to predict; a table
    /// read off disk would be missing there and the job would fail for a reason that has nothing to
    /// do with determinism.
    /// </remarks>
    [Fact]
    public void The_committed_table_is_embedded_in_this_assembly()
    {
        typeof(BattleLogHashBaseline).Assembly.GetManifestResourceNames().ShouldContain(
            "SlayIdleRepeat.Core.Tests.Rules.Combat.Determinism.BattleLogHashBaseline.json");

        BattleLogHashBaseline.RawText.Length.ShouldBeGreaterThan(
            1_000, "an embedded resource that resolved to an empty stream would read as a table.");
    }

    // ═══════════════════════════════════════════════ the corpus, and what it covers

    /// <summary>
    /// The generator is a pure function of the committed seed and the ordinal — the property that lets
    /// a second architecture build the identical corpus from the seed alone.
    /// </summary>
    [Fact]
    public void A_triple_is_a_pure_function_of_the_baseline_seed_and_its_ordinal()
    {
        // Compared as canonical bytes rather than with record equality: a positional record's
        // generated Equals compares an IReadOnlyList member by reference, so two identically drawn
        // triples are never `==` and the case would fail for a reason that is not about determinism.
        var once = Canonical(BattleTripleGenerator.TripleAt(4_242));

        Canonical(BattleTripleGenerator.TripleAt(4_242)).ShouldBe(
            once,
            "two draws of the same ordinal must be the same triple, or the corpus is not " +
            "reproducible off the seed and nothing downstream means anything.");

        Canonical(BattleTripleGenerator.TripleAt(4_243)).ShouldNotBe(
            once,
            "the negative control: if consecutive ordinals produced the same triple, the corpus " +
            "would be one fight repeated and every hash above would still be green.");
    }

    /// <summary>The simulator itself is deterministic — the property the whole table rests on.</summary>
    [Fact]
    public void Simulating_the_same_triple_twice_produces_the_same_LogHash()
    {
        var triple = BattleTripleGenerator.TripleAt(1_234);

        var first = TripleResolution.Resolve(triple);
        var second = TripleResolution.Resolve(triple);

        second.LogHash.ShouldBe(
            first.LogHash,
            "a fight that is not reproducible in one process cannot be compared across two " +
            "architectures. This is the assumption every chunk hash above is built on, asserted " +
            "rather than assumed.");

        TripleResolution.Resolve(BattleTripleGenerator.TripleAt(1_235)).LogHash.ShouldNotBe(
            first.LogHash,
            "the negative control: a LogHash that ignored its inputs would satisfy the case above.");
    }

    /// <summary>
    /// The corpus is ten thousand different fights, not one fight ten thousand times.
    /// </summary>
    [Fact]
    public void The_corpus_produces_distinct_fights()
    {
        Corpus.Outcomes.Select(outcome => outcome.LogHash).Distinct().Count()
            .ShouldBeGreaterThanOrEqualTo(
                DistinctLogHashFloor,
                "the non-vacuity floor. Every hash assertion in this class is over a hash of a hash, " +
                "so a corpus that collapsed to one repeated fight would be entirely green while " +
                "comparing one battle across the two architectures instead of ten thousand.");
    }

    /// <summary>
    /// The corpus reaches the branches a cross-architecture comparison exists to compare.
    /// </summary>
    /// <remarks>
    /// Floors on the generator's coverage, arm by arm. A drawn corpus can lose a shape silently — a
    /// range edited by one, a probability that stopped firing — and every hash above would go on being
    /// green over a narrower space than the one that was reviewed.
    /// </remarks>
    [Fact]
    public void The_corpus_reaches_every_shape_the_named_rows_claim()
    {
        Corpus.Outcomes.Count(outcome => outcome.HeroWon).ShouldBeGreaterThan(
            100, "a corpus the hero always loses never runs the kill, loot or overkill arms.");

        Corpus.Outcomes.Count(outcome => !outcome.HeroWon).ShouldBeGreaterThan(
            100, "and a corpus the hero always wins never runs the hero-death arm.");

        Corpus.Outcomes.Count(outcome => outcome.DurationTicks == CombatLog.MaxTicks)
            .ShouldBeGreaterThan(
                10,
                "the fights that run the full bound are where an accumulation drift too small to " +
                "change the winner still shows up, tick after tick.");

        Corpus.Triples.Count(triple => triple.EliteIndex >= 0).ShouldBeGreaterThan(
            100, "the elite derivation is a separate multiplier arm of the enemy derivation.");

        Corpus.Triples.Count(triple => triple.EliteIndex < 0).ShouldBeGreaterThan(
            100,
            "and its negative control: a corpus in which every roster carried an elite would never " +
            "run the plain derivation the elite multiplier is measured against.");

        Corpus.Triples.Select(triple => triple.EnemyPowers.Count).Distinct().OrderBy(count => count)
            .ToArray()
            .ShouldBe(
                Enumerable.Range(1, BattleTripleGenerator.MaxRoster).ToArray(),
                "every roster size from one body to a full roster, so multi-target selection is " +
                "compared as well as single-target.");

        Corpus.Triples.Select(triple => triple.TierOrdinal).Distinct().OrderBy(tier => tier).ToArray()
            .ShouldBe(
                new[] { 0, 1, 2 }, "NORMAL, HEROIC and MYTHIC all scale the roster differently.");

        Corpus.Triples.Select(triple => triple.Chapter).Distinct().OrderBy(chapter => chapter).ToArray()
            .ShouldBe(
                Enumerable.Range(BattleTripleGenerator.FirstChapter, BattleTripleGenerator.ChapterSpan)
                    .ToArray(),
                "each chapter pool weights the archetypes differently, so a chapter missing from the " +
                "corpus is an archetype nobody compared.");

        Corpus.Triples.Count(BattleTripleGenerator.OverAShippedCap)
            .ShouldBeGreaterThan(
                100,
                "the stat-cap step is itself an accumulation point, and it is reached per stat: a " +
                "build is over a ceiling when one of the six capped stats is above the value " +
                "content/combat_caps.json gives THAT stat. A corpus drawn entirely under them would " +
                "never run the clamp at all.");

        Corpus.Triples.Count(triple => !BattleTripleGenerator.OverAShippedCap(triple))
            .ShouldBeGreaterThan(
                100,
                "and its negative control: a corpus in which every build was clamped would never " +
                "run the aggregation's uncapped path.");
    }

    // ═══════════════════════════════════════════════════ the header, and the review

    [Fact]
    public void The_committed_header_is_the_one_the_writer_renders()
    {
        BattleLogHashBaseline.Comment.ShouldBe(
            BattleLogHashBaselineWriter.HeaderLines,
            "the header states what this table proves and what it does not. A committed file whose " +
            "header has drifted from the writer's is a table making a claim nobody wrote.");
    }

    [Fact]
    public void The_committed_header_says_the_table_is_scheduled_to_move()
    {
        var header = string.Join(" ", BattleLogHashBaseline.Comment);

        header.ShouldContain(
            "M7-06g",
            Case.Sensitive,
            "D45 re-baselines every LogHash in the repository and is still open. A header that did " +
            "not name it would let the next reader take these numbers for permanent.");

        header.ShouldContain("M4-16d", Case.Sensitive, "and D46 does the same.");

        header.ShouldContain(
            "never between them",
            Case.Sensitive,
            "the two rulings must land together so the tables move once rather than twice.");

        header.ShouldContain(
            "Android ARM64",
            Case.Sensitive,
            "the live legs are Linux x64 and Android ARM64; a single-architecture corpus proves " +
            "nothing `14` §8.2 asks for.");

        header.ShouldContain(
            "D34",
            Case.Sensitive,
            "and the iOS ARM64 leg is authored and gated off rather than forgotten.");
    }

    [Fact]
    public void The_committed_table_was_reviewed_by_a_person()
    {
        BattleLogHashBaseline.ReviewStatus.ShouldBe(BattleLogHashBaselineWriter.ReviewedStatus);
        BattleLogHashBaseline.ReviewedBy.ShouldNotBeNullOrWhiteSpace();
        BattleLogHashBaseline.ReviewedOn.ShouldNotBeNullOrWhiteSpace();
        BattleLogHashBaseline.ReviewWhy.Length.ShouldBeGreaterThan(
            80,
            "the review is a record of what moved and why, not a checkbox. The reader refuses a blank " +
            "one; this is the floor under a one-word one.");
    }

    // ═══════════════════════════════════════════════════════════════ regeneration

    /// <summary>
    /// The regeneration command's own output, round-tripped. Also the door itself: with
    /// <c>SIR_M5_12_LOGHASH_OUT</c> set, this writes the table.
    /// </summary>
    [Fact]
    public void The_text_the_documented_regeneration_command_writes_is_the_committed_table()
    {
        var destination = Environment.GetEnvironmentVariable(BattleLogHashBaselineWriter.DestinationVariable);
        var reasons = destination is { Length: > 0 }
            ? BattleLogHashBaselineWriter.ReasonsIn(destination)
            : BattleLogHashBaseline.Reasons;

        var rendered = BattleLogHashBaselineWriter.Render(Corpus, reasons);

        if (destination is { Length: > 0 })
        {
            // An exported variable cannot send a render anywhere but the one committed table.
            BattleLogHashBaselineWriter.NamesTheCommittedBaseline(destination).ShouldBeTrue(
                $"{BattleLogHashBaselineWriter.DestinationVariable} is '{destination}', which is not " +
                $"{BattleLogHashBaselineWriter.CanonicalPath}. Regeneration replaces the committed " +
                "table and nothing else — the hand-written reasons are carried over from it.");

            // LF, no BOM — the .gitattributes beside the file pins the same thing for git.
            File.WriteAllBytes(destination, new UTF8Encoding(false).GetBytes(rendered));
        }

        rendered.ShouldNotContain(
            "\r", Case.Sensitive, "the table is LF-only on both architectures the job compares");

        WithoutHandWrittenRegions(rendered).ShouldBe(
            WithoutHandWrittenRegions(BattleLogHashBaseline.RawText),
            "the committed table is exactly what this build's regeneration command would write, byte " +
            "for byte, apart from the review block and the per-row reasons a human owns");

        using var parsed = JsonDocument.Parse(rendered);
        parsed.RootElement.GetProperty("review").GetProperty("status").GetString().ShouldBe(
            BattleLogHashBaselineWriter.UnreviewedStatus,
            "🔒 every render is unreviewed. The reader refuses that, so regenerating the table cannot " +
            "make a determinism break go green on its own — a human has to say why it moved.");
    }

    // ══════════════════════════════════════════════════════ theory data and helpers

    /// <summary>Every chunk of the committed table.</summary>
    public static TheoryData<int> ChunkIds()
    {
        var data = new TheoryData<int>();
        for (var chunk = 0; chunk < BattleLogHashBaseline.Chunks.Count; chunk++)
        {
            data.Add(chunk);
        }

        return data;
    }

    /// <summary>Every individually pinned row of the committed table.</summary>
    public static TheoryData<string> NamedRowIds()
    {
        var data = new TheoryData<string>();
        foreach (var row in BattleLogHashBaseline.Named)
        {
            data.Add(row.Id);
        }

        return data;
    }

    private static string HexOf(ulong value) =>
        "0x" + value.ToString("x16", CultureInfo.InvariantCulture);

    /// <summary>One triple's canonical wire hash, through the one serialiser `14` §16.6 allows.</summary>
    private static string Canonical(BattleTriple triple) =>
        CanonicalStateWriter.HashMetaCommandState(triple);

    /// <summary>
    /// One baseline file with its two hand-written regions blanked: the whole <c>review</c> object,
    /// and every named row's <c>why</c>. What is left is the writer's output and only that.
    /// </summary>
    private static string WithoutHandWrittenRegions(string json) =>
        Regex.Replace(
            Regex.Replace(json, "\"review\": \\{.*?\\n  \\}", "\"review\": {}", RegexOptions.Singleline),
            "\"why\": \"[^\"]*\"",
            "\"why\": \"\"");
}
