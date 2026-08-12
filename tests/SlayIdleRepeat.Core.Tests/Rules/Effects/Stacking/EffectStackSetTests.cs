using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Stacking;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Stacking;

/// <summary>
/// 🔒 `18` §6 — the five stacking modes, <c>maxStacks</c> and <c>refreshOnReapply</c>.
/// </summary>
public sealed class EffectStackSetTests
{
    // ───────────────────────────────────────────────────────────── the five modes

    /// <summary>`18` §6's <c>ADDITIVE</c> — stacks sum.</summary>
    [Fact]
    public void ADDITIVE_stacks_sum()
    {
        var sundered = Set(StackingMode.ADDITIVE, maxStacks: 5)
            .Apply(0.06).Stacks
            .Apply(0.06).Stacks
            .Apply(0.06).Stacks;

        sundered.Count.ShouldBe(3);
        sundered.CombinedValue.ShouldBe(0.18, 1e-12, "05 §5's SUNDER stacks to 5, additively");
    }

    /// <summary>
    /// `18` §6's <c>MULTIPLICATIVE</c> — <em>"stacks multiply"</em>, the enrage's mode.
    /// </summary>
    [Fact]
    public void MULTIPLICATIVE_stacks_multiply()
    {
        var enraged = Enrage(seconds: 3);

        enraged.Count.ShouldBe(3);
        enraged.CombinedValue.ShouldBe(
            1.259712,
            1e-12,
            "05 §3.1: SYS_ENRAGE is STAT_MULT ATK x1.08, multiplicative — and 1.08^3 is 1.259712");
    }

    /// <summary>`18` §6's <c>REPLACE</c> — a new application replaces the existing one.</summary>
    [Fact]
    public void REPLACE_keeps_only_the_newest_application()
    {
        var replaced = Set(StackingMode.REPLACE).Apply(0.30).Stacks.Apply(0.10).Stacks;

        replaced.Count.ShouldBe(1);
        replaced.CombinedValue.ShouldBe(0.10, "the newest application, weaker or not");
    }

    /// <summary>
    /// `18` §6's <c>HIGHEST_WINS</c> — the strongest application wins, and a weaker reapplication
    /// does not overwrite it.
    /// </summary>
    [Fact]
    public void HIGHEST_WINS_keeps_the_strongest_application()
    {
        var strongestFirst = Set(StackingMode.HIGHEST_WINS).Apply(0.30).Stacks.Apply(0.10).Stacks;
        var strongestLast = Set(StackingMode.HIGHEST_WINS).Apply(0.10).Stacks.Apply(0.30).Stacks;

        strongestFirst.CombinedValue.ShouldBe(0.30);
        strongestLast.CombinedValue.ShouldBe(0.30);

        strongestFirst.Count.ShouldBe(1);
        strongestLast.Count.ShouldBe(1);
    }

    /// <summary>
    /// `18` §6's <c>NONE</c> — a second application is ignored. `05` §5's <c>BLEED</c>:
    /// <em>"Does not stack; reapplication refreshes"</em>.
    /// </summary>
    [Fact]
    public void NONE_ignores_every_application_after_the_first()
    {
        var first = Set(StackingMode.NONE).Apply(0.30);
        var second = first.Stacks.Apply(0.90);

        second.StackAdded.ShouldBeFalse();
        second.Stacks.Count.ShouldBe(1);
        second.Stacks.CombinedValue.ShouldBe(0.30, "the first application, and only it");
    }

    /// <summary>
    /// 🔒 <b>Every mode is handled, and each combines its own way.</b> S3 — this type's subject set
    /// is the five <see cref="StackingMode"/>s, and one falling through would leave a status stacking
    /// as whatever the last arm happened to do.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Why an expected value per mode rather than <c>Should.NotThrow</c>.</b> A
    /// <c>Should.NotThrow</c> loop is satisfied by a single <c>default</c> arm answering all five —
    /// proven by stubbing <c>Apply</c> and <c>CombinedValue</c> to constants, at which point the loop
    /// went green while every per-mode fact above went red.
    /// <para>
    /// The sequence <c>0.25 → 0.5 → 0.125</c> is chosen because it is the shortest one that separates
    /// all five: two applications cannot tell <c>REPLACE</c> from <c>HIGHEST_WINS</c> (second
    /// stronger) or from <c>NONE</c> (second weaker), and a monotone three cannot tell
    /// <c>HIGHEST_WINS</c> from <c>NONE</c>. Putting the strongest in the middle does. Every value is
    /// a negative power of two, so the sums and products below are exact in binary and need no
    /// tolerance.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_18_6_stacking_mode_is_handled()
    {
        var expected = new Dictionary<StackingMode, double>
        {
            [StackingMode.ADDITIVE] = 0.875,            // 0.25 + 0.5 + 0.125
            [StackingMode.MULTIPLICATIVE] = 0.015625,   // 0.25 x 0.5 x 0.125
            [StackingMode.REPLACE] = 0.125,             // the newest
            [StackingMode.HIGHEST_WINS] = 0.5,          // the strongest
            [StackingMode.NONE] = 0.25,                 // the first, and only it
        };

        var modes = Enum.GetValues<StackingMode>();

        modes.Length.ShouldBe(5, "18 §6: ADDITIVE, MULTIPLICATIVE, REPLACE, HIGHEST_WINS, NONE");
        expected.Keys.Order().ShouldBe(
            modes.Order(), "a sixth mode is unhandled here until this table answers for it");

        foreach (var mode in modes)
        {
            var applied = Set(mode).Apply(0.25).Stacks.Apply(0.5).Stacks.Apply(0.125).Stacks;

            applied.CombinedValue.ShouldBe(
                expected[mode], $"18 §6's {mode} reached no handler of its own in EffectStackSet");
        }
    }

    // ───────────────────────────────────────────────────────────── maxStacks

    /// <summary>`05` §5: <c>BURN</c> <em>"stacks to 5"</em>, and the sixth application adds nothing.</summary>
    [Fact]
    public void An_application_past_maxStacks_adds_no_stack()
    {
        var burning = Set(StackingMode.ADDITIVE, maxStacks: 5);
        for (var i = 0; i < 5; i++)
        {
            burning = burning.Apply(0.10).Stacks;
        }

        var surplus = burning.Apply(0.10);

        surplus.StackAdded.ShouldBeFalse();
        surplus.Stacks.Count.ShouldBe(5, "05 §5: BURN stacks to 5");
        surplus.Stacks.CombinedValue.ShouldBe(0.50, 1e-12);
    }

    /// <summary>
    /// 🔒 <c>maxStacks: null</c> is <b>uncapped</b>, not one. `05` §3.1's <c>SYS_ENRAGE</c> is
    /// <em>"multiplicative stacking, uncapped"</em> and runs for the rest of the fight.
    /// </summary>
    [Fact]
    public void maxStacks_null_is_uncapped()
    {
        var enraged = Enrage(seconds: 20);

        enraged.Count.ShouldBe(20, "18 §6: maxStacks null is uncapped, and the enrage ticks once a second");
    }

    /// <summary>
    /// A <c>maxStacks</c> below one is refused. <c>game-data/schema/effect.schema.json</c> declares
    /// it <c>"minimum": 1</c>, and the two statements of the bound have to agree — a set built in
    /// code (the balance harness, a test) must not reach a state no authored JSON can.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_maxStacks_below_one_is_refused(int maxStacks)
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(
            () => Set(StackingMode.ADDITIVE, maxStacks));

        thrown.Message.ShouldContain("18 §6");
    }

    // ───────────────────────────────────────────────────────────── refreshOnReapply

    /// <summary>
    /// 🔒 <c>refreshOnReapply</c> is an <b>independent key</b>, honoured for every mode including
    /// <c>NONE</c>. `05` §5's <c>BLEED</c> — <em>"Does not stack; reapplication refreshes"</em> — is
    /// exactly <c>NONE</c> plus <c>refreshOnReapply: true</c>, so the combination is authored rather
    /// than hypothetical.
    /// </summary>
    [Fact]
    public void BLEED_does_not_stack_and_still_refreshes()
    {
        var bleeding = Set(StackingMode.NONE, refreshOnReapply: true).Apply(12.0);
        var reapplied = bleeding.Stacks.Apply(12.0);

        reapplied.StackAdded.ShouldBeFalse("05 §5: BLEED does not stack");
        reapplied.RefreshDuration.ShouldBeTrue("05 §5: reapplication refreshes");
    }

    /// <summary>
    /// A surplus application past <c>maxStacks</c> still refreshes. The stack ceiling and the
    /// duration are two different keys, and `05` §3.1 keeps them apart: <em>"Reapplication adds
    /// stacks / refreshes duration per the status's stacking rule"</em>.
    /// </summary>
    [Fact]
    public void An_application_past_maxStacks_still_refreshes_the_duration()
    {
        var capped = Set(StackingMode.ADDITIVE, maxStacks: 1, refreshOnReapply: true).Apply(0.1);
        var surplus = capped.Stacks.Apply(0.1);

        surplus.StackAdded.ShouldBeFalse();
        surplus.RefreshDuration.ShouldBeTrue();
    }

    /// <summary>
    /// An absent <c>refreshOnReapply</c> is <b>not</b> a refresh. <c>true</c> is opt-in language and
    /// `18` §1's canonical <c>{"mode":"ADDITIVE","maxStacks":1}</c> omits the key, so the absence is
    /// the absence of the opt-in rather than a manufactured default.
    /// </summary>
    [Fact]
    public void An_absent_refreshOnReapply_does_not_refresh()
    {
        var set = Set(StackingMode.ADDITIVE, maxStacks: 5).Apply(0.1).Stacks;

        set.Apply(0.1).RefreshDuration.ShouldBeFalse(
            "and this one IS a reapplication — the first application's own false is a different rule");
    }

    /// <summary>
    /// 🔒 The <b>first</b> application is not a reapplication, so it never reports a refresh —
    /// otherwise M2-10 would re-anchor a cadence it had only just set.
    /// </summary>
    [Fact]
    public void The_first_application_is_not_a_reapplication()
    {
        var first = Set(StackingMode.ADDITIVE, maxStacks: 5, refreshOnReapply: true).Apply(0.1);

        first.StackAdded.ShouldBeTrue();
        first.RefreshDuration.ShouldBeFalse("nothing to refresh — this is the application itself");
    }

    // ───────────────────────────────────────────── the empty set has no value

    /// <summary>
    /// A set with no applications has no combined value, and neither <c>0</c> nor <c>1</c> is one:
    /// under <c>MULTIPLICATIVE</c> a silent <c>1</c> is an invisible <c>STAT_MULT ×1</c> on a boss
    /// that has never enraged, and under <c>ADDITIVE</c> a silent <c>0</c> is a status the player can
    /// see with no potency. It is refused, naming the effect (S2).
    /// </summary>
    [Fact]
    public void An_empty_stack_set_has_no_combined_value()
    {
        var empty = Set(StackingMode.MULTIPLICATIVE);

        empty.Count.ShouldBe(0);

        var thrown = Should.Throw<InvalidOperationException>(() => empty.CombinedValue);
        thrown.Message.ShouldContain("SYS_ENRAGE", Case.Sensitive);
    }

    // ───────────────────────────────────────────── immutability

    /// <summary>
    /// 🔒 <see cref="EffectStackSet.Apply"/> returns a new set and never mutates the one it was
    /// called on. `18` §4's <em>"pure functions of current state"</em> is the neighbouring layer's
    /// rule, and a stack set that mutated in place would make a status's potency depend on how many
    /// times something happened to have asked.
    /// </summary>
    [Fact]
    public void Applying_a_stack_never_mutates_the_set_it_was_called_on()
    {
        var original = Set(StackingMode.ADDITIVE, maxStacks: 5).Apply(0.1).Stacks;

        var grown = original.Apply(0.1).Stacks;

        original.Count.ShouldBe(1);
        original.CombinedValue.ShouldBe(0.1);
        grown.Count.ShouldBe(2);
    }

    // ───────────────────────────────────────────── fixtures

    private static EffectStackSet Set(
        StackingMode mode, int? maxStacks = null, bool? refreshOnReapply = null) =>
        EffectStackSet.Empty(
            "SYS_ENRAGE",
            new EffectStacking { Mode = mode, MaxStacks = maxStacks, RefreshOnReapply = refreshOnReapply });

    private static EffectStackSet Enrage(int seconds)
    {
        var set = Set(StackingMode.MULTIPLICATIVE);
        for (var i = 0; i < seconds; i++)
        {
            set = set.Apply(1.08).Stacks;
        }

        return set;
    }
}
