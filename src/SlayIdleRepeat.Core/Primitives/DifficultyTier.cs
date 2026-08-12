namespace SlayIdleRepeat.Core.Primitives;

/// <summary>
/// 🔒 The difficulty tier a run is played on (`02` §2, `10` §7): <c>NORMAL</c>, <c>HEROIC</c>,
/// <c>MYTHIC</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>An enum rather than an invention.</b> The vocabulary is already authored in three places in
/// this repository, so nothing here is being chosen: <c>game-data/tuning/progression.json</c>'s
/// <c>chapterGating</c> keys, <c>game-data/tuning/par_power.json</c>'s
/// <c>defaultFill.tierMultiplier</c> keys <em>and</em> every <c>parPower</c> row's per-tier columns,
/// and <c>game-data/schema/chapter.schema.json</c>'s <c>unlockCondition.tier</c> <c>enum</c>. `02`
/// §2 feeds it into <c>runSeed</c> as <c>tierId</c> and `10` §7 gates it.
/// <c>DifficultyTierMatchesTuningDataTests</c> in <c>SlayIdleRepeat.Application.Tests</c> keeps the
/// three authored halves and this one from drifting apart.
/// </para>
/// <para>
/// ⚠️ <b>The ladder is not built and is not claimed.</b> `10` §7 says which tier a chapter unlocks
/// at (<c>HEROIC</c> needs the same chapter cleared on <c>NORMAL</c>; <c>MYTHIC</c> needs
/// <c>HEROIC</c> plus Legend Level 60) and none of that is here: gating is M3/M4's, it needs the
/// clear-history this milestone does not store, and a partial gate in <c>Core</c> would read like
/// the real one. This enum is the <b>vocabulary</b> only.
/// </para>
/// <para>
/// 🔒 <b>In <c>Primitives/</c>, not <c>Model/</c>.</b> Measured rather than assumed — see
/// <see cref="FtueBeat"/>'s remarks, which record the measurement: a <b>public enum under
/// <c>Core/Model/</c></b> (outside the <c>Model/Snapshots/</c> exemption) fails
/// <c>AccessibilityBoundaryTests.Apply_is_the_only_public_mutation</c> on its compiler-generated
/// <c>value__</c> field, which is public, is neither <c>literal</c> nor <c>initonly</c>, and carries
/// no <c>[CompilerGenerated]</c>.
/// </para>
/// <para>
/// 🔒 <b>The numbers are wire values <em>and</em> seed inputs</b>, which is a stronger claim than
/// <see cref="CurrencyId"/>'s. <c>CanonicalStateWriter</c> writes the number, never the name, so
/// renumbering rewrites every <c>stateHash</c> that has ever carried a run — and
/// <c>SeedDerivation.RunSeed</c> widens this enum straight into <c>runSeed</c> (`02` §2's
/// <c>tierId</c>), so renumbering <b>also re-seeds every run in existence</b>: a player resuming a
/// run would find a different board under it. Append, never renumber, never reuse.
/// </para>
/// <para>
/// There is deliberately <b>no <c>0</c> member</b>, so <c>default(DifficultyTier)</c> cannot read
/// as <see cref="NORMAL"/> and quietly seed a Mythic run as an easy one.
/// <c>Run.Rehydrate</c> refuses the zero through <see cref="Enum.IsDefined{TEnum}(TEnum)"/>, the
/// same way <c>Player.Rehydrate</c> refuses <c>default(FtueBeat)</c>.
/// </para>
/// </remarks>
public enum DifficultyTier
{
    /// <summary>`10` §7 — the base tier. Gated only on the previous chapter's Normal clear.</summary>
    NORMAL = 1,

    /// <summary>`10` §7 — gated on the same chapter's Normal clear. <c>par_power.json</c> puts it at ×4.</summary>
    HEROIC = 2,

    /// <summary>`10` §7 — gated on the same chapter's Heroic clear plus Legend Level 60. ×16.</summary>
    MYTHIC = 3,
}
