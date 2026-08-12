using System.Globalization;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Rules.Economy;

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
/// 🔒 <b>A hole is never a default.</b> A <c>null</c> in the data files means "the design docs do
/// not authorise a value here" (<c>game-data/README.md</c>), and every read below goes through
/// <see cref="ContentSnapshot"/>'s typed readers, which throw
/// <see cref="UnauthorisedTunableException"/> rather than answer zero. Reading zero would produce
/// numbers, the simulator would grade them, and nobody would learn that a curve nobody authored
/// had been treated as flat.
/// </para>
/// <para>
/// ⚠️ <b>What this deliberately does not read.</b> <c>#/energy/dungeonCost</c> is authored and is
/// not read here: Resource Dungeons are M10 and the entry cost is theirs to spend. The Soul Shard
/// energy refill of `10` §3.1 is authored as "escalating cost", priced in `10` §5.1 at 300 Soul
/// Shards +150 per use per day, and <b>no tuning file holds that ladder</b> — so it is absent here
/// rather than invented, which is the whole of S6. <c>SlayIdleRepeat.Application.Tests</c>'
/// <c>EnergyTuningMatchesTuningDataTests</c> pins the absence so it cannot be filled in quietly.
/// </para>
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
    /// <exception cref="InvalidEnergyTuningException">A value is authorised but unusable.</exception>
    internal static EnergyTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var baseMax = content.ReadInt32(BaseMaxReference);
        if (baseMax < 1)
        {
            throw new InvalidEnergyTuningException(
                BaseMaxReference,
                "Max Energy must start at at least one point. 10 §3 authors a base Max Energy of " +
                "120; this document authors " + Render(baseMax) + ".");
        }

        var perLegendLevel = content.ReadInt32(PerLegendLevelReference);
        if (perLegendLevel < 0)
        {
            throw new InvalidEnergyTuningException(
                PerLegendLevelReference,
                "Max Energy must not shrink as a player gains a Legend Level. 10 §3 authors +2 per " +
                "Legend Level; this document authors " + Render(perLegendLevel) + ".");
        }

        var maxCap = content.ReadInt32(MaxCapReference);
        if (maxCap < baseMax)
        {
            throw new InvalidEnergyTuningException(
                MaxCapReference,
                "The Max Energy cap of " + Render(maxCap) + " is below the base Max Energy of " +
                Render(baseMax) + ", so a player would be capped below the tank they start with. " +
                "10 §3 authors 120 with a cap of 200.");
        }

        var regenInterval = ReadRegenInterval(content);

        var runCost = content.ReadInt32(RunCostReference);
        if (runCost < 1)
        {
            throw new InvalidEnergyTuningException(
                RunCostReference,
                "A run must cost at least one Energy, or Energy paces nothing at all. 10 §3 authors " +
                "20; this document authors " + Render(runCost) + ".");
        }

        var reserveMultipleOfMax = content.ReadInt32(ReserveMultipleOfMaxReference);
        if (reserveMultipleOfMax < 0)
        {
            throw new InvalidEnergyTuningException(
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
        var minutes = content.ReadNumber(RegenMinutesPerPointReference);
        if (minutes <= 0m)
        {
            throw new InvalidEnergyTuningException(
                RegenMinutesPerPointReference,
                "The regeneration interval must be a positive span; every accrual divides an " +
                "elapsed time by it. 10 §3 authors one Energy per 4 minutes; this document authors " +
                Render(minutes) + ".");
        }

        var ticks = decimal.Truncate(minutes * TimeSpan.TicksPerMinute);
        if (ticks < 1m)
        {
            throw new InvalidEnergyTuningException(
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
