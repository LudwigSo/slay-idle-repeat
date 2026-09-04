using System.Globalization;
using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The outcome captions and the reward tables: one caption per authored outcome token, and no
/// caption naming a token no table authors.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Both directions, because each catches a different mistake.</b> A token with no caption
/// renders on the results panel as its own dotted identifier — the player is told they won
/// <c>WIN_2_1</c>. A caption with no token is a string somebody translates twice and nothing ever
/// shows, and it survives every other test in this suite because an unused key that the document
/// still names is not an orphan.
/// </para>
/// <para>
/// 🔒 <b>The caption key is DERIVED from the authored token</b> — lower-cased into
/// <c>loc.minigame.&lt;token&gt;.outcome</c> — rather than mapped by hand in C#. That is what makes a
/// fourteenth reward row a content edit rather than a code edit, and it is what these two directions
/// are really pinning: the derivation, from both ends.
/// </para>
/// </remarks>
public sealed class MinigameOutcomeKeyAgreementTests
{
    private const string CurrenciesDocument = "tuning/currencies.json";

    private const string MinigameDocument = "content/minigame/minigame.json";

    private const string OutcomeGroup = MinigameDocument + "#/outcome";

    private const string EnglishStrings = "loc/en.json#/strings/";

    private const string GermanStrings = "loc/de.json#/strings/";

    private const string KeyPrefix = "loc.minigame.";

    private const string KeySuffix = ".outcome";

    /// <summary>The loaded shipped set, read once for the whole class rather than per assertion.</summary>
    private static readonly Lazy<ContentSnapshot> LazyShipped =
        new(() => ContentLoader.Load(RepoData.Source()).Require());

    private static ContentSnapshot Shipped => LazyShipped.Value;

    /// <summary>
    /// How many outcome tokens the four reward tables author between them — 3 + 4 + 3 + 3.
    /// </summary>
    /// <remarks>
    /// 🔴 The floor. Both sweeps below are loops over a document, which is the shape that reports a
    /// clean agreement while measuring nothing: an empty reward table satisfies "every token has a
    /// caption" perfectly. A fourteenth row is exactly the event these cases exist to be read on, so
    /// it must arrive as a failure saying to caption the new outcome.
    /// </remarks>
    private const int ShippedOutcomeTokenCount = 13;

    /// <summary>
    /// 🔒 Every authored outcome token has a caption, in the document and in both locales.
    /// </summary>
    [Fact]
    public void Every_authored_outcome_token_is_captioned()
    {
        var tokens = AuthoredTokens();

        tokens.Count.ShouldBe(
            ShippedOutcomeTokenCount,
            "the four reward tables author " + tokens.Count + " outcome tokens between them. The " +
            "count is pinned rather than merely non-zero — a sweep over an empty table reports a " +
            "clean agreement, and a new row is the one event this case exists to be read on.");

        var snapshot = Shipped;
        var named = NamedCaptionKeys();

        foreach (var token in tokens)
        {
            var key = KeyFor(token);

            named.ShouldContain(
                key,
                "'" + token + "' is an outcome the rules layer can pay and " + MinigameDocument +
                " names no caption for it. The results panel would show the player the token " +
                "itself — they would be told they won '" + token + "'.");
            snapshot.ReadText(EnglishStrings + key).ShouldNotBeNullOrWhiteSpace(
                "'" + key + "' is named by the document but carried by no English locale row, so it " +
                "renders as its own key.");
            snapshot.ReadText(GermanStrings + key).ShouldNotBe(
                snapshot.ReadText(EnglishStrings + key),
                "'" + key + "' is the English value copied into the German file. Every new string " +
                "ships behind the untranslated-placeholder prefix, which is what makes it different.");
        }
    }

    /// <summary>
    /// 🔒 …and no caption names an outcome the reward tables do not author.
    /// </summary>
    /// <remarks>
    /// 🔴 The other direction, and the one nothing else catches: a caption for a token that no table
    /// pays is a string translated into every locale and rendered nowhere, and it stays invisible
    /// because a key the document names is not an orphan. It is also how a retuning that DROPS a row
    /// leaves its caption behind.
    /// </remarks>
    [Fact]
    public void No_outcome_caption_names_a_token_no_reward_table_authors()
    {
        var named = NamedCaptionKeys();

        named.Count.ShouldBe(
            ShippedOutcomeTokenCount,
            "the document names " + named.Count + " outcome captions and the reward tables author " +
            ShippedOutcomeTokenCount + " tokens. With no captions named this sweep has nothing to " +
            "check and would report a clean agreement.");

        var expected = AuthoredTokens().Select(KeyFor).ToHashSet(StringComparer.Ordinal);
        var stranded = named.Where(key => !expected.Contains(key)).ToArray();

        stranded.ShouldBeEmpty(
            "these captions name outcomes no reward table pays, so nothing can ever render them — " +
            "and, because the document still names them, they are not orphans and no other rule " +
            $"notices. A dropped reward row leaves exactly this behind: [{string.Join(", ", stranded)}]");
    }

    /// <summary>The caption key one authored token derives, the one way it may be derived.</summary>
    private static string KeyFor(string token) =>
        KeyPrefix + token.ToLower(CultureInfo.InvariantCulture) + KeySuffix;

    /// <summary>Every outcome token the four reward tables author, in document order.</summary>
    private static IReadOnlyList<string> AuthoredTokens()
    {
        using var document = JsonDocument.Parse(RepoData.Documents[CurrenciesDocument]);

        var tokens = new List<string>();

        foreach (var table in document.RootElement.GetProperty("minigameRewards").EnumerateObject())
        {
            if (table.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var row in table.Value.EnumerateArray())
            {
                tokens.Add(row.GetProperty("outcome").GetString()!);
            }
        }

        return tokens;
    }

    /// <summary>Every caption key the minigame document's <c>outcome</c> group names.</summary>
    private static IReadOnlyCollection<string> NamedCaptionKeys()
    {
        RepoData.Documents.Keys.ShouldContain(
            MinigameDocument,
            "with no document there is no outcome group to read, and both sweeps in this suite would " +
            "compare two empty lists and call them agreed.");

        var snapshot = Shipped;
        var group = snapshot.Read(OutcomeGroup);
        var keys = new List<string>();

        foreach (var name in group.MemberNames)
        {
            if (!name.StartsWith('_'))
            {
                keys.Add(snapshot.ReadText(OutcomeGroup + "/" + name));
            }
        }

        return keys;
    }
}
