using Shouldly;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Status;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Status;

/// <summary>
/// 🔒 `05` §5's <c>STUN</c> — <em>"Max 1.5 s per application, with a 3 s immunity window after"</em>,
/// and the ruling that makes both mandatory.
/// </summary>
/// <remarks>
/// `05` §5: <em>"<b>Stun immunity is mandatory.</b> Without it, stun-locking becomes the only viable
/// build."</em> Each half is probed with the other satisfied, because either alone leaves the failure
/// mode the section names: the cap alone permits a stun reapplied every 1.5 s forever, and the window
/// alone permits one 30 s stun.
/// </remarks>
public sealed class StunWindowTests
{
    private const double Cap = 1.5;
    private const double Immunity = 3.0;

    /// <summary>
    /// 🔒 <em>"Max 1.5 s per application"</em> — a 4 s application is clipped to 1.5 s, which is
    /// thirty ticks.
    /// </summary>
    /// <remarks>
    /// The assertion is on the tick the actor may act again, not on the stored duration: what the
    /// tick loop asks in slot 4a is <c>CanAct</c>, and a cap that clipped a stored number while
    /// leaving the gate open would pass a test of the number.
    /// </remarks>
    [Fact]
    public void An_application_longer_than_the_cap_is_clipped_to_it()
    {
        var window = new StunWindow(Cap, Immunity);

        window.Apply(tick: 0, requestedSeconds: 4.0).ShouldBe(29);

        window.CanAct(0).ShouldBeFalse();
        window.CanAct(29).ShouldBeFalse();
        window.CanAct(30).ShouldBeTrue();
    }

    /// <summary>
    /// An application shorter than the cap keeps its own length — the cap is a ceiling, not a length.
    /// </summary>
    /// <remarks>
    /// The negative control for the clip. Without it, an implementation that set every stun to 1.5 s
    /// would pass the test above and lengthen every short stun in the game.
    /// </remarks>
    [Fact]
    public void An_application_shorter_than_the_cap_keeps_its_own_length()
    {
        var window = new StunWindow(Cap, Immunity);

        window.Apply(tick: 0, requestedSeconds: 0.5).ShouldBe(9);

        window.CanAct(9).ShouldBeFalse();
        window.CanAct(10).ShouldBeTrue();
    }

    /// <summary>
    /// 🔒 <em>"a 3 s immunity window <b>after</b>"</em> — a second stun inside the window is refused
    /// outright, and the first one is not extended by it.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Both halves of the assertion are load-bearing.</b> A refusal that still extended the
    /// live stun would be a stun-lock wearing a refusal's return value: the actor would never act
    /// again while the attacker kept applying. So the tick the actor recovers on is asserted to be
    /// unchanged by the refused application.
    /// </remarks>
    [Fact]
    public void A_second_stun_inside_the_immunity_window_is_refused()
    {
        var window = new StunWindow(Cap, Immunity);

        var first = window.Apply(tick: 0, requestedSeconds: 1.5);
        first.ShouldBe(29);

        // 1.5 s of stun, then the window: immune through tick 29 + 60 = 89.
        window.Apply(tick: 30, requestedSeconds: 1.5).ShouldBeNull();
        window.Apply(tick: 89, requestedSeconds: 1.5).ShouldBeNull();

        window.CanAct(30).ShouldBeTrue("the refused applications must not have extended the stun");
    }

    /// <summary>
    /// 🔒 The window is measured from the tick the stun <b>ends</b>, not from the tick it was applied.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The discriminating case, and the reason this is its own test.</b> `05` §5 writes
    /// <em>"after"</em>. Measured from <em>application</em>, a 1.5 s stun applied at tick 0 would
    /// leave the actor stunnable again at tick 60; measured from the <em>end</em>, at tick 90. The
    /// difference is 1.5 s of protection out of 3 — half the window the section asked for — and no
    /// test of a single stun would notice it, because a single stun behaves identically either way.
    /// </remarks>
    [Fact]
    public void The_immunity_window_runs_from_the_end_of_the_stun_and_not_from_its_application()
    {
        var window = new StunWindow(Cap, Immunity);

        window.Apply(tick: 0, requestedSeconds: 1.5).ShouldBe(29);

        // 🔴 TICK 61 IS THE ONLY PROBE THAT SEPARATES THE TWO READINGS, and the first version of this
        // test did not have it — review found it. Application-anchored gives
        // _immuneUntilTick = 0 + 60 = 60, and the refusal is `tick <= _immuneUntilTick`, so tick 60
        // is refused under BOTH readings and proves nothing. Tick 61 is the first tick the
        // application-anchored window has opened and the end-anchored one has not.
        window.Apply(tick: 60, requestedSeconds: 1.5).ShouldBeNull();
        window.Apply(tick: 61, requestedSeconds: 1.5).ShouldBeNull(
            "application-anchored the window would have opened at tick 61; it runs from tick 29");

        window.Apply(tick: 89, requestedSeconds: 1.5).ShouldBeNull();
        window.Apply(tick: 90, requestedSeconds: 1.5).ShouldBe(119);
    }

    /// <summary>
    /// The window opens the tick after it closes — a third stun at the first legal tick lands.
    /// </summary>
    /// <remarks>
    /// The positive control for the refusals above: a window that never opened would satisfy every
    /// "is refused" assertion in this file and make the actor permanently stun-immune, which is the
    /// opposite failure and equally invisible without this.
    /// </remarks>
    [Fact]
    public void The_window_closes_and_a_later_stun_lands()
    {
        var window = new StunWindow(Cap, Immunity);

        window.Apply(tick: 0, requestedSeconds: 1.5).ShouldBe(29);
        window.Apply(tick: 89, requestedSeconds: 1.5).ShouldBeNull();
        window.Apply(tick: 90, requestedSeconds: 1.5).ShouldBe(119);
    }

    /// <summary>
    /// 🔒 Clearing the stun leaves the immunity window standing — `18` §2.3's <c>REMOVE_STATUS</c>
    /// must not make a cleansed actor a better stun-lock target than an uncleansed one.
    /// </summary>
    [Fact]
    public void A_cleanse_clears_the_stun_and_not_the_immunity()
    {
        var window = new StunWindow(Cap, Immunity);

        window.Apply(tick: 0, requestedSeconds: 1.5);
        window.Clear();

        window.CanAct(0).ShouldBeTrue();
        window.Apply(tick: 1, requestedSeconds: 1.5).ShouldBeNull(
            "05 §5's immunity is mandatory and a cleanse is not an exemption from it");
    }

    /// <summary>
    /// A cap or a window shorter than one simulation tick is refused rather than silently binding on
    /// nothing.
    /// </summary>
    /// <remarks>
    /// `05` §3's tick is 0.05 s. A window of 0.01 s rounds to zero ticks, which is no immunity at
    /// all — the state `05` §5 calls stun-locking — and would otherwise be indistinguishable from a
    /// correctly configured engine.
    /// </remarks>
    [Fact]
    public void Limits_shorter_than_one_tick_are_refused()
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(() => new StunWindow(0.01, 0.01));

        thrown.Message.ShouldContain("stun-locking", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 The window is the whole of `05` §5's protection: a stun reapplied on <b>every</b> tick still
    /// leaves the actor able to act for most of the fight.
    /// </summary>
    /// <remarks>
    /// 🔴 The rule as the outcome §5 cares about — <em>"stun-locking becomes the only viable build"</em> —
    /// rather than as the two constants the tests above pin. Over 1800 ticks the pattern is 30 stunned
    /// then 60 free. An implementation that refused correctly but extended on refusal would score 0 here
    /// and pass every other test in this file.
    /// </remarks>
    [Fact]
    public void A_stun_reapplied_every_tick_still_leaves_the_actor_acting_for_most_of_the_fight()
    {
        var window = new StunWindow(Cap, Immunity);
        var actionable = 0;

        for (var tick = 0; tick < CombatLog.MaxTicks; tick++)
        {
            window.Apply(tick, requestedSeconds: 1.5);

            if (window.CanAct(tick))
            {
                actionable++;
            }
        }

        // 30 stunned + 60 free per 90-tick cycle over 1800 ticks.
        actionable.ShouldBe(1200);
    }
}
