using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The run-end screen's strings as content: the revive is Plus-only, an unentitled player sees no
/// control and no sentence about one, so the upsell string is retired rather than kept as a placeholder.
/// </summary>
public sealed class RunEndDataTests
{
    private const string ReviveNeedsPlusBlockKey = "loc.run_end.revive_needs_plus.block";

    private const string EnglishStrings = "loc/en.json#/strings/";

    private const string GermanStrings = "loc/de.json#/strings/";

    [Fact]
    public void The_retired_revive_upsell_string_is_carried_by_neither_locale()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.TryRead(EnglishStrings + ReviveNeedsPlusBlockKey, out _).ShouldBeFalse(
            $"'{ReviveNeedsPlusBlockKey}' is still in the English locale. A key that survives there " +
            "while the document that named it is gone is an orphan the loader rejects, and one that " +
            "survives in both locales is a placeholder sentence the run-end screen was told to stop " +
            "drawing.");
        snapshot.TryRead(GermanStrings + ReviveNeedsPlusBlockKey, out _).ShouldBeFalse(
            $"'{ReviveNeedsPlusBlockKey}' is still in the German locale, so a translator is being " +
            "paid for a sentence about a control the screen does not draw.");
    }
}
