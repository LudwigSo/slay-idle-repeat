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
/// <remarks>
/// 🔒 <b>The "and only when" is enforced here, by the type, rather than trusted at every read.</b>
/// Two callers depend on it and they were reading it differently: the Home screen's view model takes
/// <see cref="PowerIndex"/> without consulting <see cref="Standing"/> at all, while
/// <c>HomePresenter.ReadPower</c> guards on <c>Standing == Computed</c> — so a source that answered a
/// number beside an absence would put a stale power on one of the two and not the other, and neither
/// reader could be called wrong. An invariant that only the implementations and a test fake uphold is
/// a convention, not a contract; stated once on the type, both readers are right by construction and
/// neither needs a branch nothing could reach.
/// </remarks>
/// <param name="Standing">How the reading ended.</param>
/// <param name="PowerIndex">The index, or <c>null</c> for every standing but <see cref="HeroPowerStanding.Computed"/>.</param>
public sealed record HeroPowerReading(HeroPowerStanding Standing, double? PowerIndex)
{
    /// <summary>The index, or <c>null</c> for every standing but <see cref="HeroPowerStanding.Computed"/>.</summary>
    /// <exception cref="ArgumentException">
    /// A computed standing carrying no number, or an absence carrying one. Both describe a reading
    /// no source can legitimately take, and both would be drawn on a screen as a power.
    /// </exception>
    public double? PowerIndex { get; } =
        (Standing == HeroPowerStanding.Computed) == (PowerIndex is not null)
            ? PowerIndex
            : throw new ArgumentException(
                "a reading standing at " + Standing + " " +
                (PowerIndex is null ? "carries no number" : "carries the number " + PowerIndex) +
                ". Computed is the one standing with an index and every other standing has none: a " +
                "number beside an absence is drawn as a power, and an absence beside Computed is a " +
                "screen that reports a hero it could not measure.",
                nameof(PowerIndex));
}

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
