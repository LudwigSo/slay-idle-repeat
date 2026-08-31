using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Status;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Status;

/// <summary>The status catalogue as a snapshot, and the doubles the status suite drives it through.</summary>
/// <remarks>
/// The catalogue is built by reading a snapshot, not by constructing the record: the document is
/// the authority and <c>StatusCatalogue.Read</c> is the only way into it, so a fixture that skipped
/// the reader would leave every behaviour test passing over a shape the real file cannot produce.
/// </remarks>
internal static class StatusFixtures
{
    /// <summary>The status table, in the order the design doc prints it.</summary>
    internal static ContentSnapshot Snapshot(
        decimal bleedMissingHpScaling = 1.0m,
        decimal stunMaxSeconds = 1.5m,
        decimal stunImmunitySeconds = 3.0m)
    {
        var root = ContentValue.Object(
        [
            new("bleedMissingHpScaling", ContentValue.Number(bleedMissingHpScaling)),
            new("stun", ContentValue.Object(
            [
                new("maxSecondsPerApplication", ContentValue.Number(stunMaxSeconds)),
                new("immunityWindowSeconds", ContentValue.Number(stunImmunitySeconds)),
            ])),
            new("statuses", ContentValue.Array(
            [
                Dot("BURN", "APPLIER_ATK_PCT_PER_SECOND", maxStacks: 5),
                Dot("POISON", "APPLIER_ATK_PCT_PER_SECOND", maxStacks: null),
                Bleed(),
                StatRow("FREEZE", "ASPD", fixedPotency: -0.5m),
                Stun(),
                StatRow("WEAKEN", "ATK"),
                StatRow("SUNDER", "DEF", maxStacks: 5),
                StatRow("SPORE", "HEAL_PCT", maxStacks: 4),
                Rage(),
                Ward(),
                StatRow("HASTE", "ASPD", kind: "BUFF"),
                Regen(),
                StatRow("CHILL", "ATK", maxStacks: 5),
            ])),
        ]);

        return new ContentSnapshot(
            ContentVersion.FromHex(new string('5', ContentVersion.HexLength)),
            [new ContentDocument(StatusCatalogue.Document, root)]);
    }

    /// <summary>
    /// <paramref name="other"/> with the status catalogue added — the snapshot shape the public
    /// <c>CombatSimulator.Simulate</c> overload requires.
    /// </summary>
    /// <remarks>
    /// That overload composes its own <c>StatusTimeline</c>, so it reads two documents and a
    /// fixture carrying only <c>combat_caps.json</c> fails loudly at <c>StatusCatalogue.Read</c>.
    /// </remarks>
    internal static ContentSnapshot With(ContentSnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return new ContentSnapshot(
            other.Version,
            other.DocumentPaths
                 .Select(other.GetDocument)
                 .Append(Snapshot().GetDocument(StatusCatalogue.Document)));
    }

    /// <summary>The status table, read.</summary>
    internal static StatusCatalogue Catalogue(
        decimal bleedMissingHpScaling = 1.0m,
        decimal stunMaxSeconds = 1.5m,
        decimal stunImmunitySeconds = 3.0m) =>
        StatusCatalogue.Read(Snapshot(bleedMissingHpScaling, stunMaxSeconds, stunImmunitySeconds));

    /// <summary>An <c>APPLY_STATUS</c> effect on a <c>PERIODIC</c>-free trigger.</summary>
    internal static EffectDefinition Apply(
        string id,
        string statusId,
        double value,
        double? seconds = null,
        EffectStacking? stacking = null,
        EffectTrigger? trigger = null,
        EffectTarget? target = null) =>
        new()
        {
            Id = id,
            Op = EffectOp.APPLY_STATUS,
            StatusId = statusId,
            Value = value,
            Trigger = trigger,
            Target = target,
            Stacking = stacking,
            Duration = seconds is { } s
                ? new EffectDuration { Scope = DurationScope.BATTLE, Seconds = s }
                : null,
        };

    /// <summary>
    /// A damage-over-time row. <paramref name="maxStacks"/> is nullable because POISON's whole
    /// character is an ADDITIVE rule with no ceiling, which is a different authored statement from
    /// carrying no stacking block at all.
    /// </summary>
    private static ContentValue Dot(string id, string basis, int? maxStacks)
    {
        var stacking = maxStacks is { } max
            ? Additive(max)
            : ContentValue.Object([new("mode", ContentValue.Text("ADDITIVE"))]);

        return ContentValue.Object(
        [
            new("id", ContentValue.Text(id)),
            new("type", ContentValue.Text("DOT")),
            new("potencyBasis", ContentValue.Text(basis)),
            new("stacking", stacking),
        ]);
    }

    private static ContentValue Bleed() =>
        ContentValue.Object(
        [
            new("id", ContentValue.Text("BLEED")),
            new("type", ContentValue.Text("DOT")),
            new("potencyBasis", ContentValue.Text("APPLIER_ATK_PCT_PER_SECOND")),
            new("scalesWithTargetMissingHp", ContentValue.Boolean(true)),
            new("stacking", ContentValue.Object(
            [
                new("mode", ContentValue.Text("ADDITIVE")),
                new("maxStacks", ContentValue.Number(5)),
                new("refreshOnReapply", ContentValue.Boolean(true)),
            ])),
        ]);

    private static ContentValue StatRow(
        string id, string stat, decimal? fixedPotency = null, int? maxStacks = null, string kind = "DEBUFF")
    {
        var members = new List<KeyValuePair<string, ContentValue>>
        {
            new("id", ContentValue.Text(id)),
            new("type", ContentValue.Text(kind)),
            new("potencyBasis", ContentValue.Text("TARGET_STAT_PCT")),
            new("stat", ContentValue.Text(stat)),
        };

        if (fixedPotency is { } potency)
        {
            members.Add(new("fixedPotency", ContentValue.Number(potency)));
        }

        if (maxStacks is { } max)
        {
            members.Add(new("stacking", Additive(max)));
        }

        return ContentValue.Object(members);
    }

    private static ContentValue Stun() =>
        ContentValue.Object(
        [
            new("id", ContentValue.Text("STUN")),
            new("type", ContentValue.Text("DEBUFF")),
            new("potencyBasis", ContentValue.Text("NONE")),
        ]);

    private static ContentValue Rage() =>
        ContentValue.Object(
        [
            new("id", ContentValue.Text("RAGE")),
            new("type", ContentValue.Text("BUFF")),
            new("potencyBasis", ContentValue.Text("TARGET_STAT_PCT")),
            new("stat", ContentValue.Text("ATK")),

            // RAGE decays and states no curve. Unauthorised, exactly as the shipped file carries it.
            new("decayCurve", ContentValue.Unauthorised),
        ]);

    private static ContentValue Ward() =>
        ContentValue.Object(
        [
            new("id", ContentValue.Text("WARD")),
            new("type", ContentValue.Text("BUFF")),
            new("potencyBasis", ContentValue.Text("FLAT_HP")),
        ]);

    private static ContentValue Regen() =>
        ContentValue.Object(
        [
            new("id", ContentValue.Text("REGEN")),
            new("type", ContentValue.Text("HOT")),
            new("potencyBasis", ContentValue.Text("TARGET_MAX_HP_PCT_PER_SECOND")),
        ]);

    private static ContentValue Additive(int maxStacks) =>
        ContentValue.Object(
        [
            new("mode", ContentValue.Text("ADDITIVE")),
            new("maxStacks", ContentValue.Number(maxStacks)),
        ]);
}

/// <summary>
/// An attack pipeline that records what a status tick asked of it, and applies the HP change the
/// same way production does — write, then <c>AfterHpDecrease</c>.
/// </summary>
/// <remarks>
/// It implements the real seam and nothing more. What the timeline can be held to is which member
/// it routes a tick through, with what number — mitigation, wards and flooring belong to the real
/// damage engine, and a double that re-implemented them would be a second one to keep in step.
/// </remarks>
internal class RecordingStatusPipeline : IAttackPipeline
{
    private readonly BattleServices _services;

    internal RecordingStatusPipeline(BattleServices services) => _services = services;

    /// <summary>
    /// Hook for a subclass that needs an attack to cost something the whole fight can see — the
    /// determinism suite uses it to make the dodge roll observable.
    /// </summary>
    internal virtual void OnAttack()
    {
    }

    /// <summary>Every <c>DealMaxHpPctDamage</c>, as <c>(tick, target, amount, bypassesWards, id)</c>.</summary>
    internal List<(int Tick, string Target, double Amount, bool BypassesWards, string SourceEffectId)> Dots
    { get; } = new();

    /// <summary>Every <c>Heal</c>, as <c>(tick, target, amount, id)</c>.</summary>
    internal List<(int Tick, string Target, double Amount, string SourceEffectId)> Heals { get; } = new();

    /// <summary>Every <c>GrantWard</c>, as <c>(tick, target, amount, id)</c>.</summary>
    internal List<(int Tick, string Target, double Amount, string SourceEffectId)> Wards { get; } = new();

    /// <summary>Every resolved swing, as <c>(tick, attacker)</c>.</summary>
    internal List<(int Tick, string Attacker)> Swings { get; } = new();

    /// <summary>How many times the pipeline ran the phase check plus <c>ON_LOW_HP</c>.</summary>
    internal int HpDecreaseNotifications { get; private set; }

    /// <inheritdoc />
    /// <remarks>
    /// A basic attack deals nothing here, and that is deliberate — it was found by a test that
    /// failed for the wrong reason. Slot 4 runs on every tick of every fight, and a double that
    /// dealt even 1 damage a swing made the target's HP a function of how many swings had happened
    /// before the cadence boundary: a <c>BLEED</c> case written to read "at full health" measured
    /// 10.2 instead of 10, because the hero had landed two hits by tick 27.
    /// </remarks>
    public AttackResolution ResolveAttack(
        IEffectActorView attacker, IEffectActorView defender, double attackMultiplier, string sourceEffectId)
    {
        Swings.Add((_services.Tick, ((BattleActor)attacker).Id));
        OnAttack();

        return new AttackResolution(Missed: false, Crit: false, Blocked: false, 0.0, 0.0);
    }

    /// <inheritdoc />
    public void DealTrueDamage(IEffectActorView target, double amount, string sourceEffectId)
    {
        var actor = (BattleActor)target;
        actor.SetCurrentHp(actor.CurrentHp - amount);
        Notify(actor);
    }

    /// <inheritdoc />
    public void DealMaxHpPctDamage(
        IEffectActorView target,
        double amount,
        bool bypassesWards,
        string sourceEffectId,
        IEffectActorView? source)
    {
        var actor = (BattleActor)target;
        Dots.Add((_services.Tick, actor.Id, amount, bypassesWards, sourceEffectId));

        actor.SetCurrentHp(actor.CurrentHp - amount);
        Notify(actor);
    }

    /// <inheritdoc />
    public void Heal(IEffectActorView target, double amount, string sourceEffectId)
    {
        var actor = (BattleActor)target;
        Heals.Add((_services.Tick, actor.Id, amount, sourceEffectId));
        actor.SetCurrentHp(actor.CurrentHp + amount);
    }

    /// <inheritdoc />
    public void GrantWard(
        IEffectActorView target, double amount, double? sourceCapPct, string sourceEffectId) =>
        Wards.Add((_services.Tick, ((BattleActor)target).Id, amount, sourceEffectId));

    /// <inheritdoc />
    public void AddThorns(
        IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId)
    {
    }

    /// <summary>
    /// A phase controller that counts the phase check — the call that is doubled if a DoT tick
    /// routes <c>AfterHpDecrease</c> itself as well as through the damage pipeline.
    /// </summary>
    /// <remarks>
    /// Counting inside <see cref="RecordingStatusPipeline"/> instead would be structurally blind to
    /// the defect: a timeline that also called <c>_services.AfterHpDecrease</c> leaves that counter
    /// unchanged. <c>BattleSimulation.AfterHpDecrease</c> fans out to here, so this sees the double.
    /// </remarks>
    internal sealed class CountingPhases : IBossPhases
    {
        /// <summary>How many times the phase check ran.</summary>
        internal int HpDecreaseCalls { get; private set; }

        /// <inheritdoc />
        public void EnterInitialPhase(BattleActor actor, int tick)
        {
        }

        /// <inheritdoc />
        public void AfterHpDecrease(BattleActor actor, int tick) => HpDecreaseCalls++;

        /// <inheritdoc />
        /// <remarks>
        /// The per-tick telegraph slot. This fixture counts the phase check and nothing else, so
        /// the slot is deliberately a no-op here — a counter would make the status suites fail on a
        /// boss-engine change that has nothing to do with statuses.
        /// </remarks>
        public void AdvanceTick(BattleActor actor, int tick)
        {
        }

        /// <inheritdoc />
        /// <remarks>
        /// The <c>PHASE</c>-scope reading. <c>null</c>, deliberately: this fixture models no phases
        /// at all, and the rule for a fight without them is that a <c>PHASE</c> scope behaves as
        /// <c>BATTLE</c>. Every status case in this file is written against that reading; the
        /// scope's own boundary is probed in <c>BossPhaseScopeTests</c>.
        /// </remarks>
        public int? CurrentPhase(BattleActor actor) => null;
    }

    private void Notify(BattleActor actor)
    {
        HpDecreaseNotifications++;
        _services.AfterHpDecrease(actor);
    }
}
