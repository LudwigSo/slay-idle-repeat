using System.Globalization;
using System.Text;

namespace SlayIdleRepeat.Core.Rules.Economy;

/// <summary>
/// 🔒 The two Energy banks of `10` §3 and `28` C, as one value: the main bar and the Energy
/// Reserve behind it.
/// </summary>
/// <remarks>
/// <para>
/// One value rather than two loose <c>int</c>s because every operation in `28` C2 moves both at
/// once — a grant fills the bar and overflows into the Reserve, a spend drains the bar and then
/// the Reserve — and a pair of <c>out</c> parameters is a pair a caller can wire up backwards.
/// </para>
/// <para>
/// It holds amounts only. It does <b>not</b> hold Max Energy or the Reserve capacity, because both
/// are functions of the player's Legend Level and the tuning, and a state carrying its own limits
/// would go stale the moment either changed. <see cref="EnergyMath"/> derives the limits per call
/// from <see cref="EnergyTuning"/>.
/// </para>
/// <para>
/// ⚠️ The upper bound is <b>not</b> enforced here. `30` §11.5 puts <em>"Energy never exceeds max +
/// reserve"</em> on the aggregate, which is where the state lives; a rule that also threw on it
/// would turn a balance patch lowering <c>baseMax</c> into an exception for every player already
/// above the new maximum. Negative amounts are refused, because no state of the game they could
/// describe exists.
/// </para>
/// <para>
/// <c>default(EnergyBanks)</c> is <c>(0, 0)</c> — empty banks, which is a legitimate state and the
/// one a player who has just spent their last run is in.
/// </para>
/// </remarks>
internal readonly record struct EnergyBanks
{
    /// <summary>Creates the pair.</summary>
    /// <param name="energy">The main bar. Never negative.</param>
    /// <param name="reserve">The Energy Reserve (`28` C). Never negative.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either amount is negative.</exception>
    internal EnergyBanks(int energy, int reserve)
    {
        if (energy < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(energy),
                energy,
                "The main Energy bar cannot hold a negative amount. A run the player cannot afford " +
                "is refused as a value (EnergyMath.Spend), never charged into the negative.");
        }

        if (reserve < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reserve),
                reserve,
                "The Energy Reserve cannot hold a negative amount. 28 C2: it receives overflow only, " +
                "and is drawn only for a shortfall the main bar could not cover.");
        }

        Energy = energy;
        Reserve = reserve;
    }

    /// <summary>The main Energy bar — what `10` §3 calls Energy.</summary>
    internal int Energy { get; }

    /// <summary>The Energy Reserve behind it (`28` C).</summary>
    internal int Reserve { get; }

    /// <summary>
    /// 🔒 Renders `28` C2's UI reading — <c>138 (+200)</c>, without the maximum this value does not
    /// know — with <see cref="CultureInfo.InvariantCulture"/>.
    /// </summary>
    /// <remarks>
    /// <c>PrintMembers</c> rather than a <c>ToString</c> override, which is the shape
    /// <c>GameContext</c> established: the synthesized <c>PrintMembers</c> formats with the
    /// <em>ambient</em> culture, and overriding <c>ToString</c> would leave that synthesized method
    /// in the assembly, unreachable and wrong. Replacing it removes it. Both members of this value
    /// are <c>internal</c>, so the synthesized version would in fact print nothing at all — which is
    /// the second reason to write it out.
    /// </remarks>
    private bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{Energy} (+{Reserve})");

        return true;
    }
}
