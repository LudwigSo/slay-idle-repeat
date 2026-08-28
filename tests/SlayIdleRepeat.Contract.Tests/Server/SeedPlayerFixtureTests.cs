using Shouldly;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Core.Model.Snapshots;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Server;

/// <summary>
/// The committed CI-probe seed (<c>build/ci/probes/seed-player.json</c>) stays a row this build can
/// read: the compose-boot probes INSERT it raw, so nothing on that path would notice it rotting —
/// this test is the tripwire, and a <c>SchemaVersion</c> bump is what fires it. Whoever bumps the
/// schema regenerates the file in the same change (encode a fresh profile through
/// <c>SnapshotCodec.EncodeSlice</c> and write it over this file).
/// </summary>
public sealed class SeedPlayerFixtureTests
{
    private static byte[] Seed()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SlayIdleRepeat.sln")))
        {
            directory = directory.Parent!;
        }

        directory.ShouldNotBeNull();

        return File.ReadAllBytes(
            Path.Combine(directory.FullName, "build", "ci", "probes", "seed-player.json"));
    }

    [Fact]
    public void The_seed_decodes_at_this_builds_schema_with_no_run()
    {
        var stored = SnapshotCodec.DecodeSlice(Seed());

        stored.Player.SchemaVersion.ShouldBe(
            SnapshotSchema.SchemaVersion,
            "the probes insert this document raw; a stale schema version would make the seeded "
            + "player's first command fail rehydration on the live stack, far from this file.");
        stored.Run.ShouldBeNull("the seed is a fresh account, straight off creation.");
    }

    [Fact]
    public void The_seed_carries_the_shape_the_probes_rewrite_and_assert()
    {
        var stored = SnapshotCodec.DecodeSlice(Seed());

        // The probes jsonb_set the {Player,Id} path and assert display_name after the first save.
        stored.Player.Id.Value.ShouldNotBeNullOrEmpty();
        stored.Player.DisplayName.ShouldBe(
            "CI Probe",
            "the round-trip probe asserts this exact text in the typed column after the save "
            + "extracts it from the document.");
    }
}
