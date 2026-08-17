using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// Key to display string, over the content set the game already loaded: which locale answers, and
/// what happens when none does.
/// </summary>
public sealed class BootStringCatalogueTests
{
    [Fact]
    public void Resolve_returns_the_English_string_for_a_key_the_English_locale_carries()
    {
        var catalogue = new BootStringCatalogue(BootContent.Complete(), BootContent.English);

        catalogue.Resolve(BootContent.TitleKey).ShouldBe(
            BootContent.EnglishTitle,
            "the whole point of a key is that something turns it into words. A catalogue that hands " +
            "back anything else here has not read the locale it was built over, and every string on " +
            "the first screen a player ever sees comes through this call.");
    }

    [Fact]
    public void Resolve_returns_the_German_string_rather_than_the_English_one_for_a_German_catalogue()
    {
        var german = new BootStringCatalogue(BootContent.Complete(), BootContent.German);
        var english = new BootStringCatalogue(BootContent.Complete(), BootContent.English);

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
        var catalogue = new BootStringCatalogue(BootContent.Complete(), BootContent.UnshippedLocale);

        catalogue.Resolve(BootContent.TitleKey).ShouldBe(
            BootContent.EnglishTitle,
            "the device reports whatever locale its owner set, and only two are authored. A handset " +
            "in French must get the English source, not a blank splash screen and not the key.");
    }

    [Fact]
    public void Resolve_returns_the_key_itself_for_a_key_no_locale_carries()
    {
        var catalogue = new BootStringCatalogue(
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
        var catalogue = new BootStringCatalogue(BootContent.Nothing(), BootContent.English);

        catalogue.Resolve(BootContent.TitleKey).ShouldBe(
            BootContent.TitleKey,
            "the catalogue is not the thing that decides an empty content set is fatal — the boot " +
            "presenter is, and it can only do that if this call answers rather than throws.");
    }

    [Fact]
    public void Constructing_over_a_content_set_with_no_locale_at_all_does_not_throw()
    {
        var construct = () => new BootStringCatalogue(BootContent.Nothing(), BootContent.English);

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
        var catalogue = new BootStringCatalogue(BootContent.Complete(), BootContent.German);

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
        var catalogue = new BootStringCatalogue(BootContent.Shipped, BootContent.English);

        catalogue.Resolve(key).ShouldNotBe(
            key,
            $"'{key}' resolved to itself against the real game-data, which is the fallback firing: " +
            "the shipped locales do not carry it. Every other case in this suite runs against an " +
            "in-memory fixture and would keep passing while the boot screen rendered dotted " +
            "identifiers on a handset — this is the one that notices.");
    }

    [Fact]
    public void Constructor_rejects_a_null_content_snapshot()
    {
        Should.Throw<ArgumentNullException>(() => new BootStringCatalogue(content: null!, localeTag: BootContent.English))
              .ParamName.ShouldBe(
                  "content",
                  "the snapshot is where every string comes from, and a null one fails at whichever " +
                  "lookup happens first rather than where the graph was wired wrong.");
    }

    [Fact]
    public void Constructor_rejects_a_null_locale_tag()
    {
        Should.Throw<ArgumentNullException>(() => new BootStringCatalogue(BootContent.Complete(), localeTag: null!))
              .ParamName.ShouldBe(
                  "localeTag",
                  "a null tag is not 'English' — it is a device query that did not happen. Silently " +
                  "reading it as the fallback would hide a platform layer that stopped answering.");
    }
}
