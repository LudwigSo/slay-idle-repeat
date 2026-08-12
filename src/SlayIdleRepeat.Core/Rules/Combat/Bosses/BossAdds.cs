namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// 🔒 `17` §1 — <em>"If a boss summons, adds use standard archetypes from `05` §6.1 at 25–35% of
/// boss power, capped at 3 alive at once."</em>
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>25–35% is a band, and the engine does not pick a number inside it.</b> What is stated here
/// is the band, so a fraction of 0.60 is refused rather than quietly spawning adds twice as strong as
/// `17` intends (steering S6).
/// </para>
/// <para>
/// 🔴 <b>Where the fraction comes from — M2-12 left this open and M2-13 has closed it: it is
/// <see cref="BossScript.AddsPowerFraction"/>, a <em>per-boss</em> field.</b> The gap was real —
/// the fraction was a constructor argument of <see cref="BossSummonSource"/> and nothing on the
/// authoring contract could state it — and the choice between per-boss and per-mechanic was decided
/// by `17`'s own fights rather than by taste: every summoning boss summons exactly one archetype,
/// and three of the five re-summon the <em>same</em> adds (`17` §2, §4, §8), so a per-mechanic
/// fraction could only ever produce one named add standing at two powers in one fight. The full
/// argument, and the fact that the number <em>inside</em> the band is authored rather than
/// transcribed, are on <see cref="BossScript.AddsPowerFraction"/>. The band below did not move.
/// <see cref="BossSummonSource"/> still takes it as a constructor argument, because the seam is
/// built per fight and the script is only one of its inputs.
/// </para>
/// <para>
/// <see cref="MaxAlive"/> is the ceiling `18` §2.4's <c>maxAlive</c> key carries on a boss's
/// <c>SUMMON</c>; <c>BattleSimulation.BattleFlowSink.Summon</c> already enforces whatever the effect
/// authors, so this is what the encounter builder checks the authoring against.
/// </para>
/// </remarks>
internal static class BossAdds
{
    /// <summary>🔒 `17` §1 — the bottom of the adds' power band.</summary>
    internal const double MinPowerFraction = 0.25;

    /// <summary>🔒 `17` §1 — the top of the adds' power band.</summary>
    internal const double MaxPowerFraction = 0.35;

    /// <summary>🔒 `17` §1 — <em>"capped at 3 alive at once"</em>.</summary>
    internal const int MaxAlive = 3;
}
