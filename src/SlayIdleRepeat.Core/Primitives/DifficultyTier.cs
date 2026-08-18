namespace SlayIdleRepeat.Core.Primitives;

/// <summary>The difficulty tier a run is played on: <c>NORMAL</c>, <c>HEROIC</c>, <c>MYTHIC</c>.</summary>
/// <remarks>
/// An enum rather than an invention — the vocabulary is already authored in tuning data
/// (<c>chapterGating</c>, <c>par_power.json</c>'s tier columns, the chapter schema's
/// <c>unlockCondition.tier</c>) and <c>DifficultyTierMatchesTuningDataTests</c> keeps this enum and
/// those authored copies from drifting apart. It feeds <c>SeedDerivation.RunSeed</c> as <c>tierId</c>.
/// <para>
/// The tier <em>ladder</em> (which tier unlocks a chapter at which clear) is not authored here —
/// this enum is vocabulary only. The ladder is authored in <c>tuning/progression.json</c>'s
/// <c>chapterGating</c> block, read by <c>ChapterGatingTuning</c> and enforced by the
/// <c>START_RUN</c> handler against the player's clear history. The per-member summaries below
/// restate what that ladder says today; they are a description of it, never the authority for it.
/// </para>
/// <para>
/// It lives in <c>Primitives/</c> rather than <c>Model/</c> because a public enum under
/// <c>Core/Model/</c> fails the accessibility-boundary test on its compiler-generated
/// <c>value__</c> field (public, not <c>literal</c>/<c>initonly</c>, uncommented by
/// <c>[CompilerGenerated]</c>).
/// </para>
/// <para>
/// The numeric values are both wire values <em>and</em> seed inputs: <c>CanonicalStateWriter</c>
/// bakes them into every <c>stateHash</c>, and <c>SeedDerivation.RunSeed</c> widens them straight
/// into the run seed. Renumbering would re-seed every run in existence — a player resuming a run
/// would find a different board under it. Append, never renumber, never reuse.
/// </para>
/// <para>
/// There is deliberately no <c>0</c> member, so <c>default(DifficultyTier)</c> cannot read as
/// <see cref="NORMAL"/> and quietly seed a Mythic run as an easy one; <c>Run.Rehydrate</c> refuses
/// the zero via <see cref="Enum.IsDefined{TEnum}(TEnum)"/>.
/// </para>
/// </remarks>
public enum DifficultyTier
{
    /// <summary>The base tier. Gated only on the previous chapter's Normal clear.</summary>
    NORMAL = 1,

    /// <summary>Gated on the same chapter's Normal clear. <c>par_power.json</c> puts it at ×4.</summary>
    HEROIC = 2,

    /// <summary>Gated on the same chapter's Heroic clear plus the Legend Level the ladder authors. ×16.</summary>
    MYTHIC = 3,
}
