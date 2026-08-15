using System.Globalization;

namespace SlayIdleRepeat.Core.Rules.Combat.Enemies;

/// <summary>
/// The unit an on-hit row's potency is stated in.
/// </summary>
/// <remarks>
/// Naming the basis is what stops <c>0.20</c> and <c>0.015</c> — a fraction of the applier's ATK and
/// a fraction of the target's Max HP — being read as the same kind of number by whatever applies them.
/// </remarks>
internal enum PotencyBasis
{
    /// <summary>X% caster ATK/s, measured against the applier at application.</summary>
    ApplierAtkPctPerSecond = 1,

    /// <summary>Target Max HP/s.</summary>
    TargetMaxHpPctPerSecond = 2,

    /// <summary>Target ASPD%.</summary>
    TargetAspdPct = 3,

    /// <summary>Target DEF% per stack.</summary>
    TargetDefPctPerStack = 4,

    /// <summary>Target healing-received% per stack.</summary>
    TargetHealingReceivedPctPerStack = 5,
}

/// <summary>
/// One row — an archetype's on-hit status, with every authored parameter.
/// </summary>
/// <remarks>
/// <para>
/// Two blocks use this shape: <c>WARDEN</c>'s <c>SUNDER</c> is a single parameter set that holds
/// wherever a <c>WARDEN</c> appears in any chapter; <c>CASTER</c>'s is one row per chapter, aligned
/// to that chapter's signature.
/// </para>
/// <para>
/// <see cref="ProcChancePerLandedHit"/> is per landed hit, not per attack — a proc rolled per
/// attack instead would fire through dodges, a strictly harder game against high-dodge builds.
/// </para>
/// <para>
/// <see cref="MaxStacks"/> is nullable and the null is the point: a stack count is authored for five
/// of eight rows and the rest defer to the status catalogue (which says nothing about FREEZE), so
/// those rows carry <c>null</c> rather than a coerced default. Anything applying a stack must fail
/// loudly on it — see <see cref="RequireMaxStacks"/>.
/// </para>
/// </remarks>
/// <param name="StatusId">The status applied.</param>
/// <param name="ProcChancePerLandedHit">The proc chance, per landed hit.</param>
/// <param name="Potency">The magnitude, in <paramref name="Basis"/>'s unit.</param>
/// <param name="Basis">What <paramref name="Potency"/> is a fraction of.</param>
/// <param name="DurationSeconds">How long one application lasts.</param>
/// <param name="MaxStacks">The stack ceiling, or <c>null</c> where none is authorised.</param>
/// <param name="RefreshOnReapply">
/// Whether reapplying refreshes rather than extends. <c>null</c> on every <c>CASTER</c> row, since
/// nothing is authored for the biome statuses and <c>false</c> would decide it unilaterally.
/// </param>
/// <param name="FlavourName">
/// The biome skin's name as a localisation key, or <c>null</c> for a row with no flavour name (the
/// <c>WARDEN</c> <c>SUNDER</c> set, which is not a biome skin).
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
    /// <exception cref="InvalidOperationException">No stack count is authorised for this row.</exception>
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
