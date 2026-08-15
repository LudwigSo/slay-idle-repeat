using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>Tests <c>content/curses/curses.json</c>: the twelve-curse catalogue and its chapter-gating column.</summary>
/// <remarks>The mechanical rules engine (no-stacking, paired-reward payout, mount immunity) is tested elsewhere, not here.</remarks>
public sealed class CursesDataTests
{
    private const string Document = "content/curses/curses.json";

    private static ContentSnapshot Data() => ContentLoader.Load(RepoData.Source()).Require();

    [Fact]
    public void The_document_is_governed_by_its_own_schema()
    {
        ContentLayout.SchemaFor(Document).ShouldBe("schema/curses.schema.json");
        ContentLoader.SchemasAwaitingContent.ShouldNotContain("schema/curses.schema.json");
        ContentLoader.VocabularySchemas.ShouldNotContain("schema/curses.schema.json");
    }

    [Fact]
    public void The_shipped_data_set_still_validates_with_the_curse_catalogue_present()
    {
        ContentLoader.Load(RepoData.Source()).Issues.ShouldBeEmpty();
    }

    [Fact]
    public void The_catalogue_carries_all_twelve_19_part_e_curses()
    {
        Data().Read($"{Document}#/curses").Items.Count.ShouldBe(12);
    }

    /// <summary>Each curse's chapter gate, pinned individually so an edit to any one gate is caught rather than only a change in the aggregate count.</summary>
    [Theory]
    [InlineData("CUR_SLIPPERY", 1)]
    [InlineData("CUR_MARKED", 1)]
    [InlineData("CUR_DIZZY", 1)]
    [InlineData("CUR_FRACTURED", 1)]
    [InlineData("CUR_HUNTED", 5)]
    [InlineData("CUR_UNTIMELY", 3)]
    [InlineData("CUR_FAMISHED", 3)]
    [InlineData("CUR_BRITTLE_BONES", 3)]
    [InlineData("CUR_MISERLY", 3)]
    [InlineData("CUR_BLIND", 3)]
    [InlineData("CUR_LEADFOOT", 3)]
    [InlineData("CUR_TITHE", 3)]
    public void Each_curse_carries_O13s_chapter_gate(string curseId, int expectedChapter)
    {
        var curses = Data().Read($"{Document}#/curses").Items;
        var curse = curses.Single(id => Member(id, "id").AsText() == curseId);

        Member(curse, "availableFromChapter").AsInt32().ShouldBe(expectedChapter);
    }

    [Theory]
    [InlineData("CUR_SLIPPERY", "-1 to all Pip rolls (minimum 1)", "+250 Gold")]
    [InlineData("CUR_MARKED", "Enemies +10% ATK for the rest of the stage", "+2 Enhance Stones")]
    [InlineData("CUR_DIZZY", "The next 3 rolls cannot be rerolled", "+180 Gold")]
    [InlineData("CUR_FRACTURED", "-8% DEF", "+500 Gold")]
    public void The_four_chapter_1_curses_match_19_part_es_effect_and_reward_columns(
        string curseId, string effect, string reward)
    {
        var curses = Data().Read($"{Document}#/curses").Items;
        var curse = curses.Single(id => Member(id, "id").AsText() == curseId);

        Member(curse, "effect").AsText().ShouldBe(effect);
        Member(curse, "reward").AsText().ShouldBe(reward);
    }

    [Fact]
    public void Every_curse_id_is_unique_and_matches_the_CUR_pattern()
    {
        var ids = Data().Read($"{Document}#/curses").Items
            .Select(c => Member(c, "id").AsText())
            .ToArray();

        ids.ShouldBeUnique();
        ids.ShouldAllBe(id => id.StartsWith("CUR_", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_curse_display_name_resolves_in_both_locales()
    {
        var data = Data();

        foreach (var curse in data.Read($"{Document}#/curses").Items)
        {
            var key = Member(curse, "displayName").AsText();

            data.ReadText($"loc/en.json#/strings/{key}").ShouldNotBeNullOrWhiteSpace();
            data.ReadText($"loc/de.json#/strings/{key}").ShouldContain("##TODO_DE##");
        }
    }

    private static ContentValue Member(ContentValue value, string name)
    {
        value.TryGetMember(name, out var member).ShouldBeTrue($"'{name}' is missing");
        return member!;
    }
}
