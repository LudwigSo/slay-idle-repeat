using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Testing;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Wire;

/// <summary>
/// The wire projection's mirror discipline: field-for-field the persisted snapshot minus exactly
/// the withheld fields, and the hash claims the M5 kickoff ruled — the seed does not move the wire
/// hash, everything client-visible does.
/// </summary>
public sealed class WireProjectionTests
{
    /// <summary>The one field the player projection withholds, by name, so the mirror sweep cannot drift.</summary>
    private const string WithheldPlayerField = "BattleHashMismatches";

    /// <summary>The one field the run projection withholds.</summary>
    private const string WithheldRunField = "RunSeed";

    private static (PlayerSnapshot Player, RunSnapshot Run) PlayedRows()
    {
        var (game, player) = Worlds.InAPlayedRun();
        var state = game.State(player);

        return (state.Player.ToSnapshot(), state.Run!.ToSnapshot());
    }

    [Fact]
    public void The_player_projection_is_the_snapshot_minus_exactly_the_anti_cheat_tally()
    {
        Mirror(typeof(PlayerSnapshot), typeof(PlayerWireProjection), WithheldPlayerField);
    }

    [Fact]
    public void The_run_projection_is_the_snapshot_minus_exactly_the_seed()
    {
        Mirror(typeof(RunSnapshot), typeof(RunWireProjection), WithheldRunField);
    }

    [Fact]
    public void The_factories_copy_every_field_by_name()
    {
        var (playerRow, runRow) = PlayedRows();

        var profile = WireProjections.Of(playerRow);
        foreach (var property in typeof(PlayerWireProjection).GetProperties())
        {
            property.GetValue(profile).ShouldBe(
                typeof(PlayerSnapshot).GetProperty(property.Name)!.GetValue(playerRow),
                $"PlayerWireProjection.{property.Name} must be the snapshot's own value, uncomputed");
        }

        var run = WireProjections.Of(runRow);
        foreach (var property in typeof(RunWireProjection).GetProperties())
        {
            property.GetValue(run).ShouldBe(
                typeof(RunSnapshot).GetProperty(property.Name)!.GetValue(runRow),
                $"RunWireProjection.{property.Name} must be the snapshot's own value, uncomputed");
        }
    }

    [Fact]
    public void The_wire_hash_ignores_the_seed_and_the_tally_and_nothing_else_probed()
    {
        var (playerRow, runRow) = PlayedRows();
        var baseline = WireProjections.HashPlayerAndRun(playerRow, runRow);

        baseline.ShouldStartWith("fnv1a:");

        // The two withheld fields: flipping them must not move the wire hash.
        WireProjections.HashPlayerAndRun(playerRow, runRow with { RunSeed = runRow.RunSeed ^ 1UL })
            .ShouldBe(baseline, "the seed never leaves the server, so no client could reproduce a hash it moves");
        WireProjections.HashPlayerAndRun(
                playerRow with { BattleHashMismatches = playerRow.BattleHashMismatches + 1 }, runRow)
            .ShouldBe(baseline, "the tally is never player-facing, so no client could reproduce a hash it moves");

        // Negative controls on both sides: client-visible fields move it.
        WireProjections.HashPlayerAndRun(playerRow with { LegendXp = playerRow.LegendXp + 1 }, runRow)
            .ShouldNotBe(baseline);
        WireProjections.HashPlayerAndRun(playerRow, runRow with { CurrentHp = Math.Max(1, runRow.CurrentHp - 1) })
            .ShouldNotBe(baseline);

        // And the meta hash is the player alone: the run cannot move it.
        var metaBaseline = WireProjections.HashPlayerAlone(playerRow);
        metaBaseline.ShouldStartWith("fnv1a:");
        metaBaseline.ShouldNotBe(baseline, "player-alone and player-then-run are different byte streams");
    }

    /// <summary>
    /// The drift alarm: the projection's constructor parameters are the snapshot's, in the
    /// snapshot's order, minus exactly the withheld name — so a snapshot field added under a
    /// <c>SchemaVersion</c> bump fails HERE until the projection (and its pin) answer for it.
    /// </summary>
    private static void Mirror(Type snapshot, Type projection, string withheld)
    {
        static IEnumerable<string> ParameterNames(Type type) =>
            type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Single()
                .GetParameters()
                .Select(p => p.Name!);

        var expected = ParameterNames(snapshot).Where(name => name != withheld).ToArray();

        ParameterNames(snapshot).ShouldContain(
            withheld, customMessage: $"{snapshot.Name} no longer declares {withheld}; the withheld list is stale");

        ParameterNames(projection).ShouldBe(
            expected,
            $"{projection.Name} mirrors {snapshot.Name} field-for-field, in order, minus exactly " +
            $"'{withheld}'. A difference here is a serialisation change to the wire stateHash: " +
            "update the projection, its factory and the WireProjectionFieldOrder.json pin together, " +
            "deliberately — never by editing this list.");
    }
}
