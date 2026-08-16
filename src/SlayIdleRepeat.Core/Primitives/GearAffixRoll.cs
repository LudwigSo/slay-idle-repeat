using System.Globalization;

namespace SlayIdleRepeat.Core.Primitives;

/// <summary>One affix as it was rolled onto a single gear instance: which affix, and at what value.</summary>
/// <param name="AffixId">
/// The affix's authored id, e.g. <c>AFX_CRIT_CHANCE</c>. Never null, empty or whitespace.
/// </param>
/// <param name="Value">
/// The rolled magnitude, inside the affix's authored range. Finite, never negative, and rounded to
/// the assembly's determinism precision.
/// </param>
/// <remarks>
/// <para>
/// A <c>readonly record struct</c> in <c>Primitives/</c> rather than a class under <c>Model/Gear/</c>,
/// on <see cref="EnergyBanks"/>'s precedent and for the same two reasons: it is a value object with
/// no identity, and it has to keep the one shape the canonical state writer can encode — one public
/// constructor, every parameter matched by a public readable property of the same name and type.
/// A public constructor is exactly what a type under <c>Model/</c> may not have.
/// </para>
/// <para>
/// The id is text rather than an enum because the affix pool is authored data: a fifteenth affix is a
/// tuning edit, and an enum here would make it a code edit in a table whose whole purpose is to be
/// re-tuned. The <em>range</em> it was rolled inside is not carried — that is content, read back from
/// the pool by id, and a copy of it on every instance would be a second answer to re-tune.
/// </para>
/// <para>
/// <see cref="Value"/> is refused unless it is already rounded, rather than rounded on the way in.
/// Persisted state may hold no unrounded double, and rounding silently here would hide the one place
/// that produced an unrounded roll.
/// </para>
/// </remarks>
public readonly record struct GearAffixRoll(string AffixId, double Value)
{
    /// <summary>The affix's authored id. Never null, empty or whitespace.</summary>
    public string AffixId { get; } = IdText.Require(AffixId, nameof(GearAffixRoll));

    /// <summary>The rolled magnitude. Finite, non-negative and rounded.</summary>
    public double Value { get; } = Rolled(Value);

    /// <summary>The id and the value, so a log line reads the affix rather than the record's shape.</summary>
    public override string ToString() =>
        AffixId is null
            ? $"default({nameof(GearAffixRoll)})"
            : string.Create(CultureInfo.InvariantCulture, $"{AffixId}={Value}");

    /// <summary>The guard behind <see cref="Value"/>.</summary>
    /// <param name="value">The candidate magnitude.</param>
    /// <returns>The magnitude, unchanged.</returns>
    /// <exception cref="ArgumentOutOfRangeException">It is not finite, is negative, or is unrounded.</exception>
    private static double Rolled(double value)
    {
        if (!DeterminismRounding.IsRounded(value) || value < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Value),
                value,
                "An affix value is a finite, non-negative magnitude already rounded to the assembly's " +
                "determinism precision. Persisted state carries no unrounded double — the canonical " +
                "writer refuses one — so an affix rounded on the way in here would hide whichever roll " +
                "produced it. Round at the roll, not at the record.");
        }

        return value;
    }
}
