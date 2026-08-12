using Shouldly;
using SlayIdleRepeat.Core.Primitives;
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
            20,
            "14 §16.2's table has twenty rows — 10 transport, 10 domain. Written as the literal 20 " +
            "rather than as Catalogue.Length: a count taken from the transcription cannot notice the " +
            "transcription itself being trimmed, which is the one edit both set differences above " +
            "would survive.");
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
            10,
            "30 §2's sentence names exactly ten domain-tier values. Written as the literal 10 rather " +
            "than as DomainTierPin.Length: the set differences above compare the tier against the pin, " +
            "so they both stay satisfied if the pin and the tier are trimmed together. Only a literal " +
            "notices that.");
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
            10,
            "14 §16.2's table has exactly ten transport rows. The literal, not TransportTierPin.Length, " +
            "for the reason given on the domain-tier case: a pin and a tier trimmed together satisfy " +
            "both set differences above.");
    }

    [Fact]
    public void The_two_tiers_partition_the_catalogue()
    {
        var all = RejectionReasons.All;

        all.Count.ShouldBe(
            20,
            "RejectionReasons.All is the whole of 14 §16.2's table — twenty rows. The literal is the " +
            "floor: without it the partition below holds just as happily over an empty All and two " +
            "empty tiers.");

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

    /// <summary>
    /// <c>IsDomainTier</c> is the question M1-06 asks of a handler result, so it is asked here of
    /// every row rather than of one value per tier — a two-sample check leaves eighteen values whose
    /// answer nothing in this suite has ever read.
    /// </summary>
    [Fact]
    public void IsDomainTier_answers_the_tier_column_of_14_16_2_for_every_value()
    {
        foreach (var (name, _, tier) in Catalogue)
        {
            var value = Enum.Parse<RejectionReason>(name);
            var isDomain = tier == RejectionReasonTier.Domain;

            RejectionReasons.IsDomainTier(value).ShouldBe(
                isDomain,
                $"14 §16.2 puts {name} in the {tier} tier, so IsDomainTier must answer {isDomain}. " +
                "M1-06 branches on this: a transport-tier value answering true would let the envelope " +
                "layer's refusal pass for a decision the domain made.");
        }
    }

    /// <summary>
    /// 🔒 The three published sets are immutable at runtime, not merely typed as if they were.
    /// </summary>
    /// <remarks>
    /// <c>IReadOnlyList&lt;T&gt;</c> over a bare array states an intention it cannot enforce: the
    /// runtime type is still <c>RejectionReason[]</c>, so one cast and one indexer write re-label a
    /// row of `14` §16.2 permanently, process-wide, for every reader of the static — with no
    /// allocation and nothing anywhere to notice. On <c>DomainTier</c> that is the set M1-06 will
    /// police handler results against, so the write inserts a transport-tier value into the list
    /// that decides whether a transport-tier value is allowed.
    /// </remarks>
    [Theory]
    [InlineData(nameof(RejectionReasons.All))]
    [InlineData(nameof(RejectionReasons.DomainTier))]
    [InlineData(nameof(RejectionReasons.TransportTier))]
    public void A_published_tier_set_cannot_be_written_through(string setName)
    {
        var set = setName switch
        {
            nameof(RejectionReasons.All) => RejectionReasons.All,
            nameof(RejectionReasons.DomainTier) => RejectionReasons.DomainTier,
            nameof(RejectionReasons.TransportTier) => RejectionReasons.TransportTier,
            _ => throw new InvalidOperationException($"Unhandled set '{setName}'."),
        };

        (set as RejectionReason[]).ShouldBeNull(
            $"RejectionReasons.{setName} is a bare array behind an IReadOnlyList. A caller who casts " +
            "it back can rewrite the catalogue for the whole process.");

        Should.Throw<NotSupportedException>(() => ((IList<RejectionReason>)set).Add(RejectionReason.RATE_LIMITED));
        Should.Throw<NotSupportedException>(() => ((IList<RejectionReason>)set)[0] = RejectionReason.RATE_LIMITED);
    }

    [Fact]
    public void TierOf_refuses_a_value_outside_the_catalogue()
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(
            () => RejectionReasons.TierOf((RejectionReason)9999));

        ShouldNameTheValueAndTheTable(thrown, "9999");
    }

    [Fact]
    public void TierOf_refuses_the_uninitialised_value()
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(
            () => RejectionReasons.TierOf(default));

        ShouldNameTheValueAndTheTable(
            thrown,
            "0",
            "default(RejectionReason) is not a rejection, and 0 is the only name it has. Classifying " +
            "it silently is how an unset field becomes a legal-looking 'no'.");
    }

    /// <summary>
    /// Both facts `14` §16.2 requires of the refusal — the offending value, and the table with no row
    /// for it — asserted as two independent fragments.
    /// </summary>
    /// <remarks>
    /// ⚠️ Deliberately <b>not</b> one ordered <c>ShouldMatchWildcard("*0*14 §16.2*")</c>.
    /// <see cref="ArgumentOutOfRangeException"/> appends its own <c>"Actual value was 0."</c> line
    /// <i>after</i> whatever message the thrower wrote, so an ordered pattern demanding the value
    /// before the citation cannot be satisfied by the idiomatic
    /// <c>new ArgumentOutOfRangeException(nameof(reason), reason, "… 14 §16.2 …")</c> at all — it
    /// would force the thrower to duplicate the value into the prose purely to satisfy a word order
    /// nothing in the spec asks for. Both fragments are the claim (steering S2: pin which rule fired);
    /// their order is not.
    /// </remarks>
    private static void ShouldNameTheValueAndTheTable(
        ArgumentOutOfRangeException thrown, string value, string? valueMessage = null)
    {
        thrown.Message.ShouldContain(
            value,
            Case.Sensitive,
            valueMessage
            ?? $"the refusal must name the unmapped value {value} — a refusal that does not say which " +
               "value it could not classify sends the reader to read the whole switch.");

        thrown.Message.ShouldContain(
            "14 §16.2",
            Case.Sensitive,
            "the refusal must cite the table that is missing a row for the value. A bare " +
            "ArgumentOutOfRangeException leaves the reader guessing which of the two enums drifted, " +
            "and TierOf is not the only thing in this file that can throw one.");
    }
}
