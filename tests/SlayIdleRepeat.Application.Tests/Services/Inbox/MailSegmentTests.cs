using Shouldly;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Inbox;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Services.Inbox;

/// <summary>
/// Targeting: who a predicate selects, which fields it may ask about, and what the audit row records.
/// </summary>
public sealed class MailSegmentTests
{
    private static readonly DateTimeOffset Now = Worlds.Start;

    private static PlayerProfile Profile(
        string id, int highestChapter = 1, int lastActiveDaysAgo = 0)
    {
        var game = Worlds.Game();
        var player = game.CreatePlayer();
        var snapshot = game.State(player).Player.ToSnapshot();

        return new PlayerProfile(
            snapshot with
            {
                Id = new PlayerId(id),
                LastAppliedAtUtc = Now.AddDays(-lastActiveDaysAgo),
                ClearedChapterTiers = new Dictionary<string, long>(StringComparer.Ordinal)
                {
                    [ChapterClearance.Key(highestChapter, DifficultyTier.NORMAL)] = 1,
                },
            },
            null);
    }

    private static MailSegmentDryRun Select(
        MailTargetKind target, IReadOnlyList<MailSegmentClause> clauses, params PlayerProfile[] candidates) =>
        MailSegmentSelection.Select(target, clauses, candidates, Now);

    private static MailSegmentClause Where(
        MailSegmentField field, MailSegmentOperator op, long operand) => new(field, op, operand);

    [Fact]
    public void An_ALL_send_reaches_every_candidate_the_operator_supplied()
    {
        var dryRun = Select(
            MailTargetKind.ALL, Array.Empty<MailSegmentClause>(),
            Profile("PLAYER_a"), Profile("PLAYER_b"));

        dryRun.Accepted.ShouldBeTrue();
        dryRun.Recipients.Select(p => p.Value).ShouldBe(new[] { "PLAYER_a", "PLAYER_b" });
        dryRun.Candidates.ShouldBe(2);
    }

    [Fact]
    public void A_segment_send_selects_only_the_candidates_the_predicate_matches()
    {
        var dryRun = Select(
            MailTargetKind.SEGMENT,
            new[] { Where(MailSegmentField.HIGHEST_CHAPTER, MailSegmentOperator.AT_LEAST, 3) },
            Profile("PLAYER_new", highestChapter: 1),
            Profile("PLAYER_far", highestChapter: 5));

        dryRun.Recipients.ShouldHaveSingleItem().Value.ShouldBe("PLAYER_far");
        dryRun.Candidates.ShouldBe(
            2,
            "the count the operator reads is how many were EXAMINED beside how many were selected — " +
            "a predicate that reached one of two is a different fact from one that reached one of a " +
            "thousand.");
    }

    [Fact]
    public void The_last_active_window_selects_the_players_inside_it()
    {
        var dryRun = Select(
            MailTargetKind.SEGMENT,
            new[] { Where(MailSegmentField.LAST_ACTIVE_WITHIN, MailSegmentOperator.AT_MOST, 7) },
            Profile("PLAYER_here", lastActiveDaysAgo: 2),
            Profile("PLAYER_gone", lastActiveDaysAgo: 40));

        dryRun.Recipients.ShouldHaveSingleItem().Value.ShouldBe("PLAYER_here");
    }

    [Fact]
    public void Every_clause_must_match_rather_than_any_of_them()
    {
        var dryRun = Select(
            MailTargetKind.SEGMENT,
            new[]
            {
                Where(MailSegmentField.HIGHEST_CHAPTER, MailSegmentOperator.AT_LEAST, 3),
                Where(MailSegmentField.LAST_ACTIVE_WITHIN, MailSegmentOperator.AT_MOST, 7),
            },
            Profile("PLAYER_both", highestChapter: 5, lastActiveDaysAgo: 1),
            Profile("PLAYER_half", highestChapter: 5, lastActiveDaysAgo: 90));

        dryRun.Recipients.ShouldHaveSingleItem().Value.ShouldBe(
            "PLAYER_both",
            "a predicate reads as an AND. Any-of would widen every cohort an operator narrows, " +
            "which is the direction that turns a mistake into an economy incident.");
    }

    [Theory]
    [InlineData(MailSegmentField.PLUS_ACTIVE)]
    [InlineData(MailSegmentField.REGION)]
    [InlineData(MailSegmentField.CLIENT_VERSION)]
    [InlineData(MailSegmentField.GUILD_MEMBERSHIP)]
    [InlineData(MailSegmentField.AFFECTED_RUN_WINDOW)]
    public void A_field_no_stored_profile_can_answer_is_refused_by_name(MailSegmentField field)
    {
        var dryRun = Select(
            MailTargetKind.SEGMENT,
            new[] { Where(field, MailSegmentOperator.AT_LEAST, 1) },
            Profile("PLAYER_a"));

        dryRun.Accepted.ShouldBeFalse();
        dryRun.Recipients.ShouldBeEmpty(
            "a clause the tool ignored would widen the send to everybody the operator listed — the " +
            "exact failure a predicate exists to prevent.");
        dryRun.Refusals.ShouldHaveSingleItem().ShouldContain(field.ToString(), Case.Sensitive);
    }

    [Theory]
    [InlineData(MailSegmentField.HIGHEST_CHAPTER)]
    [InlineData(MailSegmentField.LAST_ACTIVE_WITHIN)]
    public void The_two_answerable_fields_are_not_refused(MailSegmentField field)
    {
        MailSegmentFields.IsAnswerable(field).ShouldBeTrue(
            "the negative control: a predicate that refused every field would satisfy the five " +
            "cases above and make SEGMENT sends impossible rather than narrow.");

        Should.Throw<ArgumentException>(() => MailSegmentFields.RefusalFor(field));
    }

    [Fact]
    public void A_field_nobody_ruled_on_is_refused_rather_than_treated_as_answerable()
    {
        Should.Throw<ArgumentOutOfRangeException>(
                () => MailSegmentFields.IsAnswerable((MailSegmentField)99))
            .Message.ShouldContain(
                "an unanswered clause selects everybody",
                Case.Sensitive);
    }

    [Fact]
    public void A_segment_send_with_no_predicate_is_refused()
    {
        var dryRun = Select(MailTargetKind.SEGMENT, Array.Empty<MailSegmentClause>(), Profile("PLAYER_a"));

        dryRun.Accepted.ShouldBeFalse(
            "an empty predicate selects every candidate. If that is the intent it is spelled ALL — " +
            "an empty one reads as a clause somebody forgot to type.");
    }

    [Fact]
    public void A_predicate_on_a_non_segment_send_is_refused_rather_than_ignored()
    {
        var dryRun = Select(
            MailTargetKind.PLAYER,
            new[] { Where(MailSegmentField.HIGHEST_CHAPTER, MailSegmentOperator.AT_LEAST, 3) },
            Profile("PLAYER_a", highestChapter: 1));

        dryRun.Accepted.ShouldBeFalse();
        dryRun.Recipients.ShouldBeEmpty(
            "silently dropping the clause would send to everybody the operator listed rather than " +
            "to the subset they described.");
    }

    [Fact]
    public void An_operator_nobody_taught_it_to_compare_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Select(
            MailTargetKind.SEGMENT,
            new[] { Where(MailSegmentField.HIGHEST_CHAPTER, (MailSegmentOperator)99, 1) },
            Profile("PLAYER_a")));
    }

    // ------------------------------------------------------------------------------- the record

    [Fact]
    public void The_predicate_is_rendered_for_the_audit_row_exactly_as_written()
    {
        MailSegmentSelection.Describe(
                MailTargetKind.SEGMENT,
                new[]
                {
                    Where(MailSegmentField.HIGHEST_CHAPTER, MailSegmentOperator.AT_LEAST, 3),
                    Where(MailSegmentField.LAST_ACTIVE_WITHIN, MailSegmentOperator.AT_MOST, 7),
                })
            .ShouldBe(
                "SEGMENT: HIGHEST_CHAPTER AT_LEAST 3 AND LAST_ACTIVE_WITHIN AT_MOST 7",
                "the audit row's whole purpose is that somebody later can read what the predicate " +
                "was and re-run it. Rendered invariantly and in the order written, so two records " +
                "of one send cannot disagree.");
    }

    [Fact]
    public void A_send_with_no_predicate_renders_as_its_target_alone()
    {
        MailSegmentSelection.Describe(MailTargetKind.ALL, Array.Empty<MailSegmentClause>())
            .ShouldBe("ALL");
    }

    [Fact]
    public void The_attachments_are_rendered_for_the_audit_row()
    {
        MailSegmentSend.Render(new[]
            {
                new MailAttachment("SOUL_SHARDS", 500),
                new MailAttachment("ENERGY", 120),
            })
            .ShouldBe(
                "SOUL_SHARDSx500, ENERGYx120",
                "'sending 500 Soul Shards to the wrong predicate is an economy incident' — the " +
                "record has to say what was sent as well as to whom.");
    }

    [Fact]
    public void A_send_carrying_nothing_says_so_rather_than_rendering_blank()
    {
        MailSegmentSend.Render(Array.Empty<MailAttachment>()).ShouldBe(
            "none",
            "a blank cell reads as a column somebody forgot to fill in.");
    }
}
