using SlayIdleRepeat.Core.Events;

namespace SlayIdleRepeat.Core.Tests.Events;

/// <summary>
/// Event shapes that break one rule each, so <c>DomainEventShape</c>'s predicates can be shown to
/// bite without a violation ever being committed to <c>SlayIdleRepeat.Core</c>. These live in
/// <c>Core.Tests</c>, never in <c>Core</c>, so nothing here is ever in the real subject set. The
/// nested types and their members are public on purpose, since the predicates read
/// <c>BindingFlags.Public</c> and an internal fixture would be invisible.
/// </summary>
internal static class NonConformingEvents
{
    /// <summary>Takes <c>Sequence</c> second. Compiles, serialises, reads fine — and is wrong.</summary>
    public sealed record SequenceIsNotFirst(string Reason, int Sequence) : DomainEvent(Sequence);

    /// <summary>
    /// Declares one constructor and it takes nothing, so there is no first parameter for
    /// <c>Sequence</c> to be. The ordinal is invented at construction instead of being assigned by
    /// <c>GameRules.Apply</c>.
    /// </summary>
    public sealed record NoConstructorParameters : DomainEvent
    {
        public NoConstructorParameters()
            : base(0)
        {
        }
    }

    /// <summary>Stamps itself with a clock reading the domain has no business holding.</summary>
    public sealed record SelfStamped(int Sequence, DateTime OccurredAt) : DomainEvent(Sequence);

    /// <summary>
    /// Stamps itself with clock readings the check must reach through a nullable, a generic argument
    /// and an array element — the shapes <c>DomainEventShape.Flatten</c> exists for.
    /// </summary>
    public sealed record SelfStampedIndirectly(
        int Sequence,
        DateTimeOffset? Window,
        IReadOnlyList<TimeSpan> Durations,
        DateOnly[] Days,
        TimeOnly Cutoff) : DomainEvent(Sequence);

    /// <summary>Lets a consumer rewrite a fact that has already happened.</summary>
    public sealed record Rewritable(int Sequence) : DomainEvent(Sequence)
    {
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>
    /// Hides the same mutation surface behind an <c>internal</c> setter — reachable from every
    /// handler in Core, but invisible to a check that only looks at <c>IsPublic</c>.
    /// </summary>
    public sealed record InternallyRewritable(int Sequence) : DomainEvent(Sequence)
    {
        public string Note { get; internal set; } = string.Empty;
    }

    /// <summary>
    /// Declares two constructors, so "the first parameter" has no single answer. Its extra property
    /// is init-only, keeping this fixture a violation of the constructor rule and nothing else.
    /// </summary>
    public sealed record TwoConstructors : DomainEvent
    {
        public TwoConstructors(int sequence)
            : base(sequence) => Note = string.Empty;

        public TwoConstructors(int sequence, string note)
            : base(sequence) => Note = note;

        public string Note { get; init; }
    }
}
