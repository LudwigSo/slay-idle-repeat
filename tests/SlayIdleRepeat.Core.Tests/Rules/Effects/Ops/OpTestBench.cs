using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Ops;

/// <summary>
/// The bench every `18` §2 op test runs on: a battle, the six seams as <b>recorders</b>, and the
/// literal calls each op made.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Recorders, not stubs that swallow.</b> `18` §10 step 3 asks for a test that
/// <em>"asserts the op's <b>numeric</b> behaviour"</em>, so what these capture is the number and the
/// recipient of every seam call, in the order the op made them. A test that asserted only that the
/// resolver did not throw would pass over an op that multiplied by the wrong basis, hit the wrong
/// actor, or silently did nothing — which is what the whole unwired-default discipline
/// (<see cref="EffectOpSeams.Strict"/>) exists to make impossible in production and what these make
/// impossible in the tests.
/// </para>
/// <para>
/// The stat reader is <b>frozen by construction</b> — a dictionary handed in once — which is how
/// <c>StatCopyOpTests</c> exhibits `18` §2.4's <em>"reads the start-of-tick snapshot, so mutual
/// copies cannot recurse"</em> rather than asserting the doc comment.
/// </para>
/// </remarks>
internal sealed class OpTestBench
{
    private readonly Dictionary<(string Actor, StatId Stat), double> _stats = new();
    private readonly Dictionary<string, StatId> _highestBucket = new(StringComparer.Ordinal);

    /// <summary>Every seam call, in order, as <c>member(actor, numbers…)</c>.</summary>
    internal List<string> Calls { get; } = [];

    /// <summary>The heal/ward/damage numbers, by member, for the numeric assertions.</summary>
    internal List<(string Member, string Actor, double Amount)> Amounts { get; } = [];

    /// <summary>Whether a <c>DAMAGE_MAXHP_PCT</c> reported `05` §4.1's bypass class (b).</summary>
    internal List<bool> WardBypasses { get; } = [];

    /// <summary>The <c>sourceCapPct</c> each <c>SHIELD</c> carried.</summary>
    internal List<double?> SourceCaps { get; } = [];

    /// <summary>The `18` §2.5 ops queued rather than resolved.</summary>
    internal List<(string EffectId, EffectOp Op, string Source, double Argument)> Queued { get; } = [];

    /// <summary>The percent-bucket writes <c>STAT_COPY</c> made, and who they landed on.</summary>
    internal List<(string Holder, StatId Stat, double Fraction)> PercentBuckets { get; } = [];

    /// <summary>What <see cref="IAttackPipeline.ResolveAttack"/> answers. Set per test.</summary>
    internal AttackResolution AttackAnswer { get; set; } = new(false, false, false, 0.0, 0.0);

    /// <summary>The frozen start-of-tick stat snapshot <c>STAT_COPY</c> and <c>ATK_MULT</c> read.</summary>
    internal OpTestBench WithStat(IEffectActorView actor, StatId stat, double value)
    {
        _stats[(actor.Id, stat)] = value;

        return this;
    }

    /// <summary>`18` §2.4's <c>HIGHEST_PCT_BONUS</c> answer for one actor.</summary>
    internal OpTestBench WithHighestBucket(IEffectActorView actor, StatId stat)
    {
        _highestBucket[actor.Id] = stat;

        return this;
    }

    /// <summary>The seam set, with every member recording.</summary>
    internal EffectOpSeams Seams => new(
        AuthoredScaledValue.Instance,
        new RecordingAttackPipeline(this),
        new RecordingStatusEngine(this),
        new RecordingCombatFlow(this),
        new FrozenStatReader(this),
        new RecordingRunQueue(this));

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
    internal double OnlyAmount(string member)
    {
        var matches = Amounts.Where(a => string.Equals(a.Member, member, StringComparison.Ordinal)).ToArray();

        return matches.Length == 1
            ? matches[0].Amount
            : throw new InvalidOperationException(
                $"expected exactly one {member} call, saw {matches.Length}: {string.Join(" | ", Calls)}");
    }

    private void Record(string member, string actor, double amount)
    {
        Amounts.Add((member, actor, amount));
        Calls.Add($"{member}({actor}, {amount.ToString("R", CultureInfo.InvariantCulture)})");
    }

    private sealed class RecordingAttackPipeline(OpTestBench bench) : IAttackPipeline
    {
        public AttackResolution ResolveAttack(
            IEffectActorView attacker, IEffectActorView defender, double attackMultiplier, string sourceEffectId)
        {
            bench.Record(nameof(ResolveAttack), $"{attacker.Id}->{defender.Id}", attackMultiplier);

            return bench.AttackAnswer;
        }

        public void DealTrueDamage(IEffectActorView target, double amount, string sourceEffectId) =>
            bench.Record(nameof(DealTrueDamage), target.Id, amount);

        public void DealMaxHpPctDamage(
            IEffectActorView target, double amount, bool bypassesWards, string sourceEffectId)
        {
            bench.WardBypasses.Add(bypassesWards);
            bench.Record(nameof(DealMaxHpPctDamage), target.Id, amount);
        }

        public void Heal(IEffectActorView target, double amount, string sourceEffectId) =>
            bench.Record(nameof(Heal), target.Id, amount);

        public void GrantWard(
            IEffectActorView target, double amount, double? sourceCapPct, string sourceEffectId)
        {
            bench.SourceCaps.Add(sourceCapPct);
            bench.Record(nameof(GrantWard), target.Id, amount);
        }

        public void AddThorns(
            IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId) =>
            bench.Record(nameof(AddThorns), target.Id, fraction);
    }

    private sealed class RecordingStatusEngine(OpTestBench bench) : IStatusEngine
    {
        public void Apply(
            IEffectActorView target, string statusId, double potency, EffectDuration? duration,
            EffectStacking? stacking, string sourceEffectId) =>
            bench.Record($"{nameof(Apply)}:{statusId}", target.Id, potency);

        public void Remove(IEffectActorView target, string statusId, string sourceEffectId) =>
            bench.Record($"{nameof(Remove)}:{statusId}", target.Id, 0.0);

        public void RemoveByTag(IEffectActorView target, StatusTag tag, string sourceEffectId) =>
            bench.Record($"{nameof(RemoveByTag)}:{tag.Value}", target.Id, 0.0);

        public void Extend(IEffectActorView target, string statusId, double seconds, string sourceEffectId) =>
            bench.Record($"{nameof(Extend)}:{statusId}", target.Id, seconds);

        public void GrantImmunity(
            IEffectActorView target, string statusId, EffectDuration? duration, string sourceEffectId) =>
            bench.Record($"{nameof(GrantImmunity)}:{statusId}", target.Id, 0.0);

        public void ScaleOutgoingPower(
            IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId) =>
            bench.Record(nameof(ScaleOutgoingPower), target.Id, fraction);

        public void ScaleIncomingDuration(
            IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId) =>
            bench.Record(nameof(ScaleIncomingDuration), target.Id, fraction);
    }

    private sealed class RecordingCombatFlow(OpTestBench bench) : ICombatFlowSink
    {
        public void ExtraAttack(
            IEffectActorView attacker, IEffectActorView target, int attacks, string sourceEffectId) =>
            bench.Record(nameof(ExtraAttack), $"{attacker.Id}->{target.Id}", attacks);

        public void GrantAttackMultiplierCharges(
            IEffectActorView holder, double multiplier, int charges, string sourceEffectId)
        {
            bench.Record(nameof(GrantAttackMultiplierCharges), holder.Id, multiplier);
            bench.Calls.Add($"charges={charges.ToString(CultureInfo.InvariantCulture)}");
        }

        public void GrantForcedCritCharges(IEffectActorView holder, int charges, string sourceEffectId) =>
            bench.Record(nameof(GrantForcedCritCharges), holder.Id, charges);

        public void ReduceCooldowns(IEffectActorView target, double fraction, string sourceEffectId) =>
            bench.Record(nameof(ReduceCooldowns), target.Id, fraction);

        public void ArmSurviveLethal(IEffectActorView holder, double hp, string sourceEffectId) =>
            bench.Record(nameof(ArmSurviveLethal), holder.Id, hp);

        public void ArmRevive(IEffectActorView holder, double hp, string sourceEffectId) =>
            bench.Record(nameof(ArmRevive), holder.Id, hp);

        public void Summon(
            IEffectActorView summoner, string archetype, int count, int? maxAlive, string sourceEffectId)
        {
            bench.Record($"{nameof(Summon)}:{archetype}", summoner.Id, count);
            bench.Calls.Add($"maxAlive={maxAlive?.ToString(CultureInfo.InvariantCulture) ?? "null"}");
        }

        public void ClearSummons(IEffectActorView owner, string sourceEffectId) =>
            bench.Record(nameof(ClearSummons), owner.Id, 0.0);

        public void SetTargetPriority(IEffectActorView target, double priority, string sourceEffectId) =>
            bench.Record(nameof(SetTargetPriority), target.Id, priority);

        public void AddDamageTakenMultiplier(
            IEffectActorView target, double multiplier, EffectDuration? duration, string sourceEffectId) =>
            bench.Record(nameof(AddDamageTakenMultiplier), target.Id, multiplier);

        public void AddPercentBucket(
            IEffectActorView holder, StatId stat, double fraction, EffectDuration? duration, string sourceEffectId)
        {
            bench.PercentBuckets.Add((holder.Id, stat, fraction));
            bench.Record($"{nameof(AddPercentBucket)}:{stat}", holder.Id, fraction);
        }
    }

    /// <summary>
    /// 🔒 `18` §2.4's start-of-tick snapshot, made literal: a map fixed before the ops run and never
    /// written to. Two actors copying each other therefore cannot see each other's output, which is
    /// the property §2.4 states and the op cannot enforce for itself.
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

    private sealed class RecordingRunQueue(OpTestBench bench) : IRunEffectQueue
    {
        public void Queue(EffectDefinition effect, IEffectActorView source, double argument)
        {
            bench.Queued.Add((effect.Id, effect.Op, source.Id, argument));
            bench.Calls.Add($"Queue({effect.Id}, {effect.Op})");
        }
    }
}

/// <summary>Effect literals for the op tests, in the shapes `18` and `06` author.</summary>
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
}
