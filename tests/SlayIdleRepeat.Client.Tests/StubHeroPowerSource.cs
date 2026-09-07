using SlayIdleRepeat.Application.Services;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Model.Snapshots;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// An <see cref="IHeroPowerSource"/> answering a fixed reading and recording what it was asked about.
/// </summary>
/// <remarks>
/// 🔒 Records the arguments because the one fact the Home screen has to get right about power is
/// invisible in the answer: a hero inside a run is composed over the run's frozen loadout and
/// drafted perks, a hero between runs over the profile's own loadout, and a source handed the wrong
/// one answers a perfectly plausible number for the wrong hero.
/// </remarks>
internal sealed class StubHeroPowerSource : IHeroPowerSource
{
    private readonly HeroPowerReading _reading;

    private StubHeroPowerSource(HeroPowerReading reading) => _reading = reading;

    /// <summary>How many times the screen asked.</summary>
    internal int ReadCallCount { get; private set; }

    /// <summary>The player row the last read was about.</summary>
    internal PlayerSnapshot? LastPlayer { get; private set; }

    /// <summary>The run row the last read was about, or null when it named none.</summary>
    internal RunSnapshot? LastRun { get; private set; }

    /// <summary>A source that computed the given index.</summary>
    internal static StubHeroPowerSource Computing(double powerIndex) =>
        new(new HeroPowerReading(HeroPowerStanding.Computed, powerIndex));

    /// <summary>A source that could not compute, for the given reason.</summary>
    /// <remarks>
    /// <see cref="HeroPowerStanding.Computed"/> handed here would describe a reading no real source
    /// produces — a computed standing with no number — so it is refused at the fixture.
    /// </remarks>
    internal static StubHeroPowerSource Standing(HeroPowerStanding standing) =>
        standing == HeroPowerStanding.Computed
            ? throw new ArgumentOutOfRangeException(
                nameof(standing), standing, "a Computed reading carries a number; use Computing(...).")
            : new(new HeroPowerReading(standing, PowerIndex: null));

    /// <inheritdoc/>
    public HeroPowerReading Read(PlayerSnapshot player, RunSnapshot? run)
    {
        ReadCallCount++;
        LastPlayer = player;
        LastRun = run;

        return _reading;
    }
}
