using SlayIdleRepeat.Application.Services;
using SlayIdleRepeat.Core.Model.Snapshots;

namespace SlayIdleRepeat.Application.Tests.Services;

/// <summary>An <see cref="IHeroPowerSource"/> answering a fixed reading.</summary>
/// <remarks>
/// The real source composes a hero out of the rows and measures it with <c>PowerCalculator</c>,
/// which is a rules concern with its own suite. What these cases are about is whether the number
/// the source produced reaches the view model at all, and whether an absent reading arrives as an
/// absence rather than as a zero — neither of which needs a real hero.
/// </remarks>
internal sealed class StubHeroPower : IHeroPowerSource
{
    private readonly HeroPowerReading _reading;

    private StubHeroPower(HeroPowerReading reading) => _reading = reading;

    /// <summary>A source that computed the given index.</summary>
    internal static StubHeroPower Computing(double powerIndex) =>
        new(new HeroPowerReading(HeroPowerStanding.Computed, powerIndex));

    /// <summary>A source that could not compute, for the given reason.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="HeroPowerStanding.Computed"/>, which would describe a reading no real source
    /// produces: a computed standing with no number.
    /// </exception>
    internal static StubHeroPower Standing(HeroPowerStanding standing) =>
        standing == HeroPowerStanding.Computed
            ? throw new ArgumentOutOfRangeException(
                nameof(standing), standing, "a Computed reading carries a number; use Computing(...).")
            : new StubHeroPower(new HeroPowerReading(standing, PowerIndex: null));

    /// <inheritdoc/>
    public HeroPowerReading Read(PlayerSnapshot player, RunSnapshot? run) => _reading;
}
