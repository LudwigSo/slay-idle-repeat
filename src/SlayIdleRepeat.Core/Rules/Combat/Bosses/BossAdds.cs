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
/// 🔴 <b>Where the fraction itself comes from — recorded, because <see cref="BossScript"/> has no
/// field for it.</b> The fraction is a constructor argument of <see cref="BossSummonSource"/>,
/// supplied by whoever builds the encounter's seams. It is <b>not</b> on the authoring contract
/// M2-13 writes against: neither <see cref="BossScript"/> nor <see cref="BossMechanic"/> carries one,
/// so a boss script cannot state it today and the wiring must. That is a real gap and it is left
/// absent and greppable rather than filled with a plausible midpoint, which would have the engine
/// choose a number `17` §1 gives to content. <b>Whoever authors the eight scripts decides where it
/// belongs</b> — a per-boss field on <see cref="BossScript"/> if one fraction per boss is enough, or
/// a per-mechanic field on <see cref="BossMechanic"/> if `17` §2's Thornmaw adds and §5's Rimehold
/// shards want different ones. The band below does not move either way.
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
