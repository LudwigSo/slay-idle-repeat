namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// 🔒 `17` §1 — <em>"If a boss summons, adds use standard archetypes from `05` §6.1 at 25–35% of
/// boss power, capped at 3 alive at once."</em>
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>25–35% is a band, and the engine does not pick a number inside it.</b> Which fraction a
/// given mechanic uses is boss-script data handed to <see cref="BossSummonSource"/>; what is stated
/// here is the band it must sit in, so a script that authors 0.60 is refused rather than quietly
/// spawning adds twice as strong as `17` intends (steering S6).
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

/// <summary>
/// 🔒 `17` §1 / `05` §9 — the fight-duration band a boss is tuned to, stated as named numbers so the
/// balance harness and the documents cannot drift apart.
/// </summary>
/// <remarks>
/// <para>
/// `17` §1: <em>"Duration — 35–60 s at par power. Balance guardrail: never below 12 s, never above
/// 70 s (`05` §9)."</em>
/// </para>
/// <para>
/// 🔒 <b>These are <em>balance-harness</em> assertions, not engine invariants, and nothing here
/// throws.</b> A fight that runs 8 s is a tuning failure the harness (M2-16a) reports against a
/// whole distribution; an engine that refused it would turn a balance finding into a crash in the
/// middle of a player's run, and would make the harness unable to measure the very thing it exists
/// to measure.
/// </para>
/// </remarks>
internal static class BossDurationGuardrails
{
    /// <summary>🔒 `17` §1 — the bottom of the intended band at par power.</summary>
    internal const double ParMinSeconds = 35.0;

    /// <summary>🔒 `17` §1 — the top of the intended band at par power.</summary>
    internal const double ParMaxSeconds = 60.0;

    /// <summary>🔒 `17` §1 / `05` §9 — <em>"never below"</em>.</summary>
    internal const double HardFloorSeconds = 12.0;

    /// <summary>
    /// 🔒 `17` §1 / `05` §9 — <em>"never above"</em>. The same 70 s as
    /// <see cref="BossBuiltIns.EnrageStartDelaySeconds"/>, and not by coincidence: `17` §1 states
    /// the enrage <em>"guarantees termination without a draw"</em>, so the ceiling is the moment the
    /// guarantee starts working.
    /// </summary>
    internal const double HardCeilingSeconds = 70.0;
}
