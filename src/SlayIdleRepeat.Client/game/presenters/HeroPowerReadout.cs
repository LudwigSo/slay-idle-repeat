using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Client.Game.Presenters;

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

/// <summary>
/// The real source: the rules' own hero, measured by the rules' own calculator, over the loaded
/// content set.
/// </summary>
/// <remarks>
/// <para>
/// <c>PowerCalculator</c> is the one public rules type built to be read by a screen, so this is not a
/// second copy of the power formula — it is the formula, handed the aggregated <c>Stats</c> of the
/// hero <c>HeroBuild.Of</c> composes from the rows, gear worn and perks drafted included.
/// </para>
/// <para>
/// Every way that composition can refuse is caught here and named, because the alternative is the
/// whole Home read failing over a tile that could simply be empty. The aggregation fault arrives as
/// an <see cref="InvalidOperationException"/>: the exception type the rules raise for it is internal
/// to <c>SlayIdleRepeat.Core</c>, and that is the base it reaches this assembly as.
/// </para>
/// </remarks>
public sealed class HeroPowerReadout : IHeroPowerSource
{
    private readonly ContentSnapshot _content;

    /// <summary>Builds the source over the loaded content set.</summary>
    /// <param name="content">The content set the hero curve, the gear catalogue and the power model are read from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public HeroPowerReadout(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        _content = content;
    }

    /// <inheritdoc/>
    public HeroPowerReading Read(PlayerSnapshot player, RunSnapshot? run)
    {
        ArgumentNullException.ThrowIfNull(player);

        try
        {
            var hero = HeroBuild.Of(player, run, _content);

            return new HeroPowerReading(
                HeroPowerStanding.Computed,
                PowerCalculator.PowerIndex(hero.Stats, player.LegendLevel, _content));
        }
        catch (ArgumentOutOfRangeException)
        {
            // Before ArgumentException, which it derives from: the curve refuses a level by range,
            // and a row fault reported for it would blame the profile for a curve nobody extended.
            return NoReading(HeroPowerStanding.LegendLevelOutsideCurve);
        }
        catch (ArgumentException)
        {
            return NoReading(HeroPowerStanding.RowNotRehydratable);
        }
        catch (InvalidOperationException)
        {
            return NoReading(HeroPowerStanding.BuildNotAggregable);
        }
        catch (ContentException)
        {
            return NoReading(HeroPowerStanding.ContentUnavailable);
        }
    }

    private static HeroPowerReading NoReading(HeroPowerStanding standing) => new(standing, PowerIndex: null);
}
