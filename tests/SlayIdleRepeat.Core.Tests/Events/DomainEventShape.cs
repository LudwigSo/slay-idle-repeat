using System.Reflection;
using SlayIdleRepeat.Core.Events;

namespace SlayIdleRepeat.Core.Tests.Events;

/// <summary>
/// The shape rules `30` §7 puts on the <c>DomainEvent</c> hierarchy, factored out of the tests
/// that state them so each one's <b>teeth</b> can be proven against a deliberately wrong shape.
/// </summary>
/// <remarks>
/// <para>
/// The hierarchy is two types today — <see cref="DomainEvent"/> and
/// <see cref="CurrencyChanged"/> — because four of `30` §7's six events name payload types no
/// milestone has authored yet (see <c>GapRegister</c> in the architecture suite). Every rule
/// below is therefore stated over a set that is small now and grows through M3, M4, M12 and M14,
/// and each one is written so that the <i>next</i> event is governed without a test edit.
/// </para>
/// <para>
/// 🔒 A rule over a two-element set proves very little about itself, which is why every predicate
/// here takes its subject as a parameter rather than reading the assembly: the self-tests drive
/// them with test-only shapes that violate each rule, so "this rule can fail" is demonstrated
/// rather than asserted. Same construction as
/// <c>SlayIdleRepeat.Core.Tests.Model.Snapshots.SnapshotFieldOrderPin.Violations</c>.
/// </para>
/// </remarks>
internal static class DomainEventShape
{
    /// <summary>
    /// Every concrete event in <c>SlayIdleRepeat.Core.Events</c>: public, non-nested, derived
    /// from <see cref="DomainEvent"/>.
    /// </summary>
    /// <remarks>
    /// 🔒 This is the subject set of every rule in <c>DomainEventTests</c>, and it must never be
    /// allowed to become empty quietly (steering S3). Renaming the namespace, or M1-06 making the
    /// hierarchy internal, would take every rule below permanently green over nothing.
    /// <c>DomainEventTests.The_hierarchy_this_suite_governs_is_the_one_that_exists</c> is the
    /// floor under it.
    /// </remarks>
    internal static IReadOnlyList<Type> ConcreteEvents { get; } =
        typeof(DomainEvent).Assembly
            .GetTypes()
            .Where(t => t.IsPublic && !t.IsNested)
            .Where(t => t.Namespace is not null && IsUnderEvents(t.Namespace))
            .Where(t => t != typeof(DomainEvent) && typeof(DomainEvent).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Every public, non-nested type declared under <c>Core/Events/</c>, event or not.</summary>
    /// <remarks>
    /// Kept separate from <see cref="ConcreteEvents"/> so a type that lands in the namespace
    /// <i>without</i> deriving from <see cref="DomainEvent"/> — a helper, an enum, a payload
    /// record — is visible to
    /// <c>DomainEventTests.Core_Events_holds_the_event_hierarchy_and_nothing_else</c> instead of
    /// silently falling outside every rule here.
    /// </remarks>
    internal static IReadOnlyList<Type> PublicTypesUnderEvents { get; } =
        typeof(DomainEvent).Assembly
            .GetTypes()
            .Where(t => t.IsPublic && !t.IsNested)
            .Where(t => t.Namespace is not null && IsUnderEvents(t.Namespace))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// 🔒 <c>30</c> §7 — the event's ordinal within one <c>CommandResult</c>'s list, and the first
    /// thing every constructor takes. Named once here so the rule and its message cannot drift.
    /// </summary>
    internal const string SequenceParameter = "Sequence";

    /// <summary>
    /// The clock readings an event must never carry. `30` §9's
    /// <c>Domain_has_no_ambient_time_or_randomness</c> bans the <i>calls</i> that produce these
    /// inside <c>Core</c>; nothing banned an event <b>field</b> of one, which a caller outside
    /// <c>Core</c> could fill from a real clock and which would then travel into the economy log
    /// and every <c>stateHash</c> downstream of it.
    /// </summary>
    private static readonly IReadOnlyList<Type> ClockReadings = new[]
    {
        typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan), typeof(DateOnly), typeof(TimeOnly),
    };

    /// <summary>
    /// The one constructor an event declares, or <c>null</c> when it declares none or several.
    /// The compiler-generated copy constructor of a record is not one of the author's.
    /// </summary>
    internal static ConstructorInfo? SoleConstructor(Type candidate)
    {
        var declared = candidate
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(c => !IsCopyConstructor(c, candidate))
            .ToArray();

        return declared.Length == 1 ? declared[0] : null;
    }

    /// <summary>
    /// 🔒 `30` §7 — the event takes <c>int Sequence</c> as its <b>first</b> constructor parameter.
    /// Empty means the rule holds.
    /// </summary>
    /// <remarks>
    /// The position is the rule, not just the presence. <c>GameRules.Apply</c> (M1-06) is the sole
    /// assigner, and every one of `30` §7's six events is written <c>(int Sequence, …)</c>; an
    /// event that took it second would still compile, still serialise, and still read correctly to
    /// a human — and would quietly break any construction that positions the ordinal by index.
    /// </remarks>
    internal static IReadOnlyList<string> SequenceParameterViolations(Type candidate)
    {
        var constructor = SoleConstructor(candidate);

        if (constructor is null)
        {
            return new[]
            {
                $"{candidate.FullName} does not declare exactly one constructor. An event is a positional " +
                "record with one constructor (30 §7); several make 'the first parameter' undefined.",
            };
        }

        var parameters = constructor.GetParameters();

        if (parameters.Length == 0)
        {
            return new[] { $"{candidate.FullName} takes no constructor parameters, so it carries no {SequenceParameter}. {Consequence}" };
        }

        var first = parameters[0];

        if (first.Name is SequenceParameter && first.ParameterType == typeof(int))
        {
            return Array.Empty<string>();
        }

        return new[]
        {
            $"{candidate.FullName} takes '{first.ParameterType.Name} {first.Name}' as its first constructor " +
            $"parameter, not 'Int32 {SequenceParameter}'. {Consequence}",
        };
    }

    /// <summary>
    /// 🔒 `30` §7 / `30` §3 — the event carries no clock reading. Empty means the rule holds.
    /// </summary>
    internal static IReadOnlyList<string> ClockReadingViolations(Type candidate) =>
        candidate
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(p => Flatten(p.PropertyType).Select(t => (Property: p, Type: t)))
            .Where(x => ClockReadings.Contains(x.Type))
            .Select(x =>
                $"{candidate.FullName}.{x.Property.Name} is typed {x.Type.Name}. An event must not stamp " +
                "itself with a time: time enters Core as GameContext.NowUtc and IClockPort must not appear " +
                "in Core at all (30 §3). A timestamp on the event is the same ambient clock, one indirection " +
                "further out, and it would travel into the 14 §7.1 economy log as if the domain had produced it.")
            .ToArray();

    /// <summary>
    /// 🔒 `14` §7.1 / `14` §2.4 — the event is immutable. Empty means the rule holds.
    /// </summary>
    /// <remarks>
    /// An <c>init</c> accessor is construction, not mutation, and is permitted — it is how a
    /// positional record is written. A real <c>set</c> is not: the same list is an append-only
    /// Postgres log, an analytics payload, a Feats counter input and the client's animation script,
    /// and a consumer that can rewrite it changes what the other three see.
    /// </remarks>
    internal static IReadOnlyList<string> SettablePropertyViolations(Type candidate) =>
        candidate
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true } setter && !IsInitOnly(setter))
            .Select(p =>
                $"{candidate.FullName}.{p.Name} has a public setter. A domain event is an immutable record of " +
                "something that already happened — it is persisted append-only (14 §7.1), replayed as the " +
                "client's animation script (14 §2.4) and counted by Feats (28 D). An 'init' accessor is fine; " +
                "a 'set' is not.")
            .ToArray();

    /// <summary>What a shape violation means, said once.</summary>
    internal const string Consequence =
        "30 §7 writes every event as (int Sequence, …). Sequence is the ORDINAL OF THE EVENT WITHIN ONE " +
        "CommandResult's event list — it orders the animation script (14 §2.4) and the economy-log rows " +
        "produced by a single Apply call. It is assigned by GameRules.Apply (M1-06), never by the " +
        "constructor and never by a caller. It is NOT 14 §16.3's wire 'sequence', which is the " +
        "per-run/per-player COMMAND counter on the envelope.";

    /// <summary>A record's compiler-generated copy constructor: one parameter, of the record's own type.</summary>
    private static bool IsCopyConstructor(ConstructorInfo constructor, Type declaring)
    {
        var parameters = constructor.GetParameters();
        return parameters.Length == 1 && parameters[0].ParameterType == declaring;
    }

    /// <summary>An <c>init</c> accessor is a construction-time setter, not a mutation surface.</summary>
    private static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter
            .GetRequiredCustomModifiers()
            .Any(m => m == typeof(System.Runtime.CompilerServices.IsExternalInit));

    /// <summary>A type and every type reachable through its nullability, generic arguments and element type.</summary>
    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            yield return underlying;
        }

        if (type.HasElementType && type.GetElementType() is { } element)
        {
            foreach (var nested in Flatten(element))
            {
                yield return nested;
            }
        }

        foreach (var argument in type.IsGenericType ? type.GetGenericArguments() : Array.Empty<Type>())
        {
            foreach (var nested in Flatten(argument))
            {
                yield return nested;
            }
        }
    }

    /// <summary>
    /// Whether a namespace is <c>Core/Events/</c> or a folder beneath it. Read off
    /// <see cref="DomainEvent"/> rather than written out, so it cannot drift from the directory.
    /// </summary>
    private static bool IsUnderEvents(string candidate)
    {
        var events = typeof(DomainEvent).Namespace!;

        return candidate.Equals(events, StringComparison.Ordinal) ||
               candidate.StartsWith(events + ".", StringComparison.Ordinal);
    }
}
