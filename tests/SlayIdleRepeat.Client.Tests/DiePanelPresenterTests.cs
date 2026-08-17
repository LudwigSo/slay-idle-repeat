using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Content.Dice;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// `04` §2 / `13` §3 — the Die Panel (S12): the face vocabulary, and the honesty about what it
/// cannot show.
/// </summary>
public sealed class DiePanelPresenterTests
{
    [Fact]
    public void Every_face_kind_the_game_defines_is_listed()
    {
        var panel = new DiePanelPresenter(BoardContent.Catalogue());

        panel.Faces.Select(face => face.Kind).ShouldBe(Enum.GetValues<DieFaceKind>());
    }

    /// <summary>
    /// 🔒 The panel exists because a hidden die is a hostile die, so a face kind with no line is a
    /// face the player can roll and never read about. Stated over the enum rather than over a count,
    /// so a kind added to the game is caught by name.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Each_face_kind_carries_a_name_and_an_effect_line(DieFaceKind kind)
    {
        var panel = new DiePanelPresenter(BoardContent.Catalogue());

        panel.NameOf(kind).ShouldBe(BoardContent.EnglishValueOf(BoardContent.NameKeyOf(kind)));
        panel.EffectOf(kind).ShouldBe(BoardContent.EnglishValueOf(BoardContent.EffectKeyOf(kind)));
    }

    public static TheoryData<DieFaceKind> AllKinds()
    {
        var data = new TheoryData<DieFaceKind>();

        foreach (var kind in Enum.GetValues<DieFaceKind>())
        {
            data.Add(kind);
        }

        return data;
    }

    /// <summary>
    /// Six kinds must read as six different things, <b>as authored</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 Over the shipped locale, not a fixture. A fixture value derived from its own key is
    /// distinct by construction, so a fixture-based version of this case would pass however many of
    /// the six real lines said the same thing — and a panel whose six faces read alike discloses
    /// nothing while appearing to disclose everything, which is the hostile die this screen exists
    /// to prevent.
    /// </remarks>
    [Fact]
    public void No_two_face_kinds_are_authored_the_same()
    {
        var kinds = Enum.GetValues<DieFaceKind>();

        AuthoredLinesFor(kinds, BoardContent.NameKeyOf).Distinct(StringComparer.Ordinal)
            .Count().ShouldBe(kinds.Length, "two face kinds are authored with the same NAME.");

        AuthoredLinesFor(kinds, BoardContent.EffectKeyOf).Distinct(StringComparer.Ordinal)
            .Count().ShouldBe(kinds.Length, "two face kinds are authored with the same EFFECT line.");
    }

    /// <summary>
    /// 🔒 And no authored effect line carries a digit. Every face's magnitude — how far it moves,
    /// how much it heals, how many times it chains — is a tunable the rules layer owns, and a copy
    /// of one inside a translated string could not be tuned with the original and would be wrong in
    /// two languages at once.
    /// </summary>
    [Fact]
    public void No_authored_effect_line_states_a_magnitude()
    {
        foreach (var kind in Enum.GetValues<DieFaceKind>())
        {
            var line = Authored(BoardContent.EffectKeyOf(kind));

            line.ShouldNotContain(
                character => char.IsDigit(character),
                $"the effect line for {kind} carries a number. Face magnitudes live in the rules " +
                "layer and are tunable; a second copy of one here is a balance value nobody can tune " +
                $"and nobody will find: \"{line}\"");
        }
    }

    private static IEnumerable<string> AuthoredLinesFor(
        IReadOnlyList<DieFaceKind> kinds, Func<DieFaceKind, string> keyOf) =>
        kinds.Select(kind => Authored(keyOf(kind)));

    private static string Authored(string key)
    {
        BoardContent.ShippedEnglish.TryGetValue(key, out var line).ShouldBeTrue(
            $"'{key}' is not in the shipped English locale, so the face it describes has no line and " +
            "a player is shown its key.");

        line!.ShouldNotBeEmpty();

        return line;
    }

    /// <summary>
    /// 🔒 The panel says, permanently and in words, that it cannot show which face sits in which
    /// slot. That line is the whole difference between a panel that discloses a die and one that
    /// asserts a die the player may not have.
    /// </summary>
    [Fact]
    public void The_panel_admits_that_the_composed_die_is_not_readable()
    {
        var panel = new DiePanelPresenter(BoardContent.Catalogue());

        panel.FacesUnavailableStatus.ShouldBe(
            BoardContent.EnglishValueOf(BoardContent.DiePanelFacesUnavailableStatusKey));
        panel.FacesUnavailableStatus.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_headings_and_the_pre_roll_line_resolve()
    {
        var panel = new DiePanelPresenter(BoardContent.Catalogue());

        panel.Title.ShouldBe(BoardContent.EnglishValueOf(BoardContent.DiePanelTitleKey));
        panel.LastFaceLabel.ShouldBe(BoardContent.EnglishValueOf(BoardContent.DiePanelLastFaceLabelKey));
        panel.NoRollYetStatus.ShouldBe(BoardContent.EnglishValueOf(BoardContent.DiePanelNoRollYetStatusKey));
    }

    [Fact]
    public void It_refuses_to_be_built_without_a_catalogue()
    {
        Should.Throw<ArgumentNullException>(() => new DiePanelPresenter(null!));
    }
}
