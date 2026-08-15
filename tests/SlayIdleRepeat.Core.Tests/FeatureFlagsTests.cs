using System.Reflection;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// <c>FeatureFlags</c> is a closed record of exactly four remote-config kill switches (PvP, ad
/// placements, the Plus offer, chapters) with no general string-keyed bag, so a new flag is a diff
/// someone has to justify rather than an ungoverned addition.
/// </summary>
public sealed class FeatureFlagsTests
{
    private const string ElitePlacement = "AD_ELITE_GUARANTEE";
    private const string LuckPlacement = "AD_ENHANCE_LUCK";
    private const string FirstChapter = "CH_01_EMBERFALL";

    private const string UnnamedChapter = "A_CHAPTER_NO_KILL_SWITCH_NAMES";

    [Fact]
    public void FeatureFlags_is_closed_at_the_four_kill_switches_of_14_section_14()
    {
        var declared = typeof(FeatureFlags)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal);

        declared.ShouldBe(
            new[]
            {
                ".ctor",
                "DisabledAdPlacements",
                "DisabledChapters",
                "IsAdPlacementEnabled",
                "IsChapterEnabled",
                "PlusOfferEnabled",
                "PvpEnabled",
                "get_DisabledAdPlacements",
                "get_DisabledChapters",
                "get_PlusOfferEnabled",
                "get_PvpEnabled",
            },
            Case.Sensitive,
            "14 §14 names four kill switches: PvP, each ad placement, the Plus offer, each chapter. " +
            "This record is closed at those four on purpose — M5-10 is the task that extends it, and " +
            "a flag added here without a decision is the ungoverned config edit the list exists to " +
            "prevent. If a fifth switch is genuinely authorised, add it here with the doc that " +
            "authorises it.");
    }

    /// <summary>Pinned by type too, not only by name — a member widened in place would pass a name-only pin.</summary>
    [Fact]
    public void The_four_kill_switches_keep_their_declared_types()
    {
        typeof(FeatureFlags)
            .GetConstructors()
            .ShouldHaveSingleItem()
            .GetParameters()
            .Select(p => $"{p.Name}:{p.ParameterType.FullName}")
            .ShouldBe(new[]
            {
                $"pvpEnabled:{typeof(bool).FullName}",
                $"plusOfferEnabled:{typeof(bool).FullName}",
                $"disabledAdPlacements:{typeof(IEnumerable<string>).FullName}",
                $"disabledChapters:{typeof(IEnumerable<string>).FullName}",
            },
            Case.Sensitive);

        typeof(FeatureFlags).GetProperty(nameof(FeatureFlags.PvpEnabled))!.PropertyType.ShouldBe(typeof(bool));
        typeof(FeatureFlags).GetProperty(nameof(FeatureFlags.PlusOfferEnabled))!.PropertyType.ShouldBe(typeof(bool));
        typeof(FeatureFlags).GetProperty(nameof(FeatureFlags.DisabledAdPlacements))!.PropertyType
            .ShouldBe(typeof(IReadOnlySet<string>));
        typeof(FeatureFlags).GetProperty(nameof(FeatureFlags.DisabledChapters))!.PropertyType
            .ShouldBe(typeof(IReadOnlySet<string>));
    }

    [Fact]
    public void FeatureFlags_offers_no_general_purpose_flag_bag()
    {
        typeof(FeatureFlags)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length > 0 || IsKeyedCollection(p.PropertyType))
            .Select(p => $"{p.Name}:{p.PropertyType.Name}")
            .ShouldBeEmpty(
                "a string-keyed bag lets any later task introduce a flag with no decision behind " +
                "it. 14 §14's list is enumerated precisely so a new kill switch is a diff someone " +
                "has to justify.");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void The_PvP_and_Plus_offer_switches_are_stored_as_given(bool pvp, bool plusOffer)
    {
        var flags = new FeatureFlags(pvp, plusOffer, [], []);

        flags.PvpEnabled.ShouldBe(pvp);
        flags.PlusOfferEnabled.ShouldBe(plusOffer);
    }

    /// <summary>The set-valued switches are kill lists, not allow lists: an unnamed identifier is enabled.</summary>
    [Fact]
    public void An_identifier_no_kill_switch_names_is_enabled()
    {
        var flags = new FeatureFlags(true, true, [], []);

        flags.IsAdPlacementEnabled(ElitePlacement).ShouldBeTrue();
        flags.IsChapterEnabled(FirstChapter).ShouldBeTrue();
    }

    [Fact]
    public void A_named_identifier_is_disabled_and_its_neighbours_are_not()
    {
        var flags = new FeatureFlags(true, true, [ElitePlacement], [FirstChapter]);

        flags.IsAdPlacementEnabled(ElitePlacement).ShouldBeFalse();
        flags.IsAdPlacementEnabled(LuckPlacement).ShouldBeTrue();
        flags.IsChapterEnabled(FirstChapter).ShouldBeFalse();
        flags.IsChapterEnabled(UnnamedChapter).ShouldBeTrue();
    }

    /// <summary>Membership is ordinal: a case-insensitive comparison could silently kill an unintended near-match.</summary>
    [Fact]
    public void Membership_is_ordinal_so_a_respelling_is_not_a_silent_hit()
    {
        var flags = new FeatureFlags(true, true, [ElitePlacement], [FirstChapter]);

        flags.IsAdPlacementEnabled("ad_elite_guarantee").ShouldBeTrue();
        flags.IsChapterEnabled("ch_01_emberfall").ShouldBeTrue();
    }

    /// <summary>The record copies the collections it is handed, so a caller mutating its list afterward has no effect.</summary>
    [Fact]
    public void A_kill_list_is_copied_so_the_caller_cannot_change_it_afterwards()
    {
        var placements = new List<string> { ElitePlacement };
        var chapters = new List<string> { FirstChapter };

        var flags = new FeatureFlags(true, true, placements, chapters);

        placements.Add(LuckPlacement);
        chapters.Add(UnnamedChapter);

        flags.DisabledAdPlacements.ShouldBe(new[] { ElitePlacement }, StringComparer.Ordinal, ignoreOrder: true);
        flags.DisabledChapters.ShouldBe(new[] { FirstChapter }, StringComparer.Ordinal, ignoreOrder: true);
        flags.IsAdPlacementEnabled(LuckPlacement).ShouldBeTrue();
    }

    /// <summary>
    /// The exposed set cannot be written through a cast — an <c>IReadOnlySet&lt;string&gt;</c> that
    /// is actually a <c>HashSet&lt;string&gt;</c> can otherwise be cast back and mutated.
    /// </summary>
    [Fact]
    public void A_kill_list_cannot_be_written_through_a_cast()
    {
        var flags = new FeatureFlags(true, true, [ElitePlacement], [FirstChapter]);

        Should.Throw<NotSupportedException>(() => ((ICollection<string>)flags.DisabledAdPlacements).Add("x"));
        Should.Throw<NotSupportedException>(() => ((ICollection<string>)flags.DisabledChapters).Clear());
    }

    /// <summary>A duplicate entry is the config saying the same thing twice, not an error.</summary>
    [Fact]
    public void A_repeated_identifier_is_the_same_kill_switch_named_twice()
    {
        var flags = new FeatureFlags(true, true, [ElitePlacement, ElitePlacement], []);

        flags.DisabledAdPlacements.ShouldBe(new[] { ElitePlacement }, Case.Sensitive);
        flags.IsAdPlacementEnabled(ElitePlacement).ShouldBeFalse();
    }

    /// <summary>A missing identifier is a caller bug, not "enabled".</summary>
    [Fact]
    public void A_null_identifier_throws_rather_than_reading_as_enabled()
    {
        var flags = new FeatureFlags(true, true, [], []);

        Should.Throw<ArgumentNullException>(() => flags.IsAdPlacementEnabled(null!))
            .ParamName.ShouldBe("adPlacementId");

        Should.Throw<ArgumentNullException>(() => flags.IsChapterEnabled(null!))
            .ParamName.ShouldBe("chapterId");
    }

    [Fact]
    public void FeatureFlags_refuses_a_null_kill_list()
    {
        Should.Throw<ArgumentNullException>(() => new FeatureFlags(true, true, null!, []))
            .ParamName.ShouldBe("disabledAdPlacements");

        Should.Throw<ArgumentNullException>(() => new FeatureFlags(true, true, [], null!))
            .ParamName.ShouldBe("disabledChapters");
    }

    /// <summary>
    /// A blank identifier inside a kill list is refused at construction rather than accepted silently
    /// — an entry no lookup could ever match would make the switch look armed while killing nothing.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FeatureFlags_refuses_a_blank_identifier_inside_a_kill_list(string? blank)
    {
        Should.Throw<ArgumentException>(() => new FeatureFlags(true, true, [ElitePlacement, blank!], []))
            .ParamName.ShouldBe("disabledAdPlacements");

        Should.Throw<ArgumentException>(() => new FeatureFlags(true, true, [], [blank!]))
            .ParamName.ShouldBe("disabledChapters");
    }

    [Fact]
    public void FeatureFlags_is_sealed_and_read_only()
    {
        typeof(FeatureFlags).IsSealed.ShouldBeTrue();

        typeof(FeatureFlags)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is not null)
            .Select(p => p.Name)
            .ShouldBeEmpty();

        typeof(FeatureFlags)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Select(f => f.Name)
            .ShouldBeEmpty(
                "a hand-rolled public field is the only way a switch here could be writable while " +
                "the property check above stayed green. A tripwire, not noise.");
    }

    /// <summary>True for a dictionary-shaped property — the bag shape the rule above forbids.</summary>
    private static bool IsKeyedCollection(Type type) =>
        type.IsGenericType &&
        (type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>) ||
         type.GetGenericTypeDefinition() == typeof(IDictionary<,>) ||
         type.GetGenericTypeDefinition() == typeof(Dictionary<,>));
}
