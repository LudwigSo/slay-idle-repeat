using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>The energy numbers, read out of <c>tuning/progression.json</c>.</summary>
/// <remarks>
/// <para>
/// Lives in <c>Content/</c> rather than beside the energy math: <c>Model</c> may not reference
/// <c>Rules</c>, so the <c>Player</c> aggregate's "Energy never exceeds max + reserve" invariant
/// and <c>Rules</c>' energy math both need this from a layer beneath them both.
/// </para>
/// <para>
/// A hole is never a default: every read below goes through <see cref="ContentSnapshot"/>'s typed
/// readers, which throw <see cref="UnauthorisedTunableException"/> rather than answer zero.
/// </para>
/// <para>
/// Deliberately not read here: <c>#/energy/dungeonCost</c> (Resource Dungeons are a later
/// milestone), the per-source grant caps and reset flags under <c>#/energy/sources/*</c> and
/// <c>reserve*</c> (daily-counter/reset concerns owned by the granting command, not this type),
/// the telemetry alarm threshold, and the Soul Shard refill price ladder (shop state, priced by
/// the Daily-tab command — only <see cref="EnergyMath.RefillToFull"/>'s energy side lives here).
/// </para>
/// </remarks>
internal sealed class EnergyTuning
{
    /// <summary>The document the energy block lives in.</summary>
    internal const string DocumentPath = "tuning/progression.json";

    private const string EnergyPointer = DocumentPath + "#/energy";

    /// <summary>Max Energy before any Legend Level growth.</summary>
    internal const string BaseMaxReference = EnergyPointer + "/baseMax";

    /// <summary>Max Energy added per Legend Level.</summary>
    internal const string PerLegendLevelReference = EnergyPointer + "/perLegendLevel";

    /// <summary>The ceiling Max Energy stops growing at.</summary>
    internal const string MaxCapReference = EnergyPointer + "/maxCap";

    /// <summary>Minutes of wall-clock time per regenerated Energy point.</summary>
    internal const string RegenMinutesPerPointReference = EnergyPointer + "/regenMinutesPerPoint";

    /// <summary>The Energy a run costs.</summary>
    internal const string RunCostReference = EnergyPointer + "/runCost";

    /// <summary>The Energy Reserve's capacity, as a multiple of Max Energy.</summary>
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

    /// <summary>Max Energy at Legend Level zero. 120 as shipped.</summary>
    internal int BaseMax { get; }

    /// <summary>Max Energy added per Legend Level. 2 as shipped.</summary>
    internal int PerLegendLevel { get; }

    /// <summary>The ceiling Max Energy stops growing at. 200 as shipped.</summary>
    internal int MaxCap { get; }

    /// <summary>How long one Energy point takes to regenerate. Four minutes as shipped.</summary>
    internal TimeSpan RegenInterval { get; }

    /// <summary>The Energy a run costs. 20 as shipped.</summary>
    internal int RunCost { get; }

    /// <summary>The Reserve's capacity as a multiple of Max Energy. 1 as shipped.</summary>
    internal int ReserveMultipleOfMax { get; }

    /// <summary>
    /// Max Energy at a Legend Level: the base plus the per-level increment, stopped at the cap.
    /// 120 (+2 per Legend Level, cap 200) as shipped.
    /// </summary>
    /// <remarks>
    /// Derived here (not just in <c>EnergyMath</c>) because both <c>EnergyMath</c> (<c>Rules/</c>)
    /// and the <c>Player</c> aggregate (<c>Model/</c>, which may not reference <c>Rules</c>) need
    /// it, and <c>Content/</c> is the one layer beneath both. The increment counts levels
    /// <em>gained</em>, i.e. <c>(legendLevel − 1)</c> — a player starts at Legend Level 1, which is
    /// where the authored base of 120 belongs. Consequently the 200 cap is first reached at Legend
    /// Level 41, not 40.
    /// </remarks>
    /// <param name="legendLevel">
    /// The player's Legend Level. Anything below 1 has no meaning for the formula and is refused.
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
    /// The Energy Reserve's capacity: a function of the player's <em>current</em> Max Energy
    /// (1x, as shipped) rather than of the 200 cap. At Legend Level 1 it is 120, not 200.
    /// </summary>
    /// <param name="legendLevel">The player's Legend Level. Never below 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="legendLevel"/> is below 1.</exception>
    internal int ReserveCapacityAt(int legendLevel) =>
        (int)Math.Min((long)MaxEnergyAt(legendLevel) * ReserveMultipleOfMax, int.MaxValue);

    /// <summary>The Legend Level guard every derivation over this tuning shares.</summary>
    /// <remarks>
    /// Zero is not a player state and is refused with the rest: <see cref="MaxEnergyAt"/> counts
    /// levels <b>gained</b>, so anything below 1 subtracts from the starting base.
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
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">A pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">A whole-number tunable holds a fraction.</exception>
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
    /// Converted to ticks by exact decimal arithmetic rather than through
    /// <c>TimeSpan.FromMinutes(double)</c>, so the result does not depend on binary-double
    /// rounding. Minutes are a duration, so a fractional value is legitimate here.
    /// </remarks>
    private static TimeSpan ReadRegenInterval(ContentSnapshot content)
    {
        // Guarded because a value past this throws OverflowException out of the decimal multiply
        // or the checked (long) cast, escaping the ContentException family a composition root
        // catches to report a bad data set.
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
    /// Renders a number with <see cref="CultureInfo.InvariantCulture"/> — a bare interpolation
    /// would render <c>4,5</c> on a German laptop and <c>4.5</c> in the Linux container.
    /// </summary>
    private static string Render(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc cref="Render(decimal)"/>
    private static string Render(int value) => value.ToString(CultureInfo.InvariantCulture);
}
