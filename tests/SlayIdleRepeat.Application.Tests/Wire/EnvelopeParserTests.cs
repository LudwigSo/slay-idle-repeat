using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Application.Wire;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Wire;

/// <summary>The 400 boundary in isolation: JSON parse plus required-field presence, and nothing past it.</summary>
public sealed class EnvelopeParserTests
{
    [Fact]
    public void A_complete_envelope_parses_with_every_field_as_sent()
    {
        var parse = EnvelopeParser.Parse(
            "{\"protocolVersion\": 1, \"commandId\": \"c_8f3a\", \"sequence\": 47, " +
            "\"type\": \"ROLL_DICE\", \"payload\": {\"a\": 1}}");

        parse.Fault.ShouldBeNull();
        var envelope = parse.Envelope!;
        envelope.ProtocolVersion.ShouldBe(1);
        envelope.CommandId.Value.ShouldBe("c_8f3a");
        envelope.Sequence.ShouldBe(47);
        envelope.Type.ShouldBe("ROLL_DICE");
        envelope.Payload.GetProperty("a").GetInt32().ShouldBe(1);
    }

    [Fact]
    public void An_absent_payload_reads_as_an_empty_object()
    {
        var parse = EnvelopeParser.Parse(
            "{\"protocolVersion\": 1, \"commandId\": \"c-1\", \"sequence\": 1, \"type\": \"ROLL_DICE\"}");

        parse.Envelope!.Payload.ValueKind.ShouldBe(JsonValueKind.Object);
        parse.Envelope.Payload.EnumerateObject().Count().ShouldBe(0);
    }

    [Fact]
    public void An_unknown_top_level_member_is_ignored_for_the_skew_windows_sake()
    {
        var parse = EnvelopeParser.Parse(
            "{\"protocolVersion\": 1, \"commandId\": \"c-1\", \"sequence\": 1, " +
            "\"type\": \"ROLL_DICE\", \"aFieldFromVersion2\": true}");

        parse.Envelope.ShouldNotBeNull(
            "a server that 400s on an unknown envelope member breaks the very version skew " +
            "14 §16.1 keeps open; the payload is the strict half, not the envelope");
    }

    [Theory]
    [InlineData("not json", "not JSON at all")]
    [InlineData("42", "JSON but not an object")]
    [InlineData("{\"commandId\": \"c\", \"sequence\": 1, \"type\": \"T\"}", "protocolVersion absent")]
    [InlineData("{\"protocolVersion\": 1.5, \"commandId\": \"c\", \"sequence\": 1, \"type\": \"T\"}", "protocolVersion not an integer")]
    [InlineData("{\"protocolVersion\": 1, \"sequence\": 1, \"type\": \"T\"}", "commandId absent")]
    [InlineData("{\"protocolVersion\": 1, \"commandId\": \"\", \"sequence\": 1, \"type\": \"T\"}", "commandId empty")]
    [InlineData("{\"protocolVersion\": 1, \"commandId\": \"a b\", \"sequence\": 1, \"type\": \"T\"}", "commandId with whitespace")]
    [InlineData("{\"protocolVersion\": 1, \"commandId\": \"c\", \"type\": \"T\"}", "sequence absent")]
    [InlineData("{\"protocolVersion\": 1, \"commandId\": \"c\", \"sequence\": \"1\", \"type\": \"T\"}", "sequence not a number")]
    [InlineData("{\"protocolVersion\": 1, \"commandId\": \"c\", \"sequence\": 1}", "type absent")]
    [InlineData("{\"protocolVersion\": 1, \"commandId\": \"c\", \"sequence\": 1, \"type\": \"\"}", "type empty")]
    public void A_body_outside_the_boundary_is_a_fault_not_an_envelope(string body, string why)
    {
        var parse = EnvelopeParser.Parse(body);

        parse.Envelope.ShouldBeNull(why);
        parse.Fault.ShouldNotBeNullOrEmpty("the fault text is the telemetry breadcrumb");
    }

    [Fact]
    public void The_parsed_payload_survives_its_documents_disposal()
    {
        // The parser clones the payload element; a forgotten Clone here is an
        // ObjectDisposedException on the first decode, but only sometimes — pin it.
        var parse = EnvelopeParser.Parse(
            "{\"protocolVersion\": 1, \"commandId\": \"c-1\", \"sequence\": 1, " +
            "\"type\": \"PICK_PERK\", \"payload\": {\"optionIndex\": 1}}");

        GC.Collect();

        parse.Envelope!.Payload.GetProperty("optionIndex").GetInt32().ShouldBe(1);
    }

    [Fact]
    public void Parse_refuses_a_null_body_as_the_host_adapters_fault()
    {
        Should.Throw<ArgumentNullException>(() => EnvelopeParser.Parse(null!));
    }
}
