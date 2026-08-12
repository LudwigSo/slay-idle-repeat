using System.Globalization;

namespace SlayIdleRepeat.Core.Rules.Combat.Enemies;

/// <summary>
/// 🔒 `05` §6.1a — the unit an on-hit row's potency is stated in.
/// </summary>
/// <remarks>
/// The names are a transcription of the units `05` §6.1a's own table already writes; no basis exists
/// here that the table does not state. Naming them is what stops <c>0.20</c> and <c>0.015</c> —
/// a fraction of the applier's ATK and a fraction of the target's Max HP — being read as the same
/// kind of number by whatever applies them.
/// </remarks>
internal enum PotencyBasis
{
    /// <summary>`05` §6.1a — <em>"X% caster ATK/s"</em>, measured against the applier at application.</summary>
    ApplierAtkPctPerSecond = 1,

    /// <summary>`05` §6.1a — <em>"1.5% target Max HP/s"</em>.</summary>
    TargetMaxHpPctPerSecond = 2,

    /// <summary>`05` §6.1a — <em>"−50% ASPD"</em>.</summary>
    TargetAspdPct = 3,

    /// <summary>`05` §6.1a — <em>"−5% DEF per stack"</em>.</summary>
    TargetDefPctPerStack = 4,

    /// <summary>`05` §6.1a — <em>"−10% healing received per stack"</em>.</summary>
    TargetHealingReceivedPctPerStack = 5,
}

/// <summary>
/// 🔒 One row of `05` §6.1a — an archetype's on-hit status, with every parameter the section states.
/// </summary>
/// <remarks>
/// <para>
/// Two blocks use this shape. <c>WARDEN</c>'s <c>SUNDER</c> is a single parameter set that holds
/// <em>"wherever a WARDEN appears (any chapter, including Cogitator Prime's drones — `17` §7)"</em>;
/// <c>CASTER</c>'s is one row per chapter, aligned to that chapter's signature.
/// </para>
/// <para>
/// 🔒 <b><see cref="ProcChancePerLandedHit"/> is per LANDED hit and the qualifier is load-bearing.</b>
/// `05` §6.1a states it three times. A proc rolled per <em>attack</em> instead would fire through
/// dodges, which is a different — and strictly harder — game against every high-dodge build.
/// </para>
/// <para>
/// 🔒 <b><see cref="MaxStacks"/> is nullable and the null is the point.</b> `05` §6.1a states a
/// stack count for five of its eight rows and defers the rest to the status catalogue, which fixes
/// <c>BLEED</c> as non-stacking and says nothing at all about <c>FREEZE</c>. So the <c>FREEZE</c>
/// row carries <c>null</c>: the documents authorise no value, and <c>game-data/README.md</c>'s rule
/// is that such a hole stays greppable rather than being coerced to a plausible default. Anything
/// applying a stack must fail loudly on it — see <see cref="RequireMaxStacks"/>.
/// </para>
/// </remarks>
/// <param name="StatusId">`05` §6.1a — the status applied. The catalogue itself is M2-10's.</param>
/// <param name="ProcChancePerLandedHit">`05` §6.1a — the proc chance, per landed hit.</param>
/// <param name="Potency">`05` §6.1a — the magnitude, in <paramref name="Basis"/>'s unit.</param>
/// <param name="Basis">`05` §6.1a — what <paramref name="Potency"/> is a fraction of.</param>
/// <param name="DurationSeconds">`05` §6.1a — how long one application lasts.</param>
/// <param name="MaxStacks">`05` §6.1a — the stack ceiling, or <c>null</c> where none is authorised.</param>
/// <param name="RefreshOnReapply">
/// `05` §6.1a — whether reapplying refreshes rather than extends. 🔒 <c>null</c> on every
/// <c>CASTER</c> row: the section states <em>"refresh on reapply"</em> for the <c>WARDEN</c> set
/// and nothing at all for the biome statuses, and a <c>false</c> here would decide on `05`'s behalf
/// that reapplying extends them.
/// </param>
/// <param name="FlavourName">
/// `05` §6.1a — the biome skin's name as a localisation key, or <c>null</c> for a row the section
/// gives no flavour name (the <c>WARDEN</c> <c>SUNDER</c> set, which is not a biome skin).
/// </param>
internal sealed record OnHitStatus(
    string StatusId,
    double ProcChancePerLandedHit,
    double Potency,
    PotencyBasis Basis,
    double DurationSeconds,
    int? MaxStacks,
    bool? RefreshOnReapply,
    string? FlavourName)
{
    /// <summary>
    /// The stack ceiling, or a throw naming the hole.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// `05` §6.1a and the status catalogue authorise no stack count for this row.
    /// </exception>
    internal int RequireMaxStacks() =>
        MaxStacks ?? throw new InvalidOperationException(
            $"05 §6.1a authorises no stack count for {StatusId}: it states one for five of its eight " +
            "rows and defers the rest to 05's status catalogue, which fixes BLEED as non-stacking and " +
            "says nothing about FREEZE. 16 R6: a plausible default here would be invisible, so this " +
            "fails instead. The milestone that authors the status catalogue rules on it.");

    /// <inheritdoc />
    public override string ToString() =>
        $"{StatusId} {Potency.ToString("R", CultureInfo.InvariantCulture)} {Basis} for " +
        $"{DurationSeconds.ToString("R", CultureInfo.InvariantCulture)}s, " +
        $"stacks {MaxStacks?.ToString(CultureInfo.InvariantCulture) ?? "unauthorised"}, " +
        $"proc {ProcChancePerLandedHit.ToString("R", CultureInfo.InvariantCulture)}/landed hit";
}
