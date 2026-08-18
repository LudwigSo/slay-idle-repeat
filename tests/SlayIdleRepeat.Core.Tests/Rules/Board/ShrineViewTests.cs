using System.Globalization;
using System.Text;
using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Tests.Handlers;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

/// <summary>
/// <c>ShrineView</c> — the two buff rows a pending shrine will draw, projected so the Shrine screen
/// can show them before <c>RESOLVE_TILE</c> applies one.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The load-bearing claim is that the view and the resolver draw the SAME pair</b> (steering
/// S2). Nothing about the offer is persisted: the rows come off the run's committed <c>shrine</c>
/// stream position, and the command that applies one re-derives them from the same place. A view
/// that drew a second, plausible pair would satisfy every structural case in this file and would
/// name two buffs while the tile handed over a third.
/// <see cref="The_taken_row_is_the_heal_RESOLVE_TILE_actually_applies"/> is what says otherwise, and
/// <see cref="Both_heal_arms_occur_across_the_swept_seeds"/> is what stops it being a one-armed
/// claim about the eight rows that heal nothing.
/// </para>
/// <para>
/// ⚠️ Two projections are never compared by record equality (steering S17) — the row list is an
/// <c>IReadOnlyList</c>. <see cref="Canonical"/> is the comparison, and
/// <see cref="Two_different_run_seeds_project_different_rows"/> is the negative control proving it
/// can see a difference at all.
/// </para>
/// </remarks>
public sealed class ShrineViewTests
{
    /// <summary>The seeds every swept case walks. Fixed, so a failure re-runs exactly.</summary>
    private static readonly ulong[] Seeds =
        [1UL, 2UL, 3UL, 5UL, 8UL, 13UL, 21UL, 34UL, 55UL, 89UL, 144UL, 233UL];

    private static ContentSnapshot Content => TileWorlds.Context.Content;

    // ------------------------------------------------------------------------------------------
    // The gate.
    // ------------------------------------------------------------------------------------------

    /// <summary>A run standing on no tile is standing at no shrine.</summary>
    [Fact]
    public void A_run_with_no_pending_tile_projects_nothing()
    {
        ShrineView.Project(TileWorlds.OnNoTile().Run!.ToSnapshot(), Content).ShouldBeNull();
    }

    /// <summary>…and a pending tile of another kind is not one either.</summary>
    [Theory]
    [InlineData((int)TileKind.Campfire)]
    [InlineData((int)TileKind.Empty)]
    [InlineData((int)TileKind.Event)]
    [InlineData((int)TileKind.Shop)]
    public void A_pending_tile_of_another_kind_projects_nothing(int kind)
    {
        ShrineView.Project(
            TileWorlds.OnTile((TileKind)kind).Run!.ToSnapshot(), Content).ShouldBeNull();
    }

    // ------------------------------------------------------------------------------------------
    // The offer.
    // ------------------------------------------------------------------------------------------

    /// <summary>A pending shrine offers two rows, and they are two different buffs.</summary>
    /// <remarks>
    /// 🔒 Distinctness is not decoration: the second row is drawn out of the nine indices that are
    /// not the first and mapped back by stepping <em>at or above</em> the first, so a mapping that
    /// stepped only above it would hand the first index back and offer one buff twice.
    /// <para>
    /// The sweep is floored rather than sampled, and that floor was earned. Five hand-picked seeds
    /// asserted distinctness and caught none of that break: it only shows on a seed whose two rows
    /// come back adjacent, and none of the five did. So the floor demands the sweep actually reach
    /// an adjacent pair — the one shape the broken mapping collapses — and this case now fails as
    /// loudly when it has stopped exercising the mapping as when the mapping is wrong.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_pending_shrine_projects_two_distinct_rows()
    {
        var pool = ShrineTuning.Read(Content);
        var adjacentPairs = 0;

        for (var runSeed = 1UL; runSeed <= 60UL; runSeed++)
        {
            var view = Projected(runSeed);

            view.Rows.Count.ShouldBe(2, "the shrine pool authors two options offered, seed " + runSeed);
            view.Rows[0].BuffId.ShouldNotBe(
                view.Rows[1].BuffId,
                "seed " + runSeed + " offered the same buff twice, so the second row's index was " +
                "mapped back past the first rather than at it.");
            view.IsCleanse.ShouldBeFalse("a run holds no curse list, so the cleanse arm cannot fire");
            view.TakenRowIndex.ShouldBe(0, "slot 1 is the one the resolver applies");

            if (PoolIndexOf(pool, view.Rows[1].BuffId) == PoolIndexOf(pool, view.Rows[0].BuffId) + 1)
            {
                adjacentPairs++;
            }
        }

        adjacentPairs.ShouldBeGreaterThan(
            0,
            "no seed in the sweep drew the row immediately after the first one, which is the only " +
            "shape a mapping that stepped past the first index instead of at it would break. Every " +
            "assertion above would hold against that broken mapping, so this sweep is no longer " +
            "testing what its own name claims. Widen the sweep rather than deleting this line.");
    }

    /// <summary>Where a buff sits in the authored pool — the index the draw actually works in.</summary>
    private static int PoolIndexOf(ShrineTuning pool, string buffId)
    {
        for (var index = 0; index < pool.Buffs.Count; index++)
        {
            if (string.Equals(pool.Buffs[index].Id, buffId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        throw new InvalidOperationException("'" + buffId + "' is not a row of the authored pool.");
    }

    /// <summary>Every row is a row of the authored pool, named by its own loc key.</summary>
    /// <remarks>
    /// Stated against the tuning reader rather than against a transcribed list of ten ids: a view
    /// that invented a display name would otherwise only be caught by somebody reading the screen.
    /// </remarks>
    [Theory]
    [InlineData(1UL)]
    [InlineData(13UL)]
    [InlineData(233UL)]
    public void Every_row_carries_the_authored_pool_rows_own_facts(ulong runSeed)
    {
        var pool = ShrineTuning.Read(Content);

        foreach (var row in Projected(runSeed).Rows)
        {
            var authored = pool.Buffs.SingleOrDefault(b => string.Equals(b.Id, row.BuffId, StringComparison.Ordinal));

            authored.Id.ShouldBe(row.BuffId, "'" + row.BuffId + "' is not a row of the authored pool");
            row.DisplayNameKey.ShouldBe(authored.DisplayName);
            row.ImmediateHealPctMaxHp.ShouldBe(authored.ImmediateHealPctMaxHp);
        }
    }

    // ------------------------------------------------------------------------------------------
    // 🔒 The view and the resolver are looking at the same shrine.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 The HP <c>RESOLVE_TILE</c> moves is exactly the projected taken row's immediate heal — and
    /// exactly nothing when that row heals nothing.
    /// </summary>
    /// <remarks>
    /// Both arms in one assertion rather than two cases, so the seed sweep exercises whichever arm
    /// it lands on and neither can be quietly dropped. The run starts at 40 of 100 so a 40% heal and
    /// an 18% one land on different numbers and neither overheals.
    /// </remarks>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(5UL)]
    [InlineData(8UL)]
    [InlineData(13UL)]
    [InlineData(21UL)]
    [InlineData(34UL)]
    [InlineData(55UL)]
    [InlineData(89UL)]
    [InlineData(144UL)]
    [InlineData(233UL)]
    public void The_taken_row_is_the_heal_RESOLVE_TILE_actually_applies(ulong runSeed)
    {
        var state = TileWorlds.OnTile(TileKind.Shrine, currentHp: 40, runSeed: runSeed);
        var before = state.Run!.ToSnapshot();
        var view = ShrineView.Project(before, Content)!;
        var taken = view.Rows[view.TakenRowIndex];

        var resolved = SlayIdleRepeat.Core.GameRules.Apply(
            state, new ResolveTileCommand(), TileWorlds.Context);

        resolved.Accepted.ShouldBeTrue();
        resolved.NewState.Run!.CurrentHp.ShouldBe(
            Healed(before, taken.ImmediateHealPctMaxHp),
            "the shrine applied a different row than the one the view named as taken: the view drew " +
            taken.BuffId + " into the taken slot and " + view.Rows[1].BuffId + " into the other.");
    }

    /// <summary>
    /// 🔒 …and both arms of that assertion really occur across the swept seeds, so it is not a
    /// statement about the eight pool rows that heal nothing.
    /// </summary>
    /// <remarks>
    /// ⚠️ Coupled to the authored pool's ORDER and to how many of its rows heal — the draw is an
    /// index into the array as the document lists it, and exactly two of the ten rows carry an
    /// immediate heal. Re-ordering the pool, or authoring a heal onto another row, moves which buff
    /// each swept seed lands on and can leave the sweep one-armed. That is a loud failure rather
    /// than a quiet one, and it is meant to be: re-read the pool and re-pick the seeds, never widen
    /// this to "at least one arm".
    /// </remarks>
    [Fact]
    public void Both_heal_arms_occur_across_the_swept_seeds()
    {
        var healing = Seeds.Select(s => Projected(s))
            .Select(v => v.Rows[v.TakenRowIndex].ImmediateHealPctMaxHp is not null)
            .ToArray();

        healing.ShouldContain(true, "no swept seed drew a healing row into the taken slot, so the " +
            "case above never once asserted that a heal lands");
        healing.ShouldContain(false, "every swept seed drew a healing row, so the case above never " +
            "once asserted that a non-healing row moves nothing");
    }

    // ------------------------------------------------------------------------------------------
    // 🔒 Projecting moves nothing.
    // ------------------------------------------------------------------------------------------

    /// <summary>Two projections of one run answer the same two rows.</summary>
    [Fact]
    public void Projecting_twice_answers_the_same_rows()
    {
        var run = TileWorlds.OnTile(TileKind.Shrine).Run!.ToSnapshot();

        Canonical(ShrineView.Project(run, Content)!)
            .ShouldBe(Canonical(ShrineView.Project(run, Content)!));
    }

    /// <summary>…and the negative control: two different seeds do not.</summary>
    [Fact]
    public void Two_different_run_seeds_project_different_rows()
    {
        var drawn = Seeds
            .Select(seed => Canonical(Projected(seed)))
            .Distinct(StringComparer.Ordinal)
            .Count();

        drawn.ShouldBeGreaterThan(
            1, "every swept seed drew the same pair, so this projection could be a constant");
    }

    /// <summary>🔒 Projecting mutates nothing on the run — not its HP, not a stream position.</summary>
    [Fact]
    public void Projecting_moves_nothing_on_the_run()
    {
        var state = TileWorlds.OnTile(TileKind.Shrine, gold: 500, currentHp: 40);
        var before = CanonicalStateWriter.CanonicalBytes(state.Run!.ToSnapshot());

        ShrineView.Project(state.Run!.ToSnapshot(), Content);

        CanonicalStateWriter.CanonicalBytes(state.Run!.ToSnapshot()).ShouldBe(
            before, "a read-only projection moved something on the run it was drawing.");
    }

    /// <summary>🔒 …and a shrine that was LOOKED at resolves byte-for-byte like one that was not.</summary>
    /// <remarks>
    /// The bytes above are read off the same immutable snapshot the projection was handed, so they
    /// cannot see a projection that consumed a draw index the resolver would then continue past.
    /// Resolving on both paths and comparing what the run became is what can.
    /// </remarks>
    [Fact]
    public void A_shrine_that_was_projected_resolves_exactly_like_one_that_was_not()
    {
        var looked = TileWorlds.OnTile(TileKind.Shrine, currentHp: 40);
        ShrineView.Project(looked.Run!.ToSnapshot(), Content);

        var blind = TileWorlds.OnTile(TileKind.Shrine, currentHp: 40);

        var afterLooking = SlayIdleRepeat.Core.GameRules.Apply(
            looked, new ResolveTileCommand(), TileWorlds.Context);
        var afterBlind = SlayIdleRepeat.Core.GameRules.Apply(
            blind, new ResolveTileCommand(), TileWorlds.Context);

        CanonicalStateWriter.CanonicalBytes(afterLooking.NewState.Run!.ToSnapshot()).ShouldBe(
            CanonicalStateWriter.CanonicalBytes(afterBlind.NewState.Run!.ToSnapshot()),
            "looking at a shrine changed what resolving it did.");
    }

    // ------------------------------------------------------------------------------------------
    // The doors.
    // ------------------------------------------------------------------------------------------

    /// <summary>Neither argument may be null.</summary>
    [Fact]
    public void Project_refuses_a_null_argument()
    {
        Should.Throw<ArgumentNullException>(() => ShrineView.Project(null!, Content));
        Should.Throw<ArgumentNullException>(
            () => ShrineView.Project(TileWorlds.OnTile(TileKind.Shrine).Run!.ToSnapshot(), null!));
    }

    // ------------------------------------------------------------------------------------------
    // Fixtures.
    // ------------------------------------------------------------------------------------------

    private static ShrineView Projected(ulong runSeed) =>
        ShrineView.Project(TileWorlds.OnTile(TileKind.Shrine, runSeed: runSeed).Run!.ToSnapshot(), Content)
        ?? throw new InvalidOperationException("the fixture run is not standing on a shrine");

    /// <summary>
    /// What the run's HP becomes when a row carrying <paramref name="share"/> is applied — the
    /// resolver's own clamp and rounding, stated once.
    /// </summary>
    private static int Healed(RunSnapshot run, double? share) =>
        share is { } fraction
            ? Math.Min(run.MaxHp, run.CurrentHp + (int)Math.Round(run.MaxHp * fraction, MidpointRounding.ToEven))
            : run.CurrentHp;

    /// <summary>One projection rendered to text, for the same reason <c>BoardView</c>'s cases do it.</summary>
    private static string Canonical(ShrineView view)
    {
        var text = new StringBuilder();

        text.Append(view.TakenRowIndex.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(view.IsCleanse).Append('\n');

        foreach (var row in view.Rows)
        {
            text.Append(row.BuffId).Append('|')
                .Append(row.DisplayNameKey).Append('|')
                .Append(row.ImmediateHealPctMaxHp?.ToString(CultureInfo.InvariantCulture) ?? "<none>")
                .Append('\n');
        }

        return text.ToString();
    }
}
