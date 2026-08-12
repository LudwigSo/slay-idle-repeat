using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// The state one `18` §4 condition evaluation or `18` §5 target resolution reads — assembled by the
/// caller, never gathered by the evaluator.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Everything is relative to <see cref="Holder"/>.</b> `18` §5 lists eleven targets and never
/// says whose enemies <c>ALL_ENEMIES</c> means; §7.10 settles it by worked example, authoring the
/// Volatile elite's death explosion — prose: <em>"explodes on death for 15% of <b>hero</b> Max
/// HP"</em> — as <c>{"op":"DAMAGE_MAXHP_PCT", …, "target":"ALL_ENEMIES"}</c> on an <b>enemy</b> actor.
/// That only does what its own prose says if the enemy tokens mean <em>the actors hostile to the
/// holder</em>. <see cref="Holder"/> is therefore the origin of every token: <c>SELF</c> is the
/// holder, the enemy tokens are the opposing side <em>of the holder</em>, <c>ALL_PETS</c> is the
/// holder's side's pets, <c>OWNER</c> is the holder's summoner.
/// </para>
/// <para>
/// 🔒 <b>The context is handed in, never derived.</b> Nothing here is read from ambient state: there
/// is no clock (`30` §3 — time enters <c>Core</c> as <c>GameContext.NowUtc</c>, and combat time is
/// <see cref="BattleTimeSeconds"/>), and the RNG arrives already seeded (see <see cref="Rng"/>).
/// That is what makes `18` §4's <em>"All are pure functions of current state"</em> mechanically true
/// rather than aspirational, and it is enforced by <c>ConditionPurityRuleTests</c>.
/// </para>
/// </remarks>
internal sealed record EffectEvaluationContext
{
    /// <summary>
    /// 🔒 The actor holding the effect — the origin of every `18` §5 token and the subject of
    /// <c>SELF_HP_PCT</c>, <c>SELF_MISSING_HP_PCT</c>, <c>HAS_STATUS</c> and <c>STATUS_STACKS</c>.
    /// </summary>
    public required IEffectActorView Holder { get; init; }

    /// <summary>
    /// The holder's current attack target — <c>CURRENT_TARGET</c>, and the subject of
    /// <c>TARGET_HP_PCT</c>, <c>TARGET_IS_ELITE</c> and <c>TARGET_IS_BOSS</c>. In a duel this is the
    /// opposing hero (`05` §3.3).
    /// </summary>
    /// <remarks>
    /// ⚠️ Its presence is also what marks an <b>attack context</b>, which is `18` §5's gate on
    /// <c>OTHER_ENEMIES</c>: <em>"Valid only inside an attack context; elsewhere it degrades to
    /// <c>ALL_ENEMIES</c>."</em> The two are treated as the same condition because §5 defines the
    /// token as <em>"all enemies except the attack's primary target"</em> — with no primary target
    /// there is nothing to except, and the degradation is what the document asks for. A separate
    /// <c>IsAttackContext</c> flag would be a second source of truth for one fact, and a context
    /// carrying a target while denying an attack would resolve <c>PK_CLEAVE</c> onto its own primary.
    /// </remarks>
    public IEffectActorView? CurrentTarget { get; init; }

    /// <summary>
    /// The actor that dealt the hit being reacted to — the <c>ATTACKER</c> target, and the subject of
    /// the three <c>ATTACKER_IS_*</c> conditions.
    /// </summary>
    /// <remarks>
    /// `18` §4 names the contexts in which it is set: <em>"<c>ON_HIT_TAKEN</c>,
    /// <c>ON_DODGE</c>/<c>ON_BLOCK</c>, and <c>DAMAGE_TAKEN_MULT</c> evaluation inside `05` §4 step
    /// 6"</em>. Elsewhere it is <c>null</c>, and the three conditions read <c>false</c> — an authored
    /// default, not a failure. The <c>ATTACKER</c> <em>target</em> has no such default and throws.
    /// </remarks>
    public IEffectActorView? Attacker { get; init; }

    /// <summary>
    /// 🔒 Every actor in the battle, both sides, living and dead, in `05` §3.1's fixed index order.
    /// </summary>
    /// <remarks>
    /// The dead are included rather than pre-filtered because the filter is a rule, not a caller's
    /// choice: `05` §3.1 step 6 puts an actor out of play the moment its HP reaches 0, and every
    /// enemy token and <c>ENEMY_COUNT</c> apply that here, once. A caller handing in a pre-filtered
    /// roster would get the same answer; a caller handing in a roster filtered by a slightly
    /// different rule would get a silently different one.
    /// </remarks>
    public required IReadOnlyList<IEffectActorView> Actors { get; init; }

    /// <summary>
    /// <c>BATTLE_TIME</c> — seconds of battle time elapsed. `05` §3 ticks at 20 Hz, so this advances
    /// in steps of 0.05.
    /// </summary>
    public double BattleTimeSeconds { get; init; }

    /// <summary>
    /// When the enrage begins, in battle-time seconds — `05` §3.1's <c>SYS_ENRAGE</c>
    /// (<c>startDelay: 70.0</c>). <c>null</c> in a fight that has no enrage, which is every fight but
    /// a boss's: <em>"Bosses only; ordinary fights rely on the 90 s timeout."</em>
    /// </summary>
    /// <remarks>
    /// 🔒 A reading, not a constant. <c>70.0</c> is a tunable (`17` §1) and this layer authors no
    /// tunables (`21` §3.1).
    /// </remarks>
    public double? EnrageAtSeconds { get; init; }

    /// <summary>
    /// When this fight is forced to end, in battle-time seconds — `05` §3's 90 s timeout, or `05`
    /// §3.3's 60 s <c>pvpMaxFightSeconds</c> in a duel.
    /// </summary>
    /// <remarks>
    /// 🔒 Also a reading rather than a constant, for the same reason. Together with
    /// <see cref="EnrageAtSeconds"/> it is the horizon <c>BATTLE_TIME_REMAINING_EST</c> counts down
    /// to — see <c>ConditionEvaluator</c> for the ruling.
    /// </remarks>
    public required double FightHorizonSeconds { get; init; }

    /// <summary>
    /// <c>IS_PVP</c> — `18` §4: <em>"the hook that lets a perk behave differently in a duel"</em>.
    /// </summary>
    /// <remarks>
    /// Also the switch behind `05` §3.3's three duel rules, which that section states as rules rather
    /// than as consequences of the roster: <c>TARGET_IS_ELITE</c>/<c>TARGET_IS_BOSS</c> always false,
    /// <c>ENEMY_COUNT</c> always 1.
    /// </remarks>
    public bool IsPvp { get; init; }

    /// <summary>
    /// The run's state, for the nine `18` §4 conditions that read it. <c>null</c> where there is no
    /// run — a duel (`05` §3.3), or the balance harness against synthetic stat blocks (`05` §9).
    /// </summary>
    /// <remarks>
    /// A run condition evaluated against a <c>null</c> view throws rather than reading zero: `18`
    /// §9.3 rules that clauses with no duel meaning are <em>"simply skipped"</em> via <c>IS_PVP</c>,
    /// so reaching one in a duel means the content did not skip it, and a silent zero would hide that
    /// forever.
    /// </remarks>
    public IRunStateView? Run { get; init; }

    /// <summary>
    /// The battle's draw stream, for <c>RANDOM_ENEMY</c> — the only `18` §5 token that draws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Handed in, already seeded, and never derived here.</b> `14` §8.1 and
    /// <see cref="SeedDerivation.BattleSeed"/> fix the shape: combat draw <c>i</c> of a battle is
    /// <c>Hash64(battleSeed, "combat", i)</c>, which is
    /// <c>new DeterministicRng(battleSeed, RngStreams.Combat)</c>. The <c>battleSeed</c> is the
    /// caller's — the simulator's (M2-08) — and this layer never holds <c>runSeed</c>, which never
    /// leaves the server (`02` §2).
    /// </para>
    /// <para>
    /// <c>null</c> outside a battle. <c>RANDOM_ENEMY</c> then throws rather than picking the first
    /// candidate, because a deterministic "random" pick is the determinism failure that reproduces
    /// only for whoever wrote it.
    /// </para>
    /// </remarks>
    public DeterministicRng? Rng { get; init; }
}
