using System.Collections.ObjectModel;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>The combat log under construction — the <c>log(...)</c> every part of the simulator calls, and the only way a <see cref="SimulationResult"/> is made.</summary>
/// <remarks>
/// <para>The emission contract, stated once so the several parts of the simulator that append into it cannot disagree.</para>
/// <list type="number">
///   <item>
///     <b>Append at the moment of the state change, never in a batch.</b> Collecting a tick's events
///     and sorting them at the end of the tick produces a different order — and therefore a different
///     <see cref="SimulationResult.LogHash"/> — from appending them as they happen, even though every
///     event is present in both. Order is inside the hash.
///   </item>
///   <item>
///     <b>Ticks never go backwards.</b> <see cref="Append"/> refuses an event whose tick is below the
///     previous one. This is what makes the log seekable: a speed toggle and a skip both need to find
///     the events for a tick range without a scan — see <see cref="FirstIndexAtOrAfter"/>.
///   </item>
///   <item>
///     <b>Every <see cref="CombatEvent.Value"/> is already rounded to 4 dp.</b> The log records what
///     happened; it does not round on the way out. A value that arrives unrounded means an accumulation
///     point upstream is missing its rounding, and rounding it here would hide exactly the drift the
///     cross-platform determinism gate exists to find.
///   </item>
///   <item>
///     <b>Order within one tick is the tick loop's own order</b> — statuses, expiries, periodics,
///     attacks in fixed initiative order, pet abilities, deaths. The log inherits it by construction;
///     nothing here re-imposes it.
///   </item>
///   <item>
///     <b>The log is sealed by <see cref="Complete"/>.</b> After it, appending throws — a log that
///     could still grow after the result was formed would make "the outcome is already determined" untrue.
///   </item>
/// </list>
/// <para>
/// <b>What the log does not carry, deliberately.</b> Nothing in it refers to simulator state: no
/// object references, no stat blocks, no effect instances — an actor is a <see cref="byte"/> and a
/// content id is a <see cref="ushort"/>. That is what makes the visual battle a replay of a
/// pre-computed log rather than a live simulation: a replayer holds the log, the battle's roster and
/// the content tables, and needs no part of the simulator.
/// </para>
/// <para>Not thread-safe, and it does not need to be: one battle is simulated by one caller.</para>
/// <para>A stateful builder, on <c>CombatLog</c>'s own precedent for the accumulator having to live somewhere: events are appended as they happen, across every step of a 1800-tick loop, so a stateless function would have to take and return the whole log on every call.</para>
/// </remarks>
internal sealed class CombatLog
{
    /// <summary>The tick rate: <c>TICK = 0.05 s</c>.</summary>
    public const int TicksPerSecond = BattleTicks.PerSecond;

    /// <summary>90 s at 20 ticks/second. Ticks run <c>0..1799</c>.</summary>
    /// <remarks>
    /// This is the PvE cap. A duel has a 60 s cap (1200 ticks) which this class does not enforce, since
    /// it has no way to know which kind of fight it is logging — the duel entry point inherits that bound.
    /// </remarks>
    public const int MaxTicks = BattleTicks.MaxPerFight;

    /// <summary>The shortest wind-up a damaging mechanic may have.</summary>
    public const double MinTelegraphSeconds = 1.0;

    /// <summary>The longest.</summary>
    public const double MaxTelegraphSeconds = 1.5;

    /// <summary>The <see cref="CombatEvent.DataId"/> that names no content — no status, no ability, no effect.</summary>
    public const ushort NoDataId = 0;

    /// <summary>The specification quoted in every refusal, so a failure says which rule it broke.</summary>
    private const string Specification = "05 §3.1 step 7";

    private readonly List<CombatEvent> _events = [];

    private int _lastTick;
    private bool _sealed;
    private bool _started;

    /// <summary>The events appended so far, in emission order.</summary>
    /// <remarks>
    /// A read-only view, not the list itself: a caller that could cast this back to a mutable list
    /// could append past the seal <see cref="Complete"/> applies, or reorder events already inside a
    /// computed <c>LogHash</c>.
    /// </remarks>
    public IReadOnlyList<CombatEvent> Events => _events.AsReadOnly();

    /// <summary>How many events have been appended.</summary>
    public int Count => _events.Count;

    /// <summary>Appends one event, at the moment the state change occurs.</summary>
    /// <param name="entry">The event.</param>
    /// <exception cref="InvalidOperationException">
    /// The log is sealed, the tick goes backwards or out of range, the value is not a rounded finite
    /// number, or the event breaks a rule stated on <see cref="CombatEventType"/>.
    /// </exception>
    public void Append(CombatEvent entry)
    {
        // Members with rules of their own are routed to the method that enforces them, rather than
        // trusting every caller to remember.
        if (entry.Type is CombatEventType.Telegraph or CombatEventType.RunEffectQueued)
        {
            throw new InvalidOperationException(
                $"A {entry.Type} was appended through {nameof(Append)}, which cannot enforce the rules that " +
                $"member carries — `17` §1's 1.0–1.5 s wind-up band for Telegraph, and the RUN target of " +
                $"`18` §5 for RunEffectQueued. Use {nameof(AppendTelegraph)} or " +
                $"{nameof(AppendRunEffectQueued)}, so those rules hold by construction rather than by everyone " +
                "remembering them.");
        }

        AppendCore(entry);
    }

    /// <summary>Every rule that governs any event, applied to one entry. The helpers that enforce a member's own extra rules reach the log through here, having applied them.</summary>
    private void AppendCore(CombatEvent entry)
    {
        if (_sealed)
        {
            throw new InvalidOperationException(
                $"The log was sealed by {nameof(Complete)} and a {entry.Type} at tick {entry.Tick} was appended " +
                "after it. `05` §8 offers skip because \"the outcome is already determined\" — a log that can " +
                "still grow once the result exists makes that false, and the SimulationResult's LogHash would no " +
                "longer cover its own Log.");
        }

        if (entry.Tick < 0 || entry.Tick >= MaxTicks)
        {
            throw new InvalidOperationException(
                $"A {entry.Type} carries tick {entry.Tick}, outside 0..{MaxTicks - 1}. `05` §3 caps a fight at " +
                $"90 s at 20 ticks/second, so {MaxTicks} ticks is the whole fight and there is no tick after it.");
        }

        if (entry.Tick < _lastTick)
        {
            throw new InvalidOperationException(
                $"A {entry.Type} at tick {entry.Tick} follows an event at tick {_lastTick}. {Specification} " +
                "appends events as they happen, so the log is ordered by construction — and `05` §8's speed " +
                "toggle and skip both seek it by tick. An event that goes backwards means it was batched and " +
                "flushed out of order, which also changes LogHash.");
        }

        // The vocabulary is closed. An undefined value would hash as its ordinal and replay as
        // nothing — a log the client and the server would agree on and neither could draw.
        if (!Enum.IsDefined(entry.Type))
        {
            throw new InvalidOperationException(
                $"{(int)entry.Type} is not a CombatEventType. `05` §7 is a closed vocabulary of " +
                $"{Enum.GetValues<CombatEventType>().Length} members; an undefined value would still be hashed " +
                "into LogHash, so client and server would agree on an event no replayer can draw.");
        }

        if (entry.Type == CombatEventType.BattleEnd)
        {
            throw new InvalidOperationException(
                $"BattleEnd was appended directly. It is {nameof(Complete)}'s to emit, so that \"the last event " +
                "of every log is BattleEnd\" holds by construction rather than by everyone remembering.");
        }

        RequireLoggableValue(entry);

        if (entry.Type == CombatEventType.BattleStart)
        {
            if (_started)
            {
                throw new InvalidOperationException(
                    "A second BattleStart was appended. `05` §3.1 step 0d emits exactly one, at the end of the " +
                    "battle-start pre-tick; a replayer keys its opening banner on it.");
            }

            if (entry.Tick != 0)
            {
                throw new InvalidOperationException(
                    $"BattleStart carries tick {entry.Tick}. `05` §3.1 runs the pre-tick BEFORE tick 0, so its " +
                    "events — the ward grants and opening buffs of step 0b, then BattleStart itself at step 0d — " +
                    "all carry tick 0.");
            }

            // Both slots must be None. BattleStart names no actor, and a client/server disagreement
            // about that would make an identical fight hash differently and be discarded as tampering.
            if (entry.SourceId != CombatActor.None || entry.TargetId != CombatActor.None)
            {
                throw new InvalidOperationException(
                    $"BattleStart names actors ({entry.SourceId}, {entry.TargetId}). It names none — both slots " +
                    "are CombatActor.None. This is inside LogHash, so a client that filled them and a server " +
                    "that did not would disagree about an identical fight (`11` §6).");
            }

            _started = true;
        }

        _events.Add(entry);
        _lastTick = entry.Tick;
    }

    /// <summary>Appends one event, spelled out. The overload the simulator's <c>log(...)</c> call sites use.</summary>
    /// <param name="tick">The tick the state change occurred on.</param>
    /// <param name="type">Which state change.</param>
    /// <param name="sourceId">Who caused it, or <see cref="CombatActor.None"/>.</param>
    /// <param name="targetId">Who it happened to, or <see cref="CombatActor.None"/>.</param>
    /// <param name="value">The event's number, rounded to 4 dp. See <see cref="CombatEvent"/>.</param>
    /// <param name="dataId">The content id the event names, or <see cref="NoDataId"/>.</param>
    public void Append(
        int tick,
        CombatEventType type,
        byte sourceId,
        byte targetId,
        double value = 0.0,
        ushort dataId = NoDataId) =>
        Append(new CombatEvent(tick, type, sourceId, targetId, value, dataId));

    /// <summary>Records that a combat trigger emitted a run/board op. The simulator never resolves it.</summary>
    /// <param name="tick">The tick the trigger fired on.</param>
    /// <param name="sourceId">The actor whose effect fired.</param>
    /// <param name="effectIndex">
    /// The battle-local effect index: the position of the firing effect's authored id in the battle's
    /// effect table, ordered by ascending ordinal effect id. See the remarks for why this, and not the
    /// id itself.
    /// </param>
    /// <param name="argument">The op's one runtime-resolved argument, rounded to 4 dp. <c>0</c> when the op has none and every argument is authored.</param>
    /// <remarks>
    /// <para>
    /// A combat trigger may emit a run/board op — the sanctioned case is a die-face modifier fired from
    /// a periodic trigger. The simulator still never resolves it: it appends a
    /// <c>RunEffectQueued</c> event and the run controller applies the queued ops in log order when the
    /// battle resolves, after the outcome is fixed and before <c>ON_BATTLE_END</c> effects are granted.
    /// In a PvP duel the queue is discarded.
    /// </para>
    /// <para>
    /// <b>Why the payload is an index and one number.</b> A <see cref="CombatEvent"/> has one
    /// <see cref="ushort"/> and one <see cref="double"/> to spend, and nearly everything else about a
    /// queued op is already authored on the effect definition. So <see cref="CombatEvent.DataId"/>
    /// identifies the effect and <see cref="CombatEvent.Value"/> carries the resolved argument, and
    /// <see cref="CombatEvent.TargetId"/> is <see cref="CombatActor.None"/> because the run target is
    /// not an actor.
    /// </para>
    /// <para>
    /// <b>Why an index rather than the effect id.</b> Effect ids are strings, and no string fits a
    /// <see cref="ushort"/>. The index is into the battle's effect table sorted by ascending ordinal
    /// effect id — an order everything else already uses, so it introduces no new rule and is identical
    /// on client and server.
    /// </para>
    /// <para>
    /// <b>The obligation this creates on the consumer.</b> The index is only meaningful against the
    /// same effect table, which does not travel with the result — whoever drains the queue must rebuild
    /// the battle's effect table from the same inputs, in the same order. That is already how the client
    /// re-runs the simulation and how the server re-runs a duel.
    /// </para>
    /// <para>
    /// <b>What does not fit.</b> One event carries at most one runtime-resolved scalar. An op needing
    /// two cannot be expressed here and must not be smuggled through by packing two numbers into one
    /// double. If one becomes reachable, the honest fixes are: author the second argument on the effect,
    /// or emit consecutive events with a documented arity — not a wider <see cref="CombatEvent"/>.
    /// </para>
    /// </remarks>
    public void AppendRunEffectQueued(int tick, byte sourceId, ushort effectIndex, double argument = 0.0) =>
        AppendCore(new CombatEvent(
            tick, CombatEventType.RunEffectQueued, sourceId, CombatActor.None, argument, effectIndex));

    /// <summary>Announces a damaging mechanic's wind-up, 1.0–1.5 s before it lands.</summary>
    /// <param name="tick">The tick the wind-up begins on.</param>
    /// <param name="sourceId">The actor winding up.</param>
    /// <param name="targetId">Who the mechanic will hit, or <see cref="CombatActor.None"/> for an AoE.</param>
    /// <param name="effectIndex">The battle-local effect index of the mechanic being announced.</param>
    /// <param name="leadSeconds">
    /// How long the wind-up lasts, in seconds. Bounded to <see cref="MinTelegraphSeconds"/>..<see cref="MaxTelegraphSeconds"/>:
    /// too short is unreadable and too long stops reading as a wind-up, since the player cannot act on
    /// it but must be able to read what is happening.
    /// </param>
    /// <remarks>
    /// <para>
    /// The wind-up is expressed in seconds rather than ticks because the replayer scales it by the
    /// speed toggle; the tick it lands on is <c>tick + leadSeconds × 20</c>, which the replayer computes.
    /// </para>
    /// <para>
    /// The lead must be a whole number of ticks: the simulation is fixed-tick, so the mechanic lands on
    /// an integer tick, and a lead of <c>1.0001 s</c> would put the announced landing between two ticks
    /// — on neither the event it announces nor any other. 1.0–1.5 s is 20–30 ticks.
    /// </para>
    /// </remarks>
    public void AppendTelegraph(int tick, byte sourceId, byte targetId, ushort effectIndex, double leadSeconds)
    {
        // NaN first, and explicitly: `NaN < Min` and `NaN > Max` are BOTH false, so a NaN lead
        // would sail through the band check below and be caught downstream by the rounding guard,
        // reporting the wrong rule.
        if (double.IsNaN(leadSeconds) ||
            leadSeconds < MinTelegraphSeconds || leadSeconds > MaxTelegraphSeconds)
        {
            throw new InvalidOperationException(
                $"A telegraph at tick {tick} announces a {Format(leadSeconds)} s wind-up, outside `17` §1's " +
                $"{Format(MinTelegraphSeconds)}–{Format(MaxTelegraphSeconds)} s band. Every damaging boss " +
                "mechanic has a visible wind-up the player can read; one that is too short cannot be read and " +
                "one that is too long stops reading as a wind-up at all.");
        }

        // Compared with a tolerance rather than exact equality: `1.2 * 20` is not bit-exactly 24.0
        // for every value in the band, while the defect being caught — a lead of 1.0001 s, so
        // 20.002 ticks — misses by 2e-3.
        if (!BattleTicks.IsWhole(leadSeconds, out _))
        {
            throw new InvalidOperationException(
                $"A telegraph at tick {tick} announces a {Format(leadSeconds)} s wind-up, which is " +
                $"{Format(leadSeconds * TicksPerSecond)} ticks. `05` §3's simulation is fixed-tick, so the " +
                "mechanic it announces lands on a whole tick — a fractional lead points between two ticks and " +
                $"therefore at nothing. Use a multiple of {Format(BattleTicks.SecondsPerTick)} s.");
        }

        AppendCore(new CombatEvent(tick, CombatEventType.Telegraph, sourceId, targetId, leadSeconds, effectIndex));
    }

    /// <summary>Seals the log with a <see cref="CombatEventType.BattleEnd"/> and returns the result, <see cref="SimulationResult.LogHash"/> included.</summary>
    /// <param name="heroWon">Whether the hero side won.</param>
    /// <param name="durationTicks">How many ticks the fight ran, <c>1..1800</c>.</param>
    /// <param name="heroHpRemaining">The hero's HP at the end, rounded to 4 dp.</param>
    /// <returns>The finished <see cref="SimulationResult"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// The log was already sealed, no <see cref="CombatEventType.BattleStart"/> was emitted, or the
    /// duration does not cover the events already logged.
    /// </exception>
    public SimulationResult Complete(bool heroWon, int durationTicks, double heroHpRemaining)
    {
        if (_sealed)
        {
            throw new InvalidOperationException(
                $"{nameof(Complete)} was called twice. One battle produces one result; a second call would " +
                "append a second BattleEnd and hash a log that is no longer the one the first result carried.");
        }

        if (!_started)
        {
            throw new InvalidOperationException(
                "The log carries no BattleStart. `05` §3.1 step 0d emits one at the end of the battle-start " +
                "pre-tick, before tick 0 runs — a log without it is a replay with no beginning, and the missing " +
                "pre-tick is where ward grants and opening buffs live (step 0b).");
        }

        if (durationTicks < 1 || durationTicks > MaxTicks)
        {
            throw new InvalidOperationException(
                $"A fight of {durationTicks} ticks is outside 1..{MaxTicks}. `05` §3 caps a fight at 90 s at " +
                "20 ticks/second, and a fight that ran no ticks at all has no outcome to report.");
        }

        var endTick = durationTicks - 1;
        if (endTick < _lastTick)
        {
            throw new InvalidOperationException(
                $"The fight is reported as {durationTicks} ticks, so its last tick is {endTick} — but an event " +
                $"is already logged at tick {_lastTick}. DurationTicks counts the ticks that ran, so it can " +
                "never be shorter than the log it summarises.");
        }

        RequireRoundedFinite(heroHpRemaining, nameof(heroHpRemaining));

        if (heroHpRemaining < 0.0)
        {
            throw new InvalidOperationException(
                $"The hero is reported with {Format(heroHpRemaining)} HP remaining. `05` §4's damage pipeline and " +
                "§4.3's healing keep HP at or above 0 by construction, so a negative value is an unclamped " +
                "subtraction upstream, not an outcome to record.");
        }

        // Build the finished log and hash it BEFORE mutating, so a refusal from the writer leaves
        // the builder untouched and Complete stays retryable.
        var log = _events.Append(new CombatEvent(
                endTick, CombatEventType.BattleEnd, CombatActor.None, CombatActor.None, 0.0, NoDataId))
            .ToArray();

        var logHash = CanonicalStateWriter.HashCombatLog(log);

        _events.Add(log[^1]);
        _lastTick = endTick;
        _sealed = true;

        // Wrapped, not handed over raw: a caller that could cast the result's Log back to a mutable
        // array could edit a replay after its LogHash was computed.
        return new SimulationResult(
            heroWon, durationTicks, heroHpRemaining, new ReadOnlyCollection<CombatEvent>(log), logHash);
    }

    /// <summary>The index of the first event at or after <paramref name="tick"/>, or the log's length when there is none — by binary search, which the non-decreasing tick order makes valid.</summary>
    /// <param name="log">A completed log.</param>
    /// <param name="tick">The tick to seek to.</param>
    /// <remarks>
    /// This is what replay affordances rest on. The speed toggle simply consumes the log faster — the
    /// same events, a different wall-clock rate — and skip is always available because the outcome is
    /// already fixed. Both are seeks, not simulations, and neither needs to scan from the start of the fight.
    /// </remarks>
    public static int FirstIndexAtOrAfter(IReadOnlyList<CombatEvent> log, int tick)
    {
        ArgumentNullException.ThrowIfNull(log);

        var low = 0;
        var high = log.Count;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (log[middle].Tick < tick)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>The rules an event's <see cref="CombatEvent.Value"/> has to satisfy to be logged.</summary>
    private static void RequireLoggableValue(CombatEvent entry) =>
        RequireRoundedFinite(entry.Value, $"the Value of a {entry.Type} at tick {entry.Tick}");

    /// <summary>A logged number is finite and already rounded to 4 decimal places.</summary>
    /// <remarks>
    /// Checked here as well as in <c>CanonicalStateWriter</c>, and it is not a duplicated rule: the
    /// writer refuses the whole log and can only name the value, while this refuses the one
    /// <see cref="CombatEvent"/> that carries it and names its type and tick.
    /// </remarks>
    private static void RequireRoundedFinite(double value, string what)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new InvalidOperationException(
                $"{what} is {Format(value)}. A combat number is finite: `05` §4's damage pipeline and §4.3's " +
                "healing produce real quantities, and a NaN or an infinity is an overflow or a divide-by-zero " +
                "upstream, not a value to record and hash.");
        }

        if (double.IsNegative(value) && value == 0.0)
        {
            throw new InvalidOperationException(
                $"{what} is negative zero. It compares equal to 0.0 in C# but carries a different bit pattern, " +
                "so two logs the language calls identical would carry different LogHashes — a false divergence " +
                "in `11` §6's tamper check and in M5-12's cross-platform gate. Normalise at the accumulation " +
                "point (`x + 0.0` is `+0.0`), not here.");
        }

        if (DeterminismRounding.Round(value) != value)
        {
            throw new InvalidOperationException(
                $"{what} is {Format(value)}, which is not rounded to 4 decimal places. `05` §1.1 rounds at " +
                "every accumulation point; a value that reaches the log unrounded means one is missing its " +
                "Math.Round(x, 4), and x64 and ARM64 will disagree about the low bits of the LogHash M5-12 " +
                "compares. The log records what happened — rounding it here would hide that.");
        }
    }

    // The convention, not a second statement of it — see Primitives/InvariantText. This one is
    // the load-bearing case: its output reaches the tamper check's LogHash.
    private static string Format(double value) => InvariantText.Text(value);
}
