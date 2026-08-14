using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Status;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Status;

/// <summary>
/// `05` §5's catalogue as a snapshot, and the doubles the status suite drives it through.
/// </summary>
/// <remarks>
/// 🔒 <b>The catalogue is built by reading a snapshot, not by constructing the record.</b> The
/// document is the authority on `05` §5 and <c>StatusCatalogue.Read</c> is the only way into it, so a
/// fixture that skipped the reader would leave every behaviour test passing over a shape the real
/// file cannot produce. The shipped file's own numbers are asserted separately, in
/// <c>SlayIdleRepeat.Application.Tests</c>, which is the assembly that can load it.
/// </remarks>
internal static class StatusFixtures
{
    /// <summary>`05` §5's table, in the order the section prints it.</summary>
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
                Dot("POISON", "TARGET_MAX_HP_PCT_PER_SECOND", maxStacks: 3),
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
            ])),
        ]);

        return new ContentSnapshot(
            ContentVersion.FromHex(new string('5', ContentVersion.HexLength)),
            [new ContentDocument(StatusCatalogue.Document, root)]);
    }

    /// <summary>
    /// 🔒 <paramref name="other"/> with `05` §5's catalogue added — the snapshot shape the public
    /// <c>CombatSimulator.Simulate</c> overload requires.
    /// </summary>
    /// <remarks>
    /// That overload composes its own <c>StatusTimeline</c> (a fight it cannot apply a status in is not
    /// a fight `14` §2.4's client can run), so it reads <b>two</b> documents and a fixture carrying only
    /// <c>combat_caps.json</c> fails loudly at <c>StatusCatalogue.Read</c>.
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

    /// <summary>`05` §5, read.</summary>
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

    private static ContentValue Dot(string id, string basis, int maxStacks) =>
        ContentValue.Object(
        [
            new("id", ContentValue.Text(id)),
            new("type", ContentValue.Text("DOT")),
            new("potencyBasis", ContentValue.Text(basis)),
            new("stacking", Additive(maxStacks)),
        ]);

    private static ContentValue Bleed() =>
        ContentValue.Object(
        [
            new("id", ContentValue.Text("BLEED")),
            new("type", ContentValue.Text("DOT")),
            new("potencyBasis", ContentValue.Text("APPLIER_ATK_PCT_PER_SECOND")),
            new("scalesWithTargetMissingHp", ContentValue.Boolean(true)),
            new("stacking", ContentValue.Object(
            [
                new("mode", ContentValue.Text("NONE")),
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

            // 🔒 `05` §5 says RAGE decays and states no curve. Unauthorised, exactly as the shipped
            // file carries it.
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
/// A `05` §4 pipeline that records what a status tick asked of it, and applies the HP change the
/// way `05` §4 step 9 does — write, then <c>AfterHpDecrease</c>.
/// </summary>
/// <remarks>
/// 🔒 It implements the real seam and nothing more, on <c>RecordingAttackPipeline</c>'s stated
/// doctrine. What M2-10 can be held to is <em>which member it routes a tick through, with what
/// number</em> — the mitigation, the wards and the floor are M2-09's and a double that re-implemented
/// them would be a second damage engine to keep in step.
/// </remarks>
internal class RecordingStatusPipeline : IAttackPipeline
{
    private readonly BattleServices _services;

    internal RecordingStatusPipeline(BattleServices services) => _services = services;

    /// <summary>
    /// Hook for a subclass that needs an attack to cost something the whole fight can see — the
    /// determinism suite uses it to make `05` §4's dodge roll observable.
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

    /// <summary>Every resolved swing, as <c>(tick, attacker)</c> — `05` §3.1 slot 4's actual output.</summary>
    internal List<(int Tick, string Attacker)> Swings { get; } = new();

    /// <summary>How many times the pipeline ran `05` §3.1's phase check plus <c>ON_LOW_HP</c>.</summary>
    internal int HpDecreaseNotifications { get; private set; }

    /// <inheritdoc />
    /// <remarks>
    /// 🔴 <b>A basic attack deals nothing here, and that is deliberate — it was found by a test that
    /// failed for the wrong reason.</b> The subject of this suite is the status engine, and slot 4
    /// runs on every tick of every fight. A double that dealt even 1 damage a swing made the target's
    /// HP a function of <em>how many swings had happened before the cadence boundary</em>: a
    /// <c>BLEED</c> case written to read "at full health" measured 10.2 instead of 10, because the
    /// hero had landed two hits by tick 27, and the phase-check count was 8 rather than the 2 DoT
    /// ticks it was asserting about. Both would have been "fixed" by loosening the assertions, which
    /// would have thrown away the two sharpest tests in the file.
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
        IEffectActorView target, double amount, bool bypassesWards, string sourceEffectId)
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
    /// A phase controller that counts `05` §3.1's phase check — the call that is <b>doubled</b> if a DoT
    /// tick routes <c>AfterHpDecrease</c> itself as well as through §4 step 9.
    /// </summary>
    /// <remarks>
    /// 🔴 Counting inside <see cref="RecordingStatusPipeline"/> measures how many times the timeline
    /// called the pipeline — which the <c>Dots.Count</c> assertion already says — and is structurally
    /// blind to the defect: a timeline that <em>also</em> called <c>_services.AfterHpDecrease</c> leaves
    /// that counter unchanged. <c>BattleSimulation.AfterHpDecrease</c> fans out to here, so this sees the
    /// double.
    /// </remarks>
    internal sealed class CountingPhases : IBossPhases
    {
        /// <summary>How many times `05` §3.1's phase check ran.</summary>
        internal int HpDecreaseCalls { get; private set; }

        /// <inheritdoc />
        public void EnterInitialPhase(BattleActor actor, int tick)
        {
        }

        /// <inheritdoc />
        public void AfterHpDecrease(BattleActor actor, int tick) => HpDecreaseCalls++;

        /// <inheritdoc />
        /// <remarks>
        /// M2-12's per-tick telegraph slot. This fixture counts `05` §3.1's phase check and nothing
        /// else, so the slot is deliberately a no-op here — a counter would make the status suites
        /// fail on a boss-engine change that has nothing to do with statuses.
        /// </remarks>
        public void AdvanceTick(BattleActor actor, int tick)
        {
        }

        /// <inheritdoc />
        /// <remarks>
        /// M2-12's `18` §6 <c>PHASE</c>-scope reading. <c>null</c>, deliberately: this fixture models
        /// no phases at all, and §6's own answer for a fight without them is that a <c>PHASE</c>
        /// scope behaves as <c>BATTLE</c>. Every status case in this file is written against that
        /// reading; the scope's own boundary is probed in <c>BossPhaseScopeTests</c>.
        /// </remarks>
        public int? CurrentPhase(BattleActor actor) => null;
    }

    private void Notify(BattleActor actor)
    {
        HpDecreaseNotifications++;
        _services.AfterHpDecrease(actor);
    }
}
