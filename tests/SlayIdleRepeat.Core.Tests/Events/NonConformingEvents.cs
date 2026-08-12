using SlayIdleRepeat.Core.Events;

namespace SlayIdleRepeat.Core.Tests.Events;

/// <summary>
/// Event shapes that break one rule each, so <c>DomainEventShape</c>'s predicates can be shown to
/// bite without a violation ever being committed to <c>SlayIdleRepeat.Core</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 These live in <c>SlayIdleRepeat.Core.Tests</c>, never in <c>Core</c>. Every rule in
/// <c>DomainEventTests</c> reads its subject set out of <c>typeof(DomainEvent).Assembly</c>, so
/// nothing here is ever in that set — which is the point: the rules govern the real hierarchy and
/// these prove the rules can fail.
/// </para>
/// <para>
/// ⚠️ The nested types and their members are <c>public</c> on purpose. The predicates under test
/// read <c>BindingFlags.Public</c>, so an <c>internal</c> fixture would be invisible to them and
/// every self-test below would pass by reflecting over nothing — a teeth-check with no teeth. The
/// enclosing class is <c>internal</c>, so none of this is visible outside the test assembly, and
/// <c>Type.IsPublic</c> is <c>false</c> for a nested type regardless, which is what keeps these
/// out of the real subject set.
/// </para>
/// </remarks>
internal static class NonConformingEvents
{
    /// <summary>Takes <c>Sequence</c> second. Compiles, serialises, reads fine — and is wrong (`30` §7).</summary>
    public sealed record SequenceIsNotFirst(string Reason, int Sequence) : DomainEvent(Sequence);

    /// <summary>Stamps itself with a clock reading the domain has no business holding (`30` §3).</summary>
    public sealed record SelfStamped(int Sequence, DateTime OccurredAt) : DomainEvent(Sequence);

    /// <summary>Lets a consumer rewrite a fact that has already happened (`14` §7.1).</summary>
    public sealed record Rewritable(int Sequence) : DomainEvent(Sequence)
    {
        /// <summary>A settable property — the one thing an append-only log row must not have.</summary>
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>
    /// Declares two constructors, so "the first parameter" has no single answer. Its extra
    /// property is <c>init</c>-only, keeping this fixture a violation of the constructor rule and
    /// of nothing else.
    /// </summary>
    public sealed record TwoConstructors : DomainEvent
    {
        /// <summary>The ordinal-only form.</summary>
        public TwoConstructors(int sequence)
            : base(sequence) => Note = string.Empty;

        /// <summary>The form that also carries a note.</summary>
        public TwoConstructors(int sequence, string note)
            : base(sequence) => Note = note;

        /// <summary>Set at construction only.</summary>
        public string Note { get; init; }
    }
}
