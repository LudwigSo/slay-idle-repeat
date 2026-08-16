using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Luck;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Luck;

/// <summary>
/// The one façade over every pity guarantee: what a resolution decides, which counters it moves, and
/// the two properties the persisted draw stream rests on — one draw index per resolution, and none
/// at all for a resolution that is refused.
/// </summary>
/// <remarks>
/// Outcomes are made deterministic by the <em>table</em> rather than by a searched-for seed. A case
/// that hunted a seed producing an S would re-break every time the draw derivation changed, and
/// <c>24</c> §11's claims are about the counter ledger, not about which hash lands where; the draw
/// itself is <c>DeterministicRngTests</c>' subject. Whole resolutions are compared as canonical text,
/// because a <c>LuckResolution</c>'s change list is a component record equality compares by
/// reference.
/// </remarks>
public sealed class LuckServiceTests
{
    private const string StandardA = "chest.standard:A";
    private const string StandardS = "chest.standard:S";
    private const string StandardSs = "chest.standard:SS";
    private const string PremiumS = "chest.premium:S";
    private const string PremiumSs = "chest.premium:SS";

    /// <summary>An arbitrary but fixed seed. Nothing here depends on which draw it produces.</summary>
    private const ulong Seed = 0x5A1D_5EED_0001UL;

    // ---------------------------------------------------------------- a forced draw moves counters normally

    /// <summary>
    /// A guarantee that fires floors the table, reports itself, and resets its own counter while
    /// advancing the one it did not satisfy.
    /// </summary>
    /// <remarks>
    /// <c>24</c> §4.0a rule 3: a floored draw <em>"advances and resets counters normally"</em>. That
    /// is what makes forcing expressible as flooring, and it is why a forced resolution still costs
    /// exactly one draw index.
    /// </remarks>
    [Fact]
    public void A_guarantee_that_fires_resets_its_own_counter_and_advances_the_one_it_missed()
    {
        var counters = PityCounters.Empty.With(PremiumS, 4).With(PremiumSs, 10);

        var resolution = LuckService.Resolve(
            SourceClass.CHEST_PREMIUM, Tuning(), LuckTables.Only(Rarity.S), counters, Rng());

        Canonical(resolution).ShouldBe(
            $"outcome=S;fromPity=1;{PremiumS}=0;{PremiumSs}=11",
            "24 §4.2: S or better every 5th premium chest. The 5th draw is forced, the S-counter goes " +
            "back to 0, and the 25-counter — which an S does not satisfy — moves on to 11.");
    }

    /// <summary>A caller-imposed floor moves counters exactly as a fired guarantee does.</summary>
    /// <remarks>
    /// <c>24</c> §4.0a rule 3's own example is the Lucky Wheel's "A-rarity or better" segment: a
    /// stated floor is not a guarantee firing, and the counters must not be able to tell the
    /// difference — <em>"a floored A still resets the A-counter, per §4.1's overshoot rule"</em>.
    /// </remarks>
    [Fact]
    public void A_caller_imposed_floor_advances_and_resets_counters_exactly_as_a_guarantee_does()
    {
        var counters = PityCounters.Empty.With(StandardA, 3).With(StandardS, 3).With(StandardSs, 3);

        var resolution = LuckService.Resolve(
            SourceClass.CHEST_STANDARD,
            Tuning(),
            LuckTables.Only(Rarity.A),
            counters,
            Rng(),
            Rarity.A);

        resolution.FromPity.ShouldBeFalse(
            "no guarantee fired — the floor came from the source, not from the ladder. A player who " +
            "is told 'from pity' when the wheel simply promised A would be shown a counter that did " +
            "not move.");
        (resolution.Outcome >= Rarity.A).ShouldBeTrue(
            "a floored draw cannot land below its floor — that is what a floor is");
        Changes(resolution).ShouldBe(
            $"{StandardA}=0;{StandardS}=4;{StandardSs}=4",
            "the A-counter resets because the draw satisfied A; the other two advance");
    }

    // ---------------------------------------------------------------- overshoot

    /// <summary>
    /// A natural draw that meets or exceeds a guarantee resets that counter, and reports that pity
    /// did not fire.
    /// </summary>
    /// <remarks>
    /// <c>24</c> §4.1's overshoot rule: <em>"the player is never punished for good luck by having a
    /// guarantee taken away later."</em> <c>FromPity</c> stays false because a natural draw and a
    /// forced one are different events to the player, to analytics and to duplicate protection, even
    /// when they land on the same rarity.
    /// </remarks>
    [Fact]
    public void A_natural_draw_that_overshoots_a_guarantee_resets_it_without_claiming_pity()
    {
        var counters = PityCounters.Empty.With(PremiumS, 1).With(PremiumSs, 1);

        var resolution = LuckService.Resolve(
            SourceClass.CHEST_PREMIUM, Tuning(), LuckTables.Only(Rarity.SS), counters, Rng());

        Canonical(resolution).ShouldBe(
            $"outcome=SS;fromPity=0;{PremiumS}=0;{PremiumSs}=0",
            "an SS on the 2nd premium chest satisfies both the 5-rung and the 25-rung, so both reset " +
            "— and neither of them fired.");
    }

    /// <summary>A draw below every guarantee advances every counter and resets none.</summary>
    /// <remarks>
    /// The negative control on the overshoot case: an implementation that reset on every draw, or
    /// that reset nothing, would pass exactly one of the two.
    /// </remarks>
    [Fact]
    public void A_draw_below_every_guarantee_advances_every_counter_and_resets_none()
    {
        var counters = PityCounters.Empty.With(PremiumS, 1).With(PremiumSs, 1);

        var resolution = LuckService.Resolve(
            SourceClass.CHEST_PREMIUM, Tuning(), LuckTables.Only(Rarity.B), counters, Rng());

        Canonical(resolution).ShouldBe($"outcome=B;fromPity=0;{PremiumS}=2;{PremiumSs}=2");
    }

    // ---------------------------------------------------------------- simultaneity

    /// <summary>Chest #10 satisfies the A-rung alone.</summary>
    [Fact]
    public void The_tenth_standard_chest_resets_the_ten_counter_and_moves_the_other_two_on()
    {
        var counters = PityCounters.Empty.With(StandardA, 9).With(StandardS, 20).With(StandardSs, 50);

        var resolution = LuckService.Resolve(
            SourceClass.CHEST_STANDARD, Tuning(), LuckTables.Only(Rarity.A), counters, Rng());

        Canonical(resolution).ShouldBe(
            $"outcome=A;fromPity=1;{StandardA}=0;{StandardS}=21;{StandardSs}=51",
            "24 §4.1: 'the three counters run independently and simultaneously'");
    }

    /// <summary>Chest #40 satisfies both the 10-counter and the 40-counter and resets both.</summary>
    /// <remarks>
    /// The standard chest's own per-item table is <c>DropShare(max(1, highestChapterCleared))</c>
    /// (<c>24</c> §4.0a rule 1) rather than a fixed row, so there is no such table to transcribe; the
    /// claim here is about the counter ledger, which does not depend on which table the draw came from.
    /// </remarks>
    [Fact]
    public void The_fortieth_standard_chest_resets_the_ten_and_forty_counters()
    {
        var counters = PityCounters.Empty.With(StandardA, 9).With(StandardS, 39).With(StandardSs, 100);

        var resolution = LuckService.Resolve(
            SourceClass.CHEST_STANDARD, Tuning(), LuckTables.Only(Rarity.S), counters, Rng());

        Canonical(resolution).ShouldBe(
            $"outcome=S;fromPity=1;{StandardA}=0;{StandardS}=0;{StandardSs}=101",
            "24 §4.1: 'chest #40 satisfies both the 10-counter and the 40-counter and resets both'");
    }

    /// <summary>Chest #160 resets all three.</summary>
    [Fact]
    public void The_hundred_and_sixtieth_standard_chest_resets_all_three_counters()
    {
        var counters = PityCounters.Empty.With(StandardA, 9).With(StandardS, 39).With(StandardSs, 159);

        var resolution = LuckService.Resolve(
            SourceClass.CHEST_STANDARD, Tuning(), LuckTables.Only(Rarity.SS), counters, Rng());

        Canonical(resolution).ShouldBe(
            $"outcome=SS;fromPity=1;{StandardA}=0;{StandardS}=0;{StandardSs}=0",
            "24 §4.1: 'chest #160 resets all three'");
    }

    /// <summary>
    /// When two rungs fire at once, the table is floored at the <em>higher</em> guarantee — proved by
    /// a table that can satisfy the lower one and not the higher.
    /// </summary>
    /// <remarks>
    /// The discriminating case behind chest #40. An implementation that floored at the first firing
    /// rung would happily answer A here and reset only the 10-counter, and every case above would
    /// still be green because an S satisfies the A-rung too.
    /// </remarks>
    [Fact]
    public void Two_rungs_firing_at_once_floor_the_table_at_the_higher_guarantee()
    {
        var counters = PityCounters.Empty.With(StandardA, 9).With(StandardS, 39);
        var draws = Rng();

        Should.Throw<InvalidOperationException>(() => LuckService.Resolve(
            SourceClass.CHEST_STANDARD, Tuning(), LuckTables.Only(Rarity.A), counters, draws));

        draws.Position.ShouldBe(0UL, "a refusal is decided before the draw, so it consumes no index");
    }

    // ---------------------------------------------------------------- isolation

    /// <summary>One class's draw never advances another class's counter.</summary>
    /// <remarks>
    /// <c>24</c> §1.2, the anti-farming rule: <em>"if a cheap source and an expensive source shared a
    /// counter, the optimal play would be to spam the cheap source until the counter is nearly full,
    /// then spend on the expensive one."</em>
    /// </remarks>
    [Fact]
    public void A_draw_of_one_class_never_moves_another_classes_counter()
    {
        var counters = PityCounters.Empty
            .With(StandardA, 9)
            .With(PremiumS, 4)
            .With("egg.pet:S", 29)
            .With("crate.mount:SS", 29);

        var resolution = LuckService.Resolve(
            SourceClass.CHEST_STANDARD, Tuning(), LuckTables.Only(Rarity.A), counters, Rng());

        Changes(resolution).ShouldBe(
            $"{StandardA}=0;{StandardS}=1;{StandardSs}=1",
            "the premium, egg and crate counters sit one draw from their own guarantees. A standard " +
            "chest that touched any of them would let the cheapest class in the game fill the most " +
            "expensive class's ladder.");
    }

    // ---------------------------------------------------------------- determinism

    /// <summary>The same seed and the same counters resolve to the same thing, byte for byte.</summary>
    [Fact]
    public void The_same_seed_and_counters_resolve_identically()
    {
        var counters = PityCounters.Empty.With(PremiumS, 2).With(PremiumSs, 12);

        var first = LuckService.Resolve(
            SourceClass.CHEST_PREMIUM, Tuning(), LuckTables.ChestPremium(), counters, Rng());
        var second = LuckService.Resolve(
            SourceClass.CHEST_PREMIUM, Tuning(), LuckTables.ChestPremium(), counters, Rng());

        Canonical(second).ShouldBe(
            Canonical(first),
            "14 §8.2: the same (seed, snapshot) is byte-identical on client and server. A resolution " +
            "that varied would make every container open unverifiable.");
    }

    /// <summary>
    /// …and the resolution is not simply a constant: across sixty-four seeds the premium table
    /// produces more than one rarity.
    /// </summary>
    /// <remarks>
    /// The floor under the case above, which an implementation returning a fixed rarity would pass.
    /// Sixty-four seeds rather than two, because two could legitimately coincide on a table whose
    /// commonest band is 45%.
    /// </remarks>
    [Fact]
    public void The_resolution_depends_on_the_draw_rather_than_being_a_constant()
    {
        var counters = PityCounters.Empty.With(PremiumS, 1).With(PremiumSs, 1);

        var outcomes = Enumerable.Range(1, 64)
            .Select(index => LuckService.Resolve(
                    SourceClass.CHEST_PREMIUM,
                    Tuning(),
                    LuckTables.ChestPremium(),
                    counters,
                    DeterministicRng.OpenAt((ulong)index, RngStreams.Drops, 0))
                .Outcome)
            .Distinct()
            .ToArray();

        outcomes.Length.ShouldBeGreaterThan(
            1,
            "24 §4.0a authors the premium chest as B 20 · A 45 · S 30 · SS 5. A resolution that " +
            "answered one band for every seed would satisfy every same-seed assertion in this file.");
    }

    /// <summary>Exactly one draw index is consumed when no guarantee fires.</summary>
    [Fact]
    public void A_resolution_with_no_guarantee_consumes_exactly_one_draw_index()
    {
        var draws = Rng();

        LuckService.Resolve(
            SourceClass.CHEST_PREMIUM,
            Tuning(),
            LuckTables.ChestPremium(),
            PityCounters.Empty,
            draws);

        draws.Position.ShouldBe(1UL);
    }

    /// <summary>Exactly one draw index is consumed when a guarantee <em>does</em> fire.</summary>
    /// <remarks>
    /// The property the whole "forcing is flooring" reading exists for: a forced draw that
    /// short-circuited to a fixed rarity would consume none, and a resumed stream would land in a
    /// different place depending on the player's counters — which are not part of the seed.
    /// </remarks>
    [Fact]
    public void A_resolution_whose_guarantee_fires_consumes_exactly_one_draw_index_as_well()
    {
        var draws = Rng();

        var resolution = LuckService.Resolve(
            SourceClass.CHEST_PREMIUM,
            Tuning(),
            LuckTables.ChestPremium(),
            PityCounters.Empty.With(PremiumS, 4),
            draws);

        resolution.FromPity.ShouldBeTrue();
        draws.Position.ShouldBe(1UL);
    }

    /// <summary>A resumed stream advances from where it was, one index per resolution.</summary>
    [Fact]
    public void A_resumed_stream_advances_by_one_index_per_resolution()
    {
        var draws = DeterministicRng.OpenAt(Seed, RngStreams.Drops, 41);

        LuckService.Resolve(
            SourceClass.CHEST_APEX, Tuning(), LuckTables.ChestApex(), PityCounters.Empty, draws);
        LuckService.Resolve(
            SourceClass.CHEST_APEX, Tuning(), LuckTables.ChestApex(), PityCounters.Empty, draws);

        draws.Position.ShouldBe(43UL, "the caller opens the stream; this service only ever continues it");
    }

    /// <summary>A floor the table cannot satisfy is refused, and consumes no draw index.</summary>
    [Fact]
    public void A_floor_the_table_cannot_satisfy_is_refused_before_the_draw()
    {
        var draws = Rng();

        Should.Throw<InvalidOperationException>(() => LuckService.Resolve(
            SourceClass.CHEST_PREMIUM,
            Tuning(),
            LuckTables.BelowA(),
            PityCounters.Empty,
            draws,
            Rarity.A));

        draws.Position.ShouldBe(
            0UL,
            "DeterministicRng's own discipline: a rejected call is not a call. A refusal that had " +
            "drawn would shift every later draw on the stream and desynchronise the persisted counter.");
    }

    /// <summary>A class that states its protection in another shape is refused, and consumes no index.</summary>
    [Theory]
    [InlineData(SourceClass.DROP_RUN)]
    [InlineData(SourceClass.ENHANCE)]
    [InlineData(SourceClass.DRAFT)]
    [InlineData(SourceClass.WHEEL)]
    [InlineData(SourceClass.MINIGAME)]
    public void A_class_with_no_rarity_ladder_is_refused_before_the_draw(SourceClass source)
    {
        var draws = Rng();

        Should.Throw<InvalidOperationException>(() => LuckService.Resolve(
                source, Tuning(), LuckTables.ChestPremium(), PityCounters.Empty, draws))
            .Message.ShouldContain(
                source.ToString(),
                Case.Sensitive,
                "the message names the class, the block that holds its real rule and the task that " +
                "wires it — several refusals here throw InvalidOperationException and the caller has " +
                "to be able to tell them apart.");

        draws.Position.ShouldBe(0UL);
    }

    // ---------------------------------------------------------------- the read-only questions

    /// <summary>The next draw is forced exactly when the counter stands one below the rung.</summary>
    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(23, false)]
    [InlineData(24, false)]
    public void GuaranteeFires_answers_the_same_question_the_resolution_decides(int misses, bool expected)
    {
        LuckService.GuaranteeFires(
                SourceClass.CHEST_PREMIUM,
                Tuning(),
                PityCounters.Empty.With(PremiumS, misses),
                Rarity.S)
            .ShouldBe(
                expected,
                "24 §1.1 Visibility: the client shows 'Guaranteed S in 1 chest'. It must be the same " +
                "decision the resolution makes, not a second statement of the rule that can drift.");
    }

    /// <summary>…and it agrees with what the resolution then reports.</summary>
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void GuaranteeFires_agrees_with_the_resolution_it_predicts(int misses)
    {
        var counters = PityCounters.Empty.With(PremiumS, misses);

        LuckService.Resolve(
                SourceClass.CHEST_PREMIUM, Tuning(), LuckTables.ChestPremium(), counters, Rng())
            .FromPity.ShouldBe(
                LuckService.GuaranteeFires(SourceClass.CHEST_PREMIUM, Tuning(), counters, Rarity.S));
    }

    /// <summary>A guarantee the class does not state is refused rather than answered false.</summary>
    /// <remarks>
    /// The premium chest has no A-rung at all (<c>24</c> §4.2 starts it at S), and answering false
    /// would read as "not yet" rather than "never" — the client would show a counter that can never
    /// reach its own guarantee.
    /// </remarks>
    [Theory]
    [InlineData(SourceClass.CHEST_PREMIUM, Rarity.A)]
    [InlineData(SourceClass.CHEST_APEX, Rarity.S)]
    [InlineData(SourceClass.DROP_RUN, Rarity.A)]
    public void GuaranteeFires_refuses_a_rung_the_class_does_not_state(SourceClass source, Rarity guarantee)
    {
        Should.Throw<InvalidOperationException>(
            () => LuckService.GuaranteeFires(source, Tuning(), PityCounters.Empty, guarantee));
    }

    /// <summary>The soft-pity weight is the class's own curve, read at the target's own counter.</summary>
    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(100, 1.0)]
    [InlineData(158, 3.9)]
    public void SoftPityWeight_is_the_classes_own_curve_at_its_targets_counter(int misses, double expected)
    {
        DeterminismRounding.Round(LuckService.SoftPityWeight(
                SourceClass.CHEST_STANDARD,
                Tuning(),
                PityCounters.Empty.With(StandardSs, misses)))
            .ShouldBe(
                expected,
                "24 §4.1's curve is on the 160-counter, not on the 10- or 40-counter. Reading the " +
                "wrong counter would ramp the SS odds off a counter that resets every ten chests.");
    }

    /// <summary>A class with no curve weighs 1, whatever its counters read.</summary>
    /// <remarks>The negative control: a ramp that ignored the authored null would invent a curve.</remarks>
    [Fact]
    public void A_class_that_authors_no_curve_weighs_one()
    {
        LuckService.SoftPityWeight(
                SourceClass.CHEST_APEX, Tuning(), PityCounters.Empty.With("chest.apex:SS", 2))
            .ShouldBe(1.0, "24 §4.2: 'no soft pity needed at that density'");
    }

    /// <summary>A class with no rarity ladder has no curve to ask about.</summary>
    [Fact]
    public void SoftPityWeight_refuses_a_class_with_no_rarity_ladder()
    {
        Should.Throw<InvalidOperationException>(
            () => LuckService.SoftPityWeight(SourceClass.WHEEL, Tuning(), PityCounters.Empty));
    }

    // ---------------------------------------------------------------- the mercy façade

    /// <summary>The façade answers <c>24</c> §4.6's formula, so no caller has to name the primitive.</summary>
    [Theory]
    [InlineData(3, 0.49)]
    [InlineData(10, 1.0)]
    public void MercyRate_answers_the_additive_ramp(int failures, double expected)
    {
        DeterminismRounding.Round(LuckService.MercyRate(0.25, failures, 0.08, 1.0)).ShouldBe(expected);
    }

    /// <summary>The mercy bank is reachable through the façade, accrual and redemption alike.</summary>
    [Fact]
    public void The_mercy_bank_is_reachable_through_the_facade()
    {
        LuckService.AccrueMercy(59, 1).ShouldBe(60);
        LuckService.RedeemMercy(60, 60).ShouldBe(0);
        Should.Throw<InvalidOperationException>(() => LuckService.RedeemMercy(59, 60));
    }

    // ---------------------------------------------------------------- refusals

    /// <summary>Every reference argument is required.</summary>
    [Fact]
    public void A_null_argument_is_refused()
    {
        var table = LuckTables.ChestPremium();

        Should.Throw<ArgumentNullException>(() => LuckService.Resolve(
            SourceClass.CHEST_PREMIUM, null!, table, PityCounters.Empty, Rng()));
        Should.Throw<ArgumentNullException>(() => LuckService.Resolve(
            SourceClass.CHEST_PREMIUM, Tuning(), null!, PityCounters.Empty, Rng()));
        Should.Throw<ArgumentNullException>(() => LuckService.Resolve(
            SourceClass.CHEST_PREMIUM, Tuning(), table, null!, Rng()));
        Should.Throw<ArgumentNullException>(() => LuckService.Resolve(
            SourceClass.CHEST_PREMIUM, Tuning(), table, PityCounters.Empty, null!));
    }

    /// <summary>An undeclared class or floor is refused, and consumes no draw index.</summary>
    [Theory]
    [InlineData(0, 3)]
    [InlineData(11, 3)]
    [InlineData(2, 0)]
    [InlineData(2, 6)]
    public void An_undeclared_class_or_floor_is_refused_before_the_draw(int source, int floor)
    {
        var draws = Rng();

        Should.Throw<ArgumentOutOfRangeException>(() => LuckService.Resolve(
            (SourceClass)source,
            Tuning(),
            LuckTables.ChestPremium(),
            PityCounters.Empty,
            draws,
            (Rarity)floor));

        draws.Position.ShouldBe(0UL);
    }

    private static LuckTuning Tuning() => LuckTuning.Read(LuckDocuments.Shipped);

    private static DeterministicRng Rng() => DeterministicRng.OpenAt(Seed, RngStreams.Drops, 0);

    /// <summary>One whole resolution as canonical text: outcome, pity flag, then the changes.</summary>
    private static string Canonical(LuckResolution resolution) =>
        $"outcome={resolution.Outcome};fromPity={(resolution.FromPity ? "1" : "0")};" +
        Changes(resolution);

    /// <summary>One resolution's counter changes, ordinal-sorted.</summary>
    private static string Changes(LuckResolution resolution) =>
        string.Join(
            ";",
            resolution.Changes
                .OrderBy(change => change.Key, StringComparer.Ordinal)
                .Select(change =>
                    $"{change.Key}={change.Value.ToString(CultureInfo.InvariantCulture)}"));
}
