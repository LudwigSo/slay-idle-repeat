using SlayIdleRepeat.Core.Model.Snapshots;

namespace SlayIdleRepeat.Application.Services;

/// <summary>How a power reading ended: with a number, or with one of four reasons there is none.</summary>
/// <remarks>
/// Four named absences rather than one, because an empty power tile has four different causes and
/// whoever holds the handset has to be able to tell a content fault from a row the domain refused.
/// </remarks>
public enum HeroPowerStanding
{
    /// <summary>A hero was composed and measured; <see cref="HeroPowerReading.PowerIndex"/> carries the number.</summary>
    Computed = 1,

    /// <summary>The hero composed but its effects would not aggregate into a stat block.</summary>
    BuildNotAggregable = 2,

    /// <summary>The player or run row is one the domain will not rehydrate, so there is no hero in it.</summary>
    RowNotRehydratable = 3,

    /// <summary>The Legend Level lies outside the authored curve: no curve point, no stat block.</summary>
    LegendLevelOutsideCurve = 4,

    /// <summary>The content set lacks something the reading needs — the power model, most likely.</summary>
    ContentUnavailable = 5,
}

/// <summary>One power reading: its standing, and the index when — and only when — one was computed.</summary>
/// <param name="Standing">How the reading ended.</param>
/// <param name="PowerIndex">The index, or <c>null</c> for every standing but <see cref="HeroPowerStanding.Computed"/>.</param>
public sealed record HeroPowerReading(HeroPowerStanding Standing, double? PowerIndex);

/// <summary>Where the Home screen's power tile gets its number.</summary>
/// <remarks>
/// A seam rather than a static call, so the screen can be proven to read power over the right hero
/// — the run's, inside a run — without composing a real one, and so the screen never learns how the
/// number is made.
/// </remarks>
public interface IHeroPowerSource
{
    /// <summary>Reads the power of the hero <paramref name="player"/> is, inside <paramref name="run"/> or between runs.</summary>
    /// <param name="player">The profile's row.</param>
    /// <param name="run">The run the hero is inside, or <c>null</c> between runs.</param>
    HeroPowerReading Read(PlayerSnapshot player, RunSnapshot? run);
}
