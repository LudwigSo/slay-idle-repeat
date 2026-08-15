using Shouldly;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Triggers;

/// <summary>The shared contract suite for <see cref="IRunTriggerCounters"/> — every implementation is run through it.</summary>
/// <remarks>
/// <b>To implement it:</b> derive a test class from this one and override <see cref="Create"/>. Nothing
/// may be overridden — a rule an implementation can opt out of is not a contract.
/// </remarks>
public abstract class RunTriggerCountersContract
{
    /// <summary>Builds an empty implementation — a run that has counted nothing yet.</summary>
    /// <remarks>
    /// <c>private protected</c> because the interface is <c>internal</c> to <c>SlayIdleRepeat.Core</c>
    /// and reaches this assembly only through an <c>InternalsVisibleTo</c> grant, so a
    /// <c>protected</c> member of a <c>public</c> class could not name it.
    /// </remarks>
    private protected abstract IRunTriggerCounters Create();

    /// <summary>
    /// An instance the run has never counted reads <c>0</c>. A run that has killed nothing with
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

    /// <summary>Two instances are two counters — the per-instance rule restated on the seam so an implementation cannot collapse them.</summary>
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

    /// <summary>Ids are matched ordinally, like every id here.</summary>
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
    /// Zero is a writable count. A run that has just fired <c>PK_MIDAS</c> and reset its cycle is
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

    /// <summary>A negative count is refused, and the exception type is part of the contract.</summary>
    [Fact]
    public void A_negative_count_throws_ArgumentOutOfRangeException()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => Create().Write(EffectInstanceId.Of("HERO#0/PK_MIDAS_T1"), -1));
    }

    /// <summary>An id that names no holding is refused on both members — <c>default</c>, empty and whitespace alike.</summary>
    /// <remarks>
    /// <c>EffectInstanceId.Of</c> refuses all three, but a <c>record struct</c>'s generated
    /// constructor is public and <c>default</c> bypasses it — so these arrive here whatever
    /// <c>Of</c> does. Two <c>new EffectInstanceId("")</c> instances sharing one run counter is the
    /// per-instance rule failing in the direction that looks like it works.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_id_that_names_no_holding_throws_ArgumentException(string? value)
    {
        var id = value is null ? default : new EffectInstanceId(value);

        Should.Throw<ArgumentException>(() => Create().Write(id, 1));
        Should.Throw<ArgumentException>(() => Create().Read(id));
    }

    /// <summary>What gets persisted comes out in ascending ordinal id order, not in the order the hero happened to kill things.</summary>
    /// <remarks>
    /// <para>
    /// A dictionary's enumeration order is an implementation detail of the runtime, and the run
    /// snapshot is hashed on x64 and ARM64 and compared. Ordering here is what stops the same run
    /// hashing differently on two devices, which is why it is on the contract and not on one
    /// implementation.
    /// </para>
    /// <para>
    /// The keys are asserted, not the counts, and the counts deliberately do not co-vary with the id
    /// order — an earlier draft assigned 1/2/3 in id order and asserted the values, so ordering by the
    /// count would have passed identically. And the ids differ by case: <c>'A' (U+0041)</c> sorts
    /// before <c>'a' (U+0061)</c> ordinally and after it under most culture-aware collations, so a
    /// culture-sensitive comparer fails here rather than in production on somebody's phone.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_entries_come_out_in_ascending_ordinal_id_order()
    {
        var counters = Create();

        counters.Write(EffectInstanceId.Of("HERO#0/a-slot/PK_MIDAS_T1"), 9);
        counters.Write(EffectInstanceId.Of("HERO#0/Z-slot/PK_MIDAS_T1"), 4);
        counters.Write(EffectInstanceId.Of("HERO#0/A-slot/PK_MIDAS_T1"), 7);

        counters.Entries.Select(e => e.Key.Value).ShouldBe(
            new[]
            {
                "HERO#0/A-slot/PK_MIDAS_T1",
                "HERO#0/Z-slot/PK_MIDAS_T1",
                "HERO#0/a-slot/PK_MIDAS_T1",
            },
            Case.Sensitive,
            "ordinal puts every uppercase letter before every lowercase one; a culture-aware " +
            "collation interleaves them");

        counters.Entries.Select(e => e.Value).ShouldBe(new[] { 7, 4, 9 });
    }

    /// <summary>An empty run enumerates to nothing rather than failing.</summary>
    [Fact]
    public void An_empty_run_has_no_entries()
    {
        Create().Entries.ShouldBeEmpty();
    }

    /// <summary>The seam declares exactly one read, one write and one enumeration, and nothing else. Floored, or the claim passes forever over an emptied interface.</summary>
    /// <remarks>
    /// <c>Entries</c> is on the interface and not only on the implementation, because without it the
    /// seam does not close: the snapshot pairs would be reachable only through the concrete type, and
    /// the contract would buy nothing.
    /// </remarks>
    [Fact]
    public void The_seam_declares_one_read_one_write_and_one_enumeration()
    {
        var members = typeof(IRunTriggerCounters).GetMethods();

        members.Length.ShouldBe(
            3,
            "18 §3 needs a read, a write and the enumeration M3 persists — and a wider seam here " +
            "becomes M3's forced shape");
        members.Count(m => m.ReturnType == typeof(void)).ShouldBe(1, "one write");
        members.Count(m => m.ReturnType == typeof(int)).ShouldBe(1, "one read");
        members.Count(m => m.Name.StartsWith("get_", StringComparison.Ordinal)).ShouldBe(
            1,
            "one enumeration, and it is a getter — nothing here takes a snapshot argument");
    }
}

/// <summary>The in-<c>Core</c> implementation, run through the shared contract.</summary>
public sealed class RunTriggerCountersTests : RunTriggerCountersContract
{
    private protected override IRunTriggerCounters Create() => new RunTriggerCounters();
}
