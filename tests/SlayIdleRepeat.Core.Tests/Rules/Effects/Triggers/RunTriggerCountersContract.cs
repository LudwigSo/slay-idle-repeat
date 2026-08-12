using Shouldly;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Triggers;

/// <summary>
/// 🔒 The shared contract suite for <see cref="IRunTriggerCounters"/> — every implementation is run
/// through it, including the ones M1-05 and M3 have not written yet.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 Steering S7: <em>"Add the <c>InMemory</c> fake AND the shared contract suite in the same change
/// as the port. M0's first port shipped without its suite and its two implementations already
/// disagreed on the exception type they threw."</em> This is not a port — it declares no I/O — but it
/// has the property that matters: it will have several implementations written by people who never
/// read each other's, months apart. <c>RunStateViewContract</c> is the precedent one directory over.
/// </para>
/// <para>
/// <b>To implement <see cref="IRunTriggerCounters"/>:</b> derive a test class from this one, override
/// <see cref="Create"/>, and the rules below run against it. Nothing may be overridden — a rule an
/// implementation may opt out of is not a contract.
/// </para>
/// </remarks>
public abstract class RunTriggerCountersContract
{
    /// <summary>Builds an empty implementation — a run that has counted nothing yet.</summary>
    /// <remarks>
    /// <c>private protected</c> for <c>RunStateViewContract</c>'s reason: the interface is
    /// <c>internal</c> to <c>SlayIdleRepeat.Core</c> and reaches this assembly only through `30`
    /// §11.3's <c>InternalsVisibleTo</c> grant, so a <c>protected</c> member of a <c>public</c> class
    /// could not name it.
    /// </remarks>
    private protected abstract IRunTriggerCounters Create();

    /// <summary>
    /// 🔒 An instance the run has never counted reads <c>0</c>. A run that has killed nothing with
    /// <c>PK_MIDAS</c> has a count of zero, which is a reading and not an error.
    /// </summary>
    [Fact]
    public void An_unknown_instance_reads_zero()
    {
        Create().Read(EffectInstanceId.Of("HERO#0/perk-slot-2/PK_MIDAS_T1")).ShouldBe(0);
    }

    /// <summary>What was written comes back.</summary>
    [Fact]
    public void A_written_count_is_read_back()
    {
        var counters = Create();
        var midas = EffectInstanceId.Of("HERO#0/perk-slot-2/PK_MIDAS_T1");

        counters.Write(midas, 5);

        counters.Read(midas).ShouldBe(5);
    }

    /// <summary>A later write replaces the earlier one — the counter advances, it does not accumulate twice.</summary>
    [Fact]
    public void A_second_write_replaces_the_first()
    {
        var counters = Create();
        var midas = EffectInstanceId.Of("HERO#0/perk-slot-2/PK_MIDAS_T1");

        counters.Write(midas, 5);
        counters.Write(midas, 6);

        counters.Read(midas).ShouldBe(6);
    }

    /// <summary>
    /// 🔒 <b>Two instances are two counters</b> — the per-instance rule of `18` §3, restated on the
    /// seam so an implementation cannot collapse them.
    /// </summary>
    [Fact]
    public void Two_instances_hold_two_counters()
    {
        var counters = Create();
        var first = EffectInstanceId.Of("HERO#0/perk-slot-2/PK_MIDAS_T1");
        var second = EffectInstanceId.Of("HERO#0/gear-affix-3/PK_MIDAS_T1");

        counters.Write(first, 5);
        counters.Write(second, 1);

        counters.Read(first).ShouldBe(5);
        counters.Read(second).ShouldBe(1);
    }

    /// <summary>🔒 Ids are matched <b>ordinally</b> (`18` §8, `14` §8.2), like every id here.</summary>
    [Fact]
    public void Instance_ids_are_matched_ordinally()
    {
        var counters = Create();

        counters.Write(EffectInstanceId.Of("HERO#0/PK_MIDAS_T1"), 5);

        counters.Read(EffectInstanceId.Of("hero#0/pk_midas_t1")).ShouldBe(
            0,
            "a differently-cased id is a different holding, not the same one on a Turkish locale");
    }

    /// <summary>
    /// 🔒 Zero is a writable count. A run that has just fired <c>PK_MIDAS</c> and reset its cycle is
    /// a real state, and an implementation that treated zero as "delete the key" would still have to
    /// read it back as zero.
    /// </summary>
    [Fact]
    public void Zero_is_a_writable_count()
    {
        var counters = Create();
        var midas = EffectInstanceId.Of("HERO#0/PK_MIDAS_T1");

        counters.Write(midas, 7);
        counters.Write(midas, 0);

        counters.Read(midas).ShouldBe(0);
    }

    /// <summary>
    /// 🔒 A negative count is refused, and the exception type is part of the contract — S7's
    /// cautionary tale is exactly two implementations of one seam that disagreed on which one they
    /// threw.
    /// </summary>
    [Fact]
    public void A_negative_count_throws_ArgumentOutOfRangeException()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => Create().Write(EffectInstanceId.Of("HERO#0/PK_MIDAS_T1"), -1));
    }

    /// <summary>An id with no value at all is refused, for the same reason a blank one is.</summary>
    [Fact]
    public void An_instance_with_no_id_throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => Create().Write(default, 1));
    }

    /// <summary>
    /// 🔒 The seam declares exactly one read and one write, and nothing else. Floored, or the claim
    /// passes forever over an emptied interface (steering S3).
    /// </summary>
    [Fact]
    public void The_seam_declares_one_read_and_one_write()
    {
        var members = typeof(IRunTriggerCounters).GetMethods();

        members.Length.ShouldBe(2, "`18` §3 needs a read and a write, and a wider seam here becomes M3's forced shape");
        members.Count(m => m.ReturnType == typeof(void)).ShouldBe(1, "one write");
        members.Count(m => m.ReturnType == typeof(int)).ShouldBe(1, "one read");
    }
}

/// <summary>The in-<c>Core</c> implementation, run through the shared contract.</summary>
public sealed class RunTriggerCountersTests : RunTriggerCountersContract
{
    private protected override IRunTriggerCounters Create() => new RunTriggerCounters();

    /// <summary>
    /// 🔒 What M3 persists comes out in ascending ordinal id order, not in the order the hero
    /// happened to kill things.
    /// </summary>
    /// <remarks>
    /// A dictionary's enumeration order is an implementation detail of the runtime, and `14` §8.2
    /// hashes the run snapshot on x64 and ARM64 and compares. Ordering here is what stops the same
    /// run hashing differently on two devices.
    /// </remarks>
    [Fact]
    public void The_entries_come_out_in_ascending_ordinal_id_order()
    {
        var counters = new RunTriggerCounters();

        counters.Write(EffectInstanceId.Of("HERO#0/z-slot/PK_MIDAS_T1"), 3);
        counters.Write(EffectInstanceId.Of("HERO#0/a-slot/PK_MIDAS_T1"), 1);
        counters.Write(EffectInstanceId.Of("HERO#0/m-slot/PK_MIDAS_T1"), 2);

        counters.Entries.Select(e => e.Value).ShouldBe(new[] { 1, 2, 3 });
    }
}
