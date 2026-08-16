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
}
