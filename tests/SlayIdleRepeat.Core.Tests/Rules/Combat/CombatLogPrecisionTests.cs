using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// 🔒 Why <see cref="CombatEvent.Value"/> is a <see cref="double"/> and not the <c>float</c> `05` §7
/// writes — pinned rather than argued.
/// </summary>
/// <remarks>
/// The three tests below are three independent reasons, each stated as the arithmetic it rests on. Any
/// one settles the question; together they make the <c>float</c> in §7 a defect rather than a
/// trade-off. This file is the evidence a future author needs before "restoring" the documented type.
/// </remarks>
public sealed class CombatLogPrecisionTests
{
    /// <summary>A <c>float</c>-valued event, exactly as `05` §7 types it.</summary>
    /// <remarks>
    /// The same six fields in the same order, with <c>Value</c> narrowed. It exists to be
    /// <b>refused</b>.
    /// </remarks>
    internal readonly record struct FloatValuedEvent(
        int Tick, CombatEventType Type, byte SourceId, byte TargetId, float Value, ushort DataId);

    /// <summary>
    /// 🔒 Reason 1 — `14` §16.6 has <b>no <c>float</c> row</b>, so a <c>float</c>
    /// <see cref="CombatEvent.Value"/> is not merely lossy, it is <b>unhashable</b>: the one
    /// serialiser <c>LogHash</c> is defined over refuses it outright.
    /// </summary>
    /// <remarks>
    /// This is decisive on its own. `05` §7 defines <c>LogHash</c> as a hash over the serialised
    /// event list, and `14` §16.6 makes <c>CanonicalStateWriter</c> the only thing that serialises
    /// state. A log whose events carry a <c>float</c> has no <c>LogHash</c> at all.
    /// </remarks>
    [Fact]
    public void A_float_valued_event_has_no_canonical_encoding()
    {
        var log = new[]
        {
            new FloatValuedEvent(3, CombatEventType.Hit, CombatActor.Hero, CombatActor.FirstEnemy, 41.2536f, 0),
        };

        var refusal = Should.Throw<NotSupportedException>(() => CanonicalStateWriter.HashCombatLog(log));

        refusal.Message.ShouldContain("Single", Case.Sensitive);
        refusal.Message.ShouldContain("14 §16.6", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 Reason 2 — narrowing to <c>float</c> <b>un-rounds</b> a value `05` §1.1 had already
    /// rounded, so it fails the 4-dp guard `14` §8.2 applies to every double the writer sees.
    /// </summary>
    /// <remarks>
    /// <c>1234.5678</c> is a perfectly ordinary damage figure. Through a <c>float</c> it comes back
    /// as <c>1234.5677490234375</c> — not a rounding difference in the last digit, a different
    /// number four decimal places in. Every damage figure in the game would trip the guard, on
    /// every platform.
    /// </remarks>
    [Fact]
    public void Narrowing_a_rounded_value_to_float_un_rounds_it()
    {
        const double rounded = 1234.5678;
        Math.Round(rounded, 4).ShouldBe(rounded, "the premise: this value satisfies 05 §1.1");

        var throughFloat = (double)(float)rounded;

        throughFloat.ToString("R", CultureInfo.InvariantCulture).ShouldBe("1234.5677490234375");
        Math.Round(throughFloat, 4).ShouldNotBe(throughFloat);

        // And the log says so, naming the event.
        var log = new CombatLog();
        log.Append(0, CombatEventType.BattleStart, CombatActor.None, CombatActor.None);

        Should.Throw<InvalidOperationException>(
            () => log.Append(3, CombatEventType.Hit, CombatActor.Hero, CombatActor.FirstEnemy, throughFloat));
    }

    /// <summary>
    /// 🔒 Reason 3, and the one that matters most: above <c>2^23</c> a <c>float</c> has <b>no fractional
    /// resolution at all</b>, so narrowing at the log boundary would <b>erase the very divergence</b>
    /// M5-12 and `11` §6 exist to detect.
    /// </summary>
    /// <remarks>
    /// <c>8388609.0001</c> and <c>8388609.0002</c> are distinct 4-dp figures, each exact as a
    /// <c>double</c> and the <b>same number</b> as <c>float</c>s. A narrowed log would hash x64's and
    /// ARM64's results equal <i>because</i> the difference had been thrown away — the gate would be
    /// measuring the narrowing, not the simulation.
    /// </remarks>
    [Fact]
    public void Float_collapses_two_distinct_damage_figures_into_one()
    {
        const double first = 8388609.0001;
        const double second = 8388609.0002;

        // The premise: both are legitimate 05 §1.1 values, and they are different.
        Math.Round(first, 4).ShouldBe(first);
        Math.Round(second, 4).ShouldBe(second);
        first.ShouldNotBe(second);

        // As floats they are indistinguishable.
        ((float)first).ShouldBe((float)second);
        ((float)first).ToString("R", CultureInfo.InvariantCulture).ShouldBe("8388609");

        // As doubles the log keeps them apart — and so does LogHash.
        var asDouble = new[]
        {
            CanonicalStateWriter.HashCombatLog(new[] { Hit(first) }),
            CanonicalStateWriter.HashCombatLog(new[] { Hit(second) }),
        };

        asDouble[0].ShouldNotBe(asDouble[1],
            "if these hashed the same, M5-12 could not tell x64 from ARM64 and 11 §6 could not tell an " +
            "honest client from a tampered one");
    }

    /// <summary>
    /// The committed reference row that carries the two colliding figures, so the property above is
    /// pinned by the table and not only by this test.
    /// </summary>
    [Fact]
    public void The_reference_table_carries_the_colliding_pair()
    {
        var row = CombatLogReferenceVectors.Row("large-damage-value");
        var events = ReferenceLogs.Instance(row.Id);

        events.Count.ShouldBe(2);
        events[0].Value.ShouldNotBe(events[1].Value);
        ((float)events[0].Value).ShouldBe((float)events[1].Value);
        CanonicalStateWriter.HashCombatLog(events).ShouldBe(row.Hash);
    }

    /// <summary>
    /// What the decision costs, measured rather than waved at: <b>nothing on the wire</b>.
    /// </summary>
    /// <remarks>
    /// `14` §16.6 widens every scalar to 8 canonical bytes — integrals sign- or zero-extended, a
    /// double's bit pattern as-is — so a <c>float</c> would have occupied 8 canonical bytes too,
    /// had the table a row for it. An event is 48 bytes either way and a log of <c>N</c> is
    /// <c>4 + 48N</c>. The entire cost of the widening is 4 bytes of the in-memory
    /// <see cref="CombatEvent.Value"/> field; the hashed stream, the replay format and the wire
    /// contract are byte-identical.
    /// </remarks>
    [Fact]
    public void The_widening_costs_nothing_on_the_wire()
    {
        CanonicalStateWriter.CanonicalBytes(new[] { Hit(1.0) }).Length.ShouldBe(4 + 48);
        CanonicalStateWriter.CanonicalBytes(new[] { Hit(1.0), Hit(2.0) }).Length.ShouldBe(4 + (48 * 2));

        // The double's own eight canonical bytes, in isolation: the same width every other scalar
        // in the §16.6 table occupies, which is why the widening is free on the wire.
        CanonicalStateWriter.CanonicalBytes(new[] { 1.0 }).Length.ShouldBe(4 + 8);
    }

    /// <summary>
    /// 🔒 <see cref="CombatLog"/>'s value guard and <c>CanonicalStateWriter</c>'s accept and refuse
    /// exactly the same doubles.
    /// </summary>
    /// <remarks>
    /// Two implementations of the same four rules, deliberately: the writer can only name the value,
    /// while the log names the <b>event</b> that carried it. Nothing else pins them together, and drift
    /// is silent in the worst direction — a value the log accepts and the writer refuses turns a
    /// finished battle into an exception at hash time, and the reverse is a determinism rule enforced in
    /// only one of two places.
    /// </remarks>
    /// <remarks>
    /// <c>MemberData</c> rather than <c>InlineData</c>: <c>-0.0 == 0.0</c>, so xUnit's analyser rejects
    /// the two as duplicate rows — and negative zero is where the two guards would most plausibly
    /// disagree.
    /// </remarks>
    [Theory]
    [MemberData(nameof(GuardAgreementValues))]
    public void The_logs_value_guard_and_the_writers_agree_exactly(double value)
    {
        var log = new CombatLog();
        log.Append(0, CombatEventType.BattleStart, CombatActor.None, CombatActor.None);

        var logAccepts = true;
        try
        {
            log.Append(1, CombatEventType.Hit, CombatActor.Hero, CombatActor.FirstEnemy, value);
        }
        catch (InvalidOperationException)
        {
            logAccepts = false;
        }

        var writerAccepts = true;
        try
        {
            CanonicalStateWriter.HashCombatLog(new[] { Hit(value) });
        }
        catch (NotSupportedException)
        {
            writerAccepts = false;
        }

        writerAccepts.ShouldBe(
            logAccepts,
            $"CombatLog and CanonicalStateWriter disagree about {value.ToString("R", CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// The agreement theory above compares two guards to each other, so it would also pass if both
    /// were deleted. This is the floor that stops that: the shared value set really does contain
    /// both accepted and refused values.
    /// </summary>
    [Fact]
    public void The_guard_agreement_theory_covers_both_outcomes()
    {
        var log = new CombatLog();
        log.Append(0, CombatEventType.BattleStart, CombatActor.None, CombatActor.None);

        var accepted = 0;
        var refused = 0;

        foreach (var value in GuardAgreementSet)
        {
            try
            {
                new CombatLog().Append(new CombatEvent(0, CombatEventType.Hit, 0, 1, value, 0));
                accepted++;
            }
            catch (InvalidOperationException)
            {
                refused++;
            }
        }

        accepted.ShouldBeGreaterThanOrEqualTo(5, "a set nothing accepts would make the agreement vacuous");
        refused.ShouldBeGreaterThanOrEqualTo(4, "a set nothing refuses would make the agreement vacuous");
    }

    /// <summary>
    /// Values straddling every rule the two guards share: admissible, unrounded, non-finite, and
    /// negative zero.
    /// </summary>
    public static TheoryData<double> GuardAgreementValues()
    {
        var data = new TheoryData<double>();
        foreach (var value in GuardAgreementSet)
        {
            data.Add(value);
        }

        return data;
    }

    /// <summary>The one list behind both the theory and its coverage floor.</summary>
    private static readonly double[] GuardAgreementSet =
    [
        0.0,
        1.0,
        -0.5,
        41.2536,
        8388609.0001,
        0.00005,
        41.25361234,
        double.NegativeZero,
        double.NaN,
        double.PositiveInfinity,
        double.NegativeInfinity,
    ];

    private static CombatEvent Hit(double value) =>
        new(1200, CombatEventType.Hit, CombatActor.Hero, CombatActor.FirstEnemy, value, 0);
}
