using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Stacking;

/// <summary>
/// The applications of one effect on one owner, combined by its stacking mode.
/// </summary>
/// <remarks>
/// Immutable — <see cref="Apply"/> returns a new set, so a stack count read mid-tick can't shift
/// under a concurrent reader. Applications are held individually rather than folded as they arrive,
/// since <c>HIGHEST_WINS</c> can't be answered from a running total. <see cref="CombinedValue"/>
/// deliberately does not round to 4 decimal places — that rounding belongs to the stat-aggregation
/// step downstream; rounding a stack product first would be an earlier, incorrect accumulation point
/// that can shift the final answer in the last decimal place. <see cref="Apply"/> never touches a
/// DoT/HoT cadence anchor: <see cref="StackApplication.RefreshDuration"/> is about the effect's
/// duration only, not its tick timing.
/// </remarks>
internal sealed record EffectStackSet
{
    /// <summary>The effect's id — named in every failure.</summary>
    internal required string EffectId { get; init; }

    /// <summary>The stacking block.</summary>
    internal required EffectStacking Stacking { get; init; }

    /// <summary>The applications currently held, in application order.</summary>
    internal IReadOnlyList<double> Applications { get; init; } = [];

    /// <summary>How many stacks are held, read at the moment asked.</summary>
    internal int Count => Applications.Count;

    /// <summary>
    /// The applications combined per the stacking mode. Not rounded — see the type remarks.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The set holds no applications. Neither 0 nor 1 is the value of a status that isn't there:
    /// under <c>MULTIPLICATIVE</c> a silent 1 is an invisible no-op multiplier, and under
    /// <c>ADDITIVE</c> a silent 0 is a status the player can see with no potency — a hole fails loudly.
    /// </exception>
    internal double CombinedValue
    {
        get
        {
            if (Applications.Count == 0)
            {
                throw new InvalidOperationException(
                    $"'{EffectId}' holds no applications, so 18 §6 gives it no combined value. " +
                    "An empty set is not a zero and not a one — under MULTIPLICATIVE a substituted 1 " +
                    "is an invisible STAT_MULT x1 and under ADDITIVE a substituted 0 is a visible " +
                    "status with no potency. Ask Count first.");
            }

            return Stacking.Mode switch
            {
                StackingMode.ADDITIVE => Fold(static (running, next) => running + next, 0.0),
                StackingMode.MULTIPLICATIVE => Fold(static (running, next) => running * next, 1.0),

                // The three single-instance modes: Apply keeps exactly one application for each, so
                // "the one that won" is always Applications[0] by the time it gets here.
                StackingMode.REPLACE or StackingMode.HIGHEST_WINS or StackingMode.NONE =>
                    Applications[0],

                _ => throw Unhandled(nameof(CombinedValue)),
            };
        }
    }

    /// <summary>An empty set for one effect and one stacking block.</summary>
    /// <param name="effectId">The effect's id.</param>
    /// <param name="stacking">The stacking block.</param>
    /// <exception cref="ArgumentException"><paramref name="effectId"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="stacking"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <c>maxStacks</c> is below one — a ceiling of zero is not "no stacking" (that's the
    /// <c>NONE</c> mode), so it's not a value any code path should be able to construct.
    /// </exception>
    internal static EffectStackSet Empty(string effectId, EffectStacking stacking)
    {
        ArgumentException.ThrowIfNullOrEmpty(effectId);
        ArgumentNullException.ThrowIfNull(stacking);

        if (stacking.MaxStacks is { } max && max < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stacking), max,
                $"18 §6's maxStacks is a stack ceiling and '{effectId}' declares " +
                $"{max.ToString(CultureInfo.InvariantCulture)}. The schema declares it 'minimum': 1 " +
                "and null for uncapped, so a ceiling below one is a value no authored effect can " +
                "carry — and it is not 'no stacking', which is the NONE mode.");
        }

        return new EffectStackSet { EffectId = effectId, Stacking = stacking };
    }

    /// <summary>Applies one more application of the effect.</summary>
    /// <param name="value">The application's value — already scaled if the effect carries a <c>valueScale</c>.</param>
    /// <exception cref="InvalidOperationException">The mode is outside the five stacking modes.</exception>
    internal StackApplication Apply(double value)
    {
        // The first application is not a reapplication: reporting a refresh here would re-anchor a
        // cadence that was only just set.
        if (Applications.Count == 0)
        {
            return new StackApplication(
                this with { Applications = Only(value) }, StackAdded: true, RefreshDuration: false);
        }

        // An independent key, honoured for every mode including NONE — e.g. a status that doesn't
        // stack but refreshes on reapply is exactly NONE plus refreshOnReapply. An absent key is not
        // a refresh: true is opt-in language, so absence means the opt-in wasn't taken.
        var refresh = Stacking.RefreshOnReapply ?? false;

        return Stacking.Mode switch
        {
            // NONE — a second application is ignored. The stack set, not the duration.
            StackingMode.NONE => new StackApplication(this, StackAdded: false, refresh),

            // REPLACE — a new application replaces the existing one. The count does not grow.
            StackingMode.REPLACE => new StackApplication(
                this with { Applications = Only(value) }, StackAdded: false, refresh),

            // HIGHEST_WINS — the strongest application wins, compared literally rather than by
            // magnitude: a negative-valued debuff authored with this mode keeps the least negative
            // application, since there's no other reading that needs no invention.
            StackingMode.HIGHEST_WINS => new StackApplication(
                this with { Applications = Only(Math.Max(Applications[0], value)) },
                StackAdded: false,
                refresh),

            StackingMode.ADDITIVE or StackingMode.MULTIPLICATIVE => AtCeiling()
                // The surplus application is dropped rather than evicting the oldest, and the
                // duration still refreshes independently of whether a stack was added.
                ? new StackApplication(this, StackAdded: false, refresh)
                : new StackApplication(
                    this with { Applications = Grown(value) }, StackAdded: true, refresh),

            _ => throw Unhandled(nameof(Apply)),
        };
    }

    /// <summary><c>maxStacks: null</c> is uncapped.</summary>
    private bool AtCeiling() => Stacking.MaxStacks is { } max && Applications.Count >= max;

    /// <summary>The set's applications, plus one more, in application order.</summary>
    /// <remarks>
    /// An explicit array rather than a collection expression: for this shape, a collection
    /// expression is lowered by the compiler into a synthesized type in the global namespace, which
    /// trips this project's namespace-boundary test.
    /// </remarks>
    private double[] Grown(double value)
    {
        var grown = new double[Applications.Count + 1];

        for (var i = 0; i < Applications.Count; i++)
        {
            grown[i] = Applications[i];
        }

        grown[Applications.Count] = value;

        return grown;
    }

    /// <summary>The three single-instance modes' one held application. See <see cref="Grown"/>.</summary>
    private static double[] Only(double value) => new[] { value };

    private double Fold(Func<double, double, double> combine, double identity)
    {
        var running = identity;

        // In application order, and by index rather than through LINQ: a fold's order is part of
        // its answer in floating point, and two implementations of one rule must produce the same
        // double.
        for (var i = 0; i < Applications.Count; i++)
        {
            running = combine(running, Applications[i]);
        }

        return running;
    }

    private InvalidOperationException Unhandled(string member) =>
        new($"'{EffectId}' stacks with mode {Stacking.Mode}, which {member} does not answer for. " +
            "18 §6 authors five: ADDITIVE, MULTIPLICATIVE, REPLACE, HIGHEST_WINS, NONE.");
}

/// <summary>What one application of an effect did to its stack set and to its duration.</summary>
/// <param name="Stacks">The set after the application.</param>
/// <param name="StackAdded">
/// Whether the application increased <see cref="EffectStackSet.Count"/>. False for a
/// <c>NONE</c> reapplication, for one past <c>maxStacks</c>, and for the single-instance modes, whose
/// count is one from the first application onward.
/// </param>
/// <param name="RefreshDuration">
/// Whether <c>refreshOnReapply</c> asks for the effect's duration to restart. Duration only —
/// reapplication never re-anchors a DoT/HoT's tick cadence, only the timer this reports on.
/// </param>
internal readonly record struct StackApplication(
    EffectStackSet Stacks, bool StackAdded, bool RefreshDuration);
