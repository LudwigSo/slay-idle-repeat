using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// 🔒 `30` §2.1 <b>P3</b> — <em>"every command on every state returns a result rather than
/// throwing"</em>, on the one path where a host clock behind the persisted anchor used to reach
/// <c>Apply</c>'s caller as an <c>ArgumentOutOfRangeException</c>.
/// </summary>
/// <remarks>
/// 🔴 A real contradiction between two halves of one ruling: the energy span was clamped and both reset
/// guards written as <c>&gt;=</c>, all citing P3 — while <c>GameRules.MarkApplied</c> handed
/// <c>context.NowUtc</c> straight to <c>Player.MarkApplied</c>, which <em>throws</em> on an earlier
/// instant. <c>Player.MarkApplied</c>'s own remarks asserted the protection that did not exist.
/// <para>
/// 🔒 It was never hypothetical for want of a rewindable harness: the state needs only a stored
/// aggregate and a host whose clock is behind the instant it was last written at — one
/// <c>Rehydrate</c> away, and what every composition root that is not the harness does on every command.
/// </para>
/// <para>
/// 🔒 The aggregate invariant is unchanged: both <c>MarkApplied</c>s still refuse a backwards instant,
/// because an anchor moving backwards inside the model is a persistence defect and accepting it would
/// replay every reset boundary in between. What changed is that <c>Apply</c> no longer hands them one —
/// the same shape as the energy clamp, for the same reason: clamping in the rule would make a
/// persistence defect indistinguishable from skew.
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
    /// 🔒 `14` §16.3 / P3 — the <b>run's</b> anchor is floored too, on a <c>CommandKind.Run</c> command.
    /// </summary>
    /// <remarks>
    /// 🔴 Without this the run half of the clamp is untested and reverting it leaves every suite green.
    /// Every other test here drives a <c>Meta</c> command against a run-less slice, so
    /// <c>MarkApplied</c>'s run branch is never entered — and all 19 run rows are <c>Deferred</c>, so no
    /// production command reaches it either. It is also the sharper consequence: `14` §16.3 measures the
    /// sliding 48-hour run TTL off <em>this</em> field.
    /// <para>
    /// ⚠️ <c>Worlds.Context</c> rather than <c>Worlds.Drawing</c>: a run command handed a seed is a
    /// different defect with a different fixture.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_clock_behind_the_runs_anchor_is_floored_too()
    {
        var slice = Worlds.InARun();
        var stored = slice.Run!.LastAppliedAtUtc;

        var result = Core.GameRules.Execute(
            Worlds.RunTable((_, _) => HandlerResult.Accept()),
            slice,
            new Worlds.RunFixtureCommand(),
            Worlds.Context with { NowUtc = Worlds.NowUtc.AddMinutes(-5) });

        result.Accepted.ShouldBeTrue(
            "30 §2.1's P3 does not stop at the player. Run.MarkApplied throws on a backwards instant " +
            "exactly as Player.MarkApplied does, and a run command under host clock skew used to " +
            "carry that throw out of Apply.");

        result.NewState.Run!.LastAppliedAtUtc.ShouldBe(
            stored,
            "floored, not moved backwards: 14 §16.3 slides the 48-hour run TTL from this field, so " +
            "skew walking it backwards would hand a client a way to hold a run open.");
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
