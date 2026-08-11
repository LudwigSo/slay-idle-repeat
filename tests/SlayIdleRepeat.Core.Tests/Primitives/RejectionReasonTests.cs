using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Primitives;

/// <summary>
/// `14` §16.2 / `30` §2 — the <see cref="RejectionReason"/> catalogue, its permanent wire
/// numbers, and the two-tier split that decides which values <c>Apply</c> may return.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 Every list here is written out as literal names, not derived from the enum. A pin
/// generated from the thing it is pinning proves only self-consistency: deriving "the domain
/// tier" from <c>TierOf</c> and then asserting <c>TierOf</c> against it is a test that cannot
/// fail. The ten domain names come from `30` §2's sentence, the twenty rows from `14` §16.2's
/// table, and the numbers from the enum's own permanence rule.
/// </para>
/// <para>
/// ⚠️ A failure here is not a test to fix. `14` §16.2: <i>"values may be appended, never
/// renamed or reused"</i>. Appending one is a deliberate change to a wire contract, and it is
/// meant to cost a deliberate edit to these lists.
/// </para>
/// </remarks>
public sealed class RejectionReasonTests
{
    /// <summary>
    /// The `14` §16.2 table, transcribed: every value, its permanent wire number, and its tier.
    /// </summary>
    private static readonly (string Name, int Wire, RejectionReasonTier Tier)[] Catalogue =
    {
        ("MALFORMED_COMMAND", 1, RejectionReasonTier.Transport),
        ("UNKNOWN_COMMAND_TYPE", 2, RejectionReasonTier.Transport),
        ("PROTOCOL_VERSION_UNSUPPORTED", 3, RejectionReasonTier.Transport),
        ("CONTENT_VERSION_MISMATCH", 4, RejectionReasonTier.Transport),
        ("SEQUENCE_GAP", 5, RejectionReasonTier.Transport),
        ("SEQUENCE_STALE", 6, RejectionReasonTier.Transport),
        ("IDEMPOTENCY_CONFLICT", 7, RejectionReasonTier.Transport),
        ("RATE_LIMITED", 8, RejectionReasonTier.Transport),
        ("FEATURE_DISABLED", 9, RejectionReasonTier.Transport),
        ("RUN_NOT_FOUND", 10, RejectionReasonTier.Transport),
        ("RUN_EXPIRED", 11, RejectionReasonTier.Domain),
        ("RUN_ALREADY_ENDED", 12, RejectionReasonTier.Domain),
        ("ILLEGAL_STATE", 13, RejectionReasonTier.Domain),
        ("INSUFFICIENT_ENERGY", 14, RejectionReasonTier.Domain),
        ("INSUFFICIENT_FUNDS", 15, RejectionReasonTier.Domain),
        ("CAP_REACHED", 16, RejectionReasonTier.Domain),
        ("COOLDOWN_ACTIVE", 17, RejectionReasonTier.Domain),
        ("NOT_OWNED", 18, RejectionReasonTier.Domain),
        ("NOT_ENTITLED", 19, RejectionReasonTier.Domain),
        ("INVENTORY_FULL", 20, RejectionReasonTier.Domain),
    };

    /// <summary>
    /// 🔒 `30` §2, quoted: <i>"`Apply` returns only the domain-tier values (`ILLEGAL_STATE`,
    /// `INSUFFICIENT_ENERGY`, `INSUFFICIENT_FUNDS`, `CAP_REACHED`, `COOLDOWN_ACTIVE`,
    /// `NOT_OWNED`, `NOT_ENTITLED`, `INVENTORY_FULL`, `RUN_EXPIRED`, `RUN_ALREADY_ENDED`)"</i>.
    /// Written in that sentence's order rather than the enum's, so it reads as the transcription
    /// it is.
    /// </summary>
    private static readonly RejectionReason[] DomainTierPin =
    {
        RejectionReason.ILLEGAL_STATE,
        RejectionReason.INSUFFICIENT_ENERGY,
        RejectionReason.INSUFFICIENT_FUNDS,
        RejectionReason.CAP_REACHED,
        RejectionReason.COOLDOWN_ACTIVE,
        RejectionReason.NOT_OWNED,
        RejectionReason.NOT_ENTITLED,
        RejectionReason.INVENTORY_FULL,
        RejectionReason.RUN_EXPIRED,
        RejectionReason.RUN_ALREADY_ENDED,
    };

    /// <summary>
    /// The other ten of `14` §16.2 — produced by the server host / Application layer, and never
    /// reaching <c>GameRules.Apply</c> at all.
    /// </summary>
    private static readonly RejectionReason[] TransportTierPin =
    {
        RejectionReason.MALFORMED_COMMAND,
        RejectionReason.UNKNOWN_COMMAND_TYPE,
        RejectionReason.PROTOCOL_VERSION_UNSUPPORTED,
        RejectionReason.CONTENT_VERSION_MISMATCH,
        RejectionReason.SEQUENCE_GAP,
        RejectionReason.SEQUENCE_STALE,
        RejectionReason.IDEMPOTENCY_CONFLICT,
        RejectionReason.RATE_LIMITED,
        RejectionReason.FEATURE_DISABLED,
        RejectionReason.RUN_NOT_FOUND,
    };

    [Fact]
    public void The_catalogue_is_exactly_the_twenty_values_of_14_16_2()
    {
        var declared = Enum.GetNames<RejectionReason>();
        var pinned = Catalogue.Select(row => row.Name).ToArray();

        declared.Except(pinned, StringComparer.Ordinal).ShouldBeEmpty(
            "RejectionReason declares a value that 14 §16.2's table does not list. The enum IS the " +
            "wire vocabulary — a value the table has never heard of cannot be understood by any client.");

        pinned.Except(declared, StringComparer.Ordinal).ShouldBeEmpty(
            "14 §16.2 lists a value RejectionReason does not declare. Every row of that table is a " +
            "rejection some producer has to be able to send.");

        declared.Length.ShouldBe(
            Catalogue.Length,
            "the catalogue is 20 values — 10 transport, 10 domain. A count that moved without either " +
            "set difference firing means a duplicate name, which the enum cannot express.");
    }

    [Fact]
    public void Every_value_is_pinned_to_its_permanent_wire_number()
    {
        foreach (var (name, wire, _) in Catalogue)
        {
            Enum.TryParse<RejectionReason>(name, out var value).ShouldBeTrue(
                $"14 §16.2 lists '{name}'; RejectionReason does not declare it.");

            ((int)value).ShouldBe(
                wire,
                $"RejectionReason.{name} is numbered {(int)value}, not the {wire} it has always been on " +
                "the wire. CanonicalStateWriter encodes an enum as its NUMERIC value (14 §16.6), and " +
                "14 §16.2 forbids reusing one. Renumbering silently re-labels every rejection already " +
                "recorded against the old number — append a value, never renumber one.");
        }
    }

    [Fact]
    public void No_two_values_share_a_wire_number()
    {
        var numbers = Enum.GetValues<RejectionReason>().Select(value => (int)value).ToArray();

        numbers.Distinct().Count().ShouldBe(
            numbers.Length,
            "two RejectionReason members share a numeric value. 14 §16.2: a value is never reused. " +
            "An alias makes two distinct rejections indistinguishable once they are on the wire or in " +
            "a canonical hash. Duplicates: " +
            string.Join(", ", numbers.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key)));
    }

    [Fact]
    public void Zero_is_not_a_rejection_reason()
    {
        Enum.IsDefined((RejectionReason)0).ShouldBeFalse(
            "0 is default(RejectionReason). 14 §16.2 authorises no 'none' value, so numbering starts " +
            "at 1 and an uninitialised field can never read as a real rejection.");
    }

    [Fact]
    public void The_domain_tier_is_exactly_the_ten_values_30_2_names()
    {
        var actual = RejectionReasons.DomainTier;

        DomainTierPin.Except(actual).ShouldBeEmpty(
            "30 §2 names this value as a domain-tier rejection, but RejectionReasons.TierOf does not " +
            "classify it as one. GameRules.Apply would then be unable to return a rejection the domain " +
            "model is specified to produce.");

        actual.Except(DomainTierPin).ShouldBeEmpty(
            "RejectionReasons.TierOf classifies this value as domain-tier, but 30 §2 does not name it. " +
            "A transport-tier value reaching Apply means the domain is deciding something the envelope " +
            "layer already decided — 30 §8: the domain sees each command exactly once, and only " +
            "well-formed ones.");

        actual.Count.ShouldBe(
            DomainTierPin.Length,
            "30 §2's sentence names exactly ten domain-tier values. This is a floor as well as a " +
            "ceiling: a tier set that quietly emptied would satisfy both set differences above.");
    }

    [Fact]
    public void The_transport_tier_is_exactly_the_ten_values_14_16_2_names()
    {
        var actual = RejectionReasons.TransportTier;

        TransportTierPin.Except(actual).ShouldBeEmpty(
            "14 §16.2 marks this value transport-tier, but RejectionReasons.TierOf does not.");

        actual.Except(TransportTierPin).ShouldBeEmpty(
            "RejectionReasons.TierOf marks this value transport-tier, but 14 §16.2 does not. A " +
            "domain-tier value classified as transport would be refused when Apply returns it.");

        actual.Count.ShouldBe(
            TransportTierPin.Length,
            "14 §16.2's table has exactly ten transport rows.");
    }

    [Fact]
    public void The_two_tiers_partition_the_catalogue()
    {
        var all = RejectionReasons.All;

        all.Count.ShouldBe(Catalogue.Length);

        RejectionReasons.DomainTier.Intersect(RejectionReasons.TransportTier).ShouldBeEmpty(
            "a value cannot be produced by both tiers — 14 §16.2 gives every row exactly one.");

        RejectionReasons.DomainTier
            .Concat(RejectionReasons.TransportTier)
            .OrderBy(value => (int)value)
            .ShouldBe(
                all.OrderBy(value => (int)value),
                "every declared value belongs to exactly one tier. A value in neither is a rejection " +
                "no producer is allowed to send.");
    }

    [Fact]
    public void TierOf_agrees_with_the_tier_column_of_14_16_2()
    {
        foreach (var (name, _, tier) in Catalogue)
        {
            var value = Enum.Parse<RejectionReason>(name);

            RejectionReasons.TierOf(value).ShouldBe(
                tier,
                $"14 §16.2 puts {name} in the {tier} tier.");
        }
    }

    [Fact]
    public void IsDomainTier_is_the_question_M1_06_asks_of_a_handler_result()
    {
        RejectionReasons.IsDomainTier(RejectionReason.INSUFFICIENT_ENERGY).ShouldBeTrue();
        RejectionReasons.IsDomainTier(RejectionReason.SEQUENCE_GAP).ShouldBeFalse();
    }

    [Fact]
    public void TierOf_refuses_a_value_outside_the_catalogue()
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(
            () => RejectionReasons.TierOf((RejectionReason)9999));

        thrown.Message.ShouldMatchWildcard(
            "*9999*14 §16.2*",
            "the refusal must name the unmapped value and the table that is missing a row for it — a " +
            "bare ArgumentOutOfRangeException leaves the reader guessing which of the two enums drifted.");
    }

    [Fact]
    public void TierOf_refuses_the_uninitialised_value()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RejectionReasons.TierOf(default))
            .Message.ShouldMatchWildcard(
                "*0*14 §16.2*",
                "default(RejectionReason) is not a rejection. Classifying it silently is how an " +
                "unset field becomes a legal-looking 'no'.");
    }
}
