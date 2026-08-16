using Shouldly;
using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Feats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Feats;

/// <summary>
/// The event → lifetime-counter table: which counters each of today's two domain events advances,
/// and — as importantly — what it leaves alone.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The ids are asserted as string LITERALS, never through the production constants.</b> A
/// counter id is the key a player's lifetime history is stored under: renaming one orphans every
/// count ever taken, and a Feat then evaluates against a history of zero. A test that read the id
/// from the code under test would pass through exactly that rename.
/// </para>
/// <para>
/// The two enums the ids fan out from are driven exhaustively rather than sampled, so a face kind
/// or a currency added later cannot land uncounted with this file green.
/// </para>
/// </remarks>
public sealed class FeatCounterProjectionTests
{
    private static DomainEvent Rolled(DieFace face) => new DiceRolled(1, face);

    private static DomainEvent Moved(CurrencyId currency, long delta) =>
        new CurrencyChanged(1, currency, delta, "fixture_reason");

    private static IReadOnlyList<FeatCounterIncrement> Project(params DomainEvent[] events) =>
        FeatCounterProjection.Project(events);

    private static long AmountFor(IReadOnlyList<FeatCounterIncrement> increments, string counterId) =>
        increments.Where(i => i.CounterId == counterId).Sum(i => i.Amount);

    // ------------------------------------------------------------------ the die

    [Fact]
    public void A_pip_roll_advances_the_total_the_pip_counter_and_its_own_number()
    {
        var increments = Project(Rolled(DieFace.Pip(4)));

        AmountFor(increments, "dice_rolled").ShouldBe(1L);
        AmountFor(increments, "dice_rolled_pip").ShouldBe(1L);
        AmountFor(increments, "dice_rolled_pips_4").ShouldBe(1L);
        AmountFor(increments, "dice_rolled_pips_5").ShouldBe(0L, "a four is not a five.");

        increments.Count.ShouldBe(
            3, "a pip roll advances the total, the kind and the number shown, and nothing else.");
    }

    /// <summary>
    /// 🔒 How often each number came up. `28` D2.2 asks it twice — "Sixes rolled" and "Ones
    /// rolled" — and the answer is only ever knowable from the roll that already happened, so a
    /// counter added later starts from a history of zero that nothing can reconstruct.
    /// </summary>
    [Theory]
    [InlineData(1, "dice_rolled_pips_1")]
    [InlineData(2, "dice_rolled_pips_2")]
    [InlineData(3, "dice_rolled_pips_3")]
    [InlineData(4, "dice_rolled_pips_4")]
    [InlineData(5, "dice_rolled_pips_5")]
    [InlineData(6, "dice_rolled_pips_6")]
    public void Each_number_of_pips_has_its_own_pinned_counter_id(int pips, string counterId)
    {
        FeatCounterProjection.PipsRolledCounterFor(pips).ShouldBe(counterId);

        AmountFor(Project(Rolled(DieFace.Pip(pips))), counterId).ShouldBe(1L);
    }

    /// <summary>A face that is not a Pip carries no number, so it registers none.</summary>
    [Fact]
    public void A_non_pip_face_advances_no_number_counter()
    {
        Project(Rolled(DieFace.Special(DieFaceKind.Star)))
            .Select(i => i.CounterId)
            .ShouldBe(new[] { "dice_rolled", "dice_rolled_star" }, ignoreOrder: true);
    }

    /// <summary>The number vocabulary is closed at the die's own range, in both directions.</summary>
    [Fact]
    public void A_number_outside_the_dies_range_is_refused_rather_than_counted()
    {
        foreach (var outside in new[] { 0, 7, -1 })
        {
            Should.Throw<InvalidOperationException>(
                    () => FeatCounterProjection.PipsRolledCounterFor(outside))
                .Message.ShouldContain("04 §1's die shows 1..6 pips", Case.Sensitive);
        }

        Enumerable.Range(1, 6)
            .Select(FeatCounterProjection.PipsRolledCounterFor)
            .Distinct(StringComparer.Ordinal)
            .Count()
            .ShouldBe(6, "two numbers sharing a counter would merge two histories.");
    }

    [Fact]
    public void A_star_roll_advances_the_total_and_the_star_counter()
    {
        var increments = Project(Rolled(DieFace.Special(DieFaceKind.Star)));

        AmountFor(increments, "dice_rolled").ShouldBe(1L);
        AmountFor(increments, "dice_rolled_star").ShouldBe(1L);
        AmountFor(increments, "dice_rolled_pip").ShouldBe(0L, "a Star is not a Pip.");
    }

    /// <summary>A chain emits several rolls in one command's list; every one of them counts.</summary>
    [Fact]
    public void Every_roll_in_one_commands_list_counts()
    {
        var increments = Project(
            Rolled(DieFace.Special(DieFaceKind.Chain)),
            Rolled(DieFace.Special(DieFaceKind.Chain)),
            Rolled(DieFace.Pip(2)));

        AmountFor(increments, "dice_rolled").ShouldBe(3L);
        AmountFor(increments, "dice_rolled_chain").ShouldBe(2L);
        AmountFor(increments, "dice_rolled_pip").ShouldBe(1L);
        AmountFor(increments, "dice_rolled_pips_2").ShouldBe(1L);
    }

    /// <summary>
    /// 🔒 The whole face vocabulary, pinned as literals. `28` D2.2's "roll every face kind once"
    /// needs all six to exist, so a partial table would make that Feat unclaimable.
    /// </summary>
    [Theory]
    [InlineData(DieFaceKind.Pip, "dice_rolled_pip")]
    [InlineData(DieFaceKind.Star, "dice_rolled_star")]
    [InlineData(DieFaceKind.Surge, "dice_rolled_surge")]
    [InlineData(DieFaceKind.Fortune, "dice_rolled_fortune")]
    [InlineData(DieFaceKind.Void, "dice_rolled_void")]
    [InlineData(DieFaceKind.Chain, "dice_rolled_chain")]
    public void Each_face_kind_has_its_own_pinned_counter_id(DieFaceKind kind, string counterId)
    {
        FeatCounterProjection.DiceRolledCounterFor(kind).ShouldBe(counterId);

        var face = kind == DieFaceKind.Pip ? DieFace.Pip(1) : DieFace.Special(kind);

        AmountFor(Project(Rolled(face)), counterId).ShouldBe(1L);
    }

    /// <summary>S3 — the table covers the enum, not a subset of it, and covers it without collisions.</summary>
    [Fact]
    public void Every_defined_face_kind_maps_to_a_distinct_counter()
    {
        var kinds = Enum.GetValues<DieFaceKind>();

        kinds.Length.ShouldBeGreaterThanOrEqualTo(6, "04 §1 fixes six faces; a shrunken enum would make this vacuous.");

        var ids = kinds.Select(FeatCounterProjection.DiceRolledCounterFor).ToArray();

        ids.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            kinds.Length,
            "two faces sharing a counter would silently merge two Feats' histories.");

        ids.ShouldAllBe(id => id.StartsWith("dice_rolled_", StringComparison.Ordinal));
        ids.ShouldNotContain("dice_rolled", "the total is not one of the per-kind rows.");
    }

    /// <summary>
    /// An unset or undefined face names no counter, and is refused rather than counted as
    /// something. Pinned by <b>identity</b> — the exception type alone is shared by every other
    /// refusal in this file and by any catch-all a later edit might add.
    /// </summary>
    [Fact]
    public void An_unset_face_is_refused_rather_than_counted()
    {
        Should.Throw<InvalidOperationException>(() => Project(Rolled(default)))
            .Message.ShouldContain("04 §1 fixes DieFaceKind at six named members", Case.Sensitive);

        Should.Throw<InvalidOperationException>(() => FeatCounterProjection.DiceRolledCounterFor((DieFaceKind)99))
            .Message.ShouldContain("99", Case.Sensitive);
    }

    // ------------------------------------------------------------------ the wallet

    [Fact]
    public void Income_advances_the_earned_counter_by_the_delta()
    {
        var increments = Project(Moved(CurrencyId.MERGE_DUST, 250L));

        AmountFor(increments, "currency_earned_merge_dust").ShouldBe(250L);
        increments.Count.ShouldBe(1);
    }

    [Fact]
    public void A_spend_advances_the_spent_counter_by_the_magnitude()
    {
        var increments = Project(Moved(CurrencyId.ENHANCE_STONES, -40L));

        AmountFor(increments, "currency_spent_enhance_stones").ShouldBe(
            40L,
            "a spend is counted as an amount spent, not as a negative amount earned — a lifetime " +
            "counter only ever grows.");

        AmountFor(increments, "currency_earned_enhance_stones").ShouldBe(0L);
    }

    /// <summary>
    /// The negative control: a zero movement is explicitly permitted and is not a movement. Stated
    /// beside a non-zero one on the same currency, so an implementation that emitted a
    /// zero-<c>Amount</c> increment — or one that projected nothing at all — is distinguished.
    /// </summary>
    [Fact]
    public void A_zero_delta_advances_nothing_while_a_movement_on_the_same_currency_does()
    {
        Project(Moved(CurrencyId.ENERGY, 0L)).ShouldBeEmpty(
            "CurrencyChanged permits a zero delta — a clamp that had nothing left to give. Counting " +
            "it would register a counter nothing actually earned or spent.");

        Project(Moved(CurrencyId.ENERGY, 1L)).ShouldHaveSingleItem().Amount.ShouldBe(1L);

        Project(Moved(CurrencyId.ENERGY, 0L), Moved(CurrencyId.ENERGY, 4L))
            .ShouldHaveSingleItem().Amount.ShouldBe(
                4L,
                "the zero row is dropped from a mixed list rather than folded into the movement.");
    }

    /// <summary>🔒 The whole currency vocabulary, both directions, pinned as literals.</summary>
    [Theory]
    [InlineData(CurrencyId.GOLD, "currency_earned_gold", "currency_spent_gold")]
    [InlineData(CurrencyId.CROWNS, "currency_earned_crowns", "currency_spent_crowns")]
    [InlineData(CurrencyId.SOUL_SHARDS, "currency_earned_soul_shards", "currency_spent_soul_shards")]
    [InlineData(CurrencyId.ENERGY, "currency_earned_energy", "currency_spent_energy")]
    [InlineData(CurrencyId.ENHANCE_STONES, "currency_earned_enhance_stones", "currency_spent_enhance_stones")]
    [InlineData(CurrencyId.MERGE_DUST, "currency_earned_merge_dust", "currency_spent_merge_dust")]
    [InlineData(CurrencyId.BEAST_FEED, "currency_earned_beast_feed", "currency_spent_beast_feed")]
    [InlineData(CurrencyId.HONOR, "currency_earned_honor", "currency_spent_honor")]
    public void Each_currency_has_its_own_pinned_earned_and_spent_ids(
        CurrencyId currency, string earned, string spent)
    {
        FeatCounterProjection.CurrencyCounterFor(currency, earned: true).ShouldBe(earned);
        FeatCounterProjection.CurrencyCounterFor(currency, earned: false).ShouldBe(spent);

        AmountFor(Project(Moved(currency, 7L)), earned).ShouldBe(7L);
        AmountFor(Project(Moved(currency, -7L)), spent).ShouldBe(7L);
    }

    /// <summary>S3 — every currency is covered, and no two share a row in either direction.</summary>
    [Fact]
    public void Every_defined_currency_maps_to_distinct_earned_and_spent_counters()
    {
        var currencies = Enum.GetValues<CurrencyId>();

        currencies.Length.ShouldBeGreaterThanOrEqualTo(8, "10 §1 fixes eight currencies.");

        var ids = currencies.Select(c => FeatCounterProjection.CurrencyCounterFor(c, earned: true))
            .Concat(currencies.Select(c => FeatCounterProjection.CurrencyCounterFor(c, earned: false)))
            .ToArray();

        ids.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            currencies.Length * 2,
            "income and spend are separate Feats ('Merge Dust earned', 'Enhance Stones spent'), and " +
            "two currencies sharing a row would merge two histories.");
    }

    /// <summary>Pinned by identity, for the reason the face-kind refusal above is.</summary>
    [Fact]
    public void An_undefined_currency_is_refused_rather_than_counted()
    {
        foreach (var earned in new[] { true, false })
        {
            Should.Throw<InvalidOperationException>(
                    () => FeatCounterProjection.CurrencyCounterFor((CurrencyId)99, earned))
                .Message.ShouldContain("10 §1 fixes eight currencies", Case.Sensitive);
        }

        Should.Throw<InvalidOperationException>(() => Project(Moved((CurrencyId)99, 5L)))
            .Message.ShouldContain("10 §1 fixes eight currencies", Case.Sensitive);
    }

    // ------------------------------------------------------------------ the boundary of the table

    /// <summary>
    /// The negative control for the whole table: an event type it has no row for advances nothing.
    /// The table is closed on purpose — a Feat that needs an event nothing emits yet is that event's
    /// milestone, not a guess made here.
    /// </summary>
    [Fact]
    public void An_event_the_table_has_no_row_for_advances_nothing()
    {
        Project(new UnprojectedFixtureEvent(1)).ShouldBeEmpty();

        Project(new UnprojectedFixtureEvent(1), Rolled(DieFace.Pip(1)))
            .Select(i => i.CounterId)
            .ShouldBe(new[] { "dice_rolled", "dice_rolled_pip", "dice_rolled_pips_1" }, ignoreOrder: true);
    }

    /// <summary>
    /// The advances come back in the order the events happened, which is what the method promises.
    /// Two events on two different currencies, so a projection that grouped by counter — losing the
    /// list's order — is distinguished from one that folds it in place.
    /// </summary>
    [Fact]
    public void The_advances_come_back_in_the_order_the_events_happened()
    {
        Project(Moved(CurrencyId.HONOR, 1L), Moved(CurrencyId.GOLD, 2L), Moved(CurrencyId.HONOR, 3L))
            .Select(i => (i.CounterId, i.Amount))
            .ShouldBe(new[]
            {
                ("currency_earned_honor", 1L),
                ("currency_earned_gold", 2L),
                ("currency_earned_honor", 3L),
            });
    }

    [Fact]
    public void An_empty_event_list_advances_nothing()
    {
        Project().ShouldBeEmpty();
        Should.Throw<ArgumentNullException>(() => FeatCounterProjection.Project(null!));
    }

    /// <summary>
    /// 🔒 Every id the table can actually <b>emit</b> is a distinct, stable
    /// <c>lower_snake_case</c> token — swept off <see cref="FeatCounterProjection.Project"/> itself
    /// over one event of every shape, not off the mapping helpers.
    /// </summary>
    /// <remarks>
    /// A sweep built from <c>Enum.GetValues</c> and the two helpers would be a claim about the
    /// helpers; this is a claim about the table. If a row stopped emitting — a face kind dropped
    /// from the switch, one direction of the currency rule deleted — the count below falls and the
    /// helper-driven version would not have noticed.
    /// </remarks>
    [Fact]
    public void Every_id_the_table_emits_is_a_distinct_lower_snake_case_token()
    {
        var everyShape = Enum.GetValues<DieFaceKind>()
            .Where(k => k != DieFaceKind.Pip)
            .Select(k => Rolled(DieFace.Special(k)))
            .Concat(Enumerable.Range(1, 6).Select(pips => Rolled(DieFace.Pip(pips))))
            .Concat(Enum.GetValues<CurrencyId>().Select(c => Moved(c, 1L)))
            .Concat(Enum.GetValues<CurrencyId>().Select(c => Moved(c, -1L)))
            .ToArray();

        var ids = FeatCounterProjection.Project(everyShape)
            .Select(i => i.CounterId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        ids.Length.ShouldBe(
            29,
            "6 face kinds + 6 pip numbers + 8 currencies x 2 directions + the roll total. This is " +
            "what the table EMITS: a row that stopped firing lowers it, whatever the mapping " +
            "helpers still answer.");

        ids.ShouldAllBe(id => id.Length > 0 && id.All(c => (c >= 'a' && c <= 'z') || c == '_' || (c >= '1' && c <= '6')));
        ids.ShouldContain("dice_rolled");
    }

    /// <summary>An event outside the table, declared here so no production type has to exist for the negative control.</summary>
    private sealed record UnprojectedFixtureEvent(int Sequence) : DomainEvent(Sequence);
}
