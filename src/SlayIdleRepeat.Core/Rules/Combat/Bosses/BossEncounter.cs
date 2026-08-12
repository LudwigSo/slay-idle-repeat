using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;

namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// 🔒 Everything <see cref="BossEncounterBuilder"/> resolves out of a <see cref="BossScript"/>: the
/// boss's <see cref="ActorPlan"/>, its two phase boundaries, and the two maps
/// <see cref="BossPhaseController"/> reads.
/// </summary>
/// <remarks>
/// 🔒 <b>The maps are the reason this is a record and not just an <see cref="ActorPlan"/>.</b> A
/// plan is what the simulator needs; a phase is what the controller needs, and
/// <c>BattleSimulation.RegisterHoldings</c> registers plan effects without knowing that some of them
/// belong to phases 2 and 3. Everything the controller has to key on — which instance is in which
/// phase, and which instance carries a wind-up — is decided once, here, at build time.
/// </remarks>
internal sealed record BossEncounter
{
    /// <summary>The boss's actor id — <see cref="BossScript.Id"/>, and <see cref="ActorPlan.Id"/>.</summary>
    public required string BossId { get; init; }

    /// <summary>
    /// 🔒 The boss as it enters the fight: `17` §1.2's statline, every phase block's mechanics and
    /// the three <see cref="BossBuiltIns"/>, all on <see cref="ActorPlan.Effects"/> with explicit
    /// instance ids.
    /// </summary>
    public required ActorPlan Plan { get; init; }

    /// <summary>`17` §1's first-clear flag, as the encounter was built with it.</summary>
    public required bool FirstClear { get; init; }

    /// <summary>
    /// The HP fraction at or below which the boss is in phase 2 —
    /// <see cref="BossPhaseRules.Phase2HpFraction"/> for <see cref="FirstClear"/>.
    /// </summary>
    public required double Phase2HpFraction { get; init; }

    /// <summary>
    /// The HP fraction at or below which the boss is in phase 3. Always
    /// <see cref="BossPhaseRules.Phase3HpFraction"/> — the first-clear extension widens phase 1 only.
    /// </summary>
    public required double Phase3HpFraction { get; init; }

    /// <summary>
    /// 🔒 The phase map: every instance that belongs to a phase block, and which block.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>What is <em>not</em> in it is the load-bearing half.</b> The three
    /// <see cref="BossBuiltIns"/> are absent, so no phase transition can deactivate or reactivate
    /// <c>SYS_ENRAGE</c> — which is the one thing that would re-anchor its R8 clock and move the
    /// enrage from 70 s of battle to 70 s after 66% HP.
    /// </remarks>
    public required IReadOnlyDictionary<EffectInstanceId, int> PhaseOfInstance { get; init; }

    /// <summary>
    /// The wind-up map: every instance whose mechanic authored a
    /// <see cref="BossMechanic.TelegraphSeconds"/>, and the lead in seconds.
    /// </summary>
    public required IReadOnlyDictionary<EffectInstanceId, double> LeadSecondsOfInstance { get; init; }

    /// <summary>
    /// 🔒 <see cref="LeadSecondsOfInstance"/> bucketed by phase and <b>already ordered</b>, which is
    /// the only shape <see cref="BossPhaseController.AdvanceTick"/> ever asks for. A phase with no
    /// wind-up is absent rather than empty, so the per-tick pass answers with a single failed
    /// dictionary probe.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>It is precomputed because <c>AdvanceTick</c> runs once per boss on every one of `05`
    /// §3's up-to-1800 ticks</b>, and `05` §3.1 budgets a whole fight at under 5 ms. Selecting and
    /// sorting this out of the two maps at each of those ticks allocated a filter, a closure over
    /// the phase, an ordering and its sort buffer for a list that cannot change during a fight —
    /// both maps are fixed at build time. Deciding it here keeps the controller's one accumulator
    /// (<c>BossPhaseController._phase</c>) the only state in the namespace: this is plan data, not a
    /// cache.
    /// </remarks>
    public required IReadOnlyDictionary<int, IReadOnlyList<EffectInstanceId>> AnnouncingOfPhase { get; init; }
}

/// <summary>
/// Everything <see cref="BossEncounterBuilder.Build"/> needs that a <see cref="BossScript"/> does
/// not carry — the encounter's power and level, its roster position, and the content the script's
/// effect ids resolve against.
/// </summary>
/// <remarks>
/// 🔒 <b><see cref="Power"/> arrives as a parameter and is never derived here</b>, exactly as
/// <c>EnemyDerivation.Derive</c>'s does and for the same reason: `05` §6.3 and `17` §1 both state
/// that a boss's Power <b>already</b> includes <c>StageMult.Boss = 2.20</c> and must not be
/// multiplied again. Keeping the derivation on this side of the parameter is what makes the
/// double-multiplication impossible to write.
/// </remarks>
internal sealed record BossEncounterRequest
{
    /// <summary>The boss script — M2-13's data.</summary>
    public required BossScript Script { get; init; }

    /// <summary>
    /// 🔒 <b>The script's own effect set</b> — every effect the owning boss content declares, keyed
    /// by `18` §8 id. The caller supplies it, on <c>EnemyCatalogue</c>'s pattern: the boss engine
    /// does not read content.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>This is the scope every id in the script resolves in, and there is no wider one.</b> An
    /// effect is embedded in the content that owns it rather than living in a registry, so a
    /// <see cref="BossMechanic.EffectId"/> and a <c>RANDOM_OUTCOME</c> row alike name a <b>sibling</b>
    /// of the same script. <see cref="BossEncounterBuilder"/> refuses either when the id is not in
    /// here, which is what makes the reference resolvable without anything global.
    /// </remarks>
    public required IReadOnlyDictionary<string, EffectDefinition> Effects { get; init; }

    /// <summary>
    /// 🔒 `02` §4.3's <c>EnemyPower(i)</c> for the boss node, <b>with <c>StageMult.Boss</c> already
    /// inside it</b> (`05` §6.3, `17` §1). Never multiplied by 2.20 here.
    /// </summary>
    public required double Power { get; init; }

    /// <summary>`05` §6.0's <c>EnemyLevel(chapter, tier)</c>.</summary>
    public required int Level { get; init; }

    /// <summary>The boss's `05` §3.1 actor index.</summary>
    public required int Index { get; init; }

    /// <summary>The boss's `05` §7 log id.</summary>
    public required byte LogId { get; init; }

    /// <summary>
    /// 🔒 `17` §1.2's <em>"Secondary stats: every boss uses the baseline"</em> row, as authored
    /// content.
    /// </summary>
    /// <remarks>
    /// Only its four secondaries — CRIT, CDMG, DODGE, LIFESTEAL — are read; the four power
    /// coefficients are replaced by <see cref="BossScript.Coefficients"/>. It is handed in rather
    /// than written here because <em>"a number in code is a number nobody can retune without a
    /// build"</em>, and `17` §1.2 puts these in <c>data/bosses.json</c>.
    /// </remarks>
    public required ArchetypeRow Baseline { get; init; }

    /// <summary>`05` §6's derivation constants — <c>EnemyCatalogue.Derivation</c>.</summary>
    public required EnemyDerivationConstants Derivation { get; init; }

    /// <summary>`17` §1's first-clear flag for this player and this boss.</summary>
    public bool FirstClear { get; init; }
}

/// <summary>
/// 🔒 `17` §1 / §1.2 — one <see cref="BossScript"/> plus its encounter, resolved into the
/// <see cref="BossEncounter"/> the simulator and the phase controller run on.
/// </summary>
/// <remarks>
/// <para>
/// ═══ 🔒 <b>WHAT IS CHECKED HERE, AND WHY HERE</b> ═══
/// </para>
/// <para>
/// Every rule below is an <b>authoring</b> rule: it can be decided before a tick runs, and a failure
/// names the boss, the phase and the mechanic. Deferring any of them to the tick loop would surface
/// M2-13's typo as a mid-fight exception in a player's run, or — worse — as a boss whose mechanics
/// silently did nothing, which the balance harness would read as the boss being weak.
/// </para>
/// <list type="number">
///   <item>Exactly <see cref="BossScript.PhaseCount"/> blocks, numbered 1, 2, 3, <b>in order</b>.</item>
///   <item>Every referenced effect id resolves against <see cref="BossEncounterRequest.Effects"/>.</item>
///   <item>🔒 No block at phase 2 or 3 carries an <c>ON_BATTLE_START</c> trigger. `05` §3.1's 0b
///   sweep runs <b>before</b> 0c, so such an effect would fire while the boss is still in phase 1 —
///   a phase-3 mechanic landing at battle start.</item>
///   <item>No script may name one of the three <see cref="BossBuiltIns"/>: they are attached here,
///   once, for every boss.</item>
///   <item><b>T1</b>, <b>T2</b> and <b>T3</b> — see <see cref="BossTelegraphs"/>.</item>
///   <item>A <c>SUMMON</c> mechanic authors a <c>maxAlive</c>, and it is at most
///   <see cref="BossAdds.MaxAlive"/>. 🔴 <b>Authoring none is refused too</b> — `18` §2.4 leaves the
///   key optional and an absent one means <em>no cap</em>, which `17` §1 does not permit a boss.</item>
///   <item>
///   🔒 <b>O1 — every <c>RANDOM_OUTCOME</c> row names a <em>sibling</em>.</b> `18` §10.1 E6's
///   <c>outcomes</c> rows are effect ids, and the scope they resolve in is <b>this script's own
///   effect set</b> (<see cref="BossEncounterRequest.Effects"/>) — an effect is embedded in the
///   content that owns it, so there is no registry a row could reach past its owner into. A row
///   naming an id this script does not declare is refused <b>here</b>, at build time, with the boss,
///   the phase, the rolling effect and the missing id named. Deferring it would surface as
///   <c>BossOutcomes.Resolve</c> throwing mid-fight on whichever roll happened to draw the bad row —
///   a defect that appears in one fight in three and never in the same place twice.
///   </item>
/// </list>
/// <para>
/// 🔒 <b>Every refusal is an <see cref="EffectContextException"/> naming the boss, the phase and the
/// mechanic, and carrying the rule's own marker</b> (steering S2: a failure has to say <em>which</em>
/// rule fired, not merely that something was wrong). <c>CombatLog.AppendTelegraph</c> enforces T1
/// again at emission time and is the second line of defence; the message here is the better one
/// because it knows the authoring.
/// </para>
/// <para>
/// ═══ 🔒 <b>THE MARKER REGISTER — grep for one and find the rule, its message and its cases</b> ═══
/// </para>
/// <list type="table">
///   <item><term><c>A1</c></term><description>the phase blocks are not 1, 2, 3 in order.</description></item>
///   <item><term><c>A2</c></term><description>a mechanic names an effect the script does not declare.</description></item>
///   <item><term><c>A3</c></term><description>a script authors one of the three <see cref="BossBuiltIns"/>.</description></item>
///   <item><term><c>A4</c></term><description>a phase-2 or phase-3 block carries <c>ON_BATTLE_START</c>.</description></item>
///   <item><term><c>A5</c></term><description>a <c>SUMMON</c> authors <b>no</b> <c>maxAlive</c>, or one above <see cref="BossAdds.MaxAlive"/>.</description></item>
///   <item><term><c>T1</c>, <c>T2</c>, <c>T3</c></term><description>the wind-up rules — see <see cref="BossTelegraphs"/>.</description></item>
///   <item><term><c>O1</c></term><description>a <c>RANDOM_OUTCOME</c> row names a non-sibling effect id.</description></item>
/// </list>
/// <para>
/// ⚠️ The <c>A</c> markers are M2-12's implementation phase's, added so that all eight rules are
/// discriminable the same way rather than four of them being. A refusal a reader cannot tell from its
/// neighbour sends M2-13 looking in the wrong place.
/// </para>
/// </remarks>
internal static class BossEncounterBuilder
{
    /// <summary>
    /// 🔒 Builds one boss's encounter — its `17` §1.2 statline, its plan with every phase block and
    /// built-in on it, its two phase boundaries and the controller's two maps.
    /// </summary>
    /// <param name="request">The script and its encounter.</param>
    /// <returns>The resolved encounter.</returns>
    /// <exception cref="EffectContextException">One of the eight authoring rules refused.</exception>
    internal static BossEncounter Build(BossEncounterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var script = request.Script;

        RequirePhaseShape(script);

        var holdings = new List<HeldEffect>();
        var phaseOfInstance = new Dictionary<EffectInstanceId, int>();
        var leadSecondsOfInstance = new Dictionary<EffectInstanceId, double>();

        foreach (var block in script.Phases)
        {
            foreach (var mechanic in block.Mechanics)
            {
                var effect = ResolveMechanic(script, block.Phase, mechanic, request.Effects);
                var instance = BossBuiltIns.PhaseInstance(script.Id, block.Phase, effect.Id);

                RequireNoOpenerOutsidePhase1(script, block.Phase, effect);
                RequireSummonCap(script, block.Phase, effect);
                RequireSiblingOutcomes(script, block.Phase, effect, request.Effects);
                RequireWindUp(script, block.Phase, effect, mechanic.TelegraphSeconds);

                holdings.Add(new HeldEffect(effect, instance));
                phaseOfInstance[instance] = block.Phase;

                if (mechanic.TelegraphSeconds is { } lead)
                {
                    leadSecondsOfInstance[instance] = lead;
                }
            }
        }

        // 🔒 `17` §11 — attached here, once, for every boss, and deliberately NOT in the phase map:
        // nothing a transition walks can reach SYS_ENRAGE, so nothing can re-anchor its R8 clock.
        foreach (var builtIn in BossBuiltIns.All)
        {
            holdings.Add(new HeldEffect(builtIn, BossBuiltIns.BuiltInInstance(script.Id, builtIn.Id)));
        }

        return new BossEncounter
        {
            BossId = script.Id,
            Plan = PlanFor(request, holdings),
            FirstClear = request.FirstClear,
            Phase2HpFraction = BossPhaseRules.Phase2HpFraction(request.FirstClear),
            Phase3HpFraction = BossPhaseRules.Phase3HpFraction,
            PhaseOfInstance = phaseOfInstance,
            LeadSecondsOfInstance = leadSecondsOfInstance,
            AnnouncingOfPhase = AnnouncingByPhase(phaseOfInstance, leadSecondsOfInstance),
        };
    }

    /// <summary>
    /// 🔒 <see cref="BossEncounter.AnnouncingOfPhase"/> — the wind-up map bucketed by phase, each
    /// bucket in ascending instance-id order, decided once here.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>The order is <c>Ordinal</c> and is fixed <em>here</em> rather than at emission</b>: two
    /// wind-ups due on the same tick reach the log in this order, and the log <em>is</em> the replay
    /// (`05` §7). A <see cref="Dictionary{TKey,TValue}"/>'s enumeration order is not part of its
    /// contract, so leaving it to the walk would leave the log's order to an implementation detail.
    /// <para>
    /// ⚠️ <c>internal</c> rather than private so that a hand-built <see cref="BossEncounter"/> —
    /// which the controller and telegraph suites use to test the controller <em>without</em>
    /// <see cref="Build"/> — derives this map from its own lead map instead of restating it. A
    /// fixture that stated both by hand could author a lead the announce list did not carry, and the
    /// telegraph it was written to prove would simply never be emitted.
    /// </para>
    /// </remarks>
    internal static IReadOnlyDictionary<int, IReadOnlyList<EffectInstanceId>> AnnouncingByPhase(
        IReadOnlyDictionary<EffectInstanceId, int> phaseOfInstance,
        IReadOnlyDictionary<EffectInstanceId, double> leadSecondsOfInstance)
    {
        var byPhase = new Dictionary<int, List<EffectInstanceId>>();

        foreach (var instance in leadSecondsOfInstance.Keys)
        {
            // Every instance carrying a lead was put in the phase map by the same loop that put it
            // here, so the indexer is the assertion rather than a lookup that might miss.
            var phase = phaseOfInstance[instance];

            if (!byPhase.TryGetValue(phase, out var announcing))
            {
                announcing = new List<EffectInstanceId>();
                byPhase[phase] = announcing;
            }

            announcing.Add(instance);
        }

        var ordered = new Dictionary<int, IReadOnlyList<EffectInstanceId>>(byPhase.Count);

        foreach (var (phase, announcing) in byPhase)
        {
            announcing.Sort(static (left, right) =>
                EffectInstanceId.Comparer.Compare(left.Value, right.Value));

            ordered[phase] = announcing;
        }

        return ordered;
    }

    /// <summary>
    /// 🔒 `17` §1.2's statline row: <see cref="BossEncounterRequest.Baseline"/>'s secondaries with
    /// the script's four coefficients in place of the archetype's.
    /// </summary>
    /// <param name="coefficients">`17` §1.2's per-boss row.</param>
    /// <param name="baseline">The authored baseline row.</param>
    /// <returns>The row `05` §6's derivation is run over.</returns>
    internal static ArchetypeRow StatlineRow(BossCoefficients coefficients, ArchetypeRow baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        return baseline with
        {
            HpCoef = coefficients.Hp,
            AtkCoef = coefficients.Atk,
            DefCoef = coefficients.Def,
            AspdCoef = coefficients.Aspd,
        };
    }

    /// <summary>
    /// 🔒 `05` §6.3 / `17` §1 — the statline is derived from <see cref="BossEncounterRequest.Power"/>
    /// <b>as handed in</b>: <c>StageMult.Boss = 2.20</c> is already inside it.
    /// </summary>
    private static ActorPlan PlanFor(BossEncounterRequest request, IReadOnlyList<HeldEffect> holdings) =>
        new()
        {
            Id = request.Script.Id,
            Index = request.Index,
            LogId = request.LogId,
            Side = BattleSide.ENEMY,
            Kind = EffectActorKind.ENEMY,
            BaseStats = EnemyDerivation.Derive(
                request.Power,
                StatlineRow(request.Script.Coefficients, request.Baseline),
                request.Derivation),
            Level = request.Level,
            IsBoss = true,
            Effects = holdings,
        };

    /// <summary>🔒 <b>A1</b> — `17` §1's <em>"exactly 3"</em>, numbered 1, 2, 3, in that order.</summary>
    private static void RequirePhaseShape(BossScript script)
    {
        var authored = script.Phases.Select(p => p.Phase).ToArray();

        if (authored.Length == BossScript.PhaseCount &&
            authored[0] == 1 && authored[1] == 2 && authored[2] == 3)
        {
            return;
        }

        throw new EffectContextException(
            script.Id,
            $"A1 — its authored phases are [{string.Join(", ", authored.Select(Number))}]",
            "`17` §1 gives every boss exactly three phases, numbered 1, 2, 3 in that order. A block " +
            "out of order, repeated or missing would leave BossPhaseController with a phase it can " +
            "enter and no block to activate — a boss whose mechanics are silently absent, which the " +
            "balance harness reads as a boss that is weak.");
    }

    /// <summary>
    /// 🔒 <b>A2</b> and <b>A3</b> — the mechanic is a sibling of this script, and it is not one of
    /// the three universal built-ins.
    /// </summary>
    private static EffectDefinition ResolveMechanic(
        BossScript script,
        int phase,
        BossMechanic mechanic,
        IReadOnlyDictionary<string, EffectDefinition> effects)
    {
        foreach (var builtIn in BossBuiltIns.All)
        {
            if (string.Equals(builtIn.Id, mechanic.EffectId, StringComparison.Ordinal))
            {
                throw new EffectContextException(
                    mechanic.EffectId,
                    $"A3 — '{script.Id}' phase {Number(phase)} authors it, and it is a built-in",
                    "`17` §11 has the encounter builder attach the 70 s enrage and the two phase-3 " +
                    "immunities to EVERY boss — 'implemented once, applied to all bosses'. A script " +
                    "that authored one would be a second, disagreeing copy under a second instance " +
                    "id, and the phase map would then reach the copy at every transition.");
            }
        }

        // 🔴 Before the lookup, because Dictionary.TryGetValue(null) throws ArgumentNullException —
        // a refusal that names no rule, no boss and no phase, which is exactly what S2 asks a
        // refusal not to be. A BossMechanic is a record struct, so `default` is a reachable shape.
        if (string.IsNullOrWhiteSpace(mechanic.EffectId))
        {
            throw new EffectContextException(
                script.Id,
                $"A2 — '{script.Id}' phase {Number(phase)} carries a mechanic that names no effect id",
                "A mechanic is a SIBLING reference and a blank id names no sibling. Refused with the " +
                "boss and the phase in hand, rather than left to the lookup — which would raise a " +
                "bare ArgumentNullException that says neither.");
        }

        if (effects.TryGetValue(mechanic.EffectId, out var effect))
        {
            return effect;
        }

        throw new EffectContextException(
            mechanic.EffectId,
            $"A2 — '{script.Id}' phase {Number(phase)} names it and the script declares no such effect",
            "A mechanic is a SIBLING reference: an effect is embedded in the content that owns it, so " +
            "the scope an id resolves in is this script's own effect set and there is no wider one. " +
            "Deferring the check would surface as a boss whose mechanic silently never fired.");
    }

    /// <summary>
    /// 🔒 <b>A4</b> — `05` §3.1's 0b sweep runs <b>before</b> 0c, so an <c>ON_BATTLE_START</c> in a
    /// phase-2 or phase-3 block fires while the boss is still in phase 1.
    /// </summary>
    private static void RequireNoOpenerOutsidePhase1(BossScript script, int phase, EffectDefinition effect)
    {
        if (phase == BossPhaseRules.FirstPhase ||
            effect.Trigger is not { Kind: TriggerKind.ON_BATTLE_START })
        {
            return;
        }

        throw new EffectContextException(
            effect.Id,
            $"A4 — '{script.Id}' phase {Number(phase)} carries an ON_BATTLE_START trigger",
            "`05` §3.1 sweeps ON_BATTLE_START at pre-tick 0b and enters phase 1 at 0c, so such a " +
            "mechanic lands while the boss is still in phase 1 — a phase-3 mechanic at battle start, " +
            "in a fight that looks entirely legal. Phase 1 may carry one, because 0b runs for the " +
            "phase the boss is actually in.");
    }

    /// <summary>🔒 <b>A5</b> — `17` §1's <em>"capped at 3 alive at once"</em>, checked at authoring.</summary>
    /// <remarks>
    /// 🔴 <b>An <em>absent</em> <c>maxAlive</c> is refused, not admitted.</b> `18` §2.4 makes the key
    /// optional and <c>BattleFlowSink.Summon</c> reads <c>maxAlive is { } cap</c> — so no key means
    /// <b>no cap</b>, and a boss <c>SUMMON</c> that simply omitted it would spawn adds without a
    /// ceiling while passing a rule that only ever compared numbers. `17` §1 caps a <em>boss's</em>
    /// adds unconditionally, so on this side of the DSL the key is required (steering S6: the hole is
    /// refused rather than filled with a plausible <see cref="BossAdds.MaxAlive"/>, which would make
    /// the engine author a number `17` gives to content).
    /// </remarks>
    private static void RequireSummonCap(BossScript script, int phase, EffectDefinition effect)
    {
        if (effect.Op != EffectOp.SUMMON)
        {
            return;
        }

        if (effect.MaxAlive is not { } maxAlive)
        {
            throw new EffectContextException(
                effect.Id,
                $"A5 — '{script.Id}' phase {Number(phase)} summons and authors no maxAlive at all",
                $"`17` §1 caps a boss's adds at {Number(BossAdds.MaxAlive)} alive, unconditionally. " +
                "`18` §2.4 leaves maxAlive optional and BattleSimulation reads an absent one as NO " +
                "cap, so omitting it is the one authoring that produces an uncapped boss fight while " +
                "looking entirely legal. Defaulting it here would have the engine choose a number " +
                "`17` gives to content; author it on the effect.");
        }

        if (maxAlive <= BossAdds.MaxAlive)
        {
            return;
        }

        throw new EffectContextException(
            effect.Id,
            $"A5 — '{script.Id}' phase {Number(phase)} summons up to {Number(maxAlive)} adds at once",
            $"`17` §1 caps a boss's adds at {Number(BossAdds.MaxAlive)} alive. The cap is `18` §2.4's " +
            "authored maxAlive and BattleSimulation enforces whatever the effect authors, so the " +
            "authoring is what has to agree with the document — a fourth add is a fight nobody tuned.");
    }

    /// <summary>
    /// 🔒 <b>O1</b> — every <c>RANDOM_OUTCOME</c> row names a <b>sibling</b> of this same script.
    /// </summary>
    private static void RequireSiblingOutcomes(
        BossScript script,
        int phase,
        EffectDefinition effect,
        IReadOnlyDictionary<string, EffectDefinition> effects)
    {
        if (effect.Outcomes is not { } outcomes)
        {
            return;
        }

        foreach (var row in outcomes)
        {
            // 🔴 Before the lookup: Dictionary.ContainsKey(null) throws ArgumentNullException, and a
            // RandomOutcomeEntry is a record struct whose `default` carries a null id — so without
            // this, the one authoring mistake that omits an effectId is refused by a message naming
            // neither O1, nor the boss, nor the phase.
            if (string.IsNullOrWhiteSpace(row.EffectId) || !effects.ContainsKey(row.EffectId))
            {
                throw new EffectContextException(
                    effect.Id,
                    $"O1 — '{script.Id}' phase {Number(phase)} rolls it and its row " +
                    $"'{row.EffectId}' is not an effect this script declares",
                    "`18` §10.1 E6's outcome rows are effect ids, and an effect is embedded in the " +
                    "content that owns it — so the scope a row resolves in is the owning script's own " +
                    "effect set, and there is no registry to reach past it into. Deferring the check " +
                    "would surface as BossOutcomes throwing mid-fight on whichever roll drew the bad " +
                    "row: a defect that appears in one fight in three and never in the same place twice.");
            }
        }
    }

    /// <summary>
    /// 🔒 <b>T1</b>, <b>T2</b> and <b>T3</b> — the three rules a mechanic's wind-up has to satisfy.
    /// See <see cref="BossTelegraphs"/> for what each one is protecting.
    /// </summary>
    private static void RequireWindUp(
        BossScript script, int phase, EffectDefinition effect, double? leadSeconds)
    {
        if (leadSeconds is not { } lead)
        {
            if (!BossTelegraphs.RequiresLead(effect))
            {
                return;
            }

            throw new EffectContextException(
                effect.Id,
                $"T3 — '{script.Id}' phase {Number(phase)} lands damage on a schedule and authors no " +
                "wind-up",
                "`17` §1: 'every damaging mechanic has a visible 1.0-1.5 s wind-up … they must be " +
                "able to read what is happening, or the fight feels arbitrary', and `17` §11 makes " +
                "the emission a deliverable. An ON_PHASE_ENTER burst is exempt (the entry is " +
                "HP-driven) and so is a period short enough to have nowhere to put one.");
        }

        var exactTicks = BossTelegraphs.ExactLeadTicks(lead);

        if (lead < BossTelegraphs.MinLeadSeconds || lead > BossTelegraphs.MaxLeadSeconds ||
            exactTicks != Math.Floor(exactTicks))
        {
            throw new EffectContextException(
                effect.Id,
                $"T1 — '{script.Id}' phase {Number(phase)} authors a wind-up of {Format(lead)} s, " +
                $"which is {Format(exactTicks)} ticks",
                $"`17` §1's band is {Format(BossTelegraphs.MinLeadSeconds)}-" +
                $"{Format(BossTelegraphs.MaxLeadSeconds)} s AND `05` §3's simulation is fixed-tick, " +
                "so a legal wind-up is a whole number of ticks inside it. One outside the band " +
                "cannot be read as a wind-up; one between two ticks announces a landing at neither.");
        }

        if (effect.Trigger is not { Kind: TriggerKind.PERIODIC } trigger)
        {
            return;
        }

        var intervalTicks = TriggerSchedule.IntervalTicks(trigger);

        if (intervalTicks > BossTelegraphs.LeadTicks(lead))
        {
            return;
        }

        throw new EffectContextException(
            effect.Id,
            $"T2 — '{script.Id}' phase {Number(phase)} fires every {Number(intervalTicks)} ticks and " +
            $"winds up over {Format(exactTicks)} of them",
            "The period must EXCEED the wind-up: otherwise firing k+1 is announced before firing k " +
            "lands, and two wind-ups become indistinguishable in a log that IS the replay (`05` §7).");
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
