using System.Globalization;
using System.Text;

namespace SlayIdleRepeat.Core.Primitives;

/// <summary>
/// 🔒 The two Energy banks of `10` §3 and `28` C, as one value: the main bar and the Energy
/// Reserve behind it.
/// </summary>
/// <param name="Energy">The main Energy bar — what `10` §3 calls Energy. Never negative.</param>
/// <param name="Reserve">The Energy Reserve behind it (`28` C). Never negative.</param>
/// <remarks>
/// <para>
/// One value rather than two loose <c>int</c>s because every operation in `28` C2 moves both at
/// once — a grant fills the bar and overflows into the Reserve, a spend drains the bar and then
/// the Reserve — and a pair of <c>out</c> parameters is a pair a caller can wire up backwards.
/// </para>
/// <para>
/// 🔒 <b>Why it lives in <c>Primitives/</c> and not beside the energy math in <c>Rules/Economy/</c>,
/// which is where M1-10 first put it.</b> `30` §11.4 forbids <c>Model</c> from referencing
/// <c>Rules</c>, so a <c>Player</c> aggregate cannot name a type under <c>Rules/</c> — and `30`
/// §11.5 names <em>"Energy never exceeds max + reserve"</em> as an invariant the aggregate holds.
/// The two are only compatible if this value sits in a layer both may reference, which
/// <c>Primitives/</c> is, and which `30` §11.4 already describes as the home of "value objects".
/// </para>
/// <para>
/// 🔒 <b>Why <c>public</c> and positional.</b> <c>PlayerSnapshot</c> will carry it, and
/// <c>CanonicalStateWriter</c> (`14` §16.6) recognises a snapshot member by exactly one shape:
/// <em>one</em> public constructor, every parameter matched by a public readable property of the
/// same name and type, and no public property beyond them. An internal constructor or internal
/// properties fail that test, and the failure surfaces as "no canonical encoding" for whoever first
/// puts one in a snapshot rather than for whoever chose the shape. <c>PlayerId</c> and <c>RunId</c>
/// were shaped against the same writer for the same reason;
/// <c>CanonicalEncodingTests.CanonicalBytes_encodes_EnergyBanks_as_two_widened_fields</c> proves
/// this one encodes rather than assuming it.
/// </para>
/// <para>
/// It holds amounts only. It does <b>not</b> hold Max Energy or the Reserve capacity: both are
/// functions of the player's Legend Level and the tuning, and a state carrying its own limits would
/// go stale the moment either changed. <c>EnergyMath</c> derives the limits per call from
/// <c>EnergyTuning</c>.
/// </para>
/// <para>
/// ⚠️ The upper bound is <b>not</b> enforced here, and that is the division of labour `30` §11.5
/// describes: the aggregate holds the invariant, the value holds the amounts. A rule that also
/// threw on it would turn a balance patch lowering <c>baseMax</c> into an exception for every
/// player already above the new maximum. Negative amounts are refused, because no state of the game
/// they could describe exists.
/// </para>
/// <para>
/// ⚠️ <c>default(EnergyBanks)</c> bypasses the constructor, as it does for every value type — but
/// unlike <c>PlayerId</c>, whose default holds a null it must render around, this one is
/// <c>(0, 0)</c>: empty banks, a legitimate state and the one a player who has just spent their
/// last run is in.
/// </para>
/// </remarks>
public readonly record struct EnergyBanks(int Energy, int Reserve)
{
    /// <summary>The main Energy bar — what `10` §3 calls Energy. Never negative.</summary>
    public int Energy { get; } = NonNegative(
        Energy,
        nameof(Energy),
        "The main Energy bar cannot hold a negative amount. A run the player cannot afford is " +
        "refused as a value (EnergyMath.Spend), never charged into the negative.");

    /// <summary>The Energy Reserve behind it (`28` C). Never negative.</summary>
    public int Reserve { get; } = NonNegative(
        Reserve,
        nameof(Reserve),
        "The Energy Reserve cannot hold a negative amount. 28 C2: it receives overflow only, and " +
        "is drawn only for a shortfall the main bar could not cover.");

    /// <summary>
    /// 🔒 Renders `28` C2's UI reading — <c>138 (+200)</c>, without the maximum this value does not
    /// know — with <see cref="CultureInfo.InvariantCulture"/>.
    /// </summary>
    /// <remarks>
    /// <c>PrintMembers</c> rather than a <c>ToString</c> override, which is the shape
    /// <c>GameContext</c> established: overriding <c>ToString</c> would leave the synthesized
    /// <c>PrintMembers</c> in the assembly, unreachable and formatting with the ambient culture.
    /// Replacing it removes it. 🔒 A <b>method</b>, so it does not join the property set
    /// <c>CanonicalStateWriter</c> requires to be exactly the constructor's parameters.
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
