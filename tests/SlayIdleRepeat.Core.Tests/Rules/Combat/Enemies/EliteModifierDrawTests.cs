using Shouldly;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Enemies;

/// <summary>
/// 🔒 `05` §6.2's Elite Modifier draw — <em>"No Elite may draw the same modifier as the immediately
/// preceding Elite in the same run — redraw on collision."</em>
/// </summary>
public sealed class EliteModifierDrawTests
{
    private const ulong BattleSeed = 0xE117E_5EED_9001UL;

    [Fact]
    public void A_run_with_no_previous_elite_draws_from_all_eight()
    {
        var history = new EliteModifierHistory();
        var rng = new DeterministicRng(BattleSeed, RngStreams.Combat);

        var drawn = new HashSet<EliteModifier>();
        for (var i = 0; i < 500; i++)
        {
            drawn.Add(EliteModifierDraw.Draw(rng, EnemyFixtures.Modifiers, history));
        }

        drawn.Count.ShouldBe(8, "05 §6.2's list is flat, so an unconstrained draw reaches every row");
    }

    /// <summary>
    /// 🔒 The rule itself. Over many battle seeds, the drawn modifier is never the previous one.
    /// </summary>
    [Fact]
    public void The_drawn_modifier_is_never_the_immediately_preceding_one()
    {
        foreach (var previous in Enum.GetValues<EliteModifier>())
        {
            for (var seed = 0UL; seed < 200UL; seed++)
            {
                var history = EliteModifierHistory.Restore(previous);
                var rng = new DeterministicRng(BattleSeed + seed, RngStreams.Combat);

                EliteModifierDraw.Draw(rng, EnemyFixtures.Modifiers, history)
                    .ShouldNotBe(previous, $"05 §6.2 forbids drawing {previous} twice running");
            }
        }
    }

    /// <summary>
    /// 🔒 <b>Redraw, not exclude-then-draw.</b> `05` §6.2 authors a redraw, and the two differ in
    /// draw indices consumed — which is persisted (`14` §8.1) and auditable. This exhibits a seed
    /// where the first attempt collides and the stream therefore advances twice.
    /// </summary>
    [Fact]
    public void A_collision_consumes_a_draw_index_because_05_section_6_2_authors_a_redraw()
    {
        var collided = FindSeedWhoseFirstDrawIs(EliteModifier.ENRAGED);

        var rng = new DeterministicRng(collided, RngStreams.Combat);
        var history = EliteModifierHistory.Restore(EliteModifier.ENRAGED);

        var drawn = EliteModifierDraw.Draw(rng, EnemyFixtures.Modifiers, history);

        drawn.ShouldNotBe(EliteModifier.ENRAGED);
        rng.Position.ShouldBeGreaterThan(1UL,
            "a collision redraws, and 14 §8.0 makes each attempt exactly one draw index");
    }

    /// <summary>Without a collision the draw is a single index, as `14` §8.0 states.</summary>
    [Fact]
    public void A_draw_that_does_not_collide_consumes_exactly_one_draw_index()
    {
        var seed = FindSeedWhoseFirstDrawIs(EliteModifier.ENRAGED);

        var rng = new DeterministicRng(seed, RngStreams.Combat);
        var history = EliteModifierHistory.Restore(EliteModifier.SWIFT);

        EliteModifierDraw.Draw(rng, EnemyFixtures.Modifiers, history).ShouldBe(EliteModifier.ENRAGED);
        rng.Position.ShouldBe(1UL);
    }

    /// <summary>The draw is reproducible from the battle seed, like every other combat draw.</summary>
    [Fact]
    public void The_same_battle_seed_and_the_same_previous_modifier_draw_the_same_thing()
    {
        EliteModifier Draw()
        {
            var rng = new DeterministicRng(BattleSeed, RngStreams.Combat);
            return EliteModifierDraw.Draw(
                rng, EnemyFixtures.Modifiers, EliteModifierHistory.Restore(EliteModifier.CURSED));
        }

        Draw().ShouldBe(Draw());
    }

    /// <summary>
    /// 🔒 `05` §6.2 states no weighting at all, so every row carries the same weight. A weighted
    /// list here would be a fabricated number (`16` R6).
    /// </summary>
    [Fact]
    public void Every_modifier_carries_the_same_weight_because_05_section_6_2_states_none()
    {
        EliteModifierDraw.UniformWeight.ShouldBe(1.0);

        var rng = new DeterministicRng(BattleSeed, RngStreams.Combat);
        var history = new EliteModifierHistory();

        var counts = new Dictionary<EliteModifier, int>();
        for (var i = 0; i < 8000; i++)
        {
            var drawn = EliteModifierDraw.Draw(rng, EnemyFixtures.Modifiers, history);
            counts[drawn] = counts.GetValueOrDefault(drawn) + 1;
        }

        counts.Count.ShouldBe(8);
        foreach (var (modifier, count) in counts)
        {
            count.ShouldBeInRange(850, 1150, $"{modifier} should land near an eighth of 8000 draws");
        }
    }

    [Fact]
    public void An_empty_modifier_pool_fails_rather_than_producing_an_elite_with_no_modifier()
    {
        var thrown = Should.Throw<ArgumentException>(() => EliteModifierDraw.Draw(
            new DeterministicRng(BattleSeed, RngStreams.Combat),
            new List<EliteModifierRow>(),
            new EliteModifierHistory()));

        thrown.ParamName.ShouldBe("modifiers");
        thrown.Message.ShouldContain("this pool is empty");
    }

    /// <summary>
    /// 🔒 A pool of one, where the one is the previous modifier, is the editing mistake that would
    /// otherwise hang the redraw forever inside the game's hottest path.
    /// </summary>
    [Fact]
    public void A_pool_whose_only_row_is_the_previous_modifier_fails_before_it_can_loop()
    {
        var onlyEnraged = EnemyFixtures.Modifiers.Where(m => m.Id == EliteModifier.ENRAGED).ToList();

        var thrown = Should.Throw<ArgumentException>(() => EliteModifierDraw.Draw(
            new DeterministicRng(BattleSeed, RngStreams.Combat),
            onlyEnraged,
            EliteModifierHistory.Restore(EliteModifier.ENRAGED)));

        thrown.ParamName.ShouldBe("modifiers");
        thrown.Message.ShouldContain("can never terminate");
    }

    /// <summary>
    /// 🔒 S3 — the floor under this file's subject set. Every rule above enumerates
    /// <see cref="EnemyFixtures.Modifiers"/>; if it shrank, the distribution rule and the
    /// never-the-previous rule would both quantify over less while still passing.
    /// </summary>
    [Fact]
    public void The_fixture_pool_is_05_section_6_2s_eight_rows()
    {
        EnemyFixtures.Modifiers.Count.ShouldBe(8);
        EnemyFixtures.Modifiers.Select(m => m.Id).Distinct().Count().ShouldBe(8);
    }

    private static ulong FindSeedWhoseFirstDrawIs(EliteModifier modifier)
    {
        for (var seed = 1UL; seed < 10_000UL; seed++)
        {
            var rng = new DeterministicRng(seed, RngStreams.Combat);

            if (EliteModifierDraw.Draw(rng, EnemyFixtures.Modifiers, new EliteModifierHistory()) == modifier)
            {
                return seed;
            }
        }

        throw new InvalidOperationException(
            $"no seed under 10,000 draws {modifier} first — the draw or the table has changed shape.");
    }
}
