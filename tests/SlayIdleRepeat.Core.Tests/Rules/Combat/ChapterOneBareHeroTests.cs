using Shouldly;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// Chapter 1 is clearable by the hero a new player actually has: Legend Level 1, nothing equipped,
/// nothing upgraded, and no power in the run but the perks it drafted.
/// </summary>
/// <remarks>
/// <para>
/// This is what chapter 1's par of 175 and its elite multiplier of 1.4 are FOR, and the only place
/// the claim is stated as a fight rather than as a number. At the shipped 1000 and 2.2 none of the
/// cases below passed: the opening GRUNT killed the hero in ~7 s while needing ~20 s to die, and the
/// stage-1 <c>WARDEN</c> mini-boss reached <c>DEF 201</c> — a 197 s time-to-kill against a 90 s
/// fight cap, which is a wall no draft can pass.
/// </para>
/// <para>
/// 🔒 <b>Every fight here runs the real seam.</b> <c>RunBattle.Simulate</c> is what
/// <c>START_BATTLE</c> and <c>CONFIRM_BATTLE_RESULT</c> call, so each case reads the shipped par
/// table, the shipped power curve, the chapter's own elite multiplier, the real enemy draw and the
/// real perk aggregation. A case that composed its own enemy would be a claim about arithmetic
/// rather than about the chapter.
/// </para>
/// <para>
/// ⚠️ <b>Two live defects make chapter 1 easier than it is authored, and both flatter these
/// figures.</b> <c>unitsPerDraw</c> is read and never used, so a <c>SWARM</c> draw fields one
/// 0.35-HP-coefficient body instead of three; and <c>EncounterFight</c> draws an elite modifier and
/// discards it, so all eight parameter sets are inert. Fixing either is a re-tune of this chapter,
/// and this file is the thing that will say so.
/// </para>
/// </remarks>
public sealed class ChapterOneBareHeroTests
{
    /// <summary>The hero a new player has: <c>250 + 45 × 1</c> from the base curve, and nothing else.</summary>
    private const int BareMaxHp = 295;

    /// <summary>Chapter 1's stage lengths are 12/14/16, so the two gates and the boss sit here.</summary>
    private const int StageOneGate = 11;

    /// <inheritdoc cref="StageOneGate"/>
    private const int StageTwoGate = 25;

    /// <inheritdoc cref="StageOneGate"/>
    private const int LastSpineNode = 41;

    /// <inheritdoc cref="StageOneGate"/>
    private const int BossNode = 42;

    /// <summary>
    /// A draft a player who picked sensibly would hold by the boss: one offensive line taken tall,
    /// one defensive, one sustain. Every id and tier is a real row of
    /// <c>content/perks/perks.json</c>, and each category's base perk is present because the
    /// catalogue gates the rest of its category behind it.
    /// </summary>
    /// <remarks>
    /// Thirteen tiers across seven perks. A chapter-1 board is 43 nodes and every won battle but the
    /// boss opens a draft, so a run reaching the boss has drafted more often than this — the set is
    /// deliberately shy of what a good drafter ends with, not a best case.
    /// </remarks>
    private static readonly (string PerkId, int Tier)[] DecentDraft =
    [
        ("PK_MIGHT", 3),
        ("PK_SWIFTNESS", 2),
        ("PK_KEEN_EYE", 2),
        ("PK_IRONHIDE", 2),
        ("PK_VITALITY", 2),
        ("PK_REGENERATION", 1),
        ("PK_LIFESTEAL", 1),
    ];

    /// <summary>The opening fight of the game is a win, and it is not a close one.</summary>
    /// <remarks>
    /// Node 0 of stage 1 is always <c>TILE_ENEMY</c> (<c>BoardGenerator</c>), so this is the first
    /// thing a new player sees. Asserted with no perks at all: the run opens a draft before this
    /// fight, but a player who skipped it still has to win.
    /// </remarks>
    [Fact]
    public void The_first_fight_of_the_game_is_a_win_with_no_perks_at_all()
    {
        var fight = Fight(TileKind.Enemy, stage: 1, linearIndex: 0, currentHp: BareMaxHp);

        fight.HeroWon.ShouldBeTrue(
            "the first fight a new player is handed has to be winnable by the hero they have");

        fight.HeroHpRemaining.ShouldBeGreaterThan(
            BareMaxHp * 0.6,
            "and winnable without spending most of the run's health on it — the run is an " +
            $"attrition, and this fight ended at {fight.HeroHpRemaining} of {BareMaxHp}");
    }

    /// <summary>
    /// Both mini-bosses fall. They are the two fights a run cannot route around: <c>BoardGenerator</c>
    /// puts one on the last node of stage 1 and one on the last node of stage 2, and the stage-end
    /// movement clamp makes landing on them unavoidable.
    /// </summary>
    [Theory]
    [InlineData(1, StageOneGate)]
    [InlineData(2, StageTwoGate)]
    public void Neither_unskippable_mini_boss_is_a_wall(int stage, int linearIndex)
    {
        Fight(TileKind.MiniBoss, stage, linearIndex, BareMaxHp, DecentDraft).HeroWon.ShouldBeTrue(
            $"the stage-{stage} mini-boss is unskippable, so a run that cannot beat it cannot be " +
            "completed at all");
    }

    /// <summary>
    /// Thornmaw falls to a decent draft and not to an empty one. That pairing is the whole design
    /// intent: chapter 1 is a comfortable win for a player who drafts, and a loss for one who does
    /// not.
    /// </summary>
    [Fact]
    public void The_boss_falls_to_a_decent_draft_and_not_to_an_empty_one()
    {
        // Entered at the 40% the campfire BoardGenerator always places on the node before the boss
        // heals to, rather than at full health: the boss is the end of an attrition, not its own run.
        var enteringHp = BareMaxHp * 4 / 10;

        Fight(TileKind.Boss, BoardGraph.BossStage, BossNode, enteringHp, DecentDraft)
            .HeroWon.ShouldBeTrue("BOSS_THORNMAW is 17 §2's teaching boss, and a drafted hero clears it");

        Fight(TileKind.Boss, BoardGraph.BossStage, BossNode, enteringHp)
            .HeroWon.ShouldBeFalse(
                "the control: with no perks the same fight is a loss, so the case above is a claim " +
                "about the draft rather than about a boss anyone beats");
    }

    /// <summary>
    /// And the chapter survives as one attrition, not as a list of separately survivable fights. HP
    /// carries from each fight into the next (<c>RunBattle</c> passes <c>run.CurrentHp</c>), and the
    /// only healing on the route is the two stage gates' 15% and the pre-boss campfire's 40%.
    /// </summary>
    /// <remarks>
    /// The route is the shape a real run takes: the forced opening enemy, a handful of ordinary
    /// draws spread across the three stages, an elite in each of the later two, both mini-bosses at
    /// their gates, and the boss. Fewer fights than a 43-node board holds, because a d6 lands on
    /// roughly a third of its nodes.
    /// </remarks>
    [Fact]
    public void The_whole_chapter_is_survivable_as_one_carried_pool_of_health()
    {
        var hp = (double)BareMaxHp;

        hp = Survive(TileKind.Enemy, 1, 0, hp);
        hp = Survive(TileKind.Enemy, 1, 4, hp);
        hp = Survive(TileKind.Enemy, 1, 8, hp);
        hp = Survive(TileKind.MiniBoss, 1, StageOneGate, hp);
        hp = Healed(hp, 0.15, "the stage-1 gate");

        hp = Survive(TileKind.Enemy, 2, 15, hp);
        hp = Survive(TileKind.Elite, 2, 19, hp);
        hp = Survive(TileKind.Enemy, 2, 22, hp);
        hp = Survive(TileKind.MiniBoss, 2, StageTwoGate, hp);
        hp = Healed(hp, 0.15, "the stage-2 gate");

        hp = Survive(TileKind.Enemy, 3, 30, hp);
        hp = Survive(TileKind.Elite, 3, 35, hp);
        hp = Survive(TileKind.Enemy, 3, LastSpineNode, hp);
        hp = Healed(hp, 0.40, "the campfire BoardGenerator places before the boss");

        var boss = Fight(TileKind.Boss, BoardGraph.BossStage, BossNode, (int)hp, DecentDraft);

        boss.HeroWon.ShouldBeTrue(
            $"the boss was entered at {hp} of {BareMaxHp} after twelve carried fights, and a run " +
            "that arrives too hurt to finish is a chapter that is not clearable however winnable " +
            "each fight is on its own");
    }

    /// <summary>One fight of the route, asserted survived, answering the HP the next one opens at.</summary>
    private static double Survive(TileKind kind, int stage, int linearIndex, double openingHp)
    {
        var fight = Fight(kind, stage, linearIndex, (int)openingHp, DecentDraft);

        fight.HeroWon.ShouldBeTrue(
            $"the {kind} at node {linearIndex} of stage {stage} was entered at {openingHp} and lost");

        return fight.HeroHpRemaining;
    }

    /// <summary>A heal on the route, clamped to the run's ceiling exactly as <c>Run</c> clamps it.</summary>
    private static double Healed(double hp, double pctMaxHp, string what)
    {
        var healed = Math.Min(BareMaxHp, hp + (BareMaxHp * pctMaxHp));

        healed.ShouldBeGreaterThan(hp, $"{what} healed nothing, so the route below is not what it says");

        return healed;
    }

    /// <summary>
    /// One chapter-1 Normal fight, through the production seam, for a Legend-1 hero wearing nothing.
    /// </summary>
    private static SimulationResult Fight(
        TileKind kind,
        int stage,
        int linearIndex,
        int currentHp,
        params (string PerkId, int Tier)[] perks) =>
        RunBattle.Simulate(
            PlayerSnapshots.With(
                legendLevel: 1,
                inventory: new InventorySnapshot(0, [], []),
                loadout: RunBattleWorlds.BareLoadout),
            RunSnapshots.With(
                runSeed: RunBattleWorlds.RunSeed,
                chapterId: 1,
                tier: DifficultyTier.NORMAL,
                currentHp: currentHp,
                maxHp: BareMaxHp,
                pendingTileKind: (int)kind,
                pendingTileLinearIndex: linearIndex,
                pendingTileStage: stage,
                phase: RunPhase.BattlePending,
                ownedPerkTiers: RunSnapshots.OwnedPerkTiers(perks),
                rngStreamPositions: RunSnapshots.Streams((RngStreams.Combat, RunBattleWorlds.BattlesStarted)),
                startingLoadout: RunBattleWorlds.BareLoadout),
            RunBattleWorlds.Content);
}
