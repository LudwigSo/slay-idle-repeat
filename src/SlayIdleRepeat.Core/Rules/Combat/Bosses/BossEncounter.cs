using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;

namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// Everything <see cref="BossEncounterBuilder"/> resolves out of a <see cref="BossScript"/>: the
/// boss's <see cref="ActorPlan"/>, its two phase boundaries, and the two maps
/// <see cref="BossPhaseController"/> reads.
/// </summary>
/// <remarks>
/// The maps are why this is a record and not just an <see cref="ActorPlan"/>: a plan is what the
/// simulator needs, a phase is what the controller needs, and everything the controller has to key
/// on — which instance is in which phase, which carries a wind-up — is decided once here, at build
/// time.
/// </remarks>
internal sealed record BossEncounter
{
    /// <summary>The boss's actor id — <see cref="BossScript.Id"/>, and <see cref="ActorPlan.Id"/>.</summary>
    public required string BossId { get; init; }

    /// <summary>
    /// The boss as it enters the fight: its statline, every phase block's mechanics and the three
    /// <see cref="BossBuiltIns"/>, all on <see cref="ActorPlan.Effects"/> with explicit instance ids.
    /// </summary>
    public required ActorPlan Plan { get; init; }

    /// <summary>The first-clear flag the encounter was built with.</summary>
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
    /// The phase map: every instance that belongs to a phase block, and which block.
    /// </summary>
    /// <remarks>
    /// What is <em>not</em> in it is the load-bearing half: the three <see cref="BossBuiltIns"/> are
    /// absent, so no phase transition can deactivate or reactivate <c>SYS_ENRAGE</c> and re-anchor
    /// its clock.
    /// </remarks>
    public required IReadOnlyDictionary<EffectInstanceId, int> PhaseOfInstance { get; init; }

    /// <summary>
    /// The wind-up map: every instance whose mechanic authored a
    /// <see cref="BossMechanic.TelegraphSeconds"/>, and the lead in seconds.
    /// </summary>
    public required IReadOnlyDictionary<EffectInstanceId, double> LeadSecondsOfInstance { get; init; }

    /// <summary>
    /// <see cref="LeadSecondsOfInstance"/> bucketed by phase and already ordered, the only shape
    /// <see cref="BossPhaseController.AdvanceTick"/> ever asks for. A phase with no wind-up is
    /// absent rather than empty, so the per-tick pass is a single failed dictionary probe.
    /// </summary>
    /// <remarks>
    /// Precomputed rather than derived per tick: <c>AdvanceTick</c> runs once per boss on every tick
    /// of a fight budgeted under 5 ms, and both source maps are fixed at build time, so selecting and
    /// sorting here avoids reallocating a filter and a sort buffer every tick for a list that cannot
    /// change during the fight.
    /// </remarks>
    public required IReadOnlyDictionary<int, IReadOnlyList<EffectInstanceId>> AnnouncingOfPhase { get; init; }
}

/// <summary>
/// Everything <see cref="BossEncounterBuilder.Build"/> needs that a <see cref="BossScript"/> does
/// not carry — the encounter's power and level, its roster position, and the content the script's
/// effect ids resolve against.
/// </summary>
/// <remarks>
/// <see cref="Power"/> arrives as a parameter and is never derived here: a boss's power already
/// includes the boss stage multiplier, and must not be multiplied by it again. Keeping the
/// derivation on this side of the parameter makes the double-multiplication impossible to write.
/// </remarks>
internal sealed record BossEncounterRequest
{
    /// <summary>The boss script.</summary>
    public required BossScript Script { get; init; }

    /// <summary>
    /// The script's own effect set — every effect the owning boss content declares, keyed by id.
    /// Supplied by the caller; the boss engine does not read content.
    /// </summary>
    /// <remarks>
    /// This is the scope every id in the script resolves in, and there is no wider one: an effect is
    /// embedded in the content that owns it rather than living in a registry, so a
    /// <see cref="BossMechanic.EffectId"/> and a <c>RANDOM_OUTCOME</c> row alike name a sibling of
    /// the same script, and <see cref="BossEncounterBuilder"/> refuses either when the id isn't here.
    /// </remarks>
    public required IReadOnlyDictionary<string, EffectDefinition> Effects { get; init; }

    /// <summary>
    /// The boss node's power, with the boss stage multiplier already inside it. Never multiplied
    /// again here.
    /// </summary>
    public required double Power { get; init; }

    /// <summary>The enemy level for this chapter/tier.</summary>
    public required int Level { get; init; }

    /// <summary>The boss's actor index.</summary>
    public required int Index { get; init; }

    /// <summary>The boss's log id.</summary>
    public required byte LogId { get; init; }

    /// <summary>The baseline secondary-stats row every boss shares, as authored content.</summary>
    /// <remarks>
    /// Only its four secondaries — CRIT, CDMG, DODGE, LIFESTEAL — are read; the four power
    /// coefficients are replaced by <see cref="BossScript.Coefficients"/>.
    /// </remarks>
    public required ArchetypeRow Baseline { get; init; }

    /// <summary>The derivation constants — <c>EnemyCatalogue.Derivation</c>.</summary>
    public required EnemyDerivationConstants Derivation { get; init; }

    /// <summary>The first-clear flag for this player and this boss.</summary>
    public bool FirstClear { get; init; }
}

/// <summary>
/// One <see cref="BossScript"/> plus its encounter, resolved into the <see cref="BossEncounter"/>
/// the simulator and the phase controller run on.
/// </summary>
/// <remarks>
/// <para>
/// Every rule enforced here is an authoring rule: it can be decided before a tick runs, and a
/// failure names the boss, the phase and the mechanic. Deferring any of them to the tick loop would
/// surface a content typo as a mid-fight exception, or — worse — as a boss whose mechanics silently
/// did nothing, read by the balance harness as the boss being weak.
/// </para>
/// <list type="number">
///   <item>Exactly <see cref="BossScript.PhaseCount"/> blocks, numbered 1, 2, 3, in order.</item>
///   <item>Every referenced effect id resolves against <see cref="BossEncounterRequest.Effects"/>.</item>
///   <item>No block at phase 2 or 3 carries an <c>ON_BATTLE_START</c> trigger — the battle-start
///   sweep runs before phase 1 is entered, so such an effect would fire while the boss is still in
///   phase 1.</item>
///   <item>No script may name one of the three <see cref="BossBuiltIns"/>: they are attached here,
///   once, for every boss.</item>
///   <item>The wind-up rules — see <see cref="BossTelegraphs"/>.</item>
///   <item>A <c>SUMMON</c> mechanic authors a <c>maxAlive</c> of at most <see cref="BossAdds.MaxAlive"/>.
///   Authoring none is refused too — an absent key means no cap, which is never allowed.</item>
///   <item>
///   Every <c>RANDOM_OUTCOME</c> row names a sibling: an effect is embedded in the content that owns
///   it, so a row naming an id this script does not declare is refused here, at build time, rather
///   than surfacing as a mid-fight throw on whichever roll happens to draw it.
///   </item>
/// </list>
/// <para>
/// Every refusal is an <see cref="EffectContextException"/> naming the boss, the phase and the
/// mechanic, and carrying a rule marker (<c>A1</c>-<c>A5</c>, <c>T1</c>-<c>T3</c>, <c>O1</c>) so a
/// failure says which rule fired.
/// </para>
/// </remarks>
internal static class BossEncounterBuilder
{
    /// <summary>
    /// Builds one boss's encounter — its statline, its plan with every phase block and built-in on
    /// it, its two phase boundaries and the controller's two maps.
    /// </summary>
    /// <param name="request">The script and its encounter.</param>
    /// <returns>The resolved encounter.</returns>
    /// <exception cref="EffectContextException">One of the authoring rules refused.</exception>
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

        // Attached here, once, for every boss, and deliberately NOT in the phase map: nothing a
        // transition walks can reach SYS_ENRAGE, so nothing can re-anchor its clock.
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
    /// <see cref="BossEncounter.AnnouncingOfPhase"/> — the wind-up map bucketed by phase, each
    /// bucket in ascending instance-id order, decided once here.
    /// </summary>
    /// <remarks>
    /// The order is fixed here rather than at emission: two wind-ups due on the same tick reach the
    /// log in this order, and a <see cref="Dictionary{TKey,TValue}"/>'s enumeration order is not part
    /// of its contract.
    /// <para>
    /// <c>internal</c> rather than private so that a hand-built <see cref="BossEncounter"/> in tests
    /// can derive this map from its own lead map instead of restating it by hand.
    /// </para>
    /// </remarks>
    internal static IReadOnlyDictionary<int, IReadOnlyList<EffectInstanceId>> AnnouncingByPhase(
        IReadOnlyDictionary<EffectInstanceId, int> phaseOfInstance,
        IReadOnlyDictionary<EffectInstanceId, double> leadSecondsOfInstance)
    {
        var byPhase = new Dictionary<int, List<EffectInstanceId>>();

        foreach (var instance in leadSecondsOfInstance.Keys)
        {
            // Every instance with a lead was put in the phase map by the same loop, so this can't miss.
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
    /// The statline row: <see cref="BossEncounterRequest.Baseline"/>'s secondaries with the script's
    /// four coefficients in place of the archetype's.
    /// </summary>
    /// <param name="coefficients">The per-boss row.</param>
    /// <param name="baseline">The authored baseline row.</param>
    /// <returns>The row the derivation is run over.</returns>
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
    /// The statline is derived from <see cref="BossEncounterRequest.Power"/> as handed in — the boss
    /// stage multiplier is already inside it.
    /// </summary>
    private static ActorPlan PlanFor(BossEncounterRequest request, IReadOnlyList<HeldEffect> holdings) =>
        new()
        {
            Id = request.Script.Id,
            Identity = request.Script.Id,
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

    /// <summary><b>A1</b> — exactly 3 phase blocks, numbered 1, 2, 3, in that order.</summary>
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
    /// <b>A2</b> and <b>A3</b> — the mechanic is a sibling of this script, and it is not one of the
    /// three universal built-ins.
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

        // Checked before the lookup: Dictionary.TryGetValue(null) throws ArgumentNullException, and
        // BossMechanic is a record struct so `default` (a null EffectId) is reachable.
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
    /// <b>A4</b> — the battle-start sweep runs before phase 1 is entered, so an
    /// <c>ON_BATTLE_START</c> in a phase-2 or phase-3 block would fire while the boss is still in
    /// phase 1.
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

    /// <summary><b>A5</b> — a boss's adds are capped, checked at authoring.</summary>
    /// <remarks>
    /// An absent <c>maxAlive</c> is refused, not admitted: the key is optional in the DSL and reads
    /// as no cap, so a <c>SUMMON</c> that simply omitted it would spawn adds without a ceiling.
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
    /// <b>O1</b> — every <c>RANDOM_OUTCOME</c> row names a sibling of this same script.
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
            // Checked before the lookup: Dictionary.ContainsKey(null) throws ArgumentNullException,
            // and RandomOutcomeEntry is a record struct whose `default` carries a null id.
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
    /// <b>T1</b>, <b>T2</b> and <b>T3</b> — the three rules a mechanic's wind-up has to satisfy. See
    /// <see cref="BossTelegraphs"/> for what each one is protecting.
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

    private static string Number(int value) => InvariantText.Text(value);

    private static string Format(double value) => InvariantText.Text(value);
}
