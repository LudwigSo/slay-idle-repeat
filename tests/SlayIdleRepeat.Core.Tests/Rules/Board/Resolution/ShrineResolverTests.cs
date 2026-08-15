using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Board.Resolution;
using SlayIdleRepeat.Core.Tests.Handlers;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

// 🔒 Namespace SlayIdleRepeat.Core.Tests.Rules.Board, not ...Rules.Board.Resolution, and the file
// still sits under Rules/Board/Resolution/. The same measurement Worlds.cs and Run.cs both record: a
// child namespace named `Resolution` shadows SlayIdleRepeat.Core.Rules.Board.Resolution for
// everything inside SlayIdleRepeat.Core.Tests.Rules.Board, so a test written there could not name
// the very resolver it is testing. The directory is the file layout; the namespace is the layer.

/// <summary>
/// 🔒 `03` §7a.5 — <c>ShrineResolver</c>'s two-distinct-options draw and its cleanse rule, observed
/// through <c>RESOLVE_TILE</c>'s effect on the run's `14` §8.1 <c>shrine</c> counter.
/// </summary>
/// <remarks>
/// ⚠️ The resolver itself is reached through the handler rather than called directly, because the
/// draw's whole observable consequence today is <b>the stream position</b> — the offer is returned,
/// not persisted, and a heal has no domain event. The counter is what a client replaying this tile
/// has to agree with, so it is the honest subject.
/// </remarks>
public sealed class ShrineResolverTests
{
    private static CommandResult Resolve(WorldSlice state) =>
        SlayIdleRepeat.Core.GameRules.Apply(state, new ResolveTileCommand(), TileWorlds.Context);

    /// <summary>
    /// 🔒 With no cleansable curse, a shrine takes exactly TWO draws — `03` §7a.5's two distinct
    /// options.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is the mutation probe's target. Inverting the cleanse condition makes this one draw,
    /// and the cleanse case below two — so the pair fails together and neither can be satisfied by
    /// the other's behaviour.
    /// </remarks>
    [Fact]
    public void A_shrine_with_no_cleansable_curse_takes_two_draws()
    {
        var result = Resolve(TileWorlds.OnTile(TileKind.Shrine, currentHp: 50));

        result.NewState.Run!.StreamPosition(RngStreams.Shrine).ShouldBe(2UL);
    }

    /// <summary>
    /// 🔒 …and the cleanse branch takes exactly ONE, because slot 2 is DECIDED rather than drawn.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The handler passes <c>hasCleansableCurse: false</c> today and cannot pass anything
    /// else</b> — <c>Run</c> holds no curse list (M3-11's <c>Curses</c> gap) — so this case is
    /// asserted against the resolver's parameter directly. It is the branch that will go live the day
    /// M3-11 lands, and drawing-and-discarding instead would desync the stream from a client that
    /// also skips it, for the rest of the run.
    /// </remarks>
    [Fact]
    public void A_shrine_with_a_cleansable_curse_takes_one_draw_and_offers_a_cleanse()
    {
        var scope = new RunRngScope(TileWorlds.Seed, new Dictionary<string, ulong>(StringComparer.Ordinal));
        var input = new HandlerInput(
            TileWorlds.OnTile(TileKind.Shrine, currentHp: 50), TileWorlds.Context, scope);

        var offer = ShrineResolver.Resolve(input, hasCleansableCurse: true);

        offer.IsCleanse.ShouldBeTrue();
        offer.SecondBuffId.ShouldBeNull("slot 2 is a Cleanse, so no second buff was drawn");
        scope.FinalPositions()[RngStreams.Shrine].ShouldBe(1UL);
    }

    /// <summary>…and the negative control, on the same seam: without a curse it draws two.</summary>
    [Fact]
    public void The_same_seam_takes_two_draws_without_a_cleansable_curse()
    {
        var scope = new RunRngScope(TileWorlds.Seed, new Dictionary<string, ulong>(StringComparer.Ordinal));
        var input = new HandlerInput(
            TileWorlds.OnTile(TileKind.Shrine, currentHp: 50), TileWorlds.Context, scope);

        var offer = ShrineResolver.Resolve(input, hasCleansableCurse: false);

        offer.IsCleanse.ShouldBeFalse();
        offer.SecondBuffId.ShouldNotBeNull();
        scope.FinalPositions()[RngStreams.Shrine].ShouldBe(2UL);
    }

    /// <summary>
    /// 🔒 `03` §7a.5's "2 DISTINCT options" — the two slots are never the same buff, whatever the
    /// seed.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is what the sampling-without-replacement remap exists for, and the seeds are swept
    /// rather than fixed because the remap's off-by-one only bites when the second reduced draw lands
    /// at or above the first index — a single seed would miss it most of the time.
    /// </remarks>
    [Fact]
    public void The_two_offered_buffs_are_always_distinct()
    {
        for (var seed = 1UL; seed <= 200UL; seed++)
        {
            var scope = new RunRngScope(seed, new Dictionary<string, ulong>(StringComparer.Ordinal));
            var input = new HandlerInput(
                TileWorlds.OnTile(TileKind.Shrine, currentHp: 50, runSeed: seed), TileWorlds.Context, scope);

            var offer = ShrineResolver.Resolve(input, hasCleansableCurse: false);

            offer.SecondBuffId.ShouldNotBe(offer.FirstBuffId, "seed " + seed + " offered one buff twice");
        }
    }

    /// <summary>
    /// 🔒 …and the second slot genuinely reaches every OTHER index, including the one immediately
    /// after the first — the value the remap has to step past.
    /// </summary>
    [Fact]
    public void The_second_slot_reaches_every_other_buff()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var seed = 1UL; seed <= 400UL; seed++)
        {
            var scope = new RunRngScope(seed, new Dictionary<string, ulong>(StringComparer.Ordinal));
            var input = new HandlerInput(
                TileWorlds.OnTile(TileKind.Shrine, currentHp: 50, runSeed: seed), TileWorlds.Context, scope);

            var offer = ShrineResolver.Resolve(input, hasCleansableCurse: false);
            seen.Add(offer.SecondBuffId!);
        }

        seen.Count.ShouldBe(10, "every one of 03 §7a.5's ten buffs must be reachable in slot 2");
    }

    /// <summary>🔒 The draw is deterministic for a fixed seed.</summary>
    [Fact]
    public void The_shrine_draw_is_deterministic_for_a_fixed_seed()
    {
        static (string First, string? Second) Draw()
        {
            var scope = new RunRngScope(777UL, new Dictionary<string, ulong>(StringComparer.Ordinal));
            var input = new HandlerInput(
                TileWorlds.OnTile(TileKind.Shrine, currentHp: 50, runSeed: 777UL), TileWorlds.Context, scope);

            var offer = ShrineResolver.Resolve(input, hasCleansableCurse: false);

            return (offer.FirstBuffId, offer.SecondBuffId);
        }

        Draw().ShouldBe(Draw());
    }

    /// <summary>
    /// 🔒 A drawn healing row heals immediately — <c>SHR_HEAL</c> (40% of Max HP) and <c>SHR_HP</c>
    /// (18%) are the two rows that carry an <c>immediateHealPctMaxHp</c>.
    /// </summary>
    [Fact]
    public void A_drawn_healing_row_heals_immediately()
    {
        var healed = new List<int>();

        for (var seed = 1UL; seed <= 60UL; seed++)
        {
            var result = Resolve(TileWorlds.OnTile(TileKind.Shrine, currentHp: 10, runSeed: seed));
            healed.Add(result.NewState.Run!.CurrentHp);
        }

        healed.ShouldContain(50, "a shrine that offered SHR_HEAL heals 40% of a 100 Max HP bar");
        healed.ShouldContain(10, "…and one that offered neither healing row heals nothing");
    }

    /// <summary>🔒 …and an immediate heal is clamped at Max HP rather than overhealing.</summary>
    [Fact]
    public void An_immediate_heal_never_exceeds_max_hp()
    {
        for (var seed = 1UL; seed <= 60UL; seed++)
        {
            var result = Resolve(TileWorlds.OnTile(TileKind.Shrine, currentHp: 95, runSeed: seed));

            result.NewState.Run!.CurrentHp.ShouldBeLessThanOrEqualTo(100);
            result.NewState.Run!.CurrentHp.ShouldBeGreaterThanOrEqualTo(95, "a shrine never hurts");
        }
    }

    /// <summary>
    /// 🔒 A shrine moves no currency, ever — the stat half of `03` §7a.5's buffs is deliberately not
    /// applied and pays nothing in its place.
    /// </summary>
    [Fact]
    public void A_shrine_moves_no_currency()
    {
        var state = TileWorlds.OnTile(TileKind.Shrine, gold: 250, currentHp: 50);

        var result = Resolve(state);

        result.Events.ShouldBeEmpty();
        result.NewState.Run!.Gold.ShouldBe(250);
    }
}
