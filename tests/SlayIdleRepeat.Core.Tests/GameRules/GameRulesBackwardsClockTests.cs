using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// 🔒 `30` §2.1 <b>P3</b> — <em>"every command on every state returns a result rather than
/// throwing"</em>, on the one path where a host clock behind the persisted anchor used to reach
/// <c>Apply</c>'s caller as an <c>ArgumentOutOfRangeException</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Carried-forward item 20, and it was a real contradiction between two halves of one
/// ruling.</b> M1-08 clamped the energy span — <c>Math.Max(0L, sinceAnchor.Ticks)</c> — and wrote
/// the two reset guards as <c>&gt;=</c>, both citing <b>P3</b> and both saying in as many words that
/// a host clock microseconds behind the stored anchor must not throw out of <c>Apply</c>. The third
/// path was left alone: <c>GameRules.MarkApplied</c> handed <c>context.NowUtc</c> straight to
/// <c>Player.MarkApplied</c>, which <em>throws</em> on an instant before the one stored — and
/// <c>Player.MarkApplied</c>'s own remarks already asserted the protection that did not exist
/// (<em>"an earlier instant means a clock moved backwards, and M1-08's <c>AdvanceTime</c> is
/// specified to clamp that rather than pass it on"</em>). Two halves of one ruling disagreed, and
/// the comment describing the agreement was on the half that lost.
/// </para>
/// <para>
/// 🔒 <b>Why it could not be reached from the harness, and why that never made it hypothetical.</b>
/// M1-11 made <c>VirtualClock</c> forward-only rather than settle this, reasoning that a rewindable
/// harness would be the only producer of the state and whichever test was written first would settle
/// a ruling nobody had made. But the state needs no rewindable clock: it needs a stored aggregate and
/// a host whose clock is behind the instant that aggregate was last written at, which is one `30`
/// §11.3 <c>Rehydrate</c> away and is what every composition root that is not the harness does on
/// every command. <c>Worlds</c>' own fixture comment had already recorded the consequence —
/// <em>"one earlier would make every accepted command throw"</em> — as a property of the fixture
/// rather than as the defect it was.
/// </para>
/// <para>
/// 🔒 <b>The aggregate invariant is unchanged, and deliberately so.</b> <c>Player.MarkApplied</c> and
/// <c>Run.MarkApplied</c> still refuse a backwards instant: an anchor moving backwards inside the
/// model is a real persistence defect, and silently accepting it would replay every reset boundary
/// in between. What changed is that <c>Apply</c> no longer hands them one — the same shape as the
/// energy clamp, which left <c>EnergyMath.Accrue</c> refusing a negative span and floored the value
/// at the caller, for the reason stated there: <em>clamping it in the rule would make a persistence
/// defect indistinguishable from skew</em>.
/// </para>
/// </remarks>
public sealed class GameRulesBackwardsClockTests
{
    /// <summary>
    /// `30` §2.1 P3 — a host clock behind the player's stored anchor returns a result.
    /// </summary>
    [Fact]
    public void A_clock_behind_the_players_anchor_returns_a_result_rather_than_throwing()
    {
        var result = Handlers.BeginSessions.Send(
            Handlers.BeginSessions.Slice(),
            Handlers.BeginSessions.Morning.AddMinutes(-5));

        result.Accepted.ShouldBeTrue(
            "30 §2.1's P3 makes every command on every state return a result. A clock five minutes " +
            "behind the persisted LastAppliedAtUtc is host skew, not a malformed command, and it came " +
            "out of Apply as an ArgumentOutOfRangeException from Player.MarkApplied until M1-12.");
    }

    /// <summary>
    /// `14` §16.3 / `30` §2.1 P3 — the clamp leaves the TTL anchor where it was.
    /// </summary>
    /// <remarks>
    /// The half that makes the clamp safe rather than merely quiet. `14` §16.3 measures the sliding
    /// 48-hour run TTL from this instant, so letting skew walk it backwards would hand a client a way
    /// to hold a run open. Floored at the stored value, a backwards clock does neither — the same
    /// sentence M1-08 wrote about energy: it costs the player nothing and grants them nothing.
    /// </remarks>
    [Fact]
    public void A_backwards_clock_leaves_the_stored_anchor_where_it_was()
    {
        var slice = Handlers.BeginSessions.Slice();

        slice.Player.LastAppliedAtUtc.ShouldBe(
            Handlers.BeginSessions.Morning,
            "the fixture's premise: the aggregate was last written at Morning, and the command below " +
            "is applied five minutes before that.");

        var result = Handlers.BeginSessions.Send(slice, Handlers.BeginSessions.Morning.AddMinutes(-5));

        result.NewState.Player.LastAppliedAtUtc.ShouldBe(
            Handlers.BeginSessions.Morning,
            "floored at the stored instant, which is what 'a backwards clock grants nothing and costs " +
            "nothing' means for 14 §16.3's TTL.");
    }

    /// <summary>
    /// `30` §2.1 P3 / `14` §16.3 — the ordinary forwards case still advances, so the clamp is a floor
    /// and not a freeze.
    /// </summary>
    /// <remarks>
    /// 🔒 The contrast half, without which both assertions above pass just as happily over an
    /// <c>Apply</c> that had stopped writing the anchor at all — which is one line away from the
    /// clamp, and is the failure <c>GameRules.MarkApplied</c>'s own remarks describe: every command
    /// re-accruing from the same instant forever.
    /// </remarks>
    [Fact]
    public void A_clock_ahead_of_the_anchor_still_advances_it()
    {
        var later = Handlers.BeginSessions.Morning.AddMinutes(5);

        var result = Handlers.BeginSessions.Send(Handlers.BeginSessions.Slice(), later);

        result.NewState.Player.LastAppliedAtUtc.ShouldBe(
            later,
            "a clamp that never advanced would be indistinguishable from these tests' subject.");
    }
}
