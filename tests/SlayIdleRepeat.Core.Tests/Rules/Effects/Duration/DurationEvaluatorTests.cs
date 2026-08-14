using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Duration;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Duration;

/// <summary>
/// 🔒 `18` §6 — the six duration scopes and the <c>until</c> early terminator.
/// </summary>
public sealed class DurationEvaluatorTests
{
    // ───────────────────────────────────────────────────────────── INSTANT and BATTLE

    /// <summary>`18` §6's <c>INSTANT</c>: it applies once and does not persist.</summary>
    [Fact]
    public void An_INSTANT_effect_has_already_ended_when_it_is_first_probed()
    {
        var outcome = Evaluate(Scoped(DurationScope.INSTANT), At(0.0));

        outcome.HasEnded.ShouldBeTrue();
        outcome.Reason.ShouldBe(DurationEndReason.Instant);
    }

    /// <summary>`18` §6's <c>BATTLE</c>: it ends when the battle does, and not before.</summary>
    [Fact]
    public void A_BATTLE_scoped_effect_runs_until_the_battle_ends()
    {
        var battle = Scoped(DurationScope.BATTLE);

        Evaluate(battle, At(89.95)).HasEnded.ShouldBeFalse(
            "05 §3 gives an ordinary fight 90 seconds and this one has not finished");

        var ended = Evaluate(battle, At(90.0) with { BattleEnded = true });
        ended.HasEnded.ShouldBeTrue();
        ended.Reason.ShouldBe(DurationEndReason.BattleEnded);
    }

    // ───────────────────────────────────────────────────────────── the timer

    /// <summary>
    /// `18` §7.10's Ossify carries <c>{"seconds": 6.0, "scope": "BATTLE"}</c>. The timer is measured
    /// from the moment the effect was applied, not from the start of the battle.
    /// </summary>
    [Fact]
    public void A_timer_is_measured_from_the_application_not_from_the_battle_start()
    {
        var ossify = Applied(
            new EffectDuration { Scope = DurationScope.BATTLE, Seconds = 6.0 },
            appliedAtSeconds: 14.0);

        DurationEvaluator.Evaluate(ossify, At(19.95)).HasEnded.ShouldBeFalse(
            "applied at 14 s with a 6 s timer, so it runs to 20 s");

        var expired = DurationEvaluator.Evaluate(ossify, At(20.0));
        expired.HasEnded.ShouldBeTrue();
        expired.Reason.ShouldBe(DurationEndReason.TimerElapsed);
    }

    // ───────────────────────────────────────────────────────────── PHASE

    /// <summary>
    /// 🔒 `18` §6: <c>PHASE</c> <em>"ends when the boss exits the phase in which the effect was
    /// applied"</em>. `18` §7.10's Bog Air — Gulgrot phase 2, <c>{"scope": "PHASE"}</c> with no
    /// timer at all.
    /// </summary>
    [Fact]
    public void Bog_Air_ends_when_Gulgrot_leaves_the_phase_it_was_applied_in()
    {
        var bogAir = Applied(
            new EffectDuration { Scope = DurationScope.PHASE },
            appliedAtSeconds: 8.0,
            appliedInPhase: 2);

        DurationEvaluator.Evaluate(bogAir, At(40.0) with { CurrentPhase = 2 }).HasEnded.ShouldBeFalse(
            "still in phase 2, and 18 §7.10 gives it no timer to run out");

        var exited = DurationEvaluator.Evaluate(bogAir, At(40.0) with { CurrentPhase = 3 });
        exited.HasEnded.ShouldBeTrue();
        exited.Reason.ShouldBe(DurationEndReason.PhaseExited);
    }

    /// <summary>
    /// 🔴 <b>R3</b> — every boss <c>AURA</c> mechanic is <c>PHASE</c>-scoped, which is what makes the
    /// <c>{999, BATTLE}</c> idiom unnecessary.
    /// </summary>
    /// <remarks>
    /// `18` §7.8's Thornmaw RAGE predates <c>PHASE</c>; §6 makes <c>AURA</c> mechanics <c>PHASE</c>-scoped
    /// <b>by definition</b>. The two are equivalent for Thornmaw <em>only</em> because phase 3 is never
    /// exited, which is why the artifact survived. This case shows they are not equivalent in general:
    /// applied in phase 2, the <c>PHASE</c> form ends at the exit and <c>{999, BATTLE}</c> does not.
    /// </remarks>
    [Fact]
    public void A_PHASE_aura_and_the_999_second_BATTLE_idiom_are_not_the_same_effect()
    {
        var probe = At(30.0) with { CurrentPhase = 3 };

        var aura = Applied(new EffectDuration { Scope = DurationScope.PHASE }, 10.0, appliedInPhase: 2);
        var artifact = Applied(
            new EffectDuration { Scope = DurationScope.BATTLE, Seconds = 999.0 }, 10.0, appliedInPhase: 2);

        DurationEvaluator.Evaluate(aura, probe).Reason.ShouldBe(
            DurationEndReason.PhaseExited, "18 §6: an AURA ends when the boss leaves its phase");

        DurationEvaluator.Evaluate(artifact, probe).HasEnded.ShouldBeFalse(
            "18 §7.8's {999, BATTLE} outlives the phase — the artifact R3 rules out");
    }

    /// <summary>
    /// `18` §6: <em>"Outside a boss fight it behaves as <c>BATTLE</c>"</em>. Not a special case —
    /// there is no phase to exit, so the battle boundary is the only one left.
    /// </summary>
    [Fact]
    public void Outside_a_boss_fight_PHASE_behaves_as_BATTLE()
    {
        var phaseScoped = Applied(new EffectDuration { Scope = DurationScope.PHASE }, 5.0, appliedInPhase: null);

        DurationEvaluator.Evaluate(phaseScoped, At(60.0)).HasEnded.ShouldBeFalse();

        var ended = DurationEvaluator.Evaluate(phaseScoped, At(90.0) with { BattleEnded = true });
        ended.HasEnded.ShouldBeTrue();
        ended.Reason.ShouldBe(DurationEndReason.BattleEnded);
    }

    /// <summary>
    /// 🔒 A <c>PHASE</c> effect is battle-bounded, so the battle's own boundary still ends it — even in
    /// the phase it was applied in, which the boss never leaves.
    /// </summary>
    /// <remarks>
    /// ⚠️ A real defect nothing pinned in either direction: the evaluator answered "not ended" for a
    /// phase-scoped effect still inside its own phase, so Thornmaw's mechanic re-authored as R3 requires
    /// outlived the battle — the one answer `18` §6 reserves for <c>STAGE</c>/<c>RUN</c>/<c>PERMANENT</c>,
    /// directly contradicting this file's own <c>A_battle_bounded_scope_does_not_outlive_the_battle</c>,
    /// both green. The hole was that every other <c>PHASE</c> case either changes phase or runs outside
    /// a boss fight.
    /// </remarks>
    [Fact]
    public void A_PHASE_effect_still_inside_its_own_phase_ends_when_the_battle_does()
    {
        var thornmawRage = Applied(new EffectDuration { Scope = DurationScope.PHASE }, 42.0, appliedInPhase: 3);

        DurationEvaluator.Evaluate(thornmawRage, At(80.0) with { CurrentPhase = 3 }).HasEnded.ShouldBeFalse(
            "the fight is still going and phase 3 is never exited");

        var ended = DurationEvaluator.Evaluate(
            thornmawRage, At(90.0) with { CurrentPhase = 3, BattleEnded = true });

        ended.HasEnded.ShouldBeTrue(
            "18 §6 puts PHASE among the battle-bounded scopes — DurationScopes.OutlivesTheBattle(PHASE) " +
            "is false, and an effect that survived here would contradict it");
        ended.Reason.ShouldBe(DurationEndReason.BattleEnded);
    }

    /// <summary>
    /// 🔒 `05` §3.1: <em>"Phases never revert — healing back above a threshold does not re-enter an
    /// earlier phase"</em>. A probe reporting an earlier phase than the application is therefore an
    /// invariant violation, and it is refused rather than quietly read as "still inside".
    /// </summary>
    [Fact]
    public void A_phase_that_went_backwards_is_refused()
    {
        var applied = Applied(new EffectDuration { Scope = DurationScope.PHASE }, 5.0, appliedInPhase: 3);

        var thrown = Should.Throw<EffectContextException>(
            () => DurationEvaluator.Evaluate(applied, At(20.0) with { CurrentPhase = 2 }));

        thrown.Token.ShouldBe(nameof(DurationScope.PHASE));
        thrown.Message.ShouldContain("05 §3.1");
        thrown.Message.ShouldContain("BOSS_MECHANIC", Case.Sensitive);
    }

    /// <summary>
    /// A <c>PHASE</c> effect applied inside a boss fight whose probe carries no phase at all is the
    /// same contradiction from the other side — the fight cannot stop being a boss fight.
    /// </summary>
    [Fact]
    public void A_PHASE_effect_applied_in_a_phase_needs_a_phase_to_compare_against()
    {
        var applied = Applied(new EffectDuration { Scope = DurationScope.PHASE }, 5.0, appliedInPhase: 2);

        var thrown = Should.Throw<EffectContextException>(
            () => DurationEvaluator.Evaluate(applied, At(20.0) with { CurrentPhase = null }));

        thrown.Token.ShouldBe(nameof(DurationScope.PHASE));
        thrown.Message.ShouldContain("BOSS_MECHANIC_UNDER_TEST", Case.Sensitive);
    }

    // ───────────────────────────────────────────── the WARD_BROKEN terminator

    /// <summary>
    /// `18` §6 / §7.10 — Ossify's DR buff <em>"ends the moment the owner's ward pool breaks"</em>.
    /// </summary>
    [Fact]
    public void Ossifys_DR_buff_ends_the_moment_the_ward_breaks()
    {
        var ossify = Applied(
            new EffectDuration
            {
                Scope = DurationScope.BATTLE,
                Seconds = 6.0,
                Until = DurationTerminator.WARD_BROKEN,
            },
            appliedAtSeconds: 14.0);

        var broken = DurationEvaluator.Evaluate(
            ossify, At(16.0) with { OwnerWardEvent = WardPoolEvent.BrokenByDamage });

        broken.HasEnded.ShouldBeTrue();
        broken.Reason.ShouldBe(DurationEndReason.WardBroken);
    }

    /// <summary>
    /// 🔒 <b>`05` §4.1 is precise about what counts.</b> <c>WardBroken</c> fires <em>"the moment the
    /// pool reaches 0 <b>through damage</b>"</em>; <em>"segment expiry silently removes its remainder
    /// (<c>StatusExpired</c>), and does <b>not</b> fire <c>WardBroken</c>"</em>. An effect
    /// terminating on segment expiry would be a bug.
    /// </summary>
    /// <remarks>
    /// The probe carries a <see cref="WardPoolEvent"/> rather than a <c>bool</c> precisely so this is
    /// the <em>evaluator's</em> decision and not a caller's discipline — a caller cannot pass "the
    /// ward emptied" without saying <em>how</em>.
    /// </remarks>
    [Fact]
    public void A_ward_segment_expiring_does_not_fire_the_WARD_BROKEN_terminator()
    {
        var ossify = Applied(
            new EffectDuration
            {
                Scope = DurationScope.BATTLE,
                Seconds = 6.0,
                Until = DurationTerminator.WARD_BROKEN,
            },
            appliedAtSeconds: 14.0);

        DurationEvaluator.Evaluate(
            ossify, At(16.0) with { OwnerWardEvent = WardPoolEvent.SegmentExpired }).HasEnded.ShouldBeFalse(
            "05 §4.1: segment expiry emits StatusExpired and never WardBroken");

        // 🔒 The discriminating half, in the same test: an evaluator that ended nothing at all would
        // satisfy the line above. The only thing that differs between the two probes is HOW the pool
        // emptied, so this pair pins the distinction rather than the absence of an ending.
        DurationEvaluator.Evaluate(
            ossify, At(16.0) with { OwnerWardEvent = WardPoolEvent.BrokenByDamage }).HasEnded.ShouldBeTrue(
            "the same probe one second later, differing only in how the pool emptied, does end it");
    }

    /// <summary>
    /// The terminator fires only for an effect that asked for it. A ward breaking while an unrelated
    /// buff is running must not end that buff.
    /// </summary>
    [Fact]
    public void A_ward_breaking_does_not_end_an_effect_that_declared_no_terminator()
    {
        var breaking = At(16.0) with { OwnerWardEvent = WardPoolEvent.BrokenByDamage };

        var plainBuff = Applied(
            new EffectDuration { Scope = DurationScope.BATTLE, Seconds = 6.0 }, appliedAtSeconds: 14.0);

        var terminated = Applied(
            new EffectDuration
            {
                Scope = DurationScope.BATTLE,
                Seconds = 6.0,
                Until = DurationTerminator.WARD_BROKEN,
            },
            appliedAtSeconds: 14.0);

        DurationEvaluator.Evaluate(plainBuff, breaking).HasEnded.ShouldBeFalse();

        // 🔒 The discriminating half: the two applications differ only in the `until` key, so an
        // evaluator that ended nothing on this probe cannot satisfy both lines.
        DurationEvaluator.Evaluate(terminated, breaking).HasEnded.ShouldBeTrue(
            "the same ward break, on the effect that did declare the terminator");
    }

    /// <summary>
    /// `18` §6: <em>"<c>until</c> fields fire whichever comes first, terminator or timer."</em> The
    /// timer half of that sentence, on the very effect that carries both.
    /// </summary>
    [Fact]
    public void Ossifys_DR_buff_also_ends_on_its_timer_when_the_ward_survives()
    {
        var ossify = Applied(
            new EffectDuration
            {
                Scope = DurationScope.BATTLE,
                Seconds = 6.0,
                Until = DurationTerminator.WARD_BROKEN,
            },
            appliedAtSeconds: 14.0);

        var expired = DurationEvaluator.Evaluate(ossify, At(20.0));

        expired.HasEnded.ShouldBeTrue();
        expired.Reason.ShouldBe(DurationEndReason.TimerElapsed);
    }

    // ───────────────────────────────────────────── the run-layer scopes (A4 — declared, not wired)

    /// <summary>
    /// 🔒 <c>STAGE</c>, <c>RUN</c> and <c>PERMANENT</c> outlive a battle, so the battle-scoped
    /// evaluator never ends one. The run controller that does is M3's, over M1-05's <c>Run</c>
    /// aggregate (kickoff A4) — a declared scope with no run-layer consumer is the correct end state,
    /// and a placeholder controller here would be a second mechanism to delete later.
    /// </summary>
    [Theory]
    [InlineData(DurationScope.STAGE)]
    [InlineData(DurationScope.RUN)]
    [InlineData(DurationScope.PERMANENT)]
    public void A_scope_that_outlives_the_battle_is_never_ended_by_the_battle_evaluator(DurationScope scope)
    {
        DurationScopes.OutlivesTheBattle(scope).ShouldBeTrue();

        DurationEvaluator.Evaluate(
            Applied(new EffectDuration { Scope = scope }, 5.0),
            At(90.0) with { BattleEnded = true }).HasEnded.ShouldBeFalse();
    }

    /// <summary>
    /// The other half of the same claim: the three battle-bounded scopes are <b>not</b> in that set.
    /// Without this the predicate could answer <c>true</c> for everything and both arms would pass.
    /// </summary>
    [Theory]
    [InlineData(DurationScope.INSTANT)]
    [InlineData(DurationScope.BATTLE)]
    [InlineData(DurationScope.PHASE)]
    public void A_battle_bounded_scope_does_not_outlive_the_battle(DurationScope scope)
    {
        DurationScopes.OutlivesTheBattle(scope).ShouldBeFalse();
    }

    /// <summary>
    /// 🔒 A timer still ends a run-scoped effect — `18` §6's <em>"whichever comes first"</em> is
    /// about the terminator and the timer, and the scope is what happens when neither fires.
    /// </summary>
    [Fact]
    public void A_timer_still_ends_a_run_scoped_effect()
    {
        var timed = Applied(
            new EffectDuration { Scope = DurationScope.RUN, Seconds = 12.0 }, appliedAtSeconds: 4.0);

        DurationEvaluator.Evaluate(timed, At(16.0)).Reason.ShouldBe(DurationEndReason.TimerElapsed);
    }

    // ───────────────────────────────────────────── the shape `18` §1 calls the default

    /// <summary>
    /// `18` §1 writes <c>"duration": null</c> on the canonical passive. Such an effect is not a timed
    /// instance at all, and the battle evaluator never ends one.
    /// </summary>
    [Fact]
    public void An_effect_with_no_duration_block_is_not_duration_governed()
    {
        var passive = Applied(duration: null, appliedAtSeconds: 0.0);

        var outcome = DurationEvaluator.Evaluate(passive, At(90.0) with { BattleEnded = true });

        outcome.HasEnded.ShouldBeFalse();
        outcome.Reason.ShouldBe(DurationEndReason.NotEnded);
    }

    // ───────────────────────────────────────────── S3 · the subject-set floor

    /// <summary>
    /// 🔒 <b>Every scope is handled, and each answers with its own boundary.</b> S3 — a scope reaching
    /// no arm would make an effect immortal with nothing going red.
    /// </summary>
    /// <remarks>
    /// ⚠️ An expected reason per scope rather than <c>Should.NotThrow</c>, which a single <c>default</c>
    /// arm answering all six satisfies — proven by stubbing <c>Evaluate</c> as <c>=&gt; default</c>, at
    /// which point the loop went green while every per-scope fact went red.
    /// <para>
    /// The probe is the end of an ordinary 90 s fight with no boss phase, the one probe that splits the
    /// six three ways: <c>INSTANT</c> ended before it began, <c>BATTLE</c> and (with no phase to exit)
    /// <c>PHASE</c> end with the battle, and the three run-layer scopes outlive it.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_18_6_duration_scope_is_handled()
    {
        var expected = new Dictionary<DurationScope, DurationEndReason>
        {
            [DurationScope.INSTANT] = DurationEndReason.Instant,
            [DurationScope.BATTLE] = DurationEndReason.BattleEnded,
            [DurationScope.PHASE] = DurationEndReason.BattleEnded,
            [DurationScope.STAGE] = DurationEndReason.NotEnded,
            [DurationScope.RUN] = DurationEndReason.NotEnded,
            [DurationScope.PERMANENT] = DurationEndReason.NotEnded,
        };

        var scopes = Enum.GetValues<DurationScope>();

        scopes.Length.ShouldBe(6, "18 §11: '6 duration scopes = 5 + PHASE'");
        expected.Keys.Order().ShouldBe(
            scopes.Order(), "a seventh scope is unhandled here until this table answers for it");

        foreach (var scope in scopes)
        {
            DurationEvaluator.Evaluate(
                Applied(new EffectDuration { Scope = scope }, 0.0),
                At(90.0) with { BattleEnded = true }).Reason.ShouldBe(
                expected[scope], $"18 §6's {scope} reached no handler of its own in DurationEvaluator");
        }
    }

    /// <summary>
    /// 🔒 The same floor for the terminator. `18` §6 authors exactly one, and a second takes §10's
    /// route — at which point this fails until the evaluator answers for it.
    /// </summary>
    [Fact]
    public void Every_18_6_early_terminator_is_handled()
    {
        var terminators = Enum.GetValues<DurationTerminator>();

        terminators.ShouldBe([DurationTerminator.WARD_BROKEN], "18 §6 authors exactly one 'until'");

        foreach (var terminator in terminators)
        {
            DurationEvaluator.Evaluate(
                Applied(
                    new EffectDuration { Scope = DurationScope.BATTLE, Until = terminator },
                    0.0),
                At(1.0) with { OwnerWardEvent = WardPoolEvent.BrokenByDamage }).Reason.ShouldBe(
                DurationEndReason.WardBroken,
                $"18 §6's until:{terminator} reached no handler in DurationEvaluator");
        }
    }

    // ───────────────────────────────────────────── fixtures

    private static DurationOutcome Evaluate(EffectApplication application, DurationProbe probe) =>
        DurationEvaluator.Evaluate(application, probe);

    private static EffectApplication Scoped(DurationScope scope) =>
        Applied(new EffectDuration { Scope = scope }, appliedAtSeconds: 0.0);

    private static EffectApplication Applied(
        EffectDuration? duration, double appliedAtSeconds, int? appliedInPhase = null) =>
        new()
        {
            EffectId = "BOSS_MECHANIC_UNDER_TEST",
            Duration = duration,
            AppliedAtSeconds = appliedAtSeconds,
            AppliedInPhase = appliedInPhase,
        };

    private static DurationProbe At(double battleTimeSeconds) =>
        new() { BattleTimeSeconds = battleTimeSeconds };
}
