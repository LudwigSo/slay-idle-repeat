using Shouldly;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Wire;

/// <summary>
/// The pure half of the content check: which claimed stamp disagrees with the served one, and the
/// fact that only the session begin makes a claim at all.
/// </summary>
public sealed class ContentVersionCheckTests
{
    private static readonly ContentVersion Served = Worlds.Content.Version;

    /// <summary>Every command on the wire that carries no content hash, plus the floor that sweep needs.</summary>
    /// <remarks>
    /// The parameterless rows are reflected so a command added to the registry joins the sweep
    /// without an edit here; the two below them are spelled out because their payloads cannot be
    /// guessed, and one of them opens a run — the case a version check is most tempted to special-case.
    /// </remarks>
    private static readonly IReadOnlyList<GameCommand> CommandsWithoutAHash =
        GameRules.CommandTypesByWireName.Values
            .Where(type => type != typeof(BeginSessionCommand))
            .Where(type => type.GetConstructor(Type.EmptyTypes) is not null)
            .Select(type => (GameCommand)Activator.CreateInstance(type)!)
            .Concat<GameCommand>([new StartRunCommand(1, DifficultyTier.NORMAL), new PickPerkCommand(0)])
            .ToArray();

    /// <summary>Claimed stamps that are not the served one, each wrong in its own way.</summary>
    public static TheoryData<string> StampsThatDisagree() =>
        new()
        {
            new string('a', ContentVersion.HexLength),
            "deadbeef",
            "sha256:" + Worlds.Content.Version.Value,
            Worlds.Content.Version.Value.ToUpperInvariant(),
            string.Empty,
        };

    [Theory]
    [MemberData(nameof(StampsThatDisagree))]
    public void A_claimed_stamp_that_is_not_the_served_one_is_a_content_mismatch(string claimed)
    {
        ContentVersionCheck.Check(new BeginSessionCommand("1.0", claimed), Served).ShouldBe(
            RejectionReason.CONTENT_VERSION_MISMATCH,
            $"'{claimed}' is not the stamp this server serves, and a client holding the wrong content " +
            "needs to be told to fetch content — not that its envelope is broken");
    }

    [Fact]
    public void A_claimed_stamp_equal_to_the_served_one_passes()
    {
        ContentVersionCheck.Check(new BeginSessionCommand("1.0", Served.Value), Served).ShouldBeNull(
            "the whole point of the check is that a client on the right content is waved through");
    }

    [Fact]
    public void Every_command_that_carries_no_hash_passes_whatever_the_served_stamp_is()
    {
        CommandsWithoutAHash.Count.ShouldBeGreaterThanOrEqualTo(
            15,
            "the registry's hash-less commands are reflected, and a sweep over an empty or shrunken " +
            "set would agree with any implementation at all");

        CommandsWithoutAHash
            .Where(command => ContentVersionCheck.Check(command, Served) is not null)
            .Select(command => command.GetType().Name)
            .ShouldBeEmpty(
                "nothing but the session begin states which content the client loaded, so nothing " +
                "else has a claim that could disagree with the server's");
    }
}
