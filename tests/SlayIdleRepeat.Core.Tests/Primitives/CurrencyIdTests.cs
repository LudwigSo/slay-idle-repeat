using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Primitives;

/// <summary>
/// `10` §1 — the eight wallet currencies, and the counters that are deliberately not among them.
/// </summary>
/// <remarks>
/// <para>
/// This suite pins the enum against the 🔒 design decision, written out as literal names. Its
/// sibling in <c>SlayIdleRepeat.Application.Tests</c> pins the same enum against
/// <c>game-data/tuning/currencies.json</c>; the two are independent on purpose — this one can run
/// without a checkout, that one catches the data and the code drifting apart.
/// </para>
/// <para>
/// ⚠️ `16` O10 asks whether Merge Dust and Enhance Stones merge into one currency, taking the list
/// from 8 to 7. It is scheduled for a post-playtest review in M18. Until it is ruled on, eight.
/// </para>
/// </remarks>
public sealed class CurrencyIdTests
{
    /// <summary>The `10` §1 wallet, in the order <c>tuning/currencies.json</c> declares it.</summary>
    private static readonly (string Name, int Wire)[] Wallet =
    {
        ("GOLD", 1),
        ("CROWNS", 2),
        ("SOUL_SHARDS", 3),
        ("ENERGY", 4),
        ("ENHANCE_STONES", 5),
        ("MERGE_DUST", 6),
        ("BEAST_FEED", 7),
        ("HONOR", 8),
    };

    /// <summary>
    /// 🔒 `10` §1.1 — counters, not currencies. They live under <c>nonWalletCounters</c> and must
    /// never become <see cref="CurrencyId"/> members: a counter that gained a wallet slot would
    /// start emitting <c>CurrencyChanged</c> and land in the income-attribution report as income.
    /// </summary>
    private static readonly string[] NonWalletCounters = { "BEAST_MARKS", "SET_TOKENS" };

    [Fact]
    public void The_wallet_is_exactly_the_eight_currencies_of_10_1()
    {
        var declared = Enum.GetNames<CurrencyId>();
        var pinned = Wallet.Select(row => row.Name).ToArray();

        declared.Except(pinned, StringComparer.Ordinal).ShouldBeEmpty(
            "CurrencyId declares a currency 10 §1 does not fix. The currency list is a 🔒 decision; " +
            "a ninth member is a design change, not an implementation detail.");

        pinned.Except(declared, StringComparer.Ordinal).ShouldBeEmpty(
            "10 §1 fixes a currency CurrencyId does not declare. Every wallet currency needs a member, " +
            "or the exhaustive switches that pay it out cannot name it.");

        declared.Length.ShouldBe(Wallet.Length);
    }

    [Fact]
    public void Every_currency_is_pinned_to_its_permanent_wire_number()
    {
        foreach (var (name, wire) in Wallet)
        {
            Enum.TryParse<CurrencyId>(name, out var value).ShouldBeTrue(
                $"10 §1 fixes '{name}'; CurrencyId does not declare it.");

            ((int)value).ShouldBe(
                wire,
                $"CurrencyId.{name} is numbered {(int)value}, not {wire}. CanonicalStateWriter encodes " +
                "an enum as its numeric value (14 §16.6) and sorts an enum-keyed wallet map by it, so " +
                "renumbering rewrites every stateHash that has ever carried a wallet.");
        }
    }

    [Fact]
    public void No_two_currencies_share_a_wire_number()
    {
        var numbers = Enum.GetValues<CurrencyId>().Select(value => (int)value).ToArray();

        numbers.Distinct().Count().ShouldBe(
            numbers.Length,
            "two CurrencyId members share a numeric value, so a wallet keyed by CurrencyId would " +
            "silently merge two balances into one. Duplicates: " +
            string.Join(", ", numbers.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key)));
    }

    [Fact]
    public void Zero_is_not_a_currency()
    {
        Enum.IsDefined((CurrencyId)0).ShouldBeFalse(
            "0 is default(CurrencyId). Numbering starts at 1 so an uninitialised field cannot read " +
            "as GOLD and quietly credit the wrong wallet.");
    }

    [Fact]
    public void The_non_wallet_counters_of_10_1_1_are_not_currencies()
    {
        foreach (var counter in NonWalletCounters)
        {
            Enum.TryParse<CurrencyId>(counter, out _).ShouldBeFalse(
                $"'{counter}' is a nonWalletCounter (10 §1.1), not a currency. Making it a CurrencyId " +
                "member would put it in the wallet, in CurrencyChanged, and in the income-attribution " +
                "report that answers risk R10.");
        }
    }
}
