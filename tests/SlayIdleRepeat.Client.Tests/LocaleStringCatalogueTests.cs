using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// Key to display string, over the content set the game already loaded: which locale answers, and
/// what happens when none does.
/// </summary>
public sealed class LocaleStringCatalogueTests
{
    /// <summary>The document that names the boot screen's strings for the content invariants.</summary>
    private const string BootDocumentPath = "content/boot/boot.json";

    /// <summary>What a loc key looks like, so the document's prose members are not mistaken for one.</summary>
    private const string LocKeyPrefix = "loc.";

    /// <summary>
    /// 🔒 Slot by slot, the document says the same thing the screen does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing reads the document at runtime — the keys are constants in the presenter — so a slot
    /// pointed at the wrong key changes no behaviour and reddens no other case in this repository.
    /// The orphan invariant pins the key SET from below, since a key the document stops naming
    /// fails the content load; what it cannot see is two slots swapped, which leaves every key
    /// referenced and the document describing a screen that does not exist.
    /// </para>
    /// <para>
    /// Stated per slot rather than over the set for exactly that reason, and against
    /// <see cref="BootContent"/>'s constants rather than literals, because those are what the
    /// presenter cases resolve through — a key the screen stopped rendering is red there first.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("title", BootContent.TitleKey)]
    [InlineData("stageStatus/splash", BootContent.SplashStatusKey)]
    [InlineData("stageStatus/content", BootContent.ContentStatusKey)]
    [InlineData("stageStatus/profile", BootContent.ProfileStatusKey)]
    [InlineData("stageStatus/atlas", BootContent.AtlasStatusKey)]
    [InlineData("stageStatus/ready", BootContent.ReadyStatusKey)]
    [InlineData("failureStatus", BootContent.FailureStatusKey)]
    public void The_boot_document_points_each_slot_at_the_key_the_screen_renders_there(
        string slot, string key)
    {
        BootContent.Shipped.ReadText($"{BootDocumentPath}#/{slot}").ShouldBe(
            key,
            $"'{slot}' is the document's claim about what the boot screen shows there, and it is " +
            "authored data no code path loads — so the claim is worth nothing unless something " +
            "holds it against the screen. Two slots swapped leaves every key referenced, the " +
            "content load green, and the document describing a screen nobody built.");
    }

    /// <summary>
    /// 🔒 And the document names no key beyond those — the direction the orphan invariant cannot see.
    /// </summary>
    /// <remarks>
    /// A key added to both locales and named here but rendered nowhere loads clean, validates
    /// clean, and buys a translation nobody will ever read. The theory above pins seven slots; this
    /// is what stops an eighth appearing beside them.
    /// </remarks>
    [Fact]
    public void The_boot_document_names_no_key_the_boot_screen_does_not_render()
    {
        LocKeysNamedByTheBootDocument().ShouldBe(
            BootContent.AllKeys.Order(StringComparer.Ordinal).ToArray(),
            "the document is the only statement of which loc keys the boot screen is about. Held " +
            "against the keys the presenter actually resolves it stops being a claim and starts " +
            "being a fact — which is what makes it worth keeping in game-data at all.");
    }

    [Fact]
    public void Resolve_returns_the_English_string_for_a_key_the_English_locale_carries()
    {
        var catalogue = new LocaleStringCatalogue(BootContent.Complete(), BootContent.English);

        catalogue.Resolve(BootContent.TitleKey).ShouldBe(
            BootContent.EnglishTitle,
            "the whole point of a key is that something turns it into words. A catalogue that hands " +
            "back anything else here has not read the locale it was built over, and every string on " +
            "the first screen a player ever sees comes through this call.");
    }

    [Fact]
    public void Resolve_returns_the_German_string_rather_than_the_English_one_for_a_German_catalogue()
    {
        var german = new LocaleStringCatalogue(BootContent.Complete(), BootContent.German);
        var english = new LocaleStringCatalogue(BootContent.Complete(), BootContent.English);

        german.Resolve(BootContent.TitleKey).ShouldBe(
            BootContent.GermanValueOf(BootContent.TitleKey),
            "a German device must be answered out of the German locale document. Stated as the DE " +
            "value the content set carries, not as any particular wording: every DE string shipped " +
            "today is an untranslated placeholder, and asserting its text would be asserting that a " +
            "translation exists when none does.");
        german.Resolve(BootContent.TitleKey).ShouldNotBe(
            english.Resolve(BootContent.TitleKey),
            "compared against the English answer rather than a literal, because the failure this " +
            "catches is a catalogue that reads loc/en.json whatever tag it was handed — which looks " +
            "correct in every English test run and ships a German build in English.");
    }

    [Fact]
    public void Resolve_falls_back_to_English_for_a_locale_the_content_set_does_not_carry()
    {
        var catalogue = new LocaleStringCatalogue(BootContent.Complete(), BootContent.UnshippedLocale);

        catalogue.Resolve(BootContent.TitleKey).ShouldBe(
            BootContent.EnglishTitle,
            "the device reports whatever locale its owner set, and only two are authored. A handset " +
            "in French must get the English source, not a blank splash screen and not the key.");
    }

    [Fact]
    public void Resolve_returns_the_key_itself_for_a_key_no_locale_carries()
    {
        var catalogue = new LocaleStringCatalogue(
            BootContent.Missing(BootContent.ReadyStatusKey), BootContent.English);

        catalogue.Resolve(BootContent.ReadyStatusKey).ShouldBe(
            BootContent.ReadyStatusKey,
            "a missing string has to be a VISIBLE defect. Empty text renders as a gap nobody reports " +
            "and nobody can search for; the key renders as 'loc.boot.ready.status' on the screen, " +
            "which is ugly, unmistakable and greppable in one step.");
    }

    [Fact]
    public void Resolve_returns_the_key_itself_when_the_content_set_carries_no_locale_at_all()
    {
        var catalogue = new LocaleStringCatalogue(BootContent.Nothing(), BootContent.English);

        catalogue.Resolve(BootContent.TitleKey).ShouldBe(
            BootContent.TitleKey,
            "the catalogue is not the thing that decides an empty content set is fatal — the boot " +
            "presenter is, and it can only do that if this call answers rather than throws.");
    }

    [Fact]
    public void Constructing_over_a_content_set_with_no_locale_at_all_does_not_throw()
    {
        var construct = () => new LocaleStringCatalogue(BootContent.Nothing(), BootContent.English);

        Should.NotThrow(
            construct,
            "an unusable content set is a boot FAILURE with a named kind and a stage, reported on a " +
            "screen. A catalogue that threw in the composition root instead would take the exception " +
            "out through the engine's node callback, where nothing catches it and the player sees a " +
            "window that never draws.");
    }

    [Fact]
    public void LocaleTag_reports_the_locale_the_catalogue_was_built_for()
    {
        var catalogue = new LocaleStringCatalogue(BootContent.Complete(), BootContent.German);

        catalogue.LocaleTag.ShouldBe(
            BootContent.German,
            "the tag is what a bug report needs in order to say which locale a wrong string came " +
            "from. A catalogue that hard-coded 'en' here would describe an English screen whichever " +
            "locale it was actually answering out of.");
    }

    [Theory]
    [InlineData(BootContent.TitleKey)]
    [InlineData(BootContent.SplashStatusKey)]
    [InlineData(BootContent.ContentStatusKey)]
    [InlineData(BootContent.ProfileStatusKey)]
    [InlineData(BootContent.AtlasStatusKey)]
    [InlineData(BootContent.ReadyStatusKey)]
    [InlineData(BootContent.FailureStatusKey)]
    public void Resolve_finds_every_boot_string_in_the_checkouts_own_content(string key)
    {
        var catalogue = new LocaleStringCatalogue(BootContent.Shipped, BootContent.English);

        catalogue.Resolve(key).ShouldNotBeNullOrWhiteSpace(
            $"'{key}' resolved to nothing at all against the real game-data. Blank is the one answer " +
            "worse than the key itself: it renders as a gap that reads like a design choice, and it " +
            "would satisfy the 'not the key' assertion below on its own.");
        catalogue.Resolve(key).ShouldNotBe(
            key,
            $"'{key}' resolved to itself against the real game-data, which is the fallback firing: " +
            "the shipped locales do not carry it. Every other case in this suite runs against an " +
            "in-memory fixture and would keep passing while the boot screen rendered dotted " +
            "identifiers on a handset — this is the one that notices.");
    }

    /// <summary>Every loc key the shipped boot document names, wherever in it they sit.</summary>
    private static IReadOnlyList<string> LocKeysNamedByTheBootDocument()
    {
        BootContent.Shipped.TryGetDocument(BootDocumentPath, out var document).ShouldBeTrue(
            $"'{BootDocumentPath}' is not in the shipped content set, so this case would compare the " +
            "screen's keys against nothing at all and pass hardest on the checkout that deleted the " +
            "document.");

        var keys = new List<string>();
        CollectLocKeys(document!.Root, keys);

        return keys.Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Walks a document collecting loc keys, so a restructured document still reads.</summary>
    private static void CollectLocKeys(ContentValue value, List<string> keys)
    {
        if (value.Kind == ContentValueKind.Text)
        {
            var text = value.AsText();

            if (text.StartsWith(LocKeyPrefix, StringComparison.Ordinal))
            {
                keys.Add(text);
            }

            return;
        }

        if (value.Kind != ContentValueKind.Object)
        {
            return;
        }

        foreach (var name in value.MemberNames)
        {
            if (value.TryGetMember(name, out var member))
            {
                CollectLocKeys(member!, keys);
            }
        }
    }

    [Fact]
    public void Constructor_rejects_a_null_content_snapshot()
    {
        Should.Throw<ArgumentNullException>(() => new LocaleStringCatalogue(content: null!, localeTag: BootContent.English))
              .ParamName.ShouldBe(
                  "content",
                  "the snapshot is where every string comes from, and a null one fails at whichever " +
                  "lookup happens first rather than where the graph was wired wrong.");
    }

    [Fact]
    public void Constructor_rejects_a_null_locale_tag()
    {
        Should.Throw<ArgumentNullException>(() => new LocaleStringCatalogue(BootContent.Complete(), localeTag: null!))
              .ParamName.ShouldBe(
                  "localeTag",
                  "a null tag is not 'English' — it is a device query that did not happen. Silently " +
                  "reading it as the fallback would hide a platform layer that stopped answering.");
    }
}
