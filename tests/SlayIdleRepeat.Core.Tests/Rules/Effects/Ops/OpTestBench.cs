using System.Globalization;
using System.Linq;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Ops;

/// <summary>The bench every op test runs on: a battle, the six seams as recorders, and the literal calls each op made.</summary>
/// <remarks>
/// Recorders, not stubs that swallow — every argument is captured, including ones no assertion reads
/// yet, so an op that passed <c>null, null, ""</c> cannot leave the suite green. The stat reader is
/// frozen by construction, so mutual <c>STAT_COPY</c> copies cannot recurse.
/// </remarks>
internal sealed class OpTestBench
{
    private readonly Dictionary<(string Actor, StatId Stat), double> _stats = new();
    private readonly Dictionary<string, StatId> _highestBucket = new(StringComparer.Ordinal);

    /// <summary>Every seam call, in order, as <c>member(actor, number, effectId)</c>.</summary>
    internal List<string> Calls { get; } = [];

    /// <summary>The heal/ward/damage numbers, by member, for the numeric assertions.</summary>
    internal List<(string Member, string Actor, double Amount)> Amounts { get; } = [];

    /// <summary>The lifetime each seam call carried — the half of an effect no <see cref="Amounts"/> row can show.</summary>
    internal List<(string Member, EffectDuration? Duration, EffectStacking? Stacking)> Lifetimes { get; } = [];

    /// <summary>The effect id each seam call carried.</summary>
    internal List<string> EffectIds { get; } = [];

    /// <summary>Whether a <c>DAMAGE_MAXHP_PCT</c> reported a ward bypass.</summary>
    internal List<bool> WardBypasses { get; } = [];

    /// <summary>The <c>sourceCapPct</c> each <c>SHIELD</c> carried.</summary>
    internal List<double?> SourceCaps { get; } = [];

    /// <summary>The ops queued rather than resolved.</summary>
    internal List<(string EffectId, EffectOp Op, string Source, double Argument)> Queued { get; } = [];

    /// <summary>The percent-bucket writes <c>STAT_COPY</c> made, and who they landed on.</summary>
    internal List<(string Holder, StatId Stat, double Fraction)> PercentBuckets { get; } = [];

    /// <summary>
    /// Every <c>ITriggeredStatSink.Apply</c> call, one row per resolved target, as
    /// <c>(targetId, op, stat, value, sourceEffectId)</c>.
    /// </summary>
    internal List<(string TargetId, EffectOp Op, StatId Stat, double Value, string SourceEffectId)>
        TriggeredStatFirings
    { get; } = [];

    /// <summary>Every <c>RANDOM_OUTCOME</c> hand-off — a list rather than a slot, so "exactly one row per roll" is assertable.</summary>
    internal List<(string Holder, string ChosenEffectId, string SourceEffectId)> RandomOutcomes { get; } = [];

    /// <summary>What <see cref="IAttackPipeline.ResolveAttack"/> answers.</summary>
    /// <remarks>
    /// The default is a miss, deliberately: a default carrying HP would make "the target set was
    /// empty" and "two attacks landed" indistinguishable in any test that forgot to set it.
    /// </remarks>
    internal AttackResolution AttackAnswer { get; set; } = new(Missed: true, false, false, 0.0, 0.0);

    /// <summary>The frozen start-of-tick stat snapshot <c>STAT_COPY</c> and <c>ATK_MULT</c> read.</summary>
    internal OpTestBench WithStat(IEffectActorView actor, StatId stat, double value)
    {
        _stats[(actor.Id, stat)] = value;

        return this;
    }

    /// <summary><c>HIGHEST_PCT_BONUS</c> answer for one actor.</summary>
    internal OpTestBench WithHighestBucket(IEffectActorView actor, StatId stat)
    {
        _highestBucket[actor.Id] = stat;

        return this;
    }

    /// <summary>What every <c>ResolveAttack</c> on this bench answers.</summary>
    internal OpTestBench WithAttackOutcome(double basis, double hpLost)
    {
        AttackAnswer = new AttackResolution(Missed: false, Crit: false, Blocked: false, basis, hpLost);

        return this;
    }

    /// <summary>The seam set, with every member recording.</summary>
    internal EffectOpSeams Seams => new(
        AuthoredScaledValue.Instance,
        new RecordingAttackPipeline(this),
        new RecordingStatusEngine(this),
        new RecordingCombatFlow(this),
        new FrozenStatReader(this),
        new RecordingRunQueue(this),
        new RecordingTriggeredStatSink(this));

    /// <summary>A firing context over the given battle.</summary>
    internal EffectOpContext Context(
        EffectEvaluationContext evaluation,
        double? damageDealt = null,
        double? healAmount = null,
        double? overhealAmount = null) =>
        new()
        {
            Evaluation = evaluation,
            Seams = Seams,
            DamageDealt = damageDealt,
            HealAmount = healAmount,
            OverhealAmount = overhealAmount,
        };

    /// <summary>The one amount a member was called with, when exactly one call was expected.</summary>
    internal double OnlyAmount(string member) => Only(member).Amount;

    /// <summary>The one call a member was made with, when exactly one was expected.</summary>
    internal (string Member, string Actor, double Amount) Only(string member)
    {
        var matches = Amounts.Where(a => string.Equals(a.Member, member, StringComparison.Ordinal)).ToArray();

        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"expected exactly one {member} call, saw {matches.Length}: {string.Join(" | ", Calls)}");
    }

    /// <summary>The lifetime the one call to a member carried.</summary>
    internal (EffectDuration? Duration, EffectStacking? Stacking) OnlyLifetime(string member)
    {
        var matches = Lifetimes.Where(l => string.Equals(l.Member, member, StringComparison.Ordinal)).ToArray();

        return matches.Length == 1
            ? (matches[0].Duration, matches[0].Stacking)
            : throw new InvalidOperationException(
                $"expected exactly one {member} call, saw {matches.Length}: {string.Join(" | ", Calls)}");
    }

    private void Record(
        string member,
        string actor,
        double amount,
        string sourceEffectId,
        EffectDuration? duration = null,
        EffectStacking? stacking = null)
    {
        Amounts.Add((member, actor, amount));
        Lifetimes.Add((member, duration, stacking));
        EffectIds.Add(sourceEffectId);
        Calls.Add(
            $"{member}({actor}, {amount.ToString("R", CultureInfo.InvariantCulture)}, {sourceEffectId})");
    }

    private sealed class RecordingAttackPipeline(OpTestBench bench) : IAttackPipeline
    {
        public AttackResolution ResolveAttack(
            IEffectActorView attacker, IEffectActorView defender, double attackMultiplier, string sourceEffectId)
        {
            bench.Record(nameof(ResolveAttack), $"{attacker.Id}->{defender.Id}", attackMultiplier, sourceEffectId);

            return bench.AttackAnswer;
        }

        public void DealTrueDamage(IEffectActorView target, double amount, string sourceEffectId) =>
            bench.Record(nameof(DealTrueDamage), target.Id, amount, sourceEffectId);

        public void DealMaxHpPctDamage(
            IEffectActorView target,
            double amount,
            bool bypassesWards,
            string sourceEffectId,
            IEffectActorView? source)
        {
            bench.WardBypasses.Add(bypassesWards);
            bench.Record(nameof(DealMaxHpPctDamage), target.Id, amount, sourceEffectId);
        }

        public void Heal(IEffectActorView target, double amount, string sourceEffectId) =>
            bench.Record(nameof(Heal), target.Id, amount, sourceEffectId);

        public void GrantWard(
            IEffectActorView target, double amount, double? sourceCapPct, string sourceEffectId)
        {
            bench.SourceCaps.Add(sourceCapPct);
            bench.Record(nameof(GrantWard), target.Id, amount, sourceEffectId);
        }

        public void AddThorns(
            IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId) =>
            bench.Record(nameof(AddThorns), target.Id, fraction, sourceEffectId, duration);
    }

    private sealed class RecordingStatusEngine(OpTestBench bench) : IStatusEngine
    {
        public void Apply(
            IEffectActorView applier, IEffectActorView target, string statusId, double potency,
            EffectDuration? duration, EffectStacking? stacking, string sourceEffectId) =>
            bench.Record($"{nameof(Apply)}:{statusId}", target.Id, potency, sourceEffectId, duration, stacking);

        // No status on this bench carries its own literal potency — a value-less APPLY_STATUS throws.
        public bool HasFixedPotency(string statusId) => false;

        public void Remove(IEffectActorView target, string statusId, string sourceEffectId) =>
            bench.Record($"{nameof(Remove)}:{statusId}", target.Id, 0.0, sourceEffectId);

        public void RemoveByTag(IEffectActorView target, StatusTag tag, string sourceEffectId) =>
            bench.Record($"{nameof(RemoveByTag)}:{tag.Value}", target.Id, 0.0, sourceEffectId);

        public void Extend(IEffectActorView target, string statusId, double seconds, string sourceEffectId) =>
            bench.Record($"{nameof(Extend)}:{statusId}", target.Id, seconds, sourceEffectId);

        public void GrantImmunity(
            IEffectActorView target, string statusId, EffectDuration? duration, string sourceEffectId) =>
            bench.Record($"{nameof(GrantImmunity)}:{statusId}", target.Id, 0.0, sourceEffectId, duration);

        public void ScaleOutgoingPower(
            IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId) =>
            bench.Record(nameof(ScaleOutgoingPower), target.Id, fraction, sourceEffectId, duration);

        public void ScaleIncomingDuration(
            IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId) =>
            bench.Record(nameof(ScaleIncomingDuration), target.Id, fraction, sourceEffectId, duration);
    }

    private sealed class RecordingCombatFlow(OpTestBench bench) : ICombatFlowSink
    {
        public void ExtraAttack(
            IEffectActorView attacker, IEffectActorView target, int attacks, string sourceEffectId) =>
            bench.Record(nameof(ExtraAttack), $"{attacker.Id}->{target.Id}", attacks, sourceEffectId);

        public void GrantAttackMultiplierCharges(
            IEffectActorView holder, double multiplier, int charges, string sourceEffectId)
        {
            bench.Record(nameof(GrantAttackMultiplierCharges), holder.Id, multiplier, sourceEffectId);
            bench.Calls.Add($"charges={charges.ToString(CultureInfo.InvariantCulture)}");
        }

        public void GrantForcedCritCharges(IEffectActorView holder, int charges, string sourceEffectId) =>
            bench.Record(nameof(GrantForcedCritCharges), holder.Id, charges, sourceEffectId);

        public void ReduceCooldowns(IEffectActorView target, double fraction, string sourceEffectId) =>
            bench.Record(nameof(ReduceCooldowns), target.Id, fraction, sourceEffectId);

        public void ArmSurviveLethal(IEffectActorView holder, double hp, string sourceEffectId) =>
            bench.Record(nameof(ArmSurviveLethal), holder.Id, hp, sourceEffectId);

        public void ArmNegateLethal(IEffectActorView holder, string sourceEffectId) =>
            bench.Record(nameof(ArmNegateLethal), holder.Id, 0.0, sourceEffectId);

        public void ArmRevive(IEffectActorView holder, double hp, string sourceEffectId) =>
            bench.Record(nameof(ArmRevive), holder.Id, hp, sourceEffectId);

        public void Summon(
            IEffectActorView summoner, string archetype, int count, int? maxAlive, string sourceEffectId)
        {
            bench.Record($"{nameof(Summon)}:{archetype}", summoner.Id, count, sourceEffectId);
            bench.Calls.Add($"maxAlive={maxAlive?.ToString(CultureInfo.InvariantCulture) ?? "null"}");
        }

        public void RandomOutcome(IEffectActorView holder, string chosenEffectId, string sourceEffectId)
        {
            bench.RandomOutcomes.Add((holder.Id, chosenEffectId, sourceEffectId));
            bench.Record($"{nameof(RandomOutcome)}:{chosenEffectId}", holder.Id, 0.0, sourceEffectId);
        }

        public void ClearSummons(IEffectActorView owner, string sourceEffectId) =>
            bench.Record(nameof(ClearSummons), owner.Id, 0.0, sourceEffectId);

        public void SetTargetPriority(IEffectActorView target, double priority, string sourceEffectId) =>
            bench.Record(nameof(SetTargetPriority), target.Id, priority, sourceEffectId);

        public void AddDamageTakenMultiplier(
            IEffectActorView target, double multiplier, EffectDuration? duration, string sourceEffectId) =>
            bench.Record(nameof(AddDamageTakenMultiplier), target.Id, multiplier, sourceEffectId, duration);

        public void AddPercentBucket(
            IEffectActorView holder, StatId stat, double fraction, EffectDuration? duration, string sourceEffectId)
        {
            bench.PercentBuckets.Add((holder.Id, stat, fraction));
            bench.Record($"{nameof(AddPercentBucket)}:{stat}", holder.Id, fraction, sourceEffectId, duration);
        }
    }

    /// <summary>
    /// The start-of-tick snapshot, made literal: a map fixed before the ops run and never written to,
    /// so two actors copying each other cannot see each other's output.
    /// </summary>
    private sealed class FrozenStatReader(OpTestBench bench) : IResolvedStatReader
    {
        public double FinalStat(IEffectActorView actor, StatId stat) =>
            bench._stats.TryGetValue((actor.Id, stat), out var value)
                ? value
                : throw new InvalidOperationException(
                    $"the snapshot holds no {stat} for '{actor.Id}'. State it with WithStat — a default " +
                    "of 0 here would let an op that read the wrong actor pass.");

        public StatId HighestPercentBonusStat(IEffectActorView actor) =>
            bench._highestBucket.TryGetValue(actor.Id, out var stat)
                ? stat
                : throw new InvalidOperationException(
                    $"the snapshot holds no HIGHEST_PCT_BONUS answer for '{actor.Id}'.");
    }

    /// <summary>M2-R1's seam — records one row per resolved target, exactly as production does.</summary>
    private sealed class RecordingTriggeredStatSink(OpTestBench bench) : ITriggeredStatSink
    {
        public void Apply(
            IReadOnlyList<IEffectActorView> targets, EffectOp op, StatId stat, double value,
            EffectDuration? duration, EffectStacking? stacking, string sourceEffectId)
        {
            foreach (var target in targets)
            {
                bench.TriggeredStatFirings.Add((target.Id, op, stat, value, sourceEffectId));
            }

            bench.Record($"{nameof(Apply)}:{stat}", string.Join(",", targets.Select(t => t.Id)), value, sourceEffectId, duration, stacking);
        }
    }

    private sealed class RecordingRunQueue(OpTestBench bench) : IRunEffectQueue
    {
        public void Queue(EffectDefinition effect, IEffectActorView source, double argument)
        {
            bench.Queued.Add((effect.Id, effect.Op, source.Id, argument));
            bench.Calls.Add($"Queue({effect.Id}, {effect.Op})");
        }
    }
}

/// <summary>Effect literals for the op tests, in realistic authored shapes.</summary>
internal static class OpFixtures
{
    /// <summary>An effect with an id, an op and a value — the spine every op shares.</summary>
    internal static EffectDefinition Effect(
        string id, EffectOp op, double? value = null, EffectTarget? target = null) =>
        new()
        {
            Id = id,
            Op = op,
            Value = value,
            Target = target,
        };

    /// <summary>A minimal well-formed effect for one op — the keys its op row and schema branch require, and nothing more.</summary>
    /// <remarks>
    /// Shared by the resolver, seam and validation suites so that the three cannot disagree about
    /// what an authorable effect of a given op looks like.
    /// </remarks>
    internal static EffectDefinition Exemplar(
        EffectOp op, string? id = null, EffectTarget? target = EffectTarget.CURRENT_TARGET)
    {
        var effect = Effect(id ?? $"EX_{op}", op, 1.0, target);

        return op switch
        {
            EffectOp.STAT_ADD_FLAT or EffectOp.STAT_ADD_PCT or EffectOp.STAT_MULT or EffectOp.STAT_SET =>
                effect with { Stat = StatSelector.Of(StatId.ATK) },

            EffectOp.STAT_CONVERT =>
                effect with { Stat = StatSelector.Of(StatId.DEF), ToStat = StatId.ATK },

            EffectOp.STAT_CAP_OVERRIDE =>
                effect with { Stat = StatSelector.Of(StatId.CRIT), CapKind = StatCapKind.STAT_MAX },

            EffectOp.STAT_COPY => effect with { Stat = StatSelector.Of(StatId.CRIT) },

            EffectOp.HEAL_LEECH => effect with { Value = 0.2 },

            EffectOp.APPLY_STATUS or EffectOp.EXTEND_STATUS or EffectOp.IMMUNE_STATUS =>
                effect with { StatusId = "BURN", Value = 2.0 },

            EffectOp.REMOVE_STATUS => effect with { StatusId = "BURN" },

            EffectOp.ATTACK_MULT_NEXT => effect with { Charges = 1 },
            EffectOp.FORCE_CRIT_NEXT => effect with { Value = null, Charges = 1 },

            EffectOp.SUMMON => effect with { Archetype = "SWARM", Value = 2.0 },

            // RANDOM_OUTCOME carries no value (its own number is the winning row's index) and needs
            // two rows, because one outcome is not a choice.
            EffectOp.RANDOM_OUTCOME => effect with
            {
                Value = null,
                Outcomes = new[]
                {
                    new RandomOutcomeEntry("EX_OUTCOME_A", 1.0),
                    new RandomOutcomeEntry("EX_OUTCOME_B", 1.0),
                },
            },

            _ => effect,
        };
    }
}
