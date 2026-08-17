using Shouldly;
using SlayIdleRepeat.Application.Ports.Shared;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Shared;

/// <summary>
/// States what <see cref="IIdGeneratorPort"/> <em>means</em> (<c>23</c> §4.3, §5 A8) — written once
/// against the interface so a counting generator and a real one are held to one contract.
/// </summary>
/// <remarks>
/// <c>14</c> §16.2's idempotency scope is stated over the command id this port mints: a repeated id
/// makes the server replay an unrelated earlier outcome. That is why the cross-instance case below
/// exists and why it is not softened to "unique within one generator".
/// </remarks>
[ContractSuiteFor(typeof(IIdGeneratorPort))]
public abstract class IIdGeneratorPortContractTests
{
    /// <summary>How many identifiers the within-instance cases draw.</summary>
    protected const int DrawCount = 1000;

    /// <summary>How many identifiers each generator draws in the cross-instance case.</summary>
    protected const int CrossInstanceDrawCount = 500;

    /// <summary>
    /// How many identifiers of each kind every worker draws in the concurrent case. Bounded on
    /// purpose: the case is about contention, and contention is a function of the workers starting
    /// together rather than of how long they run.
    /// </summary>
    protected const int ConcurrentDrawsPerWorker = 2_000;

    /// <summary>A generator of the implementation under test. Successive calls return independent generators.</summary>
    protected abstract IIdGeneratorPort Create();

    /// <summary>A thousand draws, a thousand distinct guids.</summary>
    [Fact]
    public void A_thousand_guids_are_all_distinct()
    {
        var generator = Create();

        var drawn = Enumerable.Range(0, DrawCount).Select(_ => generator.NewGuid()).ToArray();

        drawn.Distinct().Count().ShouldBe(
            DrawCount,
            $"{DrawCount - drawn.Distinct().Count()} of {DrawCount} draws repeated a guid.");
    }

    /// <summary>No draw is <see cref="Guid.Empty"/> — the value callers use to mean "no identity".</summary>
    [Fact]
    public void No_guid_is_the_empty_guid()
    {
        var generator = Create();

        Enumerable.Range(0, DrawCount)
                  .Select(_ => generator.NewGuid())
                  .Where(id => id == Guid.Empty)
                  .ShouldBeEmpty(
                      "Guid.Empty means 'unset'. A generator that can return it makes a freshly "
                      + "minted identity indistinguishable from a missing one.");
    }

    /// <summary>A thousand draws, a thousand distinct command ids, compared ordinally.</summary>
    [Fact]
    public void A_thousand_command_ids_are_all_distinct()
    {
        var generator = Create();

        var drawn = Enumerable.Range(0, DrawCount).Select(_ => generator.NewCommandId()).ToArray();

        drawn.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            DrawCount,
            "compared ordinally, because that is how the idempotency store compares them — a pair "
            + "that differs only by case is two keys there and must be two ids here.");
    }

    /// <summary>A command id survives a URL, a log line and a header: non-empty, no whitespace, no control characters.</summary>
    /// <remarks>
    /// Collected rather than asserted inside the loop so a failure names every bad draw. One draw in
    /// a thousand going wrong is the interesting case, and an assertion that stops at the first one
    /// cannot tell "one generator is broken" from "one draw is".
    /// </remarks>
    [Fact]
    public void A_command_id_is_non_empty_and_free_of_whitespace_and_control_characters()
    {
        var generator = Create();

        var offenders = Enumerable.Range(0, DrawCount)
            .Select(_ => generator.NewCommandId())
            .Select(Fault)
            .Where(fault => fault is not null)
            .ToArray();

        offenders.ShouldBeEmpty(
            "a command id travels in a URL, a log line and a header unescaped. One that is empty, "
            + "or carries whitespace or a control character, is ambiguous in at least one of the "
            + $"three: {string.Join("; ", offenders)}");
    }

    /// <summary>What is wrong with <paramref name="id"/> as a command id, or <see langword="null"/>.</summary>
    private static string? Fault(string id) => id switch
    {
        null or "" => "a draw returned an empty command id",
        _ when id.Any(char.IsWhiteSpace) => $"'{id}' contains whitespace",
        _ when id.Any(char.IsControl) => $"'{id}' contains a control character",
        _ => null,
    };

    /// <summary>
    /// 🔒 Two independently created generators never collide, in either identifier
    /// (<c>14</c> §16.2). This is the case that fails a generator seeded from its own construction.
    /// </summary>
    /// <remarks>
    /// A counter that restarts at zero on construction satisfies every case above and produces the
    /// same first thousand ids after every process restart. An idempotency key that repeats after a
    /// restart is not an idempotency key: the server matches a brand-new command against a recorded
    /// outcome from the previous process and replays it.
    /// </remarks>
    [Fact]
    public void Two_independently_created_generators_never_collide()
    {
        var first = Create();
        var second = Create();

        // 🔒 Without this the case is satisfied by a fixture whose Create() hands back one shared
        // generator: two references to the same sequence never collide with themselves, and the
        // cross-instance claim below would then be a within-instance claim wearing its name.
        second.ShouldNotBeSameAs(
            first,
            "Create() returned the same generator twice. This case is about two INDEPENDENTLY "
            + "constructed generators; a shared instance makes it prove nothing beyond the "
            + "within-instance cases above.");

        var firstGuids = Enumerable.Range(0, CrossInstanceDrawCount).Select(_ => first.NewGuid()).ToHashSet();
        var secondGuids = Enumerable.Range(0, CrossInstanceDrawCount).Select(_ => second.NewGuid()).ToHashSet();

        firstGuids.Intersect(secondGuids).ShouldBeEmpty(
            "two generators built the same way handed out the same guid. A generator whose sequence "
            + "depends only on its own construction repeats itself on every process restart.");

        var firstIds = Enumerable.Range(0, CrossInstanceDrawCount)
                                 .Select(_ => first.NewCommandId())
                                 .ToHashSet(StringComparer.Ordinal);
        var secondIds = Enumerable.Range(0, CrossInstanceDrawCount)
                                  .Select(_ => second.NewCommandId())
                                  .ToHashSet(StringComparer.Ordinal);

        firstIds.Intersect(secondIds, StringComparer.Ordinal).ShouldBeEmpty(
            "the same defect in the command id, where it costs an idempotency replay rather than a "
            + "duplicate key.");
    }

    /// <summary>
    /// 🔒 Uniqueness holds when several threads draw from <em>one</em> generator at once
    /// (<c>14</c> §16.2).
    /// </summary>
    /// <remarks>
    /// Every case above draws on one thread, so all of them are satisfied by a generator that is
    /// unique only because nothing ever raced it. A counter advanced with <c>++</c> is a read, an
    /// add and a write: two callers minting an idempotency key at the same moment receive the same
    /// one, and the server then matches a brand-new command against an unrelated recorded outcome
    /// and replays its result. That is a defect of the generator, not of the caller — nothing about
    /// this port suggests a caller must serialise its draws — and it can exist in one implementation
    /// while the other is safe for free, which is precisely the divergence a shared suite is for.
    /// <para>
    /// 🔒 The interleaving is not deterministic and the assertion must not depend on it. It does
    /// not: a correct generator has no duplicates under <em>any</em> schedule, so this case can only
    /// go red on a real defect. There is no sleep and no timing threshold — the workers are held at
    /// a barrier so they start together, draw a bounded number of identifiers, and are joined.
    /// </para>
    /// </remarks>
    [Fact]
    public void Identifiers_stay_unique_when_several_threads_draw_from_one_generator()
    {
        var generator = Create();
        var workerCount = Math.Max(4, Environment.ProcessorCount);
        var guids = new Guid[workerCount][];
        var commandIds = new string[workerCount][];

        using (var gate = new Barrier(workerCount))
        {
            var workers = new Thread[workerCount];

            for (var worker = 0; worker < workerCount; worker++)
            {
                var index = worker;

                workers[index] = new Thread(() =>
                {
                    var drawnGuids = new Guid[ConcurrentDrawsPerWorker];
                    var drawnIds = new string[ConcurrentDrawsPerWorker];

                    // Dedicated threads and a barrier rather than pooled tasks: the pool may hand
                    // out fewer threads than workers, and the case would then measure the pool's
                    // growth rate instead of the generator's behaviour under contention.
                    gate.SignalAndWait();

                    for (var draw = 0; draw < ConcurrentDrawsPerWorker; draw++)
                    {
                        drawnGuids[draw] = generator.NewGuid();
                        drawnIds[draw] = generator.NewCommandId();
                    }

                    guids[index] = drawnGuids;
                    commandIds[index] = drawnIds;
                })
                {
                    IsBackground = true,
                    Name = $"id-generator-contract-{index}",
                };
            }

            foreach (var thread in workers)
            {
                thread.Start();
            }

            foreach (var thread in workers)
            {
                thread.Join();
            }
        }

        var allGuids = guids.SelectMany(drawn => drawn).ToArray();
        var distinctGuids = allGuids.Distinct().Count();

        distinctGuids.ShouldBe(
            allGuids.Length,
            $"{allGuids.Length - distinctGuids} of {allGuids.Length} guids drawn by {workerCount} "
            + "threads from one generator repeated. The within-instance case above draws on a single "
            + "thread and cannot see this: a counter advanced without an atomic increment loses "
            + "updates only when two callers overlap.");

        var allCommandIds = commandIds.SelectMany(drawn => drawn).ToArray();
        var distinctCommandIds = allCommandIds.Distinct(StringComparer.Ordinal).Count();

        distinctCommandIds.ShouldBe(
            allCommandIds.Length,
            $"{allCommandIds.Length - distinctCommandIds} of {allCommandIds.Length} command ids "
            + "repeated under the same contention. This is the one that costs money: a repeated "
            + "command id is a repeated idempotency key, and the server replays an unrelated earlier "
            + "outcome for a command it has never actually seen.");
    }
}
