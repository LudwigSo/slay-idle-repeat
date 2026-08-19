using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Tests.TestSupport;

/// <summary>
/// Hermetic <see cref="GameContext"/> fixtures. No I/O, no clock, no adapter — loading JSON is I/O
/// and belongs in an adapter, so a test that reached for a file here would be the first crack in that.
/// </summary>
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

    /// <summary>A player with Plus, and no lapse date known — a test using this is testing one of 12 §3.2's two reader sites.</summary>
    internal static Entitlements WithPlus { get; } = new(hasPlus: true, expiresAtUtc: null);

    /// <summary>An arbitrary but fixed instant — a 05:00 UTC game-day boundary.</summary>
    internal static DateTimeOffset FixedInstant { get; } = new(2026, 8, 12, 5, 0, 0, TimeSpan.Zero);

    /// <summary>A context at <see cref="FixedInstant"/> carrying the given command seed.</summary>
    internal static GameContext WithSeed(ulong? commandSeed) =>
        new(FixedInstant, commandSeed, EmptyContent, WithoutPlus, NoKillSwitchThrown);
}
