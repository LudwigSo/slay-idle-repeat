using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rng;

/// <summary>
/// 🔒 The two named seed derivations of the game: `02` §2's
/// <c>runSeed = Hash64(playerId, chapterId, tierId, utcUnixSeconds, runCounter)</c>, where a seed is
/// born, and `14` §8.1's <c>battleSeed = Hash64(runSeed, "combat", battleIndex)</c>, where one seed
/// spawns another. Combat draw <c>i</c> of a battle is then <c>Hash64(battleSeed, "combat", i)</c>.
/// </summary>
/// <remarks>
/// This derivation is named rather than hand-rolled because it is the seam where the client
/// gets to simulate a fight without ever holding <c>runSeed</c> (`02` §2). A caller that
/// re-derived it inline would sooner or later write <c>battleIndex + 1</c>, or the wrong stream
/// name, and the client and server would disagree about a battle nobody could reproduce.
/// </remarks>
public sealed class SeedDerivationTests
{
    // ------------------------------------------------------------------- runSeed
    //
    // 🔒 `02` §2 — runSeed = Hash64(playerId, chapterId, tierId, utcUnixSeconds, runCounter).
    // The home for this function already existed (this class's own remarks say so); M1-05 wrote it.

    private static readonly PlayerId Player = new("PLAYER_TEST");

    /// <summary>2026-08-12 09:41:07.123 UTC — deliberately carrying sub-second precision.</summary>
    private static readonly DateTimeOffset Midmorning =
        new(2026, 8, 12, 9, 41, 7, 123, TimeSpan.Zero);

    /// <summary>
    /// 🔒 `02` §2, argument for argument. Spelled out rather than restated through the helper, so
    /// changing the derivation without changing the specification fails here.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>ToUnixTimeSeconds()</c> is written out on the right-hand side because it <b>is</b> `02`
    /// §2's <c>floor(NowUtc as Unix seconds)</c>: the flooring belongs to the derivation, not to
    /// every caller who might otherwise round.
    /// </remarks>
    [Fact]
    public void RunSeed_is_Hash64_over_the_five_arguments_of_02_section_2()
    {
        SeedDerivation.RunSeed(Player, 3, DifficultyTier.HEROIC, Midmorning, 613)
            .ShouldBe(Hash64.Of(
                Player.Value,
                3,
                DifficultyTier.HEROIC,
                Midmorning.ToUnixTimeSeconds(),
                613L));
    }

    /// <summary>The same five inputs always produce the same seed — the whole point of `02` §2.</summary>
    [Fact]
    public void RunSeed_is_reproducible_for_fixed_inputs()
    {
        var first = SeedDerivation.RunSeed(Player, 3, DifficultyTier.HEROIC, Midmorning, 613);
        var second = SeedDerivation.RunSeed(Player, 3, DifficultyTier.HEROIC, Midmorning, 613);

        second.ShouldBe(first);
    }

    /// <summary>
    /// 🔒 …and changing <b>any one</b> of the five moves it — <c>runCounter</c> included, which is
    /// the argument that exists precisely so two runs started in the same second on the same chapter
    /// and tier draw different boards.
    /// </summary>
    /// <remarks>
    /// ⚠️ Stated as five one-field mutations against one baseline and asserted as <b>six distinct
    /// seeds</b>, not as five pairwise inequalities: an implementation that ignored, say,
    /// <c>chapterId</c> but happened to differ from the baseline for another reason would pass the
    /// pairwise form. The count is the assertion.
    /// </remarks>
    [Fact]
    public void RunSeed_changes_when_any_one_of_its_five_inputs_changes()
    {
        var seeds = new[]
        {
            SeedDerivation.RunSeed(Player, 3, DifficultyTier.HEROIC, Midmorning, 613),
            SeedDerivation.RunSeed(new PlayerId("OTHER_PLAYER"), 3, DifficultyTier.HEROIC, Midmorning, 613),
            SeedDerivation.RunSeed(Player, 4, DifficultyTier.HEROIC, Midmorning, 613),
            SeedDerivation.RunSeed(Player, 3, DifficultyTier.MYTHIC, Midmorning, 613),
            SeedDerivation.RunSeed(Player, 3, DifficultyTier.HEROIC, Midmorning.AddSeconds(1), 613),
            SeedDerivation.RunSeed(Player, 3, DifficultyTier.HEROIC, Midmorning, 614),
        };

        seeds.Length.ShouldBe(6, "one baseline plus one mutation per argument of 02 §2's five.");
        seeds.ShouldBeUnique();
    }

    /// <summary>
    /// 🔒 The instant is floored to whole seconds, because `02` §2 hashes <c>utcUnixSeconds</c>.
    /// </summary>
    /// <remarks>
    /// Both halves. Two instants inside the same second are one seed — otherwise the derivation would
    /// depend on a clock precision the wire never carries — and the next second is a different one,
    /// which is what stops the flooring being "ignore the timestamp".
    /// </remarks>
    [Fact]
    public void RunSeed_floors_the_instant_to_whole_seconds()
    {
        var sameSecond = new DateTimeOffset(2026, 8, 12, 9, 41, 7, 987, TimeSpan.Zero);
        var nextSecond = new DateTimeOffset(2026, 8, 12, 9, 41, 8, 0, TimeSpan.Zero);

        SeedDerivation.RunSeed(Player, 3, DifficultyTier.HEROIC, sameSecond, 613)
            .ShouldBe(SeedDerivation.RunSeed(Player, 3, DifficultyTier.HEROIC, Midmorning, 613));

        SeedDerivation.RunSeed(Player, 3, DifficultyTier.HEROIC, nextSecond, 613)
            .ShouldNotBe(SeedDerivation.RunSeed(Player, 3, DifficultyTier.HEROIC, Midmorning, 613));
    }

    /// <summary>
    /// 🔒 There is no zero-offset guard, and there does not need to be: an offset naming the same
    /// instant converts to the same Unix seconds.
    /// </summary>
    /// <remarks>
    /// This is the one place the aggregates' rule does <b>not</b> apply. <c>Run.Rehydrate</c> refuses
    /// an offset <see cref="DateTimeOffset"/> because <c>CanonicalStateWriter</c> would hash two
    /// spellings of one instant identically while record equality called them different. Here only
    /// the converted number is hashed, so the ambiguity cannot arise — and a guard would be a branch
    /// no legitimate input reaches, which is steering S1's defect rather than defence in depth.
    /// </remarks>
    [Fact]
    public void RunSeed_is_offset_agnostic_because_it_hashes_the_converted_seconds()
    {
        var elsewhere = Midmorning.ToOffset(TimeSpan.FromHours(2));

        elsewhere.Offset.ShouldBe(TimeSpan.FromHours(2), "the fixture must actually carry an offset");

        SeedDerivation.RunSeed(Player, 3, DifficultyTier.HEROIC, elsewhere, 613)
            .ShouldBe(SeedDerivation.RunSeed(Player, 3, DifficultyTier.HEROIC, Midmorning, 613));
    }

    /// <summary>
    /// A chapter below 1 is refused — <c>chapter.schema.json</c> sets <c>"minimum": 1</c>. Seeding a
    /// run for a chapter that cannot exist is a caller bug, not a run.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RunSeed_refuses_a_chapter_below_one(int chapterId)
    {
        Should.Throw<ArgumentOutOfRangeException>(
                  () => SeedDerivation.RunSeed(Player, chapterId, DifficultyTier.NORMAL, Midmorning, 0))
              .ParamName.ShouldBe(
                  "chapterId",
                  "three of this function's five arguments are guarded with the same exception " +
                  "type, so the type alone does not say which guard fired (steering S2).");
    }

    /// <summary>
    /// An undefined tier is refused, <c>default(DifficultyTier)</c> included — a zero widened into
    /// the hash would produce a perfectly stable seed for a difficulty the game does not have.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void RunSeed_refuses_an_undefined_tier(int tier)
    {
        Should.Throw<ArgumentOutOfRangeException>(
                  () => SeedDerivation.RunSeed(Player, 1, (DifficultyTier)tier, Midmorning, 0))
              .ParamName.ShouldBe(
                  "tier",
                  "the exception type alone is shared with the chapter and counter guards " +
                  "(steering S2).");
    }

    /// <summary>A negative <c>runCounter</c> is refused: it counts upwards from zero and is never reset.</summary>
    [Fact]
    public void RunSeed_refuses_a_negative_run_counter()
    {
        Should.Throw<ArgumentOutOfRangeException>(
                  () => SeedDerivation.RunSeed(Player, 1, DifficultyTier.NORMAL, Midmorning, -1))
              .ParamName.ShouldBe(
                  "runCounter",
                  "the exception type alone is shared with the chapter and tier guards " +
                  "(steering S2).");
    }

    /// <summary>
    /// ⚠️ …and a <b>zero</b> <c>runCounter</c> is accepted, deliberately.
    /// </summary>
    /// <remarks>
    /// <c>Player.BeginRun()</c> returns the counter <em>after</em> the increment, so in practice the
    /// first run is seeded with 1 — but nothing in `02` §2 says the counter's first value is 1, and
    /// refusing 0 would be a claim the documents do not authorise (steering <b>S6</b>). This case is
    /// what keeps that restraint from being tidied away later.
    /// </remarks>
    [Fact]
    public void RunSeed_accepts_a_zero_run_counter_because_no_document_forbids_it()
    {
        Should.NotThrow(() => SeedDerivation.RunSeed(Player, 1, DifficultyTier.NORMAL, Midmorning, 0));
    }

    /// <summary>
    /// A <c>default(PlayerId)</c> has a null <c>Value</c>, which has no canonical encoding — the hash
    /// refuses it rather than hashing an empty string and producing a stable seed for no player.
    /// </summary>
    [Fact]
    public void RunSeed_refuses_a_default_PlayerId_because_null_has_no_canonical_encoding()
    {
        Should.Throw<ArgumentNullException>(
            () => SeedDerivation.RunSeed(default(PlayerId), 1, DifficultyTier.NORMAL, Midmorning, 0));
    }

    // 🔒 There was a `RunSeed_and_BattleSeed_are_different_derivations` case here. It asserted that
    // BattleSeed(runSeed, 0..31) never returns runSeed itself — which is true of ANY hash of any
    // seed and would hold even if the two derivations had been refactored into one another, so its
    // name promised something its assertion could never deliver (steering S1). The claim it was
    // reaching for is already covered with teeth:
    // RunSeed_is_Hash64_over_the_five_arguments_of_02_section_2 pins this derivation argument for
    // argument, BattleSeed_is_Hash64_over_the_run_seed_the_combat_stream_and_the_battle_index pins
    // the other, and BattleSeed_differs_for_every_battle_index_of_a_run pins the injectivity
    // RunRngStreamTests leans on.

    // ----------------------------------------------------------------- battleSeed

    /// <summary>The derivation, pinned against the committed reference row.</summary>
    [Theory]
    [InlineData("derivation-battleseed-0", 0)]
    [InlineData("derivation-battleseed-1", 1)]
    public void BattleSeed_is_the_committed_hash_of_the_run_seed_the_combat_stream_and_the_index(
        string rowId, int battleIndex)
    {
        var row = ReferenceVectors.Row(rowId);
        var runSeed = 0x0123456789ABCDEFUL;

        SeedDerivation.BattleSeed(runSeed, battleIndex).ShouldBe(row.Hash);
    }

    /// <summary>
    /// The derivation is exactly <c>Hash64(runSeed, "combat", battleIndex)</c> — spelled out so
    /// that changing the helper without changing the specification fails here rather than in
    /// M2's combat suite.
    /// </summary>
    /// <remarks>
    /// The stream name is the <b>literal</b> <c>"combat"</c>, not <c>RngStreams.Combat</c>. Writing
    /// the constant here restates the implementation through the same symbol it uses, so a change
    /// to the constant's value would move the derivation and this assertion together, in silence.
    /// The literal also catches that. <c>RngStreams.Combat</c> is pinned to the same literal
    /// separately, so the indirection loses nothing.
    /// </remarks>
    [Fact]
    public void BattleSeed_is_Hash64_over_the_run_seed_the_combat_stream_and_the_battle_index()
    {
        var runSeed = 0xDEADBEEFCAFEF00DUL;

        RngStreams.Combat.ShouldBe("combat");
        SeedDerivation.BattleSeed(runSeed, 5)
            .ShouldBe(Hash64.Of(runSeed, "combat", 5UL));
    }

    /// <summary>Each battle of a run gets its own seed.</summary>
    [Fact]
    public void BattleSeed_differs_for_every_battle_index_of_a_run()
    {
        var runSeed = 0x0123456789ABCDEFUL;

        var seeds = Enumerable.Range(0, 32).Select(index => SeedDerivation.BattleSeed(runSeed, index));

        seeds.ShouldBeUnique();
    }

    /// <summary>And the same battle index of two runs is two different battles.</summary>
    [Fact]
    public void BattleSeed_differs_between_runs_for_the_same_battle_index()
    {
        SeedDerivation.BattleSeed(1UL, 0).ShouldNotBe(SeedDerivation.BattleSeed(2UL, 0));
    }

    [Fact]
    public void BattleSeed_rejects_a_negative_battle_index()
    {
        Action act = () => _ = SeedDerivation.BattleSeed(1UL, -1);

        Should.Throw<ArgumentOutOfRangeException>(act);
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

        battleSeed.ShouldBe(row.Seed);
        new DeterministicRng(battleSeed, RngStreams.Combat).NextUInt().ShouldBe(row.NextUInt);
        new DeterministicRng(runSeed, RngStreams.Combat).NextUInt().ShouldNotBe(row.NextUInt);
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
            .ShouldBe(beforeTheRevive);
    }
}
