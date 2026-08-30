using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Wire;

/// <summary>
/// The decode half in isolation: one vocabulary (the dispatch table's own index), strict payload
/// binding, and the two rejections it can earn. The gateway suites cover the same codec in flow;
/// these cases pin the mapping details a flow test would blur.
/// </summary>
public sealed class WireCommandCodecTests
{
    private static CommandEnvelope Envelope(string type, string payload) =>
        new(1, new CommandId("c-1"), 1, type, JsonDocument.Parse(payload).RootElement.Clone());

    [Fact]
    public void The_codec_resolves_names_against_the_dispatch_tables_own_index()
    {
        // Floor (S3): the registry the codec reads is the real one, not an empty stand-in.
        GameRules.CommandTypesByWireName.Count.ShouldBeGreaterThanOrEqualTo(49);
        GameRules.CommandTypesByWireName.ContainsKey("ROLL_DICE").ShouldBeTrue();

        var decoded = WireCommandCodec.Decode(Envelope("ROLL_DICE", "{}"));

        decoded.Rejection.ShouldBeNull();
        decoded.Command.ShouldBeOfType<RollDiceCommand>();
    }

    /// <summary>
    /// 🔒 <b>What the codec WRITES is spelled the way what it reads is spelled.</b>
    /// </summary>
    /// <remarks>
    /// ⚠️ The pin that was missing, and its absence is exactly why the split survived every review:
    /// the binding options are case-insensitive, so a decode test cannot see the write side at all,
    /// and <c>EncodePayload</c> emitted <c>ItemId</c>/<c>GearSlot</c> while every envelope a client
    /// sends — and `14` §2.3's own payload sketches — spell them <c>itemId</c>/<c>gearSlot</c>. Those
    /// bytes are not private: they are stored verbatim in the idempotency record and replayed to the
    /// client, so the wire carried two dialects at once.
    /// <para>
    /// Asserted on the literal text rather than by re-decoding, which is the tautology that would
    /// have passed throughout: a round-trip through case-insensitive options proves nothing about
    /// the spelling.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_encoded_payload_is_spelled_the_way_the_envelope_that_carries_it_is()
    {
        var encoded = WireCommandCodec.EncodePayload(
            new EquipCommand(new GearInstanceId("g-123"), GearSlot.WEAPON));

        encoded.ShouldBe(
            "{\"itemId\":\"g-123\",\"gearSlot\":\"WEAPON\"}",
            "the response envelope's renderer is camelCase and so is every payload the client sends; " +
            "a second dialect on the same conversation is invisible here and visible to anyone " +
            "reading idempotency_records.payload or writing a client against the document.");

        WireCommandCodec.Decode(Envelope("EQUIP", encoded))
                        .Command.ShouldBeOfType<EquipCommand>()
                        .ItemId.ShouldBe(new GearInstanceId("g-123"));
    }

    [Fact]
    public void A_payload_binds_to_the_commands_own_constructor_including_ids_and_enums()
    {
        var equip = WireCommandCodec.Decode(
            Envelope("EQUIP", "{\"itemId\": \"g-123\", \"gearSlot\": \"WEAPON\"}"));

        equip.Command.ShouldBeOfType<EquipCommand>().ItemId.ShouldBe(new GearInstanceId("g-123"));

        var merge = WireCommandCodec.Decode(Envelope(
            "MERGE", "{\"inputItemIds\": [\"g-1\", \"g-2\", \"g-3\"], \"dustSubstituted\": false}"));

        var command = merge.Command.ShouldBeOfType<MergeCommand>();
        command.InputItemIds.Select(id => id.Value).ShouldBe(["g-1", "g-2", "g-3"]);
        command.DustSubstituted.ShouldBeFalse();
    }

    [Fact]
    public void Payload_member_names_bind_case_insensitively_the_way_the_wire_sketches_spell_them()
    {
        // 14 §2.3 sketches camelCase; the records declare PascalCase. Both must decode to the same
        // command, or half the registry would be unreachable from a spec-following client.
        var camel = WireCommandCodec.Decode(Envelope("PICK_PERK", "{\"optionIndex\": 2}"));
        var pascal = WireCommandCodec.Decode(Envelope("PICK_PERK", "{\"OptionIndex\": 2}"));

        camel.Command.ShouldBe(pascal.Command);
        camel.Command.ShouldBeOfType<PickPerkCommand>().OptionIndex.ShouldBe(2);
    }

    [Theory]
    [InlineData("NOT_A_COMMAND")]
    [InlineData("roll_dice")] // the registry is SCREAMING_SNAKE and the match is ordinal
    [InlineData("USE_REROLL")]
    public void A_name_with_no_registry_row_is_UNKNOWN_COMMAND_TYPE(string type)
    {
        var decoded = WireCommandCodec.Decode(Envelope(type, "{}"));

        decoded.Command.ShouldBeNull();
        decoded.Rejection.ShouldBe(RejectionReason.UNKNOWN_COMMAND_TYPE);
    }

    [Theory]
    [InlineData("EQUIP", "{\"itemId\": \"   \", \"gearSlot\": \"WEAPON\"}", "a blank id the constructor's own guard refuses")]
    [InlineData("EQUIP", "{\"itemId\": \"g-1\", \"gearSlot\": \"NOT_A_SLOT\"}", "an enum value outside the vocabulary")]
    [InlineData("MERGE", "{\"inputItemIds\": \"g-1\", \"dustSubstituted\": false}", "a scalar where a list belongs")]
    [InlineData("PICK_PERK", "{\"optionIndex\": 1, \"stray\": true}", "an unmapped member — a silently dropped intent")]
    [InlineData("PICK_PERK", "[]", "a payload that is not an object")]
    public void A_payload_the_schema_refuses_is_MALFORMED_COMMAND(string type, string payload, string why)
    {
        var decoded = WireCommandCodec.Decode(Envelope(type, payload));

        decoded.Rejection.ShouldBe(RejectionReason.MALFORMED_COMMAND, why);
    }

    [Fact]
    public void Decode_refuses_a_null_envelope()
    {
        Should.Throw<ArgumentNullException>(() => WireCommandCodec.Decode(null!));
    }
}
