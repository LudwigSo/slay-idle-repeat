using SlayIdleRepeat.Core.Events;

namespace SlayIdleRepeat.Core.Tests.Events;

/// <summary>
/// Event shapes that break one rule each, so <c>DomainEventShape</c>'s predicates can be shown to
/// bite without a violation ever being committed to <c>SlayIdleRepeat.Core</c>.
/// </summary>
/// <remarks>
/// 🔒 These live in <c>Core.Tests</c>, never in <c>Core</c>: every rule reads its subject set out of
/// <c>typeof(DomainEvent).Assembly</c>, so nothing here is ever in that set.
/// <para>
/// ⚠️ The nested types and their members are <c>public</c> on purpose — the predicates read
/// <c>BindingFlags.Public</c>, so an <c>internal</c> fixture would be invisible and every self-test
/// would pass by reflecting over nothing. The enclosing class is <c>internal</c> and
/// <c>Type.IsPublic</c> is <c>false</c> for a nested type, which keeps these out of the real set.
/// </para>
/// </remarks>
internal static class NonConformingEvents
{
    /// <summary>Takes <c>Sequence</c> second. Compiles, serialises, reads fine — and is wrong (`30` §7).</summary>
    public sealed record SequenceIsNotFirst(string Reason, int Sequence) : DomainEvent(Sequence);

    /// <summary>
    /// Declares one constructor and it takes nothing, so there is no first parameter for
    /// <c>Sequence</c> to be. The ordinal is invented at construction instead of being assigned by
    /// <c>GameRules.Apply</c> (`30` §7).
    /// </summary>
    public sealed record NoConstructorParameters : DomainEvent
    {
        /// <summary>The only constructor, and it carries no ordinal.</summary>
        public NoConstructorParameters()
            : base(0)
        {
        }
    }

    /// <summary>Stamps itself with a clock reading the domain has no business holding (`30` §3).</summary>
    public sealed record SelfStamped(int Sequence, DateTime OccurredAt) : DomainEvent(Sequence);

    /// <summary>
    /// Stamps itself with clock readings the check must reach <i>through</i> a nullable, a generic
    /// argument and an array element — the three shapes <c>DomainEventShape.Flatten</c> exists for.
    /// </summary>
    /// <remarks>
    /// A payload wrapping its time in a nullable or a list is the ordinary shape, not a contrived one.
    /// If <c>Flatten</c> stopped unwrapping any of these, the clock rule would go silent on exactly the
    /// events most likely to carry a timestamp, and <see cref="SelfStamped"/>'s bare
    /// <c>DateTime</c> would keep it looking healthy.
    /// </remarks>
    public sealed record SelfStampedIndirectly(
        int Sequence,
        DateTimeOffset? Window,
        IReadOnlyList<TimeSpan> Durations,
        DateOnly[] Days,
        TimeOnly Cutoff) : DomainEvent(Sequence);

    /// <summary>Lets a consumer rewrite a fact that has already happened (`14` §7.1).</summary>
    public sealed record Rewritable(int Sequence) : DomainEvent(Sequence)
    {
        /// <summary>A settable property — the one thing an append-only log row must not have.</summary>
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>
    /// Hides the same mutation surface behind an <c>internal</c> setter (`14` §7.1) — reachable
    /// from every handler in <c>Core</c>, and from this suite, but invisible to a check that only
    /// looks at <c>IsPublic</c>.
    /// </summary>
    public sealed record InternallyRewritable(int Sequence) : DomainEvent(Sequence)
    {
        /// <summary>Settable from inside the assembly, which is where the damage would be done.</summary>
        public string Note { get; internal set; } = string.Empty;
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
