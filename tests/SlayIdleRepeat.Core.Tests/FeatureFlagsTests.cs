using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>The remote-config kill switches of 14 §14: kill lists, not allow lists.</summary>
public sealed class FeatureFlagsTests
{
    private const string ElitePlacement = "AD_ELITE_GUARANTEE";
    private const string LuckPlacement = "AD_ENHANCE_LUCK";
    private const string FirstChapter = "CH_01_EMBERFALL";

    private const string UnnamedChapter = "A_CHAPTER_NO_KILL_SWITCH_NAMES";

    /// <summary>The set-valued switches are kill lists, not allow lists: an unnamed identifier is enabled.</summary>
    [Fact]
    public void An_identifier_no_kill_switch_names_is_enabled()
    {
        var flags = new FeatureFlags(true, true, true, [], []);

        flags.IsAdPlacementEnabled(ElitePlacement).ShouldBeTrue();
        flags.IsChapterEnabled(FirstChapter).ShouldBeTrue();
    }

    /// <summary>The fifth switch of 14 §14's set (M5 kickoff ruling 2): mail claims.</summary>
    [Fact]
    public void MailEnabled_round_trips_independently_of_the_other_switches()
    {
        var mailKilled = new FeatureFlags(pvpEnabled: true, plusOfferEnabled: true, mailEnabled: false, [], []);

        mailKilled.MailEnabled.ShouldBeFalse();
        mailKilled.PvpEnabled.ShouldBeTrue(
            "a mail kill that read back through PvP means the switches are wired crosswise");
        mailKilled.PlusOfferEnabled.ShouldBeTrue();

        var onlyMailAlive = new FeatureFlags(pvpEnabled: false, plusOfferEnabled: false, mailEnabled: true, [], []);

        onlyMailAlive.MailEnabled.ShouldBeTrue();
        onlyMailAlive.PvpEnabled.ShouldBeFalse();
        onlyMailAlive.PlusOfferEnabled.ShouldBeFalse();
    }

    [Fact]
    public void A_named_identifier_is_disabled_and_its_neighbours_are_not()
    {
        var flags = new FeatureFlags(true, true, true, [ElitePlacement], [FirstChapter]);

        flags.IsAdPlacementEnabled(ElitePlacement).ShouldBeFalse();
        flags.IsAdPlacementEnabled(LuckPlacement).ShouldBeTrue();
        flags.IsChapterEnabled(FirstChapter).ShouldBeFalse();
        flags.IsChapterEnabled(UnnamedChapter).ShouldBeTrue();
    }

    /// <summary>Membership is ordinal: a case-insensitive comparison could silently kill an unintended near-match.</summary>
    [Fact]
    public void Membership_is_ordinal_so_a_respelling_is_not_a_silent_hit()
    {
        var flags = new FeatureFlags(true, true, true, [ElitePlacement], [FirstChapter]);

        flags.IsAdPlacementEnabled("ad_elite_guarantee").ShouldBeTrue();
        flags.IsChapterEnabled("ch_01_emberfall").ShouldBeTrue();
    }

    [Fact]
    public void A_kill_list_is_copied_so_the_caller_cannot_change_it_afterwards()
    {
        var placements = new List<string> { ElitePlacement };
        var chapters = new List<string> { FirstChapter };

        var flags = new FeatureFlags(true, true, true, placements, chapters);

        placements.Add(LuckPlacement);
        chapters.Add(UnnamedChapter);

        flags.DisabledAdPlacements.ShouldBe(new[] { ElitePlacement }, StringComparer.Ordinal, ignoreOrder: true);
        flags.DisabledChapters.ShouldBe(new[] { FirstChapter }, StringComparer.Ordinal, ignoreOrder: true);
        flags.IsAdPlacementEnabled(LuckPlacement).ShouldBeTrue();
    }

    /// <summary>An <c>IReadOnlySet</c> that is actually a <c>HashSet</c> can be cast back and mutated.</summary>
    [Fact]
    public void A_kill_list_cannot_be_written_through_a_cast()
    {
        var flags = new FeatureFlags(true, true, true, [ElitePlacement], [FirstChapter]);

        Should.Throw<NotSupportedException>(() => ((ICollection<string>)flags.DisabledAdPlacements).Add("x"));
        Should.Throw<NotSupportedException>(() => ((ICollection<string>)flags.DisabledChapters).Clear());
    }

    /// <summary>A duplicate entry is the config saying the same thing twice, not an error.</summary>
    [Fact]
    public void A_repeated_identifier_is_the_same_kill_switch_named_twice()
    {
        var flags = new FeatureFlags(true, true, true, [ElitePlacement, ElitePlacement], []);

        flags.DisabledAdPlacements.ShouldBe(new[] { ElitePlacement }, Case.Sensitive);
        flags.IsAdPlacementEnabled(ElitePlacement).ShouldBeFalse();
    }

    /// <summary>A missing identifier is a caller bug, not "enabled".</summary>
    [Fact]
    public void A_null_identifier_throws_rather_than_reading_as_enabled()
    {
        var flags = new FeatureFlags(true, true, true, [], []);

        Should.Throw<ArgumentNullException>(() => flags.IsAdPlacementEnabled(null!))
            .ParamName.ShouldBe("adPlacementId");

        Should.Throw<ArgumentNullException>(() => flags.IsChapterEnabled(null!))
            .ParamName.ShouldBe("chapterId");
    }

    [Fact]
    public void FeatureFlags_refuses_a_null_kill_list()
    {
        Should.Throw<ArgumentNullException>(() => new FeatureFlags(true, true, true, null!, []))
            .ParamName.ShouldBe("disabledAdPlacements");

        Should.Throw<ArgumentNullException>(() => new FeatureFlags(true, true, true, [], null!))
            .ParamName.ShouldBe("disabledChapters");
    }

    /// <summary>An entry no lookup could ever match would make the switch look armed while killing nothing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FeatureFlags_refuses_a_blank_identifier_inside_a_kill_list(string? blank)
    {
        Should.Throw<ArgumentException>(() => new FeatureFlags(true, true, true, [ElitePlacement, blank!], []))
            .ParamName.ShouldBe("disabledAdPlacements");

        Should.Throw<ArgumentException>(() => new FeatureFlags(true, true, true, [], [blank!]))
            .ParamName.ShouldBe("disabledChapters");
    }
}
