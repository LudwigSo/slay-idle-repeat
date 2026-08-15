namespace SlayIdleRepeat.Core.Content.Dice;

/// <summary>
/// 🔒 <b>Authored by M3-04.</b> <c>TILE_DICE_FORGE</c>'s upgrade menu — which faces the tile can
/// target and what it can turn them into. No document authors this table; `03_BOARD_AND_TILES.md`'s
/// tile description only implies one exists ("turn a `1` into a `4`, or into a `★`"), and `18` §7.9's
/// only worked example is one hardcoded outcome. This is content-authoring judgement, grounded in `04`
/// §1's existing face/tier system — not invented from nothing. See the remarks for the reasoning
/// behind each row.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Only a <see cref="DieFaceKind.Pip"/> face may be the SOURCE.</b> Both of `03`'s own examples
/// start from a Pip ("turn a `1`..."), and `04` §1 is explicit that <see cref="DieFaceKind.Void"/> is
/// "Curse-inflicted only, never an upgrade target" — the one kind the design rules out by name. A
/// face already upgraded to <see cref="DieFaceKind.Star"/>/<see cref="DieFaceKind.Surge"/>/
/// <see cref="DieFaceKind.Fortune"/>/<see cref="DieFaceKind.Chain"/> is not offered as a further
/// source: `04` §2's whole upgrade-source table shows every OTHER source targeting a <em>Pip</em>
/// face specifically (Weighted Faces: 1/3/4; Surging Fate: 2; Sixth Star: 6; Chainweaver: 4; Golden
/// Fate: 5; Starhoof Stag: 3) — none re-targets an already-special face, so Dice Forge does not
/// either.
/// </para>
/// <para>
/// 🔒 <b>Every TARGET kind below is one `04` §2 already authors a permanent or mount source
/// converting a Pip face into</b> — Dice Forge's run-scoped menu is a subset of the game's existing
/// upgrade vocabulary, not a new one:
/// </para>
/// <list type="bullet">
///   <item><see cref="DieFaceKind.Star"/> — `03`'s own flavor text names it directly, and the
///   Sixth Star keystone (`04` §2) already converts a Pip face to it permanently.</item>
///   <item><see cref="DieFaceKind.Surge"/>, at <b>tier 0</b> — the Surging Fate talent (`04` §2)
///   converts <c>2→Surge</c>. No document ties a Dice Forge grant to a higher tier, and 0 is the
///   floor `04` §1 defines — inventing a higher one would be exactly the S6 hole this table exists
///   to avoid.</item>
///   <item><see cref="DieFaceKind.Fortune"/> — both the Golden Fate keystone and the Starhoof Stag
///   mount (`04` §2) convert a Pip face to it.</item>
///   <item><see cref="DieFaceKind.Chain"/> — the Chainweaver keystone (`04` §2) converts
///   <c>4→Chain</c>.</item>
///   <item>A <b>higher Pip value</b>, 1..6 — the Weighted Faces talent's own shape
///   (<c>1→2</c> rank1, <c>1→3</c> rank3, <c>1→4</c> rank5): a Dice Forge visit lets the player raise
///   any Pip face to any higher Pip value in one step, `03`'s "turn a `1` into a `4`" example
///   exactly.</item>
/// </list>
/// <para>
/// 🔒 <b>No cost.</b> `18` §7.9's <c>MODIFY_DIE_FACE</c> shape has no cost key at all (confirmed by
/// direct read of `18`), and every other single-target board tile in `03` (Shrines, Treasure) is
/// "spent" by the act of landing on it rather than by a further currency charge. Landing on
/// <c>TILE_DICE_FORGE</c> is therefore the whole cost, matching the rest of the board's tile economy
/// rather than inventing a gold price nothing authors.
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
