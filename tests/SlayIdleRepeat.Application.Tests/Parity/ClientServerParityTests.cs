using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;
using SlayIdleRepeat.Application.Tests.Hosting;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Application.Tests.Wire;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using Xunit;
using PlayerAggregate = SlayIdleRepeat.Core.Model.Player;

namespace SlayIdleRepeat.Application.Tests.Parity;

/// <summary>
/// `14` §13's client/server parity requirement: the same 1 000 command sequences through the client's
/// and the server's <c>Core</c>, asserting identical state hashes.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Read this before trusting the green tick.</b> Both sides of this comparison reference the
/// same <c>SlayIdleRepeat.Core.dll</c>. A test that ran one host twice would therefore be green by
/// construction and would prove nothing whatsoever. What is genuinely compared is the two DISPATCH
/// PIPELINES over that one rules library — the in-process host that builds its own
/// <c>GameContext</c> and calls the use case, against the wire gateway that decodes an envelope,
/// resolves a sequencing scope, consults a ledger, a throttle and a content pin, and splits decide /
/// commit / publish across a unit of work. Those two paths are how a command actually reaches the
/// domain, and this asserts they land on the same state.
/// </para>
/// <para>
/// ⚠️ <b>The cross-RUNTIME half of parity is not here.</b> One process on one architecture cannot
/// show that a phone and a server agree about floating point; that is <c>ci.yml</c>'s
/// <c>determinism</c> matrix, and this corpus says nothing about it on its own.
/// </para>
/// </remarks>
public sealed class ClientServerParityTests
{
    /// <summary>
    /// A wall-clock guard against an algorithmic regression, not a performance target.
    /// </summary>
    /// <remarks>
    /// 🔴 Two measurements, and the second is why this number is what it is: about <b>9 s</b> when
    /// this class runs alone, and <b>48 s</b> when it runs inside the full unit group, because xUnit
    /// runs collections in parallel and 1 500 other cases are competing for the same cores. A budget
    /// set from the isolated figure goes red on a busy machine and teaches everyone to re-run the
    /// suite, which is worse than no budget at all. This one is set well above the loaded figure on
    /// purpose: the failure it exists to catch is an accidental O(n²), which moves the number by
    /// orders of magnitude rather than by a factor of five.
    /// </remarks>
    private const int BudgetSeconds = 300;

    /// <summary>
    /// The share of drawn steps the domain must accept before the walk counts as legality-aware.
    /// </summary>
    /// <remarks>
    /// 34 of the 55 registered rows are deferred and refuse unconditionally, and the phase gates
    /// refuse most of the rest, so a walker that took its first draw every time would accept a small
    /// fraction of its steps. This floor is what separates "sequences the domain actually ran" from
    /// "a thousand transcripts of ILLEGAL_STATE".
    /// </remarks>
    private const double AcceptedShareFloor = 0.30;

    private static ParityCorpus Corpus => ParityCorpus.Instance;

    // ═════════════════════════════════════════════════════════ the parity claim

    /// <summary>The claim itself: 1 000 sequences, two pipelines, one state hash per step.</summary>
    [Fact]
    public void The_two_pipelines_agree_on_every_state_hash_of_every_sequence()
    {
        Corpus.Sequences.Count.ShouldBe(
            ParitySequenceGenerator.SequenceCount,
            "`14` §13 asks for 1 000 command sequences. A corpus of none would make the emptiness " +
            "below trivially true.");

        Corpus.Outcomes.Sum(outcome => outcome.Steps.Count).ShouldBeGreaterThan(
            ParitySequenceGenerator.SequenceCount * ParitySequenceGenerator.MinLength - 1,
            "every sequence carries at least its minimum length, so a corpus of empty sequences " +
            "cannot pass while comparing nothing.");

        Corpus.Mismatches.ShouldBeEmpty(
            "the two dispatch pipelines produced different state for the same command. Each row is " +
            "sequence/step/command with both hashes; the first one is the one to reproduce.");
    }

    /// <summary>
    /// The comparison ran over commands the domain actually executed, not a thousand refusals.
    /// </summary>
    [Fact]
    public void The_corpus_is_mostly_commands_the_domain_ran()
    {
        var total = Corpus.AcceptedSteps + Corpus.RefusedSteps;

        total.ShouldBeGreaterThan(0, "an empty corpus makes the share below a division by zero.");

        ((double)Corpus.AcceptedSteps / total).ShouldBeGreaterThan(
            AcceptedShareFloor,
            $"{Corpus.AcceptedSteps.ToString(CultureInfo.InvariantCulture)} of " +
            $"{total.ToString(CultureInfo.InvariantCulture)} steps were accepted. Below this floor " +
            "the walker has stopped being legality-aware and the corpus is comparing the refusal " +
            "path over and over — which two pipelines agree on trivially, because neither of them " +
            "changes any state.");

        Corpus.RefusedSteps.ShouldBeGreaterThan(
            0,
            "and refusals must be in the corpus too: a refusal's state hash is a claim both sides " +
            "make about state that did not move, and it is a different code path from an acceptance.");
    }

    /// <summary>Every command in the registry but one is driven through both pipelines.</summary>
    [Fact]
    public void Every_registered_command_except_START_RUN_is_driven()
    {
        var expected = GameRules.CommandTypesByWireName.Keys
            .Where(name => !name.Equals(ParitySequenceGenerator.ExcludedWireName, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        GameRules.CommandTypesByWireName.Count.ShouldBeGreaterThanOrEqualTo(
            49,
            "the floor under the sweep: an emptied registry would make the comparison below true of " +
            "a corpus that drove nothing.");

        Corpus.CommandsDriven.ShouldBe(
            expected,
            "a registered command the corpus never sends is a command whose two dispatch paths " +
            "nobody compared. Adding a row to the registry adds it here for free; a row that stops " +
            "appearing means the walker can no longer construct or reach it.");
    }

    /// <summary>
    /// 🔴 <c>START_RUN</c>, the one command the two pipelines are designed to disagree on — pinned
    /// rather than quietly left out of the alphabet.
    /// </summary>
    [Fact]
    public async Task START_RUN_is_the_one_command_whose_two_pipelines_are_meant_to_differ()
    {
        var world = await GatewayWorld.WithAStartingPlayerAsync(
            ids: new LockstepIdGenerator(ParityCorpus.IdSeed));
        var before = await world.RowsAsync();

        var host = Hosts.Over(
            Worlds.CacheHolding(new WorldSlice(
                PlayerAggregate.Rehydrate(before.Player, Worlds.Content).Value, null)),
            clock: world.Clock,
            ids: new LockstepIdGenerator(ParityCorpus.IdSeed),
            content: Worlds.Content);

        var hostOutcome = await host.SubmitAsync(
            world.Player, null, new StartRunCommand(1, DifficultyTier.NORMAL), Worlds.Cancel);

        var reply = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.StartRun(sequence: 1, commandId: "c-start"), Worlds.Cancel);

        hostOutcome.Accepted.ShouldBeTrue("both sides must accept, or this case is about a refusal.");

        var body = Replies.Parse(reply, expectedStatus: 200);
        var gatewayRun = body.GetProperty("outcome").GetProperty("runId").GetString();
        var hostRun = hostOutcome.State.Run!.Id.Value;

        hostRun.ShouldNotBe(
            gatewayRun!,
            "the server allocates run identity from its id generator and the in-process host passes " +
            "no AllocatedRunId at all, so the two mint different run ids for the same command. That " +
            "is correct — a run id is the server's to issue — and it is why START_RUN is the one " +
            "command the parity corpus does not carry.");

        var hostHash = WireProjections.HashPlayerAndRun(
            hostOutcome.State.Player.ToSnapshot(), hostOutcome.State.Run!.ToSnapshot());

        hostHash.ShouldNotBe(
            body.GetProperty("stateHash").GetString()!,
            "and the run id is inside the hashed projection, so the disagreement is visible in the " +
            "state hash rather than tucked away in a field nobody compares.");
    }

    /// <summary>
    /// The two hashes are two computations, not one field read twice.
    /// </summary>
    /// <remarks>
    /// The gateway computes its <c>stateHash</c> in its own response builder; the host's outcome
    /// carries none, so the parity driver computes the client side itself. If a state hash were ever
    /// added to the use case's outcome and both sides started reading it, this comparison would
    /// become a tautology — with every case above still green.
    /// </remarks>
    [Fact]
    public void The_host_outcome_carries_no_state_hash_for_both_sides_to_read()
    {
        typeof(ApplyCommandOutcome)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ShouldNotContain(
                "StateHash",
                "the client side of the parity comparison must be computed from the outcome's state, " +
                "not read off a field the server side also reads — that would make every sequence " +
                "above agree by construction.");

        typeof(CommandResponse)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ShouldContain(
                "StateHash",
                "the negative control: the server side genuinely does answer a hash of its own, so " +
                "the assertion above is about an asymmetry that exists rather than about a name " +
                "nothing has.");
    }

    // ═══════════════════════════════════════════════════════ the committed table

    [Theory]
    [MemberData(nameof(ChunkIds))]
    public void Every_committed_chunk_hash_still_covers_its_hundred_sequences(int chunk)
    {
        var first = chunk * ParityBaseline.ChunkSize;

        Corpus.ChunkWires[chunk].ShouldBe(
            ParityBaseline.Chunks[chunk],
            $"sequences {first.ToString(CultureInfo.InvariantCulture)}–" +
            $"{(first + ParityBaseline.ChunkSize - 1).ToString(CultureInfo.InvariantCulture)} no " +
            "longer produce the committed answers. The two pipelines may still agree with each other " +
            "and both have moved — which is a determinism break, not a stale test. Establish what " +
            "changed, write it down, and only then regenerate through the documented command.");
    }

    [Fact]
    public void The_committed_aggregate_still_covers_all_the_chunks()
    {
        ParityBaseline.Chunks.Count.ShouldBe(
            ParitySequenceGenerator.SequenceCount / ParitySequenceGenerator.ChunkSize,
            "the aggregate is a hash over the chunk list; a table with no chunks would make the " +
            "comparison below true of an empty corpus.");

        Corpus.Aggregate.ShouldBe(ParityBaseline.Aggregate);
    }

    [Theory]
    [MemberData(nameof(NamedRowIds))]
    public void Every_named_row_still_pins_the_sequence_its_property_names(string id)
    {
        var committed = ParityBaseline.Row(id);
        var resolved = Corpus.Named.Single(named => named.Id.Equals(id, StringComparison.Ordinal));

        resolved.Index.ShouldBe(
            committed.Sequence,
            $"'{id}' names a property of the corpus, and the first sequence carrying it has moved.");

        Corpus.Outcomes[resolved.Index].Steps.Count.ShouldBe(committed.Steps);
        Corpus.Wires[resolved.Index].ShouldBe(committed.Hash);
    }

    [Fact]
    public void The_committed_table_is_the_corpus_the_generator_describes()
    {
        ParityBaseline.Sequences.ShouldBe(
            ParitySequenceGenerator.SequenceCount,
            "`14` §13 asks for 1 000 command sequences.");

        ParitySequenceGenerator.SequenceCount.ShouldBe(
            1_000, "transcribed from `14` §13 rather than read back off the generator.");

        ParityBaseline.ChunkSize.ShouldBe(ParitySequenceGenerator.ChunkSize);

        ParityBaseline.BaselineSeed.ShouldBe(
            "0x" + ParitySequenceGenerator.BaselineSeed.ToString("x16", CultureInfo.InvariantCulture),
            "the seed is the corpus: a thousand sequences are not committed, this one number and the " +
            "walker reproduce every one of them.");

        ParityBaseline.BaselineState.ShouldBe(
            Corpus.BaselineHash,
            "and every sequence starts from the same run. A moved baseline is a moved corpus even " +
            "when the walker has not changed at all.");
    }

    [Fact]
    public void The_committed_table_is_embedded_in_this_assembly()
    {
        typeof(ParityBaseline).Assembly.GetManifestResourceNames().ShouldContain(
            "SlayIdleRepeat.Application.Tests.Parity.ParityCorpusBaseline.json");

        ParityBaseline.RawText.Length.ShouldBeGreaterThan(1_000);
    }

    [Fact]
    public void The_committed_header_is_the_one_the_writer_renders()
    {
        ParityBaseline.Comment.ShouldBe(
            ParityBaselineWriter.HeaderLines,
            "the header states what this corpus compares and what it does not. A committed file " +
            "whose header has drifted from the writer's is a table making a claim nobody wrote.");
    }

    [Fact]
    public void The_committed_header_says_what_the_corpus_does_not_compare()
    {
        var header = string.Join(" ", ParityBaseline.Comment);

        header.ShouldContain(
            "START_RUN IS EXCLUDED",
            Case.Sensitive,
            "an alphabet with a hole in it and no explanation reads as an oversight.");

        header.ShouldContain(
            "two runtimes",
            Case.Sensitive,
            "the cross-architecture half of parity is the determinism job's, and a reader who takes " +
            "this corpus for that has been misled by the file rather than by the test.");

        header.ShouldContain("M7-06g", Case.Sensitive, "D45 moves these hashes and is still open.");
        header.ShouldContain("M4-16d", Case.Sensitive, "and D46 does the same.");
        header.ShouldContain("never between them", Case.Sensitive);
    }

    [Fact]
    public void The_committed_table_was_reviewed_by_a_person()
    {
        ParityBaseline.ReviewStatus.ShouldBe(ParityBaselineWriter.ReviewedStatus);
        ParityBaseline.ReviewedBy.ShouldNotBeNullOrWhiteSpace();
        ParityBaseline.ReviewedOn.ShouldNotBeNullOrWhiteSpace();
        ParityBaseline.ReviewWhy.Length.ShouldBeGreaterThan(
            80, "the review is a record of what moved and why, not a checkbox.");
    }

    // ══════════════════════════════════════════════════════ cost, and regeneration

    /// <summary>1 000 sequences through two pipelines belong to the unit tier, and there is no other tier.</summary>
    [Fact]
    public void The_1000_sequence_pass_stays_inside_the_unit_tier_budget()
    {
        Corpus.TotalElapsed.TotalSeconds.ShouldBeLessThan(
            BudgetSeconds,
            $"generation {Corpus.GenerationElapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture)} s, " +
            $"comparison {Corpus.ComparisonElapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture)} s. " +
            "This repository has no integration tier and is not getting one — if 1 000 sequences stop " +
            "fitting the unit tier, the answer is to say so, not to add a tier.");
    }

    /// <summary>
    /// The regeneration command's own output, round-tripped. Also the door itself: with
    /// <c>SIR_M5_12_PARITY_OUT</c> set, this writes the table.
    /// </summary>
    [Fact]
    public void The_text_the_documented_regeneration_command_writes_is_the_committed_table()
    {
        var destination = Environment.GetEnvironmentVariable(ParityBaselineWriter.DestinationVariable);
        var reasons = destination is { Length: > 0 }
            ? ParityBaselineWriter.ReasonsIn(destination)
            : ParityBaseline.Reasons;

        var rendered = ParityBaselineWriter.Render(Corpus, reasons);

        if (destination is { Length: > 0 })
        {
            ParityBaselineWriter.NamesTheCommittedBaseline(destination).ShouldBeTrue(
                $"{ParityBaselineWriter.DestinationVariable} is '{destination}', which is not " +
                $"{ParityBaselineWriter.CanonicalPath}. Regeneration replaces the committed table and " +
                "nothing else — the hand-written reasons are carried over from it.");

            File.WriteAllBytes(destination, new UTF8Encoding(false).GetBytes(rendered));
        }

        rendered.ShouldNotContain("\r", Case.Sensitive, "the table is LF-only on every leg.");

        WithoutHandWrittenRegions(rendered).ShouldBe(
            WithoutHandWrittenRegions(ParityBaseline.RawText),
            "the committed table is exactly what this build's regeneration command would write, byte " +
            "for byte, apart from the review block and the per-row reasons a human owns");

        using var parsed = JsonDocument.Parse(rendered);
        parsed.RootElement.GetProperty("review").GetProperty("status").GetString().ShouldBe(
            ParityBaselineWriter.UnreviewedStatus,
            "🔒 every render is unreviewed, so regenerating cannot make a break go green on its own.");
    }

    // ══════════════════════════════════════════════════════ theory data and helpers

    /// <summary>Every chunk of the committed table.</summary>
    public static TheoryData<int> ChunkIds()
    {
        var data = new TheoryData<int>();
        for (var chunk = 0; chunk < ParityBaseline.Chunks.Count; chunk++)
        {
            data.Add(chunk);
        }

        return data;
    }

    /// <summary>Every individually pinned row of the committed table.</summary>
    public static TheoryData<string> NamedRowIds()
    {
        var data = new TheoryData<string>();
        foreach (var row in ParityBaseline.Named)
        {
            data.Add(row.Id);
        }

        return data;
    }

    private static string WithoutHandWrittenRegions(string json) =>
        Regex.Replace(
            Regex.Replace(json, "\"review\": \\{.*?\\n  \\}", "\"review\": {}", RegexOptions.Singleline),
            "\"why\": \"[^\"]*\"",
            "\"why\": \"\"");
}
