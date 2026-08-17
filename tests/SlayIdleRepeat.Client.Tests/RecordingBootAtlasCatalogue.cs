using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// A hand-written <see cref="IBootAtlasCatalogue"/> that answers with a stated result — or refuses
/// — and records what the presenter looked like at the moment it was asked.
/// </summary>
/// <remarks>
/// A presenter exposes its stage, not its history, so the only way to show that the stages happen
/// in order and that none is skipped is to look at the presenter from inside a collaborator it
/// calls. The atlas read is the last stage before <c>Ready</c>, which makes it the one vantage
/// point that can see whether the profile was already open by the time it was reached.
/// </remarks>
internal sealed class RecordingBootAtlasCatalogue : IBootAtlasCatalogue
{
    private readonly BootAtlasResult? _result;
    private readonly Exception? _failure;

    private RecordingBootAtlasCatalogue(BootAtlasResult? result, Exception? failure)
    {
        _result = result;
        _failure = failure;
    }

    /// <summary>A catalogue that answers with the given result.</summary>
    internal static RecordingBootAtlasCatalogue Returning(BootAtlasResult result) => new(result, failure: null);

    /// <summary>A catalogue whose read throws — a manifest that is present and unreadable.</summary>
    internal static RecordingBootAtlasCatalogue Throwing(Exception failure) => new(result: null, failure);

    /// <summary>The presenter to observe when the read happens. Assigned after construction.</summary>
    internal BootPresenter? Observed { get; set; }

    /// <summary>Whether the presenter asked for the atlas at all.</summary>
    internal bool WasRead { get; private set; }

    /// <summary>The stage the presenter was in when it asked.</summary>
    internal BootStage? StageWhenRead { get; private set; }

    /// <summary>The profile the presenter had open when it asked.</summary>
    internal PlayerId? PlayerWhenRead { get; private set; }

    /// <summary>The elapsed span the presenter reported when it asked.</summary>
    internal TimeSpan? ElapsedWhenRead { get; private set; }

    /// <inheritdoc/>
    public BootAtlasResult Load()
    {
        WasRead = true;
        StageWhenRead = Observed?.Stage;
        PlayerWhenRead = Observed?.PlayerId;
        ElapsedWhenRead = Observed?.Elapsed;

        return _result ?? throw _failure!;
    }
}
