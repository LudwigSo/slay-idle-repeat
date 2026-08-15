using System.Reflection;
using SlayIdleRepeat.Core.Events;

namespace SlayIdleRepeat.Core.Tests.Events;

/// <summary>
/// The shape rules the <c>DomainEvent</c> hierarchy must follow, factored out of the tests that state
/// them so each one's teeth can be proven against a deliberately wrong shape. Every predicate takes
/// its subject as a parameter rather than reading the assembly, so self-tests can drive them with
/// test-only shapes that violate each rule.
/// </summary>
internal static class DomainEventShape
{
    /// <summary>
    /// Every concrete event in <c>SlayIdleRepeat.Core.Events</c>: public, non-nested, derived from
    /// <see cref="DomainEvent"/>. This is the subject set of every rule in <c>DomainEventTests</c>
    /// and must never be allowed to become empty quietly.
    /// </summary>
    internal static IReadOnlyList<Type> ConcreteEvents { get; } =
        typeof(DomainEvent).Assembly
            .GetTypes()
            .Where(t => t.IsPublic && !t.IsNested)
            .Where(t => t.Namespace is not null && IsUnderEvents(t.Namespace))
            .Where(t => t != typeof(DomainEvent) && typeof(DomainEvent).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Every public, non-nested type declared under <c>Core/Events/</c>, event or not. Kept separate
    /// from <see cref="ConcreteEvents"/> so a type that lands here without deriving from
    /// <see cref="DomainEvent"/> is still visible to the rules instead of silently falling outside them.
    /// </summary>
    internal static IReadOnlyList<Type> PublicTypesUnderEvents { get; } =
        typeof(DomainEvent).Assembly
            .GetTypes()
            .Where(t => t.IsPublic && !t.IsNested)
            .Where(t => t.Namespace is not null && IsUnderEvents(t.Namespace))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>The event's ordinal within one <c>CommandResult</c>'s list, and the first thing every constructor takes.</summary>
    internal const string SequenceParameter = "Sequence";

    /// <summary>
    /// The clock readings an event must never carry. Another rule bans the calls that produce these
    /// inside <c>Core</c>, but nothing bans an event field of one, which a caller outside Core could
    /// fill from a real clock and which would then travel downstream as if the domain had produced it.
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
    /// The event takes <c>int Sequence</c> as its first constructor parameter — empty means the rule
    /// holds. Position is the rule, not just presence: an event that took it second would still
    /// compile and serialise, and would quietly break any construction that positions it by index.
    /// </summary>
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
    /// The event carries no clock reading — empty means the rule holds. Flattened types are
    /// de-duplicated per property: <c>Flatten</c> reaches a <c>DateTimeOffset?</c> twice, and
    /// reporting one property as two violations would make a count-based assertion read wrong.
    /// </summary>
    internal static IReadOnlyList<string> ClockReadingViolations(Type candidate) =>
        candidate
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(p => Flatten(p.PropertyType).Distinct().Select(t => (Property: p, Type: t)))
            .Where(x => ClockReadings.Contains(x.Type))
            .Select(x =>
                $"{candidate.FullName}.{x.Property.Name} is typed {x.Type.Name}. An event must not stamp " +
                "itself with a time: time enters Core as GameContext.NowUtc and IClockPort must not appear " +
                "in Core at all (30 §3). A timestamp on the event is the same ambient clock, one indirection " +
                "further out, and it would travel into the 14 §7.1 economy log as if the domain had produced it.")
            .ToArray();

    /// <summary>
    /// The event is immutable — empty means the rule holds. An <c>init</c> accessor is construction,
    /// not mutation; a real <c>set</c> is not, at any accessibility, since the mutation this prevents
    /// would be written inside Core by a handler holding an event it already emitted.
    /// </summary>
    internal static IReadOnlyList<string> SettablePropertyViolations(Type candidate) =>
        candidate
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { } setter && !IsInitOnly(setter))
            .Select(p =>
                $"{candidate.FullName}.{p.Name} has a setter ({Accessibility(p.SetMethod!)}). A domain event is " +
                "an immutable record of something that already happened — it is persisted append-only " +
                "(14 §7.1), replayed as the client's animation script (14 §2.4) and counted by Feats (28 D). " +
                "An 'init' accessor is fine; a 'set' is not, at any accessibility.")
            .ToArray();

    /// <summary>
    /// Whether a type is a record, read off the <c>&lt;Clone&gt;$</c> method the compiler emits for
    /// every record and nothing else. Checked on <see cref="DomainEvent"/> rather than over
    /// <see cref="ConcreteEvents"/>, since C# forbids a class from deriving from a record.
    /// </summary>
    internal static bool IsRecord(Type candidate) =>
        candidate
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Any(m => m.Name.Equals("<Clone>$", StringComparison.Ordinal));

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

    /// <summary>How visible a setter is, so the message names the mutation surface it found.</summary>
    private static string Accessibility(MethodInfo setter) => setter switch
    {
        { IsPublic: true } => "public",
        { IsFamilyOrAssembly: true } => "protected internal",
        { IsFamily: true } => "protected",
        { IsAssembly: true } => "internal",
        { IsFamilyAndAssembly: true } => "private protected",
        _ => "private",
    };

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
