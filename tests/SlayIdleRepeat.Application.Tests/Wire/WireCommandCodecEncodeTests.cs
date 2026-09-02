using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Wire;

/// <summary>The codec's encode half: what a durable record stores, symmetric with what <c>Decode</c> reads.</summary>
public sealed class WireCommandCodecEncodeTests
{
    [Fact]
    public void The_wire_name_is_the_registrys_own()
    {
        WireCommandCodec.WireNameOf(new StartRunCommand(1, DifficultyTier.NORMAL)).ShouldBe("START_RUN");
        WireCommandCodec.WireNameOf(new BeginSessionCommand("0.1.0", "sha256:abc")).ShouldBe("BEGIN_SESSION");
    }

    [Fact]
    public void A_null_command_is_a_null_argument_fault()
    {
        Should.Throw<ArgumentNullException>(() => WireCommandCodec.WireNameOf(null!));
        Should.Throw<ArgumentNullException>(() => WireCommandCodec.EncodePayload(null!));
    }

    [Theory]
    [InlineData(1, DifficultyTier.NORMAL)]
    [InlineData(2, DifficultyTier.HEROIC)]
    public void An_encoded_payload_decodes_back_to_an_equal_command(int chapter, DifficultyTier tier)
    {
        var command = new StartRunCommand(chapter, tier);

        var decoded = DecodeThroughTheOneCodec(command);

        decoded.ShouldBe(command,
            "the record's payload identity is the decoded-record equality — an encode the decode "
            + "half cannot invert would make every replayed duplicate an IDEMPOTENCY_CONFLICT.");
    }

    [Fact]
    public void String_carrying_payloads_survive_the_round_trip()
    {
        var command = new BeginSessionCommand("0.1.0-dev+meta", "sha256:ünïcode-and-\"quotes\"");

        DecodeThroughTheOneCodec(command).ShouldBe(command);
    }

    private static GameCommand DecodeThroughTheOneCodec(GameCommand command)
    {
        using var payload = JsonDocument.Parse(WireCommandCodec.EncodePayload(command));

        var decode = WireCommandCodec.Decode(new CommandEnvelope(
            1, new CommandId("CMD_roundtrip"), 1,
            WireCommandCodec.WireNameOf(command), payload.RootElement.Clone()));

        decode.Command.ShouldNotBeNull(
            "the canonical encoding decoded to a refusal: " + decode.Rejection);

        return decode.Command;
    }
}
