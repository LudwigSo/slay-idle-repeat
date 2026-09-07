using SlayIdleRepeat.Application.Services;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Client.Game.Presenters;

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
