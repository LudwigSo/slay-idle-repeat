using System.Reflection;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// 🔒 `14` §14 — <em>"Kill switches: remote config flags for PvP, each ad placement, the Plus offer,
/// and each chapter — so a bad content change is a config edit, not a client patch."</em> That
/// sentence is the entire authored content of this record.
/// </summary>
/// <remarks>
/// 🔒 The record is deliberately <b>closed</b>: four switches and nothing else, with no general
/// string-keyed bag — a bag would let any later task introduce an ungoverned flag without a decision,
/// which is what an enumerated kill-switch list exists to prevent. M5-10 is the task that extends it.
/// <para>
/// ⚠️ No flag-key naming scheme is authored anywhere, and no <c>AdPlacementId</c> or <c>ChapterId</c>
/// type exists yet, so the two set-valued switches hold plain strings compared <b>ordinally</b>. The
/// identifiers used in these tests are ones the design set already writes down, not a scheme invented
/// here.
/// </para>
/// </remarks>
public sealed class FeatureFlagsTests
{
    private const string ElitePlacement = "AD_ELITE_GUARANTEE";
    private const string LuckPlacement = "AD_ENHANCE_LUCK";
    private const string FirstChapter = "CH_01_EMBERFALL";

    /// <summary>
    /// A chapter identifier no kill switch names. Deliberately not a plausible one: `game-data`
    /// writes <c>CH_01_EMBERFALL</c> and then an ellipsis, so any second chapter id spelled here
    /// would be a naming scheme invented by this test — which is M5-10's to decide (steering S6).
    /// </summary>
    private const string UnnamedChapter = "A_CHAPTER_NO_KILL_SWITCH_NAMES";

    /// <summary>
    /// 🔒 `14` §14 — the whole public surface, pinned. Four switches and the two membership
    /// readers, and no fifth flag arrives without this test going red and forcing the decision.
    /// </summary>
    [Fact]
    public void FeatureFlags_is_closed_at_the_four_kill_switches_of_14_section_14()
    {
        // Static as well as instance: a `public static FeatureFlags AllEnabled` convenience is both
        // a fifth member and a pre-decided remote-config fallback, and an instance-only filter
        // would let it in silently. Whether an unresolved config fails open or closed is M5-10's
        // decision to make explicitly, not this record's to imply.
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

    /// <summary>
    /// 🔒 …and the surface is pinned by <b>type</b>, not only by name. A fifth flag smuggled in by
    /// widening an existing member — <c>DisabledChapters</c> becoming an
    /// <c>IReadOnlyList&lt;string&gt;</c>, <c>PvpEnabled</c> becoming a <c>bool?</c> with a third
    /// "unresolved" state — changes the contract while leaving a name-only pin green.
    /// </summary>
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

    /// <summary>
    /// 🔒 `14` §14 — no general string-keyed bag, and no indexer. A <c>bool this[string key]</c> or
    /// an <c>IReadOnlyDictionary&lt;string, bool&gt; Extra</c> would reopen the closed list through
    /// the back door while leaving the test above green.
    /// </summary>
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

    /// <summary>`14` §14 — the two boolean switches are stored as given.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void The_PvP_and_Plus_offer_switches_are_stored_as_given(bool pvp, bool plusOffer)
    {
        var flags = new FeatureFlags(pvp, plusOffer, [], []);

        flags.PvpEnabled.ShouldBe(pvp);
        flags.PlusOfferEnabled.ShouldBe(plusOffer);
    }

    /// <summary>
    /// 🔒 The set-valued switches are <b>kill lists</b>, not allow lists: an identifier the config
    /// has never heard of is <em>enabled</em>. An allow list would need every chapter and all 29
    /// placements (`12` §4) enumerated in remote config, and the day one was missing the game would
    /// silently lose a chapter — the opposite of a kill switch, which exists to be the exception.
    /// </summary>
    [Fact]
    public void An_identifier_no_kill_switch_names_is_enabled()
    {
        var flags = new FeatureFlags(true, true, [], []);

        flags.IsAdPlacementEnabled(ElitePlacement).ShouldBeTrue();
        flags.IsChapterEnabled(FirstChapter).ShouldBeTrue();
    }

    /// <summary>`14` §14 — a named identifier is killed, and only that one.</summary>
    [Fact]
    public void A_named_identifier_is_disabled_and_its_neighbours_are_not()
    {
        var flags = new FeatureFlags(true, true, [ElitePlacement], [FirstChapter]);

        flags.IsAdPlacementEnabled(ElitePlacement).ShouldBeFalse();
        flags.IsAdPlacementEnabled(LuckPlacement).ShouldBeTrue();
        flags.IsChapterEnabled(FirstChapter).ShouldBeFalse();
        flags.IsChapterEnabled(UnnamedChapter).ShouldBeTrue();
    }

    /// <summary>
    /// 🔒 Membership is <b>ordinal</b>. No flag-key naming scheme is authored anywhere (that is
    /// M5-10's), so the one thing this type can promise is that two spellings are two identifiers:
    /// a case-insensitive comparison would silently kill a placement whose id merely resembled the
    /// one operations typed.
    /// </summary>
    [Fact]
    public void Membership_is_ordinal_so_a_respelling_is_not_a_silent_hit()
    {
        var flags = new FeatureFlags(true, true, [ElitePlacement], [FirstChapter]);

        flags.IsAdPlacementEnabled("ad_elite_guarantee").ShouldBeTrue();
        flags.IsChapterEnabled("ch_01_emberfall").ShouldBeTrue();
    }

    /// <summary>
    /// 🔒 The record copies the collections it is handed. The composition root resolves remote
    /// config once per command; a caller that kept its list and mutated it afterwards would be
    /// changing a kill switch out from under a rule that had already read it.
    /// </summary>
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
    /// 🔒 And the exposed set cannot be written through a cast. An
    /// <c>IReadOnlySet&lt;string&gt;</c> that <em>is</em> a <c>HashSet&lt;string&gt;</c> can be cast
    /// back and added to — the same trap <c>ContentSnapshot.DocumentPaths</c> documents.
    /// </summary>
    [Fact]
    public void A_kill_list_cannot_be_written_through_a_cast()
    {
        var flags = new FeatureFlags(true, true, [ElitePlacement], [FirstChapter]);

        Should.Throw<NotSupportedException>(() => ((ICollection<string>)flags.DisabledAdPlacements).Add("x"));
        Should.Throw<NotSupportedException>(() => ((ICollection<string>)flags.DisabledChapters).Clear());
    }

    /// <summary>
    /// A duplicate entry is the config saying the same thing twice, not an error. Pinned so the
    /// answer is a decision rather than whatever the chosen set type happens to do.
    /// </summary>
    [Fact]
    public void A_repeated_identifier_is_the_same_kill_switch_named_twice()
    {
        var flags = new FeatureFlags(true, true, [ElitePlacement, ElitePlacement], []);

        flags.DisabledAdPlacements.ShouldBe(new[] { ElitePlacement }, Case.Sensitive);
        flags.IsAdPlacementEnabled(ElitePlacement).ShouldBeFalse();
    }

    /// <summary>A missing identifier is a caller bug, not "enabled". Fail loudly (steering S6).</summary>
    [Fact]
    public void A_null_identifier_throws_rather_than_reading_as_enabled()
    {
        var flags = new FeatureFlags(true, true, [], []);

        Should.Throw<ArgumentNullException>(() => flags.IsAdPlacementEnabled(null!))
            .ParamName.ShouldBe("adPlacementId");

        Should.Throw<ArgumentNullException>(() => flags.IsChapterEnabled(null!))
            .ParamName.ShouldBe("chapterId");
    }

    /// <summary>`30` §3 — flags are resolved at the composition root; an unresolved list is not a value.</summary>
    [Fact]
    public void FeatureFlags_refuses_a_null_kill_list()
    {
        Should.Throw<ArgumentNullException>(() => new FeatureFlags(true, true, null!, []))
            .ParamName.ShouldBe("disabledAdPlacements");

        Should.Throw<ArgumentNullException>(() => new FeatureFlags(true, true, [], null!))
            .ParamName.ShouldBe("disabledChapters");
    }

    /// <summary>
    /// 🔒 …and a null or blank identifier <em>inside</em> a kill list. A remote-config document with
    /// an empty array entry is a realistic source, and both <c>HashSet</c> and <c>FrozenSet</c>
    /// accept one silently — after which the set holds an identifier no lookup can ever match, so
    /// the switch reads as thrown while killing nothing. Blank is the strongest promise available
    /// while the naming scheme is unauthored: it is scheme-independent. Refusing at construction is
    /// the loud failure S6 asks for.
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

    /// <summary>`30` §3 — sealed and read-only, like everything else on the context.</summary>
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
