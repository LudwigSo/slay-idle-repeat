using Shouldly;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Enemies;

/// <summary>
/// 🔒 The shared contract suite for <c>IEliteModifierHistory</c> — every implementation is run
/// through it, including M4-01's and M3's, which do not exist yet.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 Steering S7: <em>"Add the <c>InMemory</c> fake AND the shared contract suite in the same change
/// as the port."</em> This seam is not a port — it declares no I/O — but it has the property that
/// matters: it will have several implementations written by people who never read each other's,
/// months apart. `24` §4.10's B2 protection owns the rule and <c>LuckService</c> (M4-01) implements
/// it; the state lives on the <c>Run</c> aggregate (M1-05) under the run controller (M3).
/// </para>
/// <para>
/// <b>To implement <c>IEliteModifierHistory</c>:</b> derive a test class from this one, override
/// <see cref="Create"/> to build your implementation, and the rules below run against it. Nothing
/// else is required, and nothing below may be overridden — a rule an implementation is allowed to
/// opt out of is not a contract.
/// </para>
/// </remarks>
public abstract class EliteModifierHistoryContract
{
    /// <summary>Builds a fresh implementation, as a run that has fought no Elite yet.</summary>
    /// <remarks>
    /// ⚠️ <c>private protected</c>, not <c>protected</c>: <c>IEliteModifierHistory</c> is
    /// <c>internal</c> to <c>SlayIdleRepeat.Core</c> and reaches this assembly only through `30`
    /// §11.3's <c>InternalsVisibleTo</c> grant, so a <c>protected</c> member of a <c>public</c> class
    /// could not name it.
    /// </remarks>
    private protected abstract IEliteModifierHistory Create();

    /// <summary>
    /// 🔒 A run that has fought no Elite reads <c>null</c> — a reading, not an error. The first
    /// Elite of a run draws from all eight.
    /// </summary>
    [Fact]
    public void A_run_that_has_fought_no_elite_has_no_previous_modifier() =>
        Create().PreviousEliteModifier.ShouldBeNull();

    /// <summary>Recording is what the next draw reads.</summary>
    [Fact]
    public void The_recorded_modifier_is_the_one_the_next_draw_reads()
    {
        var history = Create();

        history.RecordEliteModifier(EliteModifier.VOLATILE);

        history.PreviousEliteModifier.ShouldBe(EliteModifier.VOLATILE);
    }

    /// <summary>
    /// 🔒 <em>"the <b>immediately</b> preceding Elite"</em> — the seam holds one value, not a
    /// history. An implementation that remembered every modifier of the run would exclude seven of
    /// eight by the fourth Elite.
    /// </summary>
    [Fact]
    public void Only_the_immediately_preceding_modifier_is_remembered()
    {
        var history = Create();

        history.RecordEliteModifier(EliteModifier.ENRAGED);
        history.RecordEliteModifier(EliteModifier.SWIFT);
        history.RecordEliteModifier(EliteModifier.CURSED);

        history.PreviousEliteModifier.ShouldBe(EliteModifier.CURSED);
    }

    /// <summary>Recording the same modifier twice is not an error — two Elites can, over a run.</summary>
    [Fact]
    public void Recording_the_same_modifier_twice_is_idempotent_rather_than_an_error()
    {
        var history = Create();

        history.RecordEliteModifier(EliteModifier.ARMORED);
        history.RecordEliteModifier(EliteModifier.ARMORED);

        history.PreviousEliteModifier.ShouldBe(EliteModifier.ARMORED);
    }

    /// <summary>
    /// 🔒 An undeclared value is rejected. Recording one would exclude nothing on the next draw,
    /// which is `05` §6.2's rule quietly switching itself off.
    /// </summary>
    [Fact]
    public void An_undeclared_modifier_is_rejected_rather_than_recorded()
    {
        var history = Create();

        Should.Throw<ArgumentOutOfRangeException>(() => history.RecordEliteModifier((EliteModifier)99));

        history.PreviousEliteModifier.ShouldBeNull("a rejected record is not a record");
    }

    /// <summary>
    /// 🔒 S3 — the floor under this contract's own subject set. Every rule above names members of
    /// <c>EliteModifier</c>; `05` §6.2 declares eight, and a shrunken enum would leave these rules
    /// asserting over a vocabulary the section does not describe.
    /// </summary>
    [Fact]
    public void The_eight_modifiers_05_section_6_2_declares_are_all_present()
    {
        Enum.GetValues<EliteModifier>().Length.ShouldBe(8);

        Enum.GetNames<EliteModifier>().ShouldBe(
            new[] { "ENRAGED", "ARMORED", "VAMPIRIC", "VOLATILE", "SHIELDED", "SWIFT", "CURSED", "REFLECTIVE" },
            ignoreOrder: true);
    }
}

/// <summary>
/// The in-<c>Core</c> implementation, run through the shared contract.
/// </summary>
public sealed class EliteModifierHistoryTests : EliteModifierHistoryContract
{
    private protected override IEliteModifierHistory Create() => new EliteModifierHistory();

    /// <summary>
    /// Rehydration from what a run's snapshot carried — the path M3 uses when a run resumes.
    /// </summary>
    [Fact]
    public void A_restored_history_reads_what_the_runs_snapshot_carried()
    {
        EliteModifierHistory.Restore(null).PreviousEliteModifier.ShouldBeNull();
        EliteModifierHistory.Restore(EliteModifier.SHIELDED).PreviousEliteModifier
            .ShouldBe(EliteModifier.SHIELDED);
    }

    [Fact]
    public void Restoring_an_undeclared_modifier_fails_rather_than_resuming_a_broken_run() =>
        Should.Throw<ArgumentOutOfRangeException>(() => EliteModifierHistory.Restore((EliteModifier)0));
}
