using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Primitives;

/// <summary>The eight wallet currencies, and the counters deliberately not among them.</summary>
/// <remarks>
/// Pinned as literal names. Its sibling in <c>Application.Tests</c> pins the same enum against
/// <c>tuning/currencies.json</c> — independent on purpose: this one runs without a checkout, that
/// one catches the data and the code drifting apart.
/// </remarks>
public sealed class CurrencyIdTests
{
    /// <summary>The wallet, in the order <c>tuning/currencies.json</c> declares it.</summary>
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

        declared.Length.ShouldBe(
            8,
            "10 §1 fixes eight wallet currencies. Written as the literal 8 rather than as " +
            "Wallet.Length: a count taken from the transcription cannot notice the transcription " +
            "itself being trimmed, which is the one edit both set differences above would survive. " +
            "16 O10 may take this to seven in M18 — that is a 🔒 ruling, and it is meant to cost a " +
            "deliberate edit here.");
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

    /// <summary>
    /// Counters, not currencies. They live under <c>nonWalletCounters</c> and must never become
    /// <see cref="CurrencyId"/> members: a counter that gained a wallet slot would start emitting
    /// <c>CurrencyChanged</c> and land in the income-attribution report as income. One
    /// <c>[InlineData]</c> row per counter rather than a loop over an array field, since a quietly
    /// emptied array is a loop that asserts nothing and still passes.
    /// </summary>
    [Theory]
    [InlineData("BEAST_MARKS")]
    [InlineData("SET_TOKENS")]
    public void A_non_wallet_counter_of_10_1_1_is_not_a_currency(string counter)
    {
        Enum.TryParse<CurrencyId>(counter, out _).ShouldBeFalse(
            $"'{counter}' is a nonWalletCounter (10 §1.1), not a currency. Making it a CurrencyId " +
            "member would put it in the wallet, in CurrencyChanged, and in the income-attribution " +
            "report that answers risk R10.");
    }
}
