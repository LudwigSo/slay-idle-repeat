using Shouldly;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// 🔒 M3-03 — <c>Run</c>'s pending-tile state: the seam between arriving at a tile and resolving it.
/// </summary>
public sealed class RunPendingTileTests
{
    private static Core.Model.Run NewRun(RunSnapshot? snapshot = null)
    {
        var run = Core.Model.Run.Rehydrate(snapshot ?? RunSnapshots.Valid);

        return run.IsSuccess
            ? run.Value
            : throw new InvalidOperationException("fixture does not rehydrate: " + run.Error);
    }

    // ------------------------------------------------------------------ the empty state

    /// <summary>A rehydrated run with nothing pending says so, and refuses to describe a tile.</summary>
    [Fact]
    public void A_run_with_no_pending_tile_describes_none()
    {
        var run = NewRun();

        run.HasPendingTile.ShouldBeFalse();
        run.PendingEventCardId.ShouldBeNull();

        Should.Throw<InvalidOperationException>(() => run.PendingTileKindValue);
        Should.Throw<InvalidOperationException>(() => run.PendingTileLinearIndex);
        Should.Throw<InvalidOperationException>(() => run.PendingTileStage);
    }

    /// <summary>
    /// ⚠️ …and the three getters throw rather than answering a default, because a default would let a
    /// rule resolve a tile the run is not standing on. The message names the gate to ask instead.
    /// </summary>
    [Fact]
    public void The_refusal_names_the_gate_to_ask_first()
    {
        Should.Throw<InvalidOperationException>(() => NewRun().PendingTileKindValue)
            .Message.ShouldContain("HasPendingTile", Case.Sensitive);
    }

    // ------------------------------------------------------------------ ArriveAtTile

    /// <summary>Arriving at a tile records all three of its facts.</summary>
    /// <remarks>
    /// ⚠️ The kind arrives as an <c>int</c> rather than a <see cref="TileKind"/> because a
    /// <b>public</b> test method may not take a parameter of an <c>internal</c> type (CS0051) — the
    /// same accessibility wall that made <c>Run</c> store the value as an <c>int</c> in the first
    /// place. The call sites below still name the kind, which is what the reader needs.
    /// </remarks>
    [Theory]
    [InlineData((int)TileKind.Empty, 0, 1)]
    [InlineData((int)TileKind.Treasure, 19, 2)]
    [InlineData((int)TileKind.Event, 41, 3)]
    [InlineData((int)TileKind.Boss, 42, BoardGraph.BossStage)]
    public void Arriving_at_a_tile_records_it(int kind, int linearIndex, int stage)
    {
        var run = NewRun();

        run.ArriveAtTile(kind, linearIndex, stage);

        run.HasPendingTile.ShouldBeTrue();
        run.PendingTileKindValue.ShouldBe(kind);
        run.PendingTileLinearIndex.ShouldBe(linearIndex);
        run.PendingTileStage.ShouldBe(stage);
        run.PendingEventCardId.ShouldBeNull();
    }

    /// <summary>
    /// 🔒 Arriving twice is a DEFECT — `03` §1's movement is forward-only, so a run that moved on
    /// without resolving is a miswired movement engine rather than a player asking twice.
    /// </summary>
    [Fact]
    public void Arriving_with_a_tile_already_pending_is_refused()
    {
        var run = NewRun();
        run.ArriveAtTile((int)TileKind.Treasure, 5, 1);

        Should.Throw<InvalidOperationException>(() => run.ArriveAtTile((int)TileKind.Empty, 6, 1));
    }

    /// <summary>…and the first tile survives the refusal untouched.</summary>
    [Fact]
    public void A_refused_second_arrival_leaves_the_first_tile_pending()
    {
        var run = NewRun();
        run.ArriveAtTile((int)TileKind.Treasure, 5, 1);

        Should.Throw<InvalidOperationException>(() => run.ArriveAtTile((int)TileKind.Empty, 6, 2));

        run.PendingTileKindValue.ShouldBe((int)TileKind.Treasure);
        run.PendingTileLinearIndex.ShouldBe(5);
        run.PendingTileStage.ShouldBe(1);
    }

    /// <summary>`03` §1.1's linear index runs from 0 upwards.</summary>
    [Fact]
    public void A_negative_linear_index_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            NewRun().ArriveAtTile((int)TileKind.Empty, -1, 1));
    }

    /// <summary>
    /// ⚠️ …and there is deliberately NO ceiling: the real bound is this run's own generated board,
    /// which is M3-02's, so an index past the shipped 42 is accepted rather than refused by an
    /// invented range.
    /// </summary>
    [Fact]
    public void A_linear_index_past_the_shipped_board_is_accepted()
    {
        Should.NotThrow(() => NewRun().ArriveAtTile((int)TileKind.Empty, 9999, 1));
    }

    /// <summary>`03` §1 authors three stages plus the boss, and no other value.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(99)]
    public void A_stage_outside_the_four_is_refused(int stage)
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            NewRun().ArriveAtTile((int)TileKind.Empty, 0, stage));
    }

    /// <summary>…and the negative control: all four ARE accepted.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(BoardGraph.BossStage)]
    public void Every_authored_stage_is_accepted(int stage)
    {
        Should.NotThrow(() => NewRun().ArriveAtTile((int)TileKind.Empty, 0, stage));
    }

    /// <summary>
    /// A negative kind collides with the "nothing pending" sentinel and is refused.
    /// </summary>
    /// <remarks>
    /// ⚠️ That floor is the whole kind check this aggregate can make — `30` §11.4 forbids
    /// <c>Model</c> from naming the tile vocabulary — so a value ABOVE the fourteen is accepted here
    /// and caught by <c>ResolveTile</c> instead. See the next test.
    /// </remarks>
    [Fact]
    public void A_negative_tile_kind_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => NewRun().ArriveAtTile(-1, 0, 1));
    }

    /// <summary>
    /// 🔒 …and the documented consequence, pinned so it is a decision rather than a hole: a kind
    /// above `03` §2's fourteen IS accepted by the aggregate, because it cannot see them.
    /// </summary>
    [Fact]
    public void A_tile_kind_above_the_vocabulary_is_accepted_by_the_aggregate()
    {
        var run = NewRun();

        Should.NotThrow(() => run.ArriveAtTile(99, 0, 1));

        run.PendingTileKindValue.ShouldBe(99);
    }

    // ------------------------------------------------------------------ SetPendingEventCard

    /// <summary>An event tile records the card it drew.</summary>
    [Fact]
    public void A_pending_event_tile_records_its_drawn_card()
    {
        var run = NewRun();
        run.ArriveAtTile((int)TileKind.Event, 3, 1);

        run.SetPendingEventCard("EVT_WELL");

        run.PendingEventCardId.ShouldBe("EVT_WELL");
    }

    /// <summary>
    /// 🔒 A second card is refused — that IS the re-draw the field exists to prevent.
    /// </summary>
    [Fact]
    public void A_second_card_on_the_same_tile_is_refused()
    {
        var run = NewRun();
        run.ArriveAtTile((int)TileKind.Event, 3, 1);
        run.SetPendingEventCard("EVT_WELL");

        Should.Throw<InvalidOperationException>(() => run.SetPendingEventCard("EVT_SIGNPOST"));

        run.PendingEventCardId.ShouldBe("EVT_WELL", "the first card survives the refusal");
    }

    /// <summary>A card with no tile to belong to is refused.</summary>
    [Fact]
    public void A_card_with_no_pending_tile_is_refused()
    {
        Should.Throw<InvalidOperationException>(() => NewRun().SetPendingEventCard("EVT_WELL"));
    }

    /// <summary>A blank id is indistinguishable from "no card drawn", which absence already says.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_card_id_is_refused(string cardId)
    {
        var run = NewRun();
        run.ArriveAtTile((int)TileKind.Event, 3, 1);

        Should.Throw<ArgumentException>(() => run.SetPendingEventCard(cardId));
    }

    // ------------------------------------------------------------------ ClearPendingTile

    /// <summary>Clearing resets all four fields to their "nothing pending" values.</summary>
    [Fact]
    public void Clearing_resets_every_pending_field()
    {
        var run = NewRun();
        run.ArriveAtTile((int)TileKind.Event, 19, 2);
        run.SetPendingEventCard("EVT_WELL");

        run.ClearPendingTile();

        run.HasPendingTile.ShouldBeFalse();
        run.PendingEventCardId.ShouldBeNull();
        run.ToSnapshot().PendingTileLinearIndex.ShouldBe(0);
        run.ToSnapshot().PendingTileStage.ShouldBe(0);
        run.ToSnapshot().PendingEventCardId.ShouldBe("");
    }

    /// <summary>
    /// 🔒 Clearing is IDEMPOTENT — the documented choice. Its promise is a postcondition ("no tile is
    /// pending"), which is already true when nothing is pending.
    /// </summary>
    [Fact]
    public void Clearing_with_nothing_pending_is_a_no_op()
    {
        var run = NewRun();

        Should.NotThrow(run.ClearPendingTile);
        Should.NotThrow(run.ClearPendingTile);

        run.HasPendingTile.ShouldBeFalse();
    }

    /// <summary>
    /// 🔒 …and clearing genuinely re-opens the seam: a run may arrive at another tile afterwards.
    /// </summary>
    [Fact]
    public void A_cleared_run_may_arrive_at_another_tile()
    {
        var run = NewRun();
        run.ArriveAtTile((int)TileKind.Treasure, 5, 1);
        run.ClearPendingTile();

        Should.NotThrow(() => run.ArriveAtTile((int)TileKind.Shrine, 6, 1));

        run.PendingTileKindValue.ShouldBe((int)TileKind.Shrine);
    }

    // ------------------------------------------------------------------ round trip

    /// <summary>🔒 `30` §11.3 — a run with a pending tile round-trips through its own snapshot.</summary>
    [Fact]
    public void A_pending_tile_round_trips_through_the_snapshot()
    {
        var run = NewRun();
        run.ArriveAtTile((int)TileKind.Event, 19, 2);
        run.SetPendingEventCard("EVT_WELL");

        var rehydrated = NewRun(run.ToSnapshot());

        rehydrated.ToSnapshot().ShouldBe(run.ToSnapshot());
        rehydrated.PendingTileKindValue.ShouldBe((int)TileKind.Event);
        rehydrated.PendingTileLinearIndex.ShouldBe(19);
        rehydrated.PendingTileStage.ShouldBe(2);
        rehydrated.PendingEventCardId.ShouldBe("EVT_WELL");
    }

    /// <summary>…and so does a run with nothing pending.</summary>
    [Fact]
    public void An_empty_pending_tile_round_trips_through_the_snapshot()
    {
        var run = NewRun();

        NewRun(run.ToSnapshot()).ToSnapshot().ShouldBe(run.ToSnapshot());
    }

    /// <summary>
    /// 🔒 The aggregate spells "no card" as <c>null</c> and the snapshot as <c>""</c>, and the seam
    /// translates both ways.
    /// </summary>
    [Fact]
    public void The_absent_card_crosses_the_seam_as_an_empty_string()
    {
        var run = NewRun();

        run.PendingEventCardId.ShouldBeNull();
        run.ToSnapshot().PendingEventCardId.ShouldBe("");
        NewRun(run.ToSnapshot()).PendingEventCardId.ShouldBeNull();
    }

    // ------------------------------------------------------------------ rehydrate faults

    /// <summary>A kind below the sentinel is not a value this column legitimately holds.</summary>
    [Fact]
    public void A_kind_below_the_sentinel_is_a_fault()
    {
        var result = Core.Model.Run.Rehydrate(RunSnapshots.With(pendingTileKind: -2));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(RunSnapshot.PendingTileKind), Case.Sensitive);
    }

    /// <summary>A negative index on a pending tile is below `03` §1.1's floor.</summary>
    [Fact]
    public void A_negative_pending_index_is_a_fault()
    {
        var result = Core.Model.Run.Rehydrate(
            RunSnapshots.OnPendingTile((int)TileKind.Empty, linearIndex: -1));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(RunSnapshot.PendingTileLinearIndex), Case.Sensitive);
    }

    /// <summary>A stage outside `03` §1's four is a row no rule could have written.</summary>
    [Fact]
    public void A_pending_stage_outside_the_four_is_a_fault()
    {
        var result = Core.Model.Run.Rehydrate(
            RunSnapshots.OnPendingTile((int)TileKind.Empty, stage: 7));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(RunSnapshot.PendingTileStage), Case.Sensitive);
    }

    /// <summary>
    /// 🔒 A stale index left behind with nothing pending is refused rather than normalised, because
    /// `14` §16.6 hashes the whole row: two identical runs must not have two stateHashes.
    /// </summary>
    [Fact]
    public void A_stale_index_with_nothing_pending_is_a_fault()
    {
        var result = Core.Model.Run.Rehydrate(RunSnapshots.With(pendingTileLinearIndex: 19));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(RunSnapshot.PendingTileLinearIndex), Case.Sensitive);
    }

    /// <summary>A card stranded on a run with no pending tile could never be resolved.</summary>
    [Fact]
    public void A_stranded_event_card_is_a_fault()
    {
        var result = Core.Model.Run.Rehydrate(RunSnapshots.With(pendingEventCardId: "EVT_WELL"));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(RunSnapshot.PendingEventCardId), Case.Sensitive);
    }

    /// <summary>The field is never null — the empty string is how it spells "no card".</summary>
    /// <remarks>
    /// Built with a record <c>with</c> rather than through <c>RunSnapshots.With</c>, whose optional
    /// parameters read <c>null</c> as "keep the fixture value" — the same reason <c>WithNull</c>
    /// exists for the two maps.
    /// </remarks>
    [Fact]
    public void A_null_card_id_is_a_fault()
    {
        var result = Core.Model.Run.Rehydrate(RunSnapshots.Valid with { PendingEventCardId = null! });

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(RunSnapshot.PendingEventCardId), Case.Sensitive);
    }

    /// <summary>
    /// 🔒 Faults ACCUMULATE — a row corrupt in three ways reports three problems, not the first.
    /// </summary>
    [Fact]
    public void Several_pending_tile_faults_accumulate()
    {
        var result = Core.Model.Run.Rehydrate(
            RunSnapshots.OnPendingTile((int)TileKind.Empty, linearIndex: -1, stage: 7));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(RunSnapshot.PendingTileLinearIndex), Case.Sensitive);
        result.Error.ShouldContain(nameof(RunSnapshot.PendingTileStage), Case.Sensitive);
    }

    /// <summary>
    /// 🔒 …but ONE defect produces ONE fault: a kind below the sentinel does not also report the
    /// three fields that describe a tile the row does not legibly name.
    /// </summary>
    [Fact]
    public void An_illegible_kind_reports_one_fault_and_not_four()
    {
        var result = Core.Model.Run.Rehydrate(
            RunSnapshots.With(pendingTileKind: -2, pendingTileLinearIndex: -5, pendingTileStage: 9));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldNotContain(nameof(RunSnapshot.PendingTileLinearIndex), Case.Sensitive);
        result.Error.ShouldNotContain(nameof(RunSnapshot.PendingTileStage), Case.Sensitive);
    }

    /// <summary>…and the negative control: a well-formed pending row rehydrates.</summary>
    [Fact]
    public void A_well_formed_pending_row_rehydrates()
    {
        Core.Model.Run.Rehydrate(RunSnapshots.OnPendingTile((int)TileKind.Campfire))
            .IsSuccess.ShouldBeTrue();
    }
}
