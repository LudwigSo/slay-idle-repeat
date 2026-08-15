namespace SlayIdleRepeat.Core.Content.Dice;

/// <summary>
/// <c>TILE_DICE_FORGE</c>'s upgrade menu — which faces the tile can target and what it can turn
/// them into. No document authors this table verbatim; it is content-authoring judgement, grounded
/// in the existing face/tier system rather than invented from nothing.
/// </summary>
/// <remarks>
/// <para>
/// Only a <see cref="DieFaceKind.Pip"/> face may be the source: <see cref="DieFaceKind.Void"/> is
/// curse-inflicted only and never an upgrade target, and every other existing upgrade source in the
/// game targets a Pip face specifically, so a face already upgraded to a special kind is not
/// offered as a further source.
/// </para>
/// <para>
/// Every target kind below is one the game's talents/mounts already convert a Pip face into
/// elsewhere — this table's run-scoped menu is a subset of the existing upgrade vocabulary, not a
/// new one: <see cref="DieFaceKind.Star"/>, <see cref="DieFaceKind.Surge"/> (at tier 0, the floor —
/// no source ties a higher tier to this grant), <see cref="DieFaceKind.Fortune"/>,
/// <see cref="DieFaceKind.Chain"/>, or a higher Pip value 1..6.
/// </para>
/// <para>
/// No cost: landing on the tile is the whole cost, matching every other single-target board tile
/// that is "spent" by the act of landing on it rather than a further currency charge.
/// </para>
/// </remarks>
public static class DiceForgeUpgradeTable
{
    /// <summary>Every target this table offers, for a <see cref="DieFaceKind.Pip"/> source face.</summary>
    public static IReadOnlyList<DiceForgeUpgradeOption> Options { get; } = Array.AsReadOnly(new[]
    {
        DiceForgeUpgradeOption.ToHigherPip(),
        DiceForgeUpgradeOption.ToKind(DieFaceKind.Star),
        DiceForgeUpgradeOption.ToKind(DieFaceKind.Surge),
        DiceForgeUpgradeOption.ToKind(DieFaceKind.Fortune),
        DiceForgeUpgradeOption.ToKind(DieFaceKind.Chain),
    });

    /// <summary>
    /// Whether <paramref name="sourceFace"/> is a legal Dice Forge source at all — i.e. a
    /// <see cref="DieFaceKind.Pip"/> face. Every other kind (including <see cref="DieFaceKind.Void"/>)
    /// is refused.
    /// </summary>
    public static bool IsLegalSource(DieFace sourceFace) =>
        !sourceFace.IsUnset && sourceFace.Kind == DieFaceKind.Pip;
}

/// <summary>One row of <see cref="DiceForgeUpgradeTable.Options"/>: a target the tile can install.</summary>
/// <remarks>
/// <see cref="IsHigherPip"/> is a distinct case rather than a fixed <see cref="DieFace"/>: "a higher
/// Pip value" is relative to the CURRENT face being upgraded, unlike the four special kinds, which
/// are the same target regardless of the source's pip count.
/// </remarks>
public readonly record struct DiceForgeUpgradeOption
{
    private DiceForgeUpgradeOption(bool isHigherPip, DieFaceKind? kind)
    {
        IsHigherPip = isHigherPip;
        Kind = kind;
    }

    /// <summary>True for the "raise to a higher Pip value" option.</summary>
    public bool IsHigherPip { get; }

    /// <summary>The special kind this option installs, or <c>null</c> for <see cref="IsHigherPip"/>.</summary>
    public DieFaceKind? Kind { get; }

    /// <summary>The "raise to a higher Pip value" option.</summary>
    public static DiceForgeUpgradeOption ToHigherPip() => new(true, null);

    /// <summary>The option that installs a fixed special <paramref name="kind"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="kind"/> is <see cref="DieFaceKind.Pip"/> or <see cref="DieFaceKind.Void"/> —
    /// neither is ever a Dice Forge target (see <see cref="DiceForgeUpgradeTable"/>'s remarks).
    /// </exception>
    public static DiceForgeUpgradeOption ToKind(DieFaceKind kind)
    {
        if (kind is DieFaceKind.Pip or DieFaceKind.Void)
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind), kind,
                "Pip is the 'raise to a higher Pip value' option's own shape (use ToHigherPip), and " +
                "Void is never a Dice Forge target — 04 §1 rules it out by name.");
        }

        return new DiceForgeUpgradeOption(false, kind);
    }
}
