using Shouldly;
using SlayIdleRepeat.Core.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// The combat-to-run bridge: the simulator marks a consequence for the run and never resolves it.
/// What is tested here is the encoding contract of the log entry itself.
/// </summary>
/// <remarks>
/// Internal <c>CombatLog</c> seam: a public fight emits these entries (see
/// <c>PvpDuelTests.A_duel_discards_the_run_effect_queue</c>) but cannot author arbitrary effect
/// indices or unrounded arguments, which is what the encoding rules below are about.
/// </remarks>
public sealed class RunEffectQueuedTests
{
    private const byte Dicelord = CombatActor.FirstEnemy;
    private const ushort ScrambleEffectIndex = 41;

    private static CombatLog Started()
    {
        var log = new CombatLog();
        log.Append(0, CombatEventType.BattleStart, CombatActor.None, CombatActor.None);
        return log;
    }

    /// <summary>The encoding, field by field: who fired it, which effect, and the resolved argument.</summary>
    [Fact]
    public void A_queued_run_effect_carries_its_source_its_effect_index_and_one_argument()
    {
        var log = Started();

        // Scramble fires at 44 s (tick 880) and the roll picks die face 3.
        log.AppendRunEffectQueued(880, Dicelord, ScrambleEffectIndex, argument: 3.0);

        var entry = log.Events[^1];

        entry.Type.ShouldBe(CombatEventType.RunEffectQueued);
        entry.Tick.ShouldBe(880);
        entry.SourceId.ShouldBe(Dicelord);
        entry.DataId.ShouldBe(ScrambleEffectIndex);
        entry.Value.ShouldBe(3.0);
    }

    /// <summary>
    /// The target is <see cref="CombatActor.None"/>, not an actor: a run/board op has no victim in
    /// the arena, and <c>0</c> — the obvious "no target" default — is the hero.
    /// </summary>
    [Fact]
    public void A_queued_run_effect_targets_no_actor()
    {
        var log = Started();
        log.AppendRunEffectQueued(880, Dicelord, ScrambleEffectIndex, 3.0);

        log.Events[^1].TargetId.ShouldBe(CombatActor.None);
        CombatActor.IsActor(log.Events[^1].TargetId).ShouldBeFalse();
    }

    /// <summary>The queue is the log's <c>RunEffectQueued</c> entries, read front to back.</summary>
    [Fact]
    public void The_queue_is_the_log_read_in_order()
    {
        var log = Started();
        log.AppendRunEffectQueued(280, Dicelord, ScrambleEffectIndex, 1.0);
        log.Append(400, CombatEventType.Hit, CombatActor.Hero, Dicelord, 12.0);
        log.AppendRunEffectQueued(560, Dicelord, ScrambleEffectIndex, 5.0);
        log.AppendRunEffectQueued(840, Dicelord, ScrambleEffectIndex, 2.0);

        var result = log.Complete(heroWon: true, 900, 40.0);

        result.Log
            .Where(e => e.Type == CombatEventType.RunEffectQueued)
            .Select(e => e.Value)
            .ShouldBe([1.0, 5.0, 2.0]);
    }

    /// <summary>
    /// The queued op is inside <see cref="SimulationResult.LogHash"/>, like every other event, so a
    /// client cannot add, drop or retarget a run effect without the tamper check noticing.
    /// </summary>
    [Fact]
    public void A_queued_run_effect_moves_the_LogHash()
    {
        var without = Started();
        var with = Started();
        with.AppendRunEffectQueued(880, Dicelord, ScrambleEffectIndex, 3.0);

        var other = Started();
        other.AppendRunEffectQueued(880, Dicelord, ScrambleEffectIndex, 4.0);

        var hashes = new[]
        {
            without.Complete(heroWon: true, 900, 40.0).LogHash,
            with.Complete(heroWon: true, 900, 40.0).LogHash,
            other.Complete(heroWon: true, 900, 40.0).LogHash,
        };

        hashes.ShouldBeUnique(
            "a queued run effect that did not move the hash would be a run-affecting consequence " +
            "outside 11 §6's tamper check");
    }

    /// <summary>
    /// The effect index is stored verbatim across the whole <see cref="ushort"/> range, including
    /// <c>0</c>: <see cref="CombatLog.NoDataId"/> is also <c>0</c>, so a consumer that treated
    /// <c>DataId == 0</c> as "names nothing" would silently drop the first effect's queued ops.
    /// </summary>
    [Fact]
    public void The_effect_index_is_stored_verbatim_including_zero()
    {
        var log = Started();

        foreach (var effectIndex in new ushort[] { 0, 1, 65534, 65535 })
        {
            log.AppendRunEffectQueued(880, Dicelord, effectIndex);
            log.Events[^1].DataId.ShouldBe(effectIndex);
        }

        // Index 0 is a real effect, and it moves the hash exactly as index 1 does.
        var withZero = Started();
        withZero.AppendRunEffectQueued(880, Dicelord, 0);
        var withoutAny = Started();

        withZero.Complete(heroWon: true, 900, 40.0).LogHash
            .ShouldNotBe(withoutAny.Complete(heroWon: true, 900, 40.0).LogHash);
    }

    /// <summary>
    /// The runtime argument obeys the same 4-dp rule as every other logged number — it is inside
    /// <c>LogHash</c>, so it has to be reproducible bit for bit across platforms.
    /// </summary>
    [Fact]
    public void The_runtime_argument_is_rounded_like_every_other_logged_number()
    {
        Should.Throw<InvalidOperationException>(
            () => Started().AppendRunEffectQueued(880, Dicelord, ScrambleEffectIndex, 0.123456789));
    }

    /// <summary>
    /// An op with no runtime-resolved argument omits it — every argument is then authored on the
    /// <c>EffectDefinition</c> the index names.
    /// </summary>
    [Fact]
    public void An_op_with_no_runtime_argument_carries_zero()
    {
        var log = Started();
        log.AppendRunEffectQueued(880, Dicelord, ScrambleEffectIndex);

        log.Events[^1].Value.ShouldBe(0.0);
    }
}
