using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// Which stored content bundles a sweep may delete. Pure policy: the moment and the inventory both
/// arrive as values, so "expiring on the tick" and "expired by one tick" are two calls rather than
/// a test that waits.
/// </summary>
/// <remarks>
/// Deleting a bundle a live run is pinned to strands that run mid-play, so the interesting cases
/// are all about what is <em>kept</em>. The one case about what is dropped is what stops the rule
/// from being satisfied by a sweep that never deletes anything.
/// </remarks>
public sealed class ContentRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromHours(48);

    private static readonly ContentVersion Current = Version('f');
    private static readonly ContentVersion Older = Version('a');

    private static ContentVersion Version(char hexDigit) =>
        ContentVersion.FromHex(new string(hexDigit, ContentVersion.HexLength));

    private static IReadOnlyList<ContentVersion> Sweep(params RetainedVersion[] stored) =>
        ContentRetention.Sweep(Now, Current, stored, Window);

    // ------------------------------------------------------------------------ what is never swept

    /// <summary>
    /// The version being served now is not a candidate whatever its age. It is the one every fresh
    /// session will ask for next, so an unreferenced-for-a-year current version is still the one
    /// bundle that must exist.
    /// </summary>
    [Fact]
    public void The_current_version_is_never_swept_even_when_its_last_reference_is_far_past_the_window()
    {
        Sweep(new RetainedVersion(Current, Now - TimeSpan.FromDays(365))).ShouldBeEmpty();
    }

    [Fact]
    public void A_version_referenced_inside_the_window_is_kept()
    {
        Sweep(new RetainedVersion(Older, Now - TimeSpan.FromHours(1))).ShouldBeEmpty();
    }

    /// <summary>
    /// The boundary, kept side: a last reference exactly at <c>now - window</c> is not yet
    /// <em>older</em> than the window. Asserted beside its one-tick neighbour below, because a
    /// comparison written with the wrong strictness passes one of the two and fails the other.
    /// </summary>
    [Fact]
    public void A_version_last_referenced_exactly_on_the_window_boundary_is_kept()
    {
        Sweep(new RetainedVersion(Older, Now - Window)).ShouldBeEmpty();
    }

    // ----------------------------------------------------------------------------- what is swept

    /// <summary>The boundary, swept side: one tick older than the boundary and it goes.</summary>
    [Fact]
    public void A_version_last_referenced_one_tick_past_the_window_boundary_is_swept()
    {
        Sweep(new RetainedVersion(Older, Now - Window - TimeSpan.FromTicks(1)))
            .ShouldBe([Older]);
    }

    [Fact]
    public void A_version_whose_last_reference_is_well_past_the_window_is_swept()
    {
        Sweep(new RetainedVersion(Older, Now - TimeSpan.FromHours(72)))
            .ShouldBe([Older]);
    }

    /// <summary>
    /// The current version and an expired one together: exactly one of them goes, which no
    /// single-entry case can show.
    /// </summary>
    [Fact]
    public void A_sweep_drops_the_expired_version_and_keeps_the_current_one_from_the_same_inventory()
    {
        var swept = Sweep(
            new RetainedVersion(Current, Now - TimeSpan.FromDays(9)),
            new RetainedVersion(Older, Now - TimeSpan.FromDays(9)));

        swept.ShouldBe([Older]);
    }

    // ------------------------------------------------------------------------ the shape of the answer

    /// <summary>
    /// A sweep has to name the same versions in the same order on every machine, so a run of it is
    /// reviewable as a diff rather than as a set.
    /// </summary>
    [Fact]
    public void The_swept_versions_come_back_ordinal_sorted_by_stamp()
    {
        var expired = Now - TimeSpan.FromDays(9);

        var swept = ContentRetention.Sweep(Now, Current,
        [
            new RetainedVersion(Version('c'), expired),
            new RetainedVersion(Version('0'), expired),
            new RetainedVersion(Version('a'), expired),
        ], Window);

        swept.Select(v => v.Value).ShouldBe(
            [Version('0').Value, Version('a').Value, Version('c').Value]);
    }

    /// <summary>
    /// A stamp stored under two references is one bundle and one deletion. Naming it twice would
    /// have the caller delete a file that is already gone and report a failure for it.
    /// </summary>
    [Fact]
    public void A_version_held_under_two_expired_references_is_named_once()
    {
        var swept = Sweep(
            new RetainedVersion(Older, Now - TimeSpan.FromDays(9)),
            new RetainedVersion(Older, Now - TimeSpan.FromDays(11)));

        swept.Count.ShouldBe(1);
        swept.ShouldBe([Older]);
    }

    /// <summary>
    /// Vacuous on purpose, and named so nobody reads it as evidence the rules work: an empty
    /// inventory has nothing to keep and nothing to drop. It pins only that an empty store is not
    /// an error and not a null.
    /// </summary>
    [Fact]
    public void An_empty_store_sweeps_to_nothing_which_is_the_vacuous_case_and_not_a_rule_passing()
    {
        ContentRetention.Sweep(Now, Current, [], Window).ShouldBeEmpty();
    }

    [Fact]
    public void A_window_that_is_not_positive_is_refused_because_it_would_delete_a_version_the_moment_it_stopped_being_current()
    {
        Action act = () => _ = ContentRetention.Sweep(Now, Current, [], TimeSpan.Zero);

        Should.Throw<ArgumentOutOfRangeException>(act);
    }

    // ------------------------------------------------------------------------------- the window

    /// <summary>
    /// ⚠️ The 48 hours is an <b>inference, not a specified number</b>. No retention interval is
    /// authored anywhere in this project: no document states one and no configuration carries one.
    /// It is taken from the run-state cache TTL, on the reasoning that a bundle stops being needed
    /// once the last run that could still be pinned to it has itself expired — the narrowest window
    /// that cannot strand a live run. This case pins the inference and its provenance together, so
    /// the day somebody authors a real interval this constant gives way to it rather than being
    /// quietly re-justified.
    /// </summary>
    [Fact]
    public void The_default_window_is_the_forty_eight_hours_inferred_from_the_run_state_cache_ttl()
    {
        ContentRetention.WindowAlignedToRunTtl.ShouldBe(TimeSpan.FromHours(48),
            "aligned to the run-state cache TTL by inference — no retention interval is authored " +
            "anywhere, so this number is derived and must never be presented as a decided one");
    }
}
