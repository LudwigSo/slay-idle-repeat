using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.BalanceHarness;
using SlayIdleRepeat.Core.Tests.Handlers;
using SlayIdleRepeat.Core.Testing;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Stats;

/// <summary>
/// 🔴 A perk carrying a CONDITIONAL effect must not brick the run that drafted it.
/// </summary>
/// <remarks>
/// <para>
/// The shipped catalogue authors conditional perks — <c>PK_THUNDERCLAP</c> is <c>+25% damage while
/// three or more enemies stand</c> — and the draft offers them like any other. Evaluating an `18`
/// §4 condition needs the live fight, so <c>StatAggregationSeams.Strict</c> REFUSES one rather than
/// guessing, which is correct: admitting it would apply an on-full-HP perk unconditionally and
/// skipping it would delete a conditional one.
/// </para>
/// <para>
/// What was NOT correct is where that refusal landed. <c>HeroBuild</c> aggregated in its
/// constructor, so composing the hero at all threw — and <c>RunBattle.Simulate</c> composes the hero
/// on every <c>CONFIRM_BATTLE_RESULT</c>. A player who took a perk the game had just offered them
/// could not fight again: <c>GameRules.Apply</c> threw instead of returning a result, for the rest
/// of the run. Found by <c>RunLivenessTests</c>, which is the first driver in this repo that PICKS
/// perks rather than skipping every draft.
/// </para>
/// <para>
/// The fight never wanted the composed block: it takes <c>BaseStats</c> and the collected holdings
/// and re-aggregates every pass with a gate of its own. So the aggregation is deferred to the one
/// caller that asks for a composed block — and the refusal is still there, waiting, for the hero
/// screen that asks.
/// </para>
/// </remarks>
public sealed class ConditionalPerkBuildTests
{
    /// <summary>An EPIC perk whose tier 1 effect carries an <c>ENEMY_COUNT &gt;= 3</c> condition.</summary>
    private const string ConditionalPerk = "PK_THUNDERCLAP";

    /// <summary>
    /// The probe that shows this is about a REAL authored perk, not a fixture: the shipped catalogue
    /// still carries a conditional effect, so the refusal below is still reachable from a draft.
    /// </summary>
    [Fact]
    public void The_shipped_catalogue_still_authors_a_conditional_perk()
    {
        var effects = Core.Content.Perks.PerkEffects.Of(ShippedHarness.Content, ConditionalPerk, 1);

        effects.Any(effect => effect.Condition != null).ShouldBeTrue(
            ConditionalPerk + " no longer carries a condition, so this suite is asserting over " +
            "nothing. Point it at another conditional perk — do not delete it — while any perk in " +
            "the catalogue carries one.");
    }

    /// <summary>A run that owns a conditional perk still composes for the fight.</summary>
    [Fact]
    public void A_run_owning_a_conditional_perk_still_composes_a_fight()
    {
        var build = Build(ConditionalPerk);

        Should.NotThrow(() => build.BaseStats);
        build.Effects.Any(effect => effect.Id.StartsWith(ConditionalPerk, StringComparison.Ordinal))
            .ShouldBeTrue(
                "the perk's own effect is collected and handed to the fight — deferring the " +
                "AGGREGATION must not drop the effect from the list the roster is built out of.");
    }

    /// <summary>
    /// …and the refusal is still there for a caller that asks for a composed block, which is what
    /// makes the fix a relocation rather than a deletion.
    /// </summary>
    /// <remarks>
    /// The negative control for the case above: without it, "composing does not throw" is equally
    /// satisfied by a gate that silently admits every conditional effect — the exact silent balance
    /// bug <c>UnconditionalEffectsOnly</c> exists to prevent.
    /// </remarks>
    [Fact]
    public void Asking_for_the_composed_block_still_refuses()
    {
        var build = Build(ConditionalPerk);

        Should.Throw<EffectContextException>(() => build.Stats);
    }

    /// <summary>
    /// 🔒 The end-to-end shape, through the production dispatch table: draft a conditional perk, then
    /// fight. This is the sequence that used to throw out of <c>Apply</c>.
    /// </summary>
    [Fact]
    public void Picking_a_conditional_perk_and_then_fighting_is_accepted()
    {
        var game = new InMemoryGame(
            ShippedHarness.Content, Harnesses.Seed, new VirtualClock(Harnesses.Start));
        var player = game.CreatePlayer(inventory: Harnesses.FarAboveParStock());

        Harnesses.Equip(game, player);
        game.Send(player, new StartRunCommand(1, DifficultyTier.NORMAL));

        // The run opens with a draft, so the perk is taken before anything else happens. Which perk
        // the draft offers is the seed's business; what matters is that a pick is taken at all and
        // that the run then fights, since the throw fired on ANY collected conditional effect.
        game.Send(player, new PickPerkCommand(0)).Accepted.ShouldBeTrue();

        var battles = 0;

        for (var issued = 0; issued < 60 && battles == 0; issued++)
        {
            var run = game.State(player).Run!;

            if (run.Phase == RunPhase.BattlePending)
            {
                var confirmed = Should.NotThrow(
                    () => game.Send(player, new ConfirmBattleResultCommand("1", Won: true)),
                    "CONFIRM_BATTLE_RESULT recomposes the hero, and composing a build holding a " +
                    "conditional perk used to throw out of GameRules.Apply — which is not a " +
                    "rejection the player can recover from, it is a run that cannot continue.");

                confirmed.Accepted.ShouldBeTrue();
                battles++;
                continue;
            }

            if (run.DraftPending)
            {
                game.Send(player, new PickPerkCommand(0));
                continue;
            }

            if (run.PendingFork is not null)
            {
                game.Send(player, new ChooseForkCommand(0));
                continue;
            }

            if (!run.HasPendingTile)
            {
                game.Send(player, new RollDiceCommand());
                continue;
            }

            var kind = (Core.Rules.Board.TileKind)run.PendingTileKindValue;

            // 🔒 The tiles that stand open for a SECOND command are sent it, rather than being sent
            // RESOLVE_TILE again. A resend is refused, and the walk would then spend its whole budget
            // on one tile and report "never reached a battle" — which is what it used to do the
            // moment the dice sequence happened to land on one of them before an enemy.
            game.Send(player, kind switch
            {
                Core.Rules.Board.TileKind.Enemy or Core.Rules.Board.TileKind.Elite
                    or Core.Rules.Board.TileKind.Boss => new StartBattleCommand(),
                Core.Rules.Board.TileKind.Shop when run.HasOpenShop => new ShopLeaveCommand(),
                Core.Rules.Board.TileKind.Shrine => new ShrineChooseCommand(0),
                Core.Rules.Board.TileKind.Campfire => new CampfireChooseCommand(0),
                Core.Rules.Board.TileKind.Event when run.PendingEventCardId is { Length: > 0 } =>
                    new EventChooseCommand(0),
                Core.Rules.Board.TileKind.Minigame when !run.HasResolvedMinigameAt(run.Position) =>
                    new MinigameSubmitCommand(MinigameCatalogue.ChestPick, 0),
                _ => new ResolveTileCommand(),
            });
        }

        battles.ShouldBe(
            1, "the walk never reached a battle, so it never exercised the recomposition at all.");
    }

    /// <summary>A build for a run owning one perk at tier 1, composed the way a fight composes it.</summary>
    private static HeroBuild Build(string perkId)
    {
        var world = TileWorlds.OnTile(Core.Rules.Board.TileKind.Enemy);

        world.Run!.UpsertPerkTier(perkId, 1);

        return HeroBuild.Of(world.Player, world.Run, ShippedHarness.Content);
    }
}
