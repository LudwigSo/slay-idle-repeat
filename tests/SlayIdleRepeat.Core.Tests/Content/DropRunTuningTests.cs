using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// The in-run drop protections read out of <c>tuning/luck.json#/dropRun</c>: the two dry-streak
/// breakers and the session floor.
/// </summary>
public sealed class DropRunTuningTests
{
    /// <summary>
    /// The elite breaker's ordinal is the ordinal of the <em>forced</em> kill, not a count of
    /// misses tolerated before it — the two readings differ by exactly one drop.
    /// </summary>
    [Fact]
    public void The_elite_breaker_is_the_authored_ordinal_miss_band_and_forced_band()
    {
        DropRunTuning.Read(LuckDocuments.Shipped).EliteMercy.ShouldBe(new DryStreakBreaker(
            LuckDocuments.ShippedDropRunEliteMercyN,
            Enum.Parse<Rarity>(LuckDocuments.ShippedEliteMercyBelowRarity),
            Enum.Parse<Rarity>(LuckDocuments.ShippedEliteMercyForceRarity)));
    }

    /// <summary>The boss breaker is its own three values, over its own band.</summary>
    [Fact]
    public void The_boss_breaker_is_the_authored_ordinal_miss_band_and_forced_band()
    {
        DropRunTuning.Read(LuckDocuments.Shipped).BossMercy.ShouldBe(new DryStreakBreaker(
            LuckDocuments.ShippedDropRunBossMercyN,
            Enum.Parse<Rarity>(LuckDocuments.ShippedBossMercyBelowRarity),
            Enum.Parse<Rarity>(LuckDocuments.ShippedBossMercyForceRarity)));
    }

    /// <summary>The session floor is the authored band, count, daily allowance and qualifier.</summary>
    [Fact]
    public void The_session_floor_is_the_authored_band_count_allowance_and_qualifier()
    {
        DropRunTuning.Read(LuckDocuments.Shipped).SessionFloor.ShouldBe(new SessionFloor(
            Enum.Parse<Rarity>(LuckDocuments.ShippedSessionFloorGrantRarity),
            LuckDocuments.ShippedSessionFloorGrantCount,
            LuckDocuments.ShippedSessionFloorMaxPerDay,
            LuckDocuments.ShippedSessionFloorRequiresVictoryOrStage3Death));
    }

    /// <summary>
    /// Each block is read from its own pointer — a reader that answered the elite block for both
    /// breakers would pass the three transcription cases above.
    /// </summary>
    [Fact]
    public void Retuning_one_block_leaves_the_other_two_where_the_document_put_them()
    {
        var tuning = DropRunTuning.Read(LuckDocuments.LuckOnly(dropRun: LuckDocuments.DropRun(
            eliteMercy: LuckDocuments.Breaker(
                ContentValue.Number(3), ContentValue.Text("S"), ContentValue.Text("SS")))));

        tuning.EliteMercy.ShouldBe(new DryStreakBreaker(3, Rarity.S, Rarity.SS));
        tuning.BossMercy.ForceOnNthKill.ShouldBe(LuckDocuments.ShippedDropRunBossMercyN);
        tuning.SessionFloor.GrantCount.ShouldBe(LuckDocuments.ShippedSessionFloorGrantCount);
    }

    /// <summary>
    /// An ordinal below one is refused: there is no zeroth kill to force, and <c>N = 0</c> would
    /// force every drop of its kind from then on.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-6)]
    public void A_breaker_ordinal_below_one_is_refused(int ordinal)
    {
        var thrown = Should.Throw<InvalidTunableException>(() => DropRunTuning.Read(
            LuckDocuments.LuckOnly(dropRun: LuckDocuments.DropRun(
                eliteMercy: LuckDocuments.Breaker(
                    ContentValue.Number(ordinal),
                    ContentValue.Text(LuckDocuments.ShippedEliteMercyBelowRarity),
                    ContentValue.Text(LuckDocuments.ShippedEliteMercyForceRarity))))));

        thrown.Reference.ShouldBe(
            DropRunTuning.EliteMercyReference + "/forceOnNthKill",
            "which breaker, not merely that some ordinal was refused — this reader raises the same " +
            "type for the boss breaker, for an unsatisfiable band pairing and for a floor that grants " +
            "nothing.");
        thrown.Message.ShouldContain("no zeroth kill", Case.Sensitive);
    }

    /// <summary>
    /// A breaker whose forced band is below its miss band is refused: the forced drop would itself
    /// count as a miss, so the counter would never reset.
    /// </summary>
    [Theory]
    [InlineData("A", "C")]
    [InlineData("SS", "S")]
    public void A_breaker_whose_forced_band_is_below_its_miss_band_is_refused(string below, string force)
    {
        var thrown = Should.Throw<InvalidTunableException>(() => DropRunTuning.Read(
            LuckDocuments.LuckOnly(dropRun: LuckDocuments.DropRun(
                bossMercy: LuckDocuments.Breaker(
                    ContentValue.Number(LuckDocuments.ShippedDropRunBossMercyN),
                    ContentValue.Text(below),
                    ContentValue.Text(force))))));

        thrown.Reference.ShouldBe(DropRunTuning.BossMercyReference);
        thrown.Message.ShouldContain("never reset", Case.Sensitive);
    }

    /// <summary>A forced band equal to the miss band is accepted — that is what the file ships.</summary>
    /// <remarks>The boundary control on the case above: the rule is strictly-below, not below-or-equal.</remarks>
    [Fact]
    public void A_breaker_whose_forced_band_equals_its_miss_band_is_accepted()
    {
        DropRunTuning.Read(LuckDocuments.LuckOnly(dropRun: LuckDocuments.DropRun(
                bossMercy: LuckDocuments.Breaker(
                    ContentValue.Number(2), ContentValue.Text("A"), ContentValue.Text("A")))))
            .BossMercy.ShouldBe(new DryStreakBreaker(2, Rarity.A, Rarity.A));
    }

    /// <summary>A floor that grants nothing is authored as no floor, not as a count of zero.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_floor_that_grants_nothing_is_refused(int grantCount)
    {
        Should.Throw<InvalidTunableException>(() => DropRunTuning.Read(
                LuckDocuments.LuckOnly(dropRun: LuckDocuments.DropRun(sessionFloor: Floor(grantCount, 2)))))
            .Reference.ShouldBe(DropRunTuning.SessionFloorReference + "/grantCount");
    }

    /// <summary>A daily allowance below one is refused: the floor may be paid at least once a day.</summary>
    [Fact]
    public void A_daily_allowance_below_one_is_refused()
    {
        Should.Throw<InvalidTunableException>(() => DropRunTuning.Read(
                LuckDocuments.LuckOnly(dropRun: LuckDocuments.DropRun(sessionFloor: Floor(1, 0)))))
            .Reference.ShouldBe(DropRunTuning.SessionFloorReference + "/maxPerDay");
    }

    /// <summary>A band token that is not exactly a member's name is refused.</summary>
    [Theory]
    [InlineData("3")]
    [InlineData("a")]
    [InlineData(" A")]
    public void A_band_token_that_is_not_exactly_a_member_name_is_refused(string authored)
    {
        Should.Throw<InvalidTunableException>(() => DropRunTuning.Read(
                LuckDocuments.LuckOnly(dropRun: LuckDocuments.DropRun(
                    eliteMercy: LuckDocuments.Breaker(
                        ContentValue.Number(LuckDocuments.ShippedDropRunEliteMercyN),
                        ContentValue.Text(authored),
                        ContentValue.Text("A"))))))
            .Reference.ShouldBe(DropRunTuning.EliteMercyReference + "/belowRarity");
    }

    /// <summary>A missing block is a <c>MissingContentException</c>, not a class with no protection.</summary>
    [Fact]
    public void A_missing_block_throws_rather_than_defaulting()
    {
        Should.Throw<MissingContentException>(
            () => DropRunTuning.Read(LuckDocuments.LuckOnly(dropRun: ContentValue.EmptyObject)));
    }

    /// <summary>A missing document is a <c>MissingContentException</c> too, at the document path.</summary>
    [Fact]
    public void A_missing_document_throws_rather_than_defaulting()
    {
        Should.Throw<MissingContentException>(() => DropRunTuning.Read(LuckDocuments.WithoutLuck()))
            .Reference.ShouldBe(DropRunTuning.DocumentPath);
    }

    /// <summary>A leaf of the wrong kind is a <c>ContentTypeMismatchException</c>.</summary>
    [Fact]
    public void A_fractional_ordinal_is_refused()
    {
        Should.Throw<ContentTypeMismatchException>(() => DropRunTuning.Read(
                LuckDocuments.LuckOnly(dropRun: LuckDocuments.DropRun(
                    eliteMercy: LuckDocuments.Breaker(
                        ContentValue.Number(6.5m),
                        ContentValue.Text("A"),
                        ContentValue.Text("A"))))))
            .Reference.ShouldBe(DropRunTuning.EliteMercyReference + "/forceOnNthKill");
    }

    /// <summary>The reader refuses a null content set rather than dereferencing it.</summary>
    [Fact]
    public void A_null_content_set_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => DropRunTuning.Read(null!))
            .ParamName.ShouldBe("content");
    }

    private static ContentValue Floor(int grantCount, int maxPerDay) => LuckDocuments.Floor(
        ContentValue.Text(LuckDocuments.ShippedSessionFloorGrantRarity),
        ContentValue.Number(grantCount),
        ContentValue.Number(maxPerDay),
        ContentValue.Boolean(LuckDocuments.ShippedSessionFloorRequiresVictoryOrStage3Death));
}
