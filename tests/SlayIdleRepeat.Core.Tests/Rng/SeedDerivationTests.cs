using FluentAssertions;
using SlayIdleRepeat.Core.Rng;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rng;

/// <summary>
/// 🔒 `14` §8.1, the Battles rule — <c>battleSeed = Hash64(runSeed, "combat", battleIndex)</c>,
/// and combat draw <c>i</c> of that battle is <c>Hash64(battleSeed, "combat", i)</c>.
/// </summary>
/// <remarks>
/// This derivation is named rather than hand-rolled because it is the seam where the client
/// gets to simulate a fight without ever holding <c>runSeed</c> (`02` §2). A caller that
/// re-derived it inline would sooner or later write <c>battleIndex + 1</c>, or the wrong stream
/// name, and the client and server would disagree about a battle nobody could reproduce.
/// </remarks>
public sealed class SeedDerivationTests
{
    /// <summary>The derivation, pinned against the committed reference row.</summary>
    [Theory]
    [InlineData("derivation-battleseed-0", 0)]
    [InlineData("derivation-battleseed-1", 1)]
    public void BattleSeed_is_the_committed_hash_of_the_run_seed_the_combat_stream_and_the_index(
        string rowId, int battleIndex)
    {
        var row = ReferenceVectors.Row(rowId);
        var runSeed = 0x0123456789ABCDEFUL;

        SeedDerivation.BattleSeed(runSeed, battleIndex).Should().Be(row.Hash);
    }

    /// <summary>
    /// The derivation is exactly <c>Hash64(runSeed, "combat", battleIndex)</c> — spelled out so
    /// that changing the helper without changing the specification fails here rather than in
    /// M2's combat suite.
    /// </summary>
    [Fact]
    public void BattleSeed_is_Hash64_over_the_run_seed_the_combat_stream_and_the_battle_index()
    {
        var runSeed = 0xDEADBEEFCAFEF00DUL;

        SeedDerivation.BattleSeed(runSeed, 5)
            .Should().Be(Hash64.Of(runSeed, RngStreams.Combat, 5UL));
    }

    /// <summary>Each battle of a run gets its own seed.</summary>
    [Fact]
    public void BattleSeed_differs_for_every_battle_index_of_a_run()
    {
        var runSeed = 0x0123456789ABCDEFUL;

        var seeds = Enumerable.Range(0, 32).Select(index => SeedDerivation.BattleSeed(runSeed, index));

        seeds.Should().OnlyHaveUniqueItems();
    }

    /// <summary>And the same battle index of two runs is two different battles.</summary>
    [Fact]
    public void BattleSeed_differs_between_runs_for_the_same_battle_index()
    {
        SeedDerivation.BattleSeed(1UL, 0).Should().NotBe(SeedDerivation.BattleSeed(2UL, 0));
    }

    [Fact]
    public void BattleSeed_rejects_a_negative_battle_index()
    {
        var act = () => SeedDerivation.BattleSeed(1UL, -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// 🔒 The combat stream is re-rooted at the <c>battleSeed</c>, not at the <c>runSeed</c>.
    /// That is what keeps <c>runSeed</c> on the server: a client holding the battle seed can
    /// reproduce the fight and nothing else.
    /// </summary>
    [Fact]
    public void A_combat_draw_is_rooted_at_the_battle_seed_not_at_the_run_seed()
    {
        var row = ReferenceVectors.DrawRow("combat-0");
        var runSeed = 0x0123456789ABCDEFUL;
        var battleSeed = SeedDerivation.BattleSeed(runSeed, 0);

        battleSeed.Should().Be(row.Seed);
        new DeterministicRng(battleSeed, RngStreams.Combat).NextUInt().Should().Be(row.NextUInt);
        new DeterministicRng(runSeed, RngStreams.Combat).NextUInt().Should().NotBe(row.NextUInt);
    }

    /// <summary>
    /// 🔒 "A revived battle restarts from draw 0 of the same battle stream: reproducible by
    /// construction." No state survives a revive — the battle seed and index 0 are the whole
    /// story.
    /// </summary>
    [Fact]
    public void A_revived_battle_replays_the_same_sequence_from_draw_zero()
    {
        var battleSeed = SeedDerivation.BattleSeed(0x0123456789ABCDEFUL, 3);

        var firstAttempt = new DeterministicRng(battleSeed, RngStreams.Combat);
        var beforeTheRevive = new[] { firstAttempt.NextUInt(), firstAttempt.NextUInt(), firstAttempt.NextUInt() };

        var afterTheRevive = new DeterministicRng(battleSeed, RngStreams.Combat);

        new[] { afterTheRevive.NextUInt(), afterTheRevive.NextUInt(), afterTheRevive.NextUInt() }
            .Should().Equal(beforeTheRevive);
    }
}
