using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Application.Tests.Content;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Primitives;

/// <summary>
/// 🔒 `10` §1 — <see cref="CurrencyId"/> and <c>game-data/tuning/currencies.json</c> cannot drift
/// apart.
/// </summary>
/// <remarks>
/// <para>
/// The enum is the compile-time half of the currency list and the JSON is the authored half. Each
/// on its own is checkable and neither check notices the other moving: a ninth wallet entry added
/// to the data would have no member to pay out to, and a ninth member added to the enum would name
/// a currency with no display name, no scope and no price anywhere.
/// </para>
/// <para>
/// It lives in <c>SlayIdleRepeat.Application.Tests</c> rather than <c>Core.Tests</c> because it
/// reads a file off disk, and <c>Core.Tests</c> is hermetic. <see cref="RepoData"/> already exists
/// for exactly this and is reused rather than reimplemented — it is still no adapter, no port, no
/// container, no network (`23` §3), just <c>System.IO</c> over the checkout the test runs from.
/// </para>
/// </remarks>
public sealed class CurrencyIdMatchesTuningDataTests
{
    private const string CurrenciesDocument = "tuning/currencies.json";

    [Fact]
    public void CurrencyId_is_exactly_the_wallet_of_tuning_currencies_json()
    {
        var wallet = IdsOf("wallet");
        var declared = Enum.GetNames<CurrencyId>();

        wallet.ShouldNotBeEmpty(
            $"{CurrenciesDocument} declares no wallet entries, so this cross-check would compare the " +
            "enum against nothing and pass forever. The document moved — fix the reader, do not " +
            "delete the case.");

        wallet.Except(declared, StringComparer.Ordinal).ShouldBeEmpty(
            $"{CurrenciesDocument} authors a wallet currency that CurrencyId does not declare. No " +
            "exhaustive switch in Core can pay it out, so every reward carrying it is silently " +
            "unspendable.");

        declared.Except(wallet, StringComparer.Ordinal).ShouldBeEmpty(
            $"CurrencyId declares a currency that {CurrenciesDocument} does not author. It has no " +
            "display name, no scope and no price — the wallet would show a currency the game cannot " +
            "describe.");
    }

    [Fact]
    public void The_non_wallet_counters_of_tuning_currencies_json_are_not_currencies()
    {
        var counters = IdsOf("nonWalletCounters");

        counters.ShouldNotBeEmpty(
            $"{CurrenciesDocument} declares no nonWalletCounters. 10 §1.1 names two (BEAST_MARKS, " +
            "SET_TOKENS); an empty list means this case is asserting nothing.");

        counters.Intersect(Enum.GetNames<CurrencyId>(), StringComparer.Ordinal).ShouldBeEmpty(
            "a nonWalletCounter became a CurrencyId member. 10 §1.1 keeps counters out of the wallet " +
            "deliberately: a counter with a wallet slot starts emitting CurrencyChanged and lands in " +
            "the income-attribution report as income it never was.");
    }

    private static string[] IdsOf(string arrayProperty)
    {
        using var document = JsonDocument.Parse(RepoData.Documents[CurrenciesDocument]);

        return document.RootElement
            .GetProperty(arrayProperty)
            .EnumerateArray()
            .Select(entry => entry.GetProperty("id").GetString()!)
            .ToArray();
    }
}
