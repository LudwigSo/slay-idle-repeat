using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// The player's pity counter map: what a counter reads, how it moves, and the two things <c>24</c>
/// §1.1 says it must never do — decay, or reset for any reason but its own guarantee firing.
/// </summary>
/// <remarks>
/// Whole maps are compared as canonical text rather than by object identity: the map's only
/// component is a dictionary, so reference equality would call two identical maps different and
/// record equality would call two different maps the same.
/// </remarks>
public sealed class PityCountersTests
{
    private const string ChestA = "chest.standard:A";
    private const string ChestS = "chest.standard:S";
    private const string PremiumS = "chest.premium:S";

    // ---------------------------------------------------------------- reading

    /// <summary>A counter nobody has advanced reads as unstarted, not as absent and not as an error.</summary>
    /// <remarks>
    /// The map is sparse by design — a new account stores no rows at all — so "never advanced" has to
    /// be a value the reader answers rather than a lookup the caller has to guard.
    /// </remarks>
    [Theory]
    [InlineData(ChestA)]
    [InlineData("egg.pet:SS")]
    [InlineData("nothing.at.all:C")]
    public void A_counter_that_has_never_advanced_reads_as_unstarted(string key)
    {
        PityCounters.Empty.Get(key).ShouldBe(PityCounters.Unstarted);
    }

    /// <summary>A set counter reads back exactly what it was set to.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(159)]
    public void A_counter_reads_back_the_value_it_was_set_to(int value)
    {
        PityCounters.Empty.With(ChestA, value).Get(ChestA).ShouldBe(value);
    }

    /// <summary>Keys are compared ordinally: a differently-cased key is a different counter.</summary>
    /// <remarks>
    /// The keys are authored ids, not display text. A culture- or case-insensitive map would address
    /// one counter on one device and two on another, and the counter is a server column the client
    /// only renders.
    /// </remarks>
    [Fact]
    public void Counter_keys_are_compared_ordinally()
    {
        var counters = PityCounters.Empty.With(ChestA, 7);

        counters.Get("CHEST.STANDARD:A").ShouldBe(PityCounters.Unstarted);
        counters.Get("chest.standard:a").ShouldBe(PityCounters.Unstarted);
        counters.Get(ChestA).ShouldBe(7);
    }

    // ---------------------------------------------------------------- moving

    /// <summary>A draw that missed moves the counter on by one.</summary>
    [Fact]
    public void Advancing_moves_the_counter_on_by_one()
    {
        PityCounters.Empty.Advanced(ChestA).Get(ChestA).ShouldBe(1);
        PityCounters.Empty.With(ChestA, 39).Advanced(ChestA).Get(ChestA).ShouldBe(40);
    }

    /// <summary>Advancing one counter leaves every other counter exactly where it was.</summary>
    /// <remarks>
    /// <c>24</c> §1.2's anti-farming rule at the storage layer: counters never pool across classes,
    /// and <c>CHEST_STANDARD</c>'s own three ladders run independently of one another.
    /// </remarks>
    [Fact]
    public void Advancing_one_counter_leaves_every_other_counter_alone()
    {
        var before = PityCounters.Empty.With(ChestA, 9).With(ChestS, 39).With(PremiumS, 4);

        var after = before.Advanced(ChestA);

        Canonical(after).ShouldBe("chest.premium:S=4;chest.standard:A=10;chest.standard:S=39");
        Canonical(before).ShouldBe(
            "chest.premium:S=4;chest.standard:A=9;chest.standard:S=39",
            "the map is immutable and replaced wholesale — a resolution answers the counters it would " +
            "leave behind, and the aggregate decides whether to keep them");
    }

    /// <summary>Counters never tick down.</summary>
    /// <remarks>
    /// <c>24</c> §1.1: <em>"counters never tick down over time. Bad luck is not a debt that
    /// expires."</em> Stated as a comparison against the value before, so an implementation answering
    /// a constant cannot satisfy it — and there is no clock to advance in the first place, which is
    /// the point.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(159)]
    public void Advancing_never_lowers_a_counter(int value)
    {
        PityCounters.Empty.With(ChestA, value).Advanced(ChestA).Get(ChestA).ShouldBeGreaterThan(value);
    }

    /// <summary>A reset puts one counter back to unstarted.</summary>
    [Fact]
    public void Resetting_puts_the_counter_back_to_unstarted()
    {
        PityCounters.Empty.With(ChestA, 159).Reset(ChestA).Get(ChestA).ShouldBe(PityCounters.Unstarted);
    }

    /// <summary>
    /// A counter resets only when it is told to, and never as a side effect of another counter's
    /// movement.
    /// </summary>
    /// <remarks>
    /// <c>24</c> §1.1 Persistence: counters <em>"reset <b>only</b> when their guarantee fires"</em> —
    /// never on chapter change, tier change, season roll, app update, subscription lapse or logout.
    /// None of those events can reach this type at all; what it can get wrong is resetting a
    /// neighbour, which is what this pins.
    /// </remarks>
    [Fact]
    public void Resetting_one_counter_leaves_every_other_counter_where_it_stood()
    {
        var counters = PityCounters.Empty.With(ChestA, 9).With(ChestS, 39).With(PremiumS, 4);

        Canonical(counters.Reset(ChestA)).ShouldBe(
            "chest.premium:S=4;chest.standard:A=0;chest.standard:S=39",
            "chest #10 satisfies the A-rung alone. Resetting the 40-counter with it would hand the " +
            "player a guarantee they had not earned and 24 §4.1's ladder would collapse to one rung.");
    }

    /// <summary>Setting a counter answers a new map and leaves the old one untouched.</summary>
    [Fact]
    public void Setting_a_counter_answers_a_new_map()
    {
        var before = PityCounters.Empty.With(ChestA, 5);

        before.With(ChestA, 99);

        before.Get(ChestA).ShouldBe(5);
    }

    // ---------------------------------------------------------------- storage

    /// <summary>A stored map rehydrates to exactly what was stored.</summary>
    /// <remarks>
    /// The counter map is one JSONB column on the player profile (<c>24</c> §11), so this round trip
    /// is what a player's whole pity history survives on.
    /// </remarks>
    [Fact]
    public void A_stored_map_rehydrates_to_exactly_what_was_stored()
    {
        var stored = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [ChestA] = 9,
            [ChestS] = 39,
            ["chest.standard:SS"] = 159,
        };

        Canonical(PityCounters.Rehydrate(stored)).ShouldBe(
            "chest.standard:A=9;chest.standard:S=39;chest.standard:SS=159");
    }

    /// <summary>An empty stored map rehydrates to a player who has never drawn anything.</summary>
    [Fact]
    public void An_empty_stored_map_rehydrates_to_an_unstarted_player()
    {
        Canonical(PityCounters.Rehydrate(new Dictionary<string, int>(StringComparer.Ordinal))).ShouldBe(
            Canonical(PityCounters.Empty));
    }

    /// <summary>The read-only view cannot be written through.</summary>
    /// <remarks>
    /// An <c>IReadOnlyDictionary</c> that <em>is</em> the backing store can be cast back and rewritten,
    /// and these counters are server-owned: the client renders a number it was told (<c>14</c> §2.1).
    /// </remarks>
    [Fact]
    public void The_counter_view_cannot_be_written_through()
    {
        var view = (IDictionary<string, int>)PityCounters.Empty.With(ChestA, 5).Counters;

        Should.Throw<NotSupportedException>(() => view[ChestA] = 99);
        Should.Throw<NotSupportedException>(() => view.Remove(ChestA));
    }

    /// <summary>A stored row that is not a counter is refused rather than loaded.</summary>
    /// <remarks>
    /// Pinned by parameter, because <see cref="ArgumentOutOfRangeException"/> <em>is</em> an
    /// <see cref="ArgumentException"/>: a value guard firing on a blank-key row would satisfy the
    /// type assertion alone and leave the key unchecked.
    /// </remarks>
    [Theory]
    [InlineData("", 1)]
    [InlineData(" ", 1)]
    [InlineData(ChestA, -1)]
    public void A_stored_row_that_is_not_a_counter_is_refused(string key, int value)
    {
        Should.Throw<ArgumentException>(() => PityCounters.Rehydrate(
                new Dictionary<string, int>(StringComparer.Ordinal) { [key] = value }))
            .ParamName.ShouldBe("counters");
    }

    /// <summary>A negative counter is not a counter.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void A_negative_counter_value_is_refused(int value)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => PityCounters.Empty.With(ChestA, value))
            .ParamName.ShouldBe(
                "value",
                "the key passed here is a legal one, so a refusal naming it would be the wrong " +
                "guard firing and every negative counter would still get stored.");
    }

    /// <summary>Every entry point refuses a null key rather than storing one.</summary>
    [Fact]
    public void A_null_key_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => PityCounters.Empty.Get(null!))
            .ParamName.ShouldBe("key");
        Should.Throw<ArgumentNullException>(() => PityCounters.Empty.With(null!, 1))
            .ParamName.ShouldBe("key");
        Should.Throw<ArgumentNullException>(() => PityCounters.Empty.Advanced(null!))
            .ParamName.ShouldBe("key");
        Should.Throw<ArgumentNullException>(() => PityCounters.Empty.Reset(null!))
            .ParamName.ShouldBe("key");
        Should.Throw<ArgumentNullException>(() => PityCounters.Rehydrate(null!))
            .ParamName.ShouldBe("counters");
    }

    /// <summary>One map as ordinal-sorted <c>key=value</c> text.</summary>
    private static string Canonical(PityCounters counters) =>
        string.Join(
            ";",
            counters.Counters
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => $"{entry.Key}={entry.Value.ToString(CultureInfo.InvariantCulture)}"));
}
