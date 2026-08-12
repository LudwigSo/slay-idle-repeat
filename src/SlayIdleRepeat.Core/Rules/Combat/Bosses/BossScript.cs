namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// 🔒 `17` §1.2 — one boss's power-to-stats row: the four coefficients that replace a shared `05`
/// §6.1 archetype's.
/// </summary>
/// <remarks>
/// <para>
/// `17` §1.2: <em>"Bosses use the same <c>EnemyStats(power, archetype)</c> derivation as everything
/// else (`05` §6), with <c>power = EnemyPower(bossNode)</c> (the 2.20 boss multiplier already inside
/// it) and a per-boss coefficient row instead of a shared archetype."</em>
/// </para>
/// <para>
/// 🔒 <b>Only the four <em>coefficients</em> are here.</b> `17` §1.2's secondaries — CRIT 0.05,
/// CDMG 0.50, DODGE 0, LS 0 — are <em>"the baseline"</em> every boss shares, so they are authored
/// once and handed to <see cref="BossEncounterRequest.Baseline"/> rather than repeated on eight
/// rows. Anything else <em>"is a phase mechanic in the fight scripts below, never a base stat"</em>.
/// </para>
/// </remarks>
/// <param name="Hp">`17` §1.2's <c>hpCoef</c> — Thornmaw's 2.40.</param>
/// <param name="Atk">`17` §1.2's <c>atkCoef</c>.</param>
/// <param name="Def">`17` §1.2's <c>defCoef</c>.</param>
/// <param name="Aspd">`17` §1.2's <c>aspdCoef</c> — Thornmaw's 0.70, which §2 calls "ASPD 0.7".</param>
internal readonly record struct BossCoefficients(double Hp, double Atk, double Def, double Aspd);

/// <summary>
/// 🔒 One mechanic inside a `17` phase block — an effect named <b>by id</b> (R19), plus the wind-up
/// `17` §1 requires of a damaging one.
/// </summary>
/// <param name="EffectId">
/// 🔒 The `18` §8 id of the authored effect. A <b>reference</b>, never an embedded effect (R19):
/// every other place in the DSL that reaches another effect does so by id, and an effect nested
/// inside a boss script would sit outside `18` §8's ascending-effect-id ordering and outside the
/// battle's effect table. It is resolved against
/// <see cref="BossEncounterRequest.Effects"/>.
/// </param>
/// <param name="TelegraphSeconds">
/// 🔒 `17` §1's <em>"visible 1.0–1.5 s wind-up"</em>, in seconds, or <c>null</c> where the mechanic
/// needs none.
/// <para>
/// ⚠️ <b>The lead is boss-script data, not a DSL key and not an engine constant.</b> `17` authors
/// 1.2 s (Thornmaw's Root, §2) and 1.5 s (the Dicelord's All In, §9) — one engine constant would be
/// wrong for one of them, and a <c>trigger</c> key would put a presentation duration inside `18`
/// §3's firing model. <see cref="BossTelegraphs"/> states the three rules a lead has to satisfy.
/// </para>
/// </param>
internal readonly record struct BossMechanic(string EffectId, double? TelegraphSeconds = null);

/// <summary>🔒 One of a boss's three `17` phase blocks — the mechanics live while that phase does.</summary>
/// <remarks>
/// 🔒 <b>Every mechanic in every block goes onto <see cref="ActorPlan.Effects"/>, phases 2 and 3
/// included</b>, and <see cref="BossPhaseController.EnterInitialPhase"/> then de-anchors the later
/// two. The alternative — registering a block at its own phase entry — would leave phase 2's and
/// phase 3's effects out of the battle's effect table, which is built once from the opening roster:
/// <c>CombatLog.AppendTelegraph</c> could not name them and the replayer could not resolve them.
/// </remarks>
internal sealed record BossPhaseBlock
{
    /// <summary>Which phase this block is — <c>1</c>, <c>2</c> or <c>3</c> (`17` §1).</summary>
    public required int Phase { get; init; }

    /// <summary>The block's mechanics. May be empty — Thornmaw's phase 1 is <em>"nothing else"</em>.</summary>
    public required IReadOnlyList<BossMechanic> Mechanics { get; init; }
}

/// <summary>
/// 🔒 <b>The authoring contract M2-13 writes its eight boss scripts against.</b> `17` §11:
/// <em>"All 8 bosses expressed purely in the effect DSL — zero bespoke boss code."</em>
/// </summary>
/// <remarks>
/// <para>
/// A script is <b>data</b>: an id, `17` §1.2's coefficient row, and exactly three phase blocks whose
/// mechanics are effect ids. Nothing here is behaviour, and nothing here is per-boss code — the one
/// <see cref="BossPhaseController"/> drives all eight.
/// </para>
/// <para>
/// 🔒 <b>The two universal built-ins are <em>not</em> authored on a script.</b> <c>SYS_ENRAGE</c>
/// (`17` §1's 70 s enrage) and the phase-3 <c>STUN</c>/<c>FREEZE</c> immunities (`17` §1) are
/// <em>"implemented once, applied to all bosses"</em>, so <see cref="BossEncounterBuilder"/> attaches
/// them to every boss and no script may name them. A per-boss opt-out would be exactly the bespoke
/// boss code `18` exists to prevent.
/// </para>
/// </remarks>
internal sealed record BossScript
{
    /// <summary>🔒 `17` §1 — the three phases every boss has, no more and no fewer.</summary>
    internal const int PhaseCount = 3;

    /// <summary>The boss's content id — <c>BOSS_THORNMAW</c> (`17` §1.2). Also its actor id.</summary>
    public required string Id { get; init; }

    /// <summary>`17` §1.2's per-boss row.</summary>
    public required BossCoefficients Coefficients { get; init; }

    /// <summary>
    /// 🔒 Exactly <see cref="PhaseCount"/> blocks, numbered <c>1</c>, <c>2</c>, <c>3</c>, in that
    /// order. <see cref="BossEncounterBuilder"/> refuses anything else.
    /// </summary>
    public required IReadOnlyList<BossPhaseBlock> Phases { get; init; }
}
