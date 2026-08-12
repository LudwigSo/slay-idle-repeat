using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Tests.TestSupport;

/// <summary>
/// Hermetic <see cref="GameContext"/> fixtures. No I/O, no clock, no adapter.
/// </summary>
/// <remarks>
/// 🔒 The content snapshot is built through <c>ContentSnapshot</c>'s public constructor, which is
/// the only way <c>Core</c> ever gets one: the M1 kickoff ruled that <c>InMemoryGame</c> takes a
/// pre-built snapshot and that <c>30</c> §6's <c>ContentSnapshot.LoadFromDisk("game-data")</c> is a
/// documented erratum — loading JSON is I/O and belongs in an adapter (<c>14</c> §6). A test that
/// reached for a file here would be the first crack in that.
/// </remarks>
internal static class GameContexts
{
    /// <summary>A stamped snapshot with no documents. Enough to build a context; reads nothing.</summary>
    internal static ContentSnapshot EmptyContent { get; } =
        new(ContentVersion.FromHex(new string('0', ContentVersion.HexLength)), []);

    /// <summary>No kill switch thrown: PvP and the Plus offer live, no placement or chapter disabled.</summary>
    internal static FeatureFlags NoKillSwitchThrown { get; } =
        new(pvpEnabled: true, plusOfferEnabled: true, disabledAdPlacements: [], disabledChapters: []);

    /// <summary>A player without Plus.</summary>
    internal static Entitlements WithoutPlus { get; } = new(hasPlus: false, expiresAtUtc: null);

    /// <summary>An arbitrary but fixed instant — a 05:00 UTC game-day boundary (<c>30</c> §2.3).</summary>
    internal static DateTimeOffset FixedInstant { get; } = new(2026, 8, 12, 5, 0, 0, TimeSpan.Zero);

    /// <summary>A context at <see cref="FixedInstant"/> carrying the given command seed.</summary>
    internal static GameContext WithSeed(ulong? commandSeed) =>
        new(FixedInstant, commandSeed, EmptyContent, WithoutPlus, NoKillSwitchThrown);
}
