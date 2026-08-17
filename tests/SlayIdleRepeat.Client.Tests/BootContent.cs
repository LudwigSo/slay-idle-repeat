using SlayIdleRepeat.Adapters.Content.LocalFile;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The boot screen's string keys, and the content snapshots the boot cases resolve them against.
/// </summary>
/// <remarks>
/// <para>
/// The keys are stated once here and pinned against the checkout's own <c>game-data</c> by
/// <c>LocaleStringCatalogueTests</c>, so a key the presenter resolves but the shipped locales do
/// not carry is caught here rather than as a screen full of dotted identifiers on a handset.
/// </para>
/// <para>
/// Snapshots are built in memory rather than loaded, so a case can state exactly which strings
/// exist — a locale file that is missing one key is a different fact from a content set with no
/// locale at all, and the two must produce different behaviour.
/// </para>
/// </remarks>
internal static class BootContent
{
    internal const string TitleKey = "loc.boot.title.name";
    internal const string SplashStatusKey = "loc.boot.splash.status";
    internal const string ContentStatusKey = "loc.boot.content.status";
    internal const string ProfileStatusKey = "loc.boot.profile.status";
    internal const string AtlasStatusKey = "loc.boot.atlas.status";
    internal const string ReadyStatusKey = "loc.boot.ready.status";
    internal const string FailureStatusKey = "loc.boot.failure.status";

    internal const string English = "en";
    internal const string German = "de";

    /// <summary>A locale tag no locale file in this repository is authored for.</summary>
    internal const string UnshippedLocale = "fr";

    /// <summary>Fixture values, deliberately unlike the shipped copy so a case cannot pass on it.</summary>
    internal const string EnglishTitle = "FIXTURE title";
    internal const string EnglishSplashStatus = "FIXTURE splash";
    internal const string EnglishContentStatus = "FIXTURE content";
    internal const string EnglishProfileStatus = "FIXTURE profile";
    internal const string EnglishAtlasStatus = "FIXTURE atlas";
    internal const string EnglishReadyStatus = "FIXTURE ready";
    internal const string EnglishFailureStatus = "FIXTURE failure";

    /// <summary>The German fixture prefix — a marker, not a translation.</summary>
    internal const string GermanPrefix = "FIXTURE-DE ";

    /// <summary>Every key the boot screen renders, in stage order, with the title first.</summary>
    internal static IReadOnlyList<string> AllKeys { get; } =
    [
        TitleKey, SplashStatusKey, ContentStatusKey, ProfileStatusKey,
        AtlasStatusKey, ReadyStatusKey, FailureStatusKey,
    ];

    private static readonly Lazy<ContentSnapshot> LazyShipped = new(LoadShipped);

    private static readonly ContentVersion FixtureStamp =
        ContentVersion.FromHex(new string('a', ContentVersion.HexLength));

    /// <summary>The checkout's own <c>game-data</c>, loaded once for the whole suite.</summary>
    internal static ContentSnapshot Shipped => LazyShipped.Value;

    /// <summary>A content set that holds nothing at all — the shape a failed content load leaves.</summary>
    internal static ContentSnapshot Nothing() => new(FixtureStamp, []);

    /// <summary>A content set carrying every boot string, in English and German.</summary>
    internal static ContentSnapshot Complete() => Carrying(AllKeys);

    /// <summary>A content set carrying every boot string except the named one.</summary>
    internal static ContentSnapshot Missing(string key) =>
        Carrying(AllKeys.Where(k => !string.Equals(k, key, StringComparison.Ordinal)).ToArray());

    /// <summary>The English fixture value for a key.</summary>
    internal static string EnglishValueOf(string key) => key switch
    {
        TitleKey => EnglishTitle,
        SplashStatusKey => EnglishSplashStatus,
        ContentStatusKey => EnglishContentStatus,
        ProfileStatusKey => EnglishProfileStatus,
        AtlasStatusKey => EnglishAtlasStatus,
        ReadyStatusKey => EnglishReadyStatus,
        FailureStatusKey => EnglishFailureStatus,
        _ => throw new ArgumentOutOfRangeException(
            nameof(key), key, "not a boot string. Add it to BootContent rather than to a case."),
    };

    /// <summary>The German fixture value for a key.</summary>
    internal static string GermanValueOf(string key) => GermanPrefix + EnglishValueOf(key);

    private static ContentSnapshot Carrying(IReadOnlyList<string> keys) =>
        new(FixtureStamp,
        [
            Locale("loc/en.json", English, keys, EnglishValueOf),
            Locale("loc/de.json", German, keys, GermanValueOf),
        ]);

    private static ContentDocument Locale(
        string path, string localeTag, IReadOnlyList<string> keys, Func<string, string> valueOf) =>
        new(path, ContentValue.Object(
        [
            new KeyValuePair<string, ContentValue>("_locale", ContentValue.Text(localeTag)),
            new KeyValuePair<string, ContentValue>("strings", ContentValue.Object(
                keys.Select(k => new KeyValuePair<string, ContentValue>(k, ContentValue.Text(valueOf(k)))))),
        ]));

    private static ContentSnapshot LoadShipped() =>
        ContentLoader.Load(
            new LocalFileContentSource(RepoPaths.ContentDataRoot),
            ContentLoadOptions.Canonical).Require();
}
