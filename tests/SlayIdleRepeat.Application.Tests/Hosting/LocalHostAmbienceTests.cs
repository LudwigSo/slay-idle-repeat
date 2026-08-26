using Shouldly;
using SlayIdleRepeat.Application.Hosting;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Hosting;

/// <summary>
/// The two values no port in this build can resolve. Each has exactly one answer that invents
/// nothing, and both are asserted as that answer rather than as a number somebody liked.
/// </summary>
public sealed class LocalHostAmbienceTests
{
    [Fact]
    public void NoSubscriptionResolved_reports_no_Plus_and_no_term_to_lapse()
    {
        var entitlements = LocalHostAmbience.NoSubscriptionResolved();

        entitlements.HasPlus.ShouldBeFalse(
            "nothing has told this host the player is subscribed, and false is the absence of an " +
            "entitlement rather than a guess at one — granting Plus for free is the other direction " +
            "of the same mistake.");

        entitlements.ExpiresAtUtc.ShouldBeNull(
            "a term that was never granted has no expiry. Any instant here is a date nobody authored, " +
            "and one in the past or future changes what the offer surface does.");
    }

    [Fact]
    public void NoRemoteConfigResolved_throws_no_kill_switch()
    {
        var flags = LocalHostAmbience.NoRemoteConfigResolved();

        flags.PvpEnabled.ShouldBeTrue("no config reached this host, so nothing has been taken offline.");
        flags.PlusOfferEnabled.ShouldBeTrue("…and nothing has withdrawn the offer either.");
        flags.MailEnabled.ShouldBeTrue("…and no config has closed the inbox — nothing killed is the identity.");
        flags.DisabledAdPlacements.ShouldBeEmpty("a placement nobody named is not a placement anybody killed.");
        flags.DisabledChapters.ShouldBeEmpty("nor is a chapter.");
    }

    /// <summary>
    /// The half a pair of empty sets cannot state on its own: these are kill lists, so an identifier
    /// no switch names is <em>enabled</em>. An allow-list reading of the same empty sets would disable
    /// the entire game, silently, on a host nobody configured.
    /// </summary>
    [Fact]
    public void NoRemoteConfigResolved_enables_an_identifier_no_switch_names()
    {
        var flags = LocalHostAmbience.NoRemoteConfigResolved();

        flags.IsAdPlacementEnabled("AD_A_PLACEMENT_NO_CONFIG_HAS_EVER_NAMED").ShouldBeTrue(
            "an unnamed placement came back disabled, so the empty set is being read as an allow list.");

        flags.IsChapterEnabled("CH_A_CHAPTER_NO_CONFIG_HAS_EVER_NAMED").ShouldBeTrue(
            "…and an unnamed chapter too, which would leave a player with nothing to enter.");
    }
}
