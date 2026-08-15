using System.Globalization;
using System.Text;

namespace SlayIdleRepeat.Core.Primitives;

/// <summary>The two Energy banks, as one value: the main bar and the Energy Reserve behind it.</summary>
/// <param name="Energy">The main Energy bar. Never negative.</param>
/// <param name="Reserve">The Energy Reserve behind it. Never negative.</param>
/// <remarks>
/// One value rather than two loose <c>int</c>s: every operation moves both at once (a grant fills
/// the bar and overflows into the Reserve, a spend drains the bar then the Reserve), and a pair of
/// <c>out</c> parameters is a pair a caller can wire up backwards.
/// <para>
/// Lives in <c>Primitives/</c> rather than beside the energy math in <c>Rules/Economy/</c> because
/// <c>Model</c> may not reference <c>Rules</c>, yet the <c>Player</c> aggregate holds "Energy never
/// exceeds max + reserve" as an invariant — so the value has to sit in a layer both can reference.
/// </para>
/// <para>
/// Public and positional because <c>CanonicalStateWriter</c> recognises a snapshot member by exactly
/// one shape: one public constructor, every parameter matched by a public readable property of the
/// same name and type, nothing else public. <c>PlayerId</c> and <c>RunId</c> are shaped the same way.
/// </para>
/// <para>
/// It holds amounts only, never Max Energy or Reserve capacity — those are functions of Legend Level
/// and tuning, and a state carrying its own limits would go stale the moment either changed.
/// </para>
/// <para>
/// The upper bound is not enforced here — the aggregate holds that invariant, the value just holds
/// the amounts, so a balance patch lowering the max doesn't turn every player above it into an
/// exception. Negative amounts are refused because no state of the game they could describe exists.
/// </para>
/// <para>
/// <c>default(EnergyBanks)</c> is <c>(0, 0)</c> — empty banks, a legitimate state (a player who just
/// spent their last run).
/// </para>
/// </remarks>
public readonly record struct EnergyBanks(int Energy, int Reserve)
{
    /// <summary>The main Energy bar. Never negative.</summary>
    public int Energy { get; } = NonNegative(
        Energy,
        nameof(Energy),
        "The main Energy bar cannot hold a negative amount. A run the player cannot afford is " +
        "refused as a value (EnergyMath.Spend), never charged into the negative.");

    /// <summary>The Energy Reserve behind it. Never negative.</summary>
    public int Reserve { get; } = NonNegative(
        Reserve,
        nameof(Reserve),
        "The Energy Reserve cannot hold a negative amount. It receives overflow only, and is " +
        "drawn only for a shortfall the main bar could not cover.");

    /// <summary>Renders the UI reading — <c>138 (+200)</c> — with <see cref="CultureInfo.InvariantCulture"/>.</summary>
    /// <remarks>
    /// A method rather than a <c>ToString</c> override: overriding <c>ToString</c> would leave the
    /// synthesized <c>PrintMembers</c> in the assembly, unreachable and formatting with the ambient
    /// culture. Replacing <c>PrintMembers</c> directly removes it, and being a method rather than a
    /// property keeps it out of the set <c>CanonicalStateWriter</c> requires to match the constructor.
    /// </remarks>
    private bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{Energy} (+{Reserve})");

        return true;
    }

    private static int NonNegative(int amount, string parameterName, string because) =>
        amount >= 0
            ? amount
            : throw new ArgumentOutOfRangeException(parameterName, amount, because);
}
