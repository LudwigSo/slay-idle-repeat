using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// 🔒 The `10` §3 / `28` C energy numbers, read out of <c>tuning/progression.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// `21` §3.1: <em>"A 📐 TUNABLE number that is not in this directory is a bug."</em> Every number
/// the energy model uses is authored in <c>game-data/tuning/progression.json</c> and reaches the
/// rules through this type — there is no <c>const int MaxEnergy = 120</c> anywhere in <c>Core</c>,
/// because a number in code is a number the economy simulator (`21`) cannot sweep, and one it
/// cannot sweep will never be tuned.
/// </para>
/// <para>
/// 🔒 <b>Why it lives in <c>Content/</c> and not beside the energy math in <c>Rules/Economy/</c>,
/// which is where M1-10 first put it.</b> `30` §11.4 describes <c>Content/</c> as
/// "<c>ContentSnapshot</c> + every definition type", and this is one: it reads a snapshot and
/// answers with values. The layering is what forces it. <c>Model</c> may not reference
/// <c>Rules</c>, so an aggregate holding `30` §11.5's "Energy never exceeds max + reserve" could
/// not have named it there; <c>Content</c> is beneath both <c>Model</c> and <c>Rules</c>, so both
/// may read it. <c>internal</c> all the same — nothing outside <c>Core</c> needs it.
/// </para>
/// <para>
/// 🔒 <b>A hole is never a default.</b> A <c>null</c> in the data files means "the design docs do
/// not authorise a value here" (<c>game-data/README.md</c>), and every read below goes through
/// <see cref="ContentSnapshot"/>'s typed readers, which throw
/// <see cref="UnauthorisedTunableException"/> rather than answer zero. Reading zero would produce
/// numbers, the simulator would grade them, and nobody would learn that a curve nobody authored
/// had been treated as flat.
/// </para>
/// <para>
/// ⚠️ <b>What this deliberately does not read, and who owns each.</b> The energy block authors more
/// than the energy math needs, and every unread leaf is a deferral rather than an oversight:
/// </para>
/// <list type="bullet">
///   <item><c>#/energy/dungeonCost</c> — Resource Dungeons are <b>M10</b>; the entry cost is theirs
///   to spend. <see cref="EnergyMath.Spend"/> takes whatever cost it is handed.</item>
///   <item><c>#/energy/sources/*</c> — the +40 / +20 / +10 amounts and their per-day caps
///   (`10` §3.1). <see cref="EnergyMath.Grant"/> takes an amount; enforcing a cap needs a daily
///   counter and the 05:00 UTC reset, which belong to the <b>granting command</b>, not to
///   arithmetic. The numbers are pinned by <c>EnergyTuningMatchesTuningDataTests</c>.</item>
///   <item><c>#/energy/reserveReceivesOverflowOnly</c>, <c>reserveRegeneratesOnItsOwn</c>,
///   <c>regenWhileOffline</c> — structural facts the math is <em>written against</em> rather than
///   branches it takes. Reading them would imply a code path for the false case, and `28` C2
///   authors none. The same test pins all three.</item>
///   <item><c>#/energy/medianSessionEnergyExhaustionAlarmShare</c> — a telemetry alarm threshold
///   (`10` §3.2), not a rule input.</item>
///   <item><b>The Soul Shard refill ladder</b> — <c>10</c> §3.1 calls it "escalating cost" and
///   `10` §5.1 prices it at 300 Soul Shards +150 per use per day. It <b>is</b> authored, in
///   <c>currencies.json#/soulShards/sinks/</c>, and is not read here because the escalation is
///   per-use-per-day shop state. The energy side of that refill is
///   <see cref="EnergyMath.RefillToFull"/>; the price is the Daily-tab command's.</item>
///   <item><c>FT_VIGOR</c> (`09` §6, named by `28` C2 as the one thing that changes the
///   regeneration rate) — +3% per rank, five ranks, and <b>no tuning key exists for it</b>. When
///   talents land it arrives as a modified <see cref="RegenInterval"/> derived here, not as a
///   second parameter on <see cref="EnergyMath.Accrue"/>, so no call site moves.</item>
/// </list>
/// </remarks>
internal sealed class EnergyTuning
{
    /// <summary>The document `10` §3's energy block lives in.</summary>
    internal const string DocumentPath = "tuning/progression.json";

    private const string EnergyPointer = DocumentPath + "#/energy";

    /// <summary>`10` §3 — Max Energy before any Legend Level growth.</summary>
    internal const string BaseMaxReference = EnergyPointer + "/baseMax";

    /// <summary>`10` §3 — Max Energy added per Legend Level.</summary>
    internal const string PerLegendLevelReference = EnergyPointer + "/perLegendLevel";

    /// <summary>`10` §3 — the ceiling Max Energy stops growing at.</summary>
    internal const string MaxCapReference = EnergyPointer + "/maxCap";

    /// <summary>`10` §3 — minutes of wall-clock time per regenerated Energy point.</summary>
    internal const string RegenMinutesPerPointReference = EnergyPointer + "/regenMinutesPerPoint";

    /// <summary>`10` §3 — the Energy a run costs.</summary>
    internal const string RunCostReference = EnergyPointer + "/runCost";

    /// <summary>`28` C2 — the Energy Reserve's capacity, as a multiple of Max Energy.</summary>
    internal const string ReserveMultipleOfMaxReference = EnergyPointer + "/reserveMultipleOfMax";

    private EnergyTuning(
        int baseMax,
        int perLegendLevel,
        int maxCap,
        TimeSpan regenInterval,
        int runCost,
        int reserveMultipleOfMax)
    {
        BaseMax = baseMax;
        PerLegendLevel = perLegendLevel;
        MaxCap = maxCap;
        RegenInterval = regenInterval;
        RunCost = runCost;
        ReserveMultipleOfMax = reserveMultipleOfMax;
    }

    /// <summary>`10` §3 — Max Energy at Legend Level zero. 120 as shipped.</summary>
    internal int BaseMax { get; }

    /// <summary>`10` §3 — Max Energy added per Legend Level. 2 as shipped.</summary>
    internal int PerLegendLevel { get; }

    /// <summary>`10` §3 — the ceiling Max Energy stops growing at. 200 as shipped.</summary>
    internal int MaxCap { get; }

    /// <summary>`10` §3 — how long one Energy point takes to regenerate. Four minutes as shipped.</summary>
    internal TimeSpan RegenInterval { get; }

    /// <summary>`10` §3 — the Energy a run costs. 20 as shipped.</summary>
    internal int RunCost { get; }

    /// <summary>`28` C2 — the Reserve's capacity as a multiple of Max Energy. 1 as shipped.</summary>
    internal int ReserveMultipleOfMax { get; }

    /// <summary>
    /// 🔒 `10` §3 — Max Energy at a Legend Level: the base plus the per-level increment, stopped at
    /// the cap. 120 (+2 per Legend Level, cap 200) as shipped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Why the derivation lives here and not only in <c>EnergyMath</c>.</b> Two callers need
    /// it and they are in layers that cannot see each other: <c>EnergyMath</c> (<c>Rules/</c>)
    /// computes grants and accruals with it, and the <c>Player</c> aggregate (<c>Model/</c>) holds
    /// `30` §11.5's <em>"Energy never exceeds max + reserve"</em> as an invariant — and `30` §11.4
    /// forbids <c>Model</c> from referencing <c>Rules</c>. <c>Content/</c> is beneath <b>both</b>,
    /// so it is the one home where the formula can exist once. M1-04 first transcribed it a second
    /// time onto the aggregate; that was two numbers that could drift, kept together only by a
    /// test, and this is the fix.
    /// </para>
    /// <para>
    /// It is a <b>definition type deriving a value from its own authored fields</b>, which is not
    /// the computation `30` §11.5 keeps off the aggregate: no state, no player, no rule — the same
    /// thing <see cref="RegenInterval"/> already does by turning authored minutes into a span.
    /// </para>
    /// <para>
    /// 🔒 <b>The increment counts levels <em>gained</em>, so it is <c>(legendLevel − 1)</c>.</b>
    /// `07` §1.1 starts a player at Legend Level <b>1</b> and <c>progression.json#/legendLevel/min</c>
    /// is 1, so Level 1 is where the authored base of 120 belongs. Four numbers in `10` §3/§3.2
    /// agree and are exact under this reading and off by a hair under <c>× legendLevel</c>: the
    /// headline "120 (+2 per Legend Level)"; "full refill time 8 hours from empty" (120 ÷ 15/hr);
    /// "runs on a full tank: 6" (120 ÷ 20); and §3.2's budget line "120 (start)". The visible
    /// consequence: the 200 cap is first reached at Legend Level <b>41</b>, not 40.
    /// </para>
    /// </remarks>
    /// <param name="legendLevel">
    /// The player's Legend Level. `07` §1.1 runs it 1..200 and the aggregate holds that range
    /// (`30` §11.5); anything below 1 has no meaning for the formula and is refused.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="legendLevel"/> is below 1.</exception>
    internal int MaxEnergyAt(int legendLevel)
    {
        RequireLegendLevel(legendLevel);

        // 64-bit, because both operands are authored numbers: a per-level increment of a few
        // million at Legend Level 200 would silently wrap in 32-bit and hand back a small or
        // negative Max Energy, which every rule below would then treat as the truth.
        var grown = BaseMax + ((long)PerLegendLevel * (legendLevel - 1));

        return (int)Math.Min(grown, MaxCap);
    }

    /// <summary>
    /// 🔒 `28` C2 — the Energy Reserve's capacity: <b>1× Max Energy</b> as shipped, and therefore a
    /// function of the player's <em>current</em> Max Energy rather than of the 200 cap. At Legend
    /// Level 1 it is 120, not 200.
    /// </summary>
    /// <param name="legendLevel">The player's Legend Level. Never below 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="legendLevel"/> is below 1.</exception>
    internal int ReserveCapacityAt(int legendLevel) =>
        (int)Math.Min((long)MaxEnergyAt(legendLevel) * ReserveMultipleOfMax, int.MaxValue);

    /// <summary>
    /// The Legend Level guard every derivation over this tuning shares.
    /// </summary>
    /// <remarks>
    /// Zero is not a player state and is refused with the rest: <see cref="MaxEnergyAt"/> counts
    /// levels <b>gained</b>, so anything below 1 subtracts from the base 120 that `10` §3 authors
    /// for a starting player. <c>internal</c> so <c>EnergyMath</c>'s entry points can fail fast on
    /// it before doing other work, rather than restating the message and letting the two copies
    /// drift.
    /// </remarks>
    internal static void RequireLegendLevel(int legendLevel)
    {
        if (legendLevel < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(legendLevel),
                legendLevel,
                "A Legend Level starts at 1 — 07 §1.1 runs it from 1 to 200, and the range itself " +
                "is the Player aggregate's invariant to hold (30 §11.5). Max Energy counts levels " +
                "GAINED, so anything below 1 subtracts from the base 120 that 10 §3 authors for a " +
                "starting player. Zero is not a player state and is refused with the rest.");
        }
    }

    /// <summary>
    /// Reads the energy block. Throws rather than defaulting on anything missing, unauthorised,
    /// mistyped or nonsensical.
    /// </summary>
    /// <param name="content">The version-stamped snapshot the command is reading (`30` §3).</param>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">A pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">
    /// A whole-number tunable holds a fraction. `10` §3 authors whole Energy points and no document
    /// authors a rounding rule, so the read fails rather than picking one (S6).
    /// </exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static EnergyTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var baseMax = content.ReadInt32(BaseMaxReference);
        if (baseMax < 1)
        {
            throw new InvalidTunableException(
                BaseMaxReference,
                "Max Energy must start at at least one point. 10 §3 authors a base Max Energy of " +
                "120; this document authors " + Render(baseMax) + ".");
        }

        var perLegendLevel = content.ReadInt32(PerLegendLevelReference);
        if (perLegendLevel < 0)
        {
            throw new InvalidTunableException(
                PerLegendLevelReference,
                "Max Energy must not shrink as a player gains a Legend Level. 10 §3 authors +2 per " +
                "Legend Level; this document authors " + Render(perLegendLevel) + ".");
        }

        var maxCap = content.ReadInt32(MaxCapReference);
        if (maxCap < baseMax)
        {
            throw new InvalidTunableException(
                MaxCapReference,
                "The Max Energy cap of " + Render(maxCap) + " is below the base Max Energy of " +
                Render(baseMax) + ", so a player would be capped below the tank they start with. " +
                "10 §3 authors 120 with a cap of 200.");
        }

        var regenInterval = ReadRegenInterval(content);

        var runCost = content.ReadInt32(RunCostReference);
        if (runCost < 1)
        {
            throw new InvalidTunableException(
                RunCostReference,
                "A run must cost at least one Energy, or Energy paces nothing at all. 10 §3 authors " +
                "20; this document authors " + Render(runCost) + ".");
        }

        var reserveMultipleOfMax = content.ReadInt32(ReserveMultipleOfMaxReference);
        if (reserveMultipleOfMax < 0)
        {
            throw new InvalidTunableException(
                ReserveMultipleOfMaxReference,
                "The Energy Reserve cannot hold a negative multiple of Max Energy. 28 C2 authors " +
                "1x Max Energy and calls that multiple the dial; this document authors " +
                Render(reserveMultipleOfMax) + ".");
        }

        return new EnergyTuning(
            baseMax, perLegendLevel, maxCap, regenInterval, runCost, reserveMultipleOfMax);
    }

    /// <summary>Reads the regeneration interval as an exact span.</summary>
    /// <remarks>
    /// Read as a <see cref="decimal"/> and converted to ticks by exact decimal arithmetic rather
    /// than through <c>TimeSpan.FromMinutes(double)</c>: `14` §8.2 wants the same answer on every
    /// architecture, and a binary <c>double</c> is the one step here that would not give it.
    /// Minutes are a duration rather than a count of Energy points, so a fractional value is
    /// legitimate — unlike every other tunable above.
    /// </remarks>
    private static TimeSpan ReadRegenInterval(ContentSnapshot content)
    {
        // The largest interval TimeSpan can hold. Not a 📐 tunable and not a design number — it is
        // the framework's own ceiling, derived rather than chosen, and it is guarded because
        // everything past it throws OverflowException out of the decimal multiply or the checked
        // (long) cast, escaping the ContentException family a composition root catches to report a
        // bad data set. Every other tunable here has a bound; this one had only a floor.
        var maxMinutes = TimeSpan.MaxValue.Ticks / (decimal)TimeSpan.TicksPerMinute;

        var minutes = content.ReadNumber(RegenMinutesPerPointReference);
        if (minutes > maxMinutes)
        {
            throw new InvalidTunableException(
                RegenMinutesPerPointReference,
                "The regeneration interval of " + Render(minutes) + " minute(s) is longer than any " +
                "span the runtime can represent, so no elapsed time would ever accrue a point. " +
                "10 §3 authors 4 minutes.");
        }

        if (minutes <= 0m)
        {
            throw new InvalidTunableException(
                RegenMinutesPerPointReference,
                "The regeneration interval must be a positive span; every accrual divides an " +
                "elapsed time by it. 10 §3 authors one Energy per 4 minutes; this document authors " +
                Render(minutes) + ".");
        }

        var ticks = decimal.Truncate(minutes * TimeSpan.TicksPerMinute);
        if (ticks < 1m)
        {
            throw new InvalidTunableException(
                RegenMinutesPerPointReference,
                "The regeneration interval of " + Render(minutes) + " minute(s) is shorter than one " +
                "tick, which truncates to a zero-length interval and would make every elapsed span " +
                "accrue without bound. 10 §3 authors 4 minutes.");
        }

        return TimeSpan.FromTicks((long)ticks);
    }

    /// <summary>
    /// 🔒 Renders a number with <see cref="CultureInfo.InvariantCulture"/>. `14` §8.2 wants
    /// <c>Core</c> reading identically everywhere, and a bare interpolation would render
    /// <c>4,5</c> on a German laptop and <c>4.5</c> in the Linux container — two diagnostics for
    /// one data defect.
    /// </summary>
    private static string Render(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc cref="Render(decimal)"/>
    private static string Render(int value) => value.ToString(CultureInfo.InvariantCulture);
}
