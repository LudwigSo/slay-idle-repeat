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

        // 🔒 The floor, and it is named rather than counted: the intersection below is empty for an
        // empty subject set, so without this the case passes forever the moment the reader stops
        // finding counters. `ShouldNotBeEmpty` would not be that floor either — it accepts one, while
        // the sentence it is defending names two. Deliberately a floor and not a ceiling: 10 §1.1 may
        // gain a third counter, and that is not this case's business.
        counters.ShouldContain(
            "BEAST_MARKS",
            $"10 §1.1 names BEAST_MARKS a nonWalletCounter, and {CurrenciesDocument} does not author " +
            "it. The document moved — fix the reader, do not delete the case.");

        counters.ShouldContain(
            "SET_TOKENS",
            $"10 §1.1 names SET_TOKENS a nonWalletCounter, and {CurrenciesDocument} does not author " +
            "it. The document moved — fix the reader, do not delete the case.");

        counters.Intersect(Enum.GetNames<CurrencyId>(), StringComparer.Ordinal).ShouldBeEmpty(
            "a nonWalletCounter became a CurrencyId member. 10 §1.1 keeps counters out of the wallet " +
            "deliberately: a counter with a wallet slot starts emitting CurrencyChanged and lands in " +
            "the income-attribution report as income it never was.");
    }

    /// <summary>
    /// 🔒 `10` §1 / milestone assumption <b>A3</b> — the <b>scope split</b> is authored too, and
    /// <c>Player.WalletCurrencies</c> is exactly the <c>META</c> half of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The case above pins the currency <em>set</em>; this one pins the <em>partition</em>. M1-04
    /// re-expressed that partition in C# — six currencies on the <c>Player</c> aggregate, with
    /// <c>GOLD</c> on <c>Run</c> and <c>ENERGY</c> in the two banks of `28` C — and nothing
    /// connected the two halves. A scope changed in the data, or a ninth <c>META</c> currency
    /// authored, would desynchronise silently, and the failure mode is the worst kind:
    /// <c>Player.Rehydrate</c> refusing <b>every</b> persisted row, because its wallet would be
    /// missing a currency the aggregate requires or carrying one it forbids.
    /// </para>
    /// <para>
    /// It is stated as an exact partition rather than a subset, so a currency moving from
    /// <c>RUN</c> to <c>META</c> is a red build on the commit that moves it rather than on the
    /// commit that next loads a player.
    /// </para>
    /// </remarks>
    [Fact]
    public void Player_WalletCurrencies_is_exactly_the_META_scoped_half_of_the_authored_split()
    {
        var meta = IdsOfScope("META");
        var onThePlayer = Core.Model.Player.WalletCurrencies.Select(c => c.ToString()).ToArray();

        meta.ShouldNotBeEmpty(
            $"{CurrenciesDocument} authors no META-scoped wallet currency, so this cross-check " +
            "would compare the aggregate against nothing. The document moved — fix the reader.");

        meta.OrderBy(id => id, StringComparer.Ordinal).ShouldBe(
            onThePlayer.OrderBy(id => id, StringComparer.Ordinal),
            $"Player.WalletCurrencies and {CurrenciesDocument}'s META scope disagree. The aggregate " +
            "requires every one of its currencies to be present in a persisted wallet and refuses " +
            "any that is not, so a mismatch here makes Player.Rehydrate reject every stored row.");

        IdsOfScope("RUN").ShouldBe(
            new[] { "GOLD" },
            "milestone assumption A3: GOLD is the run-scoped currency, and it lives on the Run " +
            "aggregate (M1-05) rather than in Player's wallet.");

        IdsOfScope("GATE").ShouldBe(
            new[] { "ENERGY" },
            "10 §3 scopes ENERGY as the run GATE. It is player-scoped state but has two banks " +
            "(28 C), so Player holds it as EnergyBanks rather than as a wallet row.");
    }

    private static string[] IdsOfScope(string scope)
    {
        using var document = JsonDocument.Parse(RepoData.Documents[CurrenciesDocument]);

        return document.RootElement
            .GetProperty("wallet")
            .EnumerateArray()
            .Where(entry => entry.GetProperty("scope").GetString() == scope)
            .Select(entry => entry.GetProperty("id").GetString()!)
            .ToArray();
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
