using System.Collections.ObjectModel;

namespace SlayIdleRepeat.Core.Tests.Model.Snapshots;

/// <summary>The shapes <c>CanonicalStateWriter</c> must <b>refuse</b>, one record per shape.</summary>
/// <remarks>
/// The writer dispatches over a closed allowlist with no <c>IEnumerable</c> fallback. These
/// records prove the allowlist is still closed: if one ever starts hashing, some fallback branch
/// grew back.
/// </remarks>
internal static class UnsupportedSnapshots
{
    /// <summary>A set has no order at all.</summary>
    internal sealed record WithHashSet(HashSet<string> Tags);

    /// <summary>An <c>ISet</c>-typed field, the interface form of the same defect.</summary>
    internal sealed record WithSetInterface(IReadOnlySet<string> Tags);

    /// <summary>A bare sequence: enumerable once, in whatever order the source felt like.</summary>
    internal sealed record WithEnumerable(IEnumerable<int> Numbers);

    /// <summary>
    /// A <c>Dictionary</c> reached through <c>ICollection</c> — insertion order, dressed up.
    /// </summary>
    internal sealed record WithKeyValueCollection(ICollection<KeyValuePair<string, int>> Entries);

    /// <summary>A dictionary keyed by something with no ordinal or numeric order.</summary>
    internal sealed record WithUnorderableKey(IReadOnlyDictionary<Guid, int> ById);

    /// <summary><c>float</c> is not <c>double</c>; only the 8-byte pattern of a double is pinned.</summary>
    internal sealed record WithSingle(float Value);

    /// <summary><c>decimal</c> has no IEEE-754 bit pattern and no rounding rule here.</summary>
    internal sealed record WithDecimal(decimal Value);

    /// <summary>A <c>char</c> is neither the string rule nor the integer rule.</summary>
    internal sealed record WithChar(char Value);

    /// <summary><c>Guid</c> has no pinned byte order — its own layout is famously mixed-endian.</summary>
    internal sealed record WithGuid(Guid Value);

    /// <summary>A <c>TimeSpan</c> is not a timestamp: instants are pinned, not durations.</summary>
    internal sealed record WithTimeSpan(TimeSpan Value);

    /// <summary><c>object</c> defers the encoding decision to runtime, which is not an encoding.</summary>
    internal sealed record WithObject(object Value);

    /// <summary>A plain class has no reflection-guaranteed declaration order.</summary>
    internal sealed class NotARecord
    {
        public int First { get; init; }

        public int Second { get; init; }
    }

    /// <summary>A record whose field is a plain class, so the refusal is reached by descent.</summary>
    internal sealed record WithPlainClass(NotARecord Nested);

    /// <summary>A record that owns two public constructors: "the" primary one is ambiguous.</summary>
    internal sealed record AmbiguousConstructors
    {
        public AmbiguousConstructors(int Value) => this.Value = Value;

        /// <summary>A second public constructor, which is what makes the order ambiguous.</summary>
        public AmbiguousConstructors(int value, int ignored) => Value = value + ignored;

        public int Value { get; init; }
    }

    /// <summary>A record with no fields at all — nothing to serialise is not a state.</summary>
    internal sealed record Empty;

    /// <summary>
    /// A positional record carrying a public property <b>outside</b> its primary constructor — the
    /// one shape whose fields the writer could silently drop.
    /// </summary>
    /// <remarks>
    /// The field list is the constructor's parameter list, so <c>RevivesUsed</c> contributes zero
    /// bytes: <c>{ RevivesUsed = 0 }</c> and <c>{ RevivesUsed = 99 }</c> would share a
    /// <c>stateHash</c> while record equality correctly reported them different.
    /// </remarks>
    internal sealed record WithPropertyOutsideTheConstructor(int SchemaVersion, int ChapterId)
    {
        public int RevivesUsed { get; init; }
    }

    /// <summary>
    /// A positional record carrying a public <b>field</b> outside its primary constructor — the same
    /// defect as <see cref="WithPropertyOutsideTheConstructor"/> through a door the property check
    /// cannot watch.
    /// </summary>
    internal sealed record WithPublicField(int SchemaVersion, int ChapterId)
    {
#pragma warning disable CA1051, SA1401 // A public field is the defect under test.
        public int RevivesUsed;
#pragma warning restore CA1051, SA1401
    }

    /// <summary>
    /// A positional record carrying public <b>fields</b> outside its primary constructor: a second,
    /// independent shape from <see cref="WithPublicField"/> — more than one field.
    /// </summary>
    /// <remarks>
    /// A field is neither a primary-constructor parameter nor a property, so it falls through both
    /// halves of the shape check. Before this was closed, the record hashed <b>only <c>Tick</c></b>.
    /// </remarks>
    internal sealed record CombatEventAsDocumented(int Tick)
    {
        public byte SourceId;

        public double Value;
    }

    /// <summary>The same defect with a <c>readonly</c> field, which is the shape a value type takes.</summary>
    /// <remarks>
    /// <c>readonly</c> makes no difference to the encoding: an invisible field is invisible whether
    /// or not it can change.
    /// </remarks>
    internal sealed record WithReadonlyPublicField(int Tick)
    {
        public readonly byte SourceId;

        /// <summary>Builds an instance carrying a field value, since a field cannot be an <c>init</c>.</summary>
        internal static WithReadonlyPublicField With(int tick, byte sourceId) => new(tick, sourceId);

        private WithReadonlyPublicField(int tick, byte sourceId)
            : this(tick) => SourceId = sourceId;
    }

    /// <summary>A record that contains itself, so a naive descent never terminates.</summary>
    internal sealed record SelfReferencing(int Depth, SelfReferencing? Next);

    /// <summary>A non-sealed record, so a subclass can smuggle fields past the pinned field list.</summary>
    internal record OpenBase(int First);

    /// <summary>The subclass that carries the smuggled field.</summary>
    internal sealed record OpenDerived(int First, int Second) : OpenBase(First);

    /// <summary>A record holding an <see cref="OpenBase"/> slot, fillable with an <see cref="OpenDerived"/>.</summary>
    internal sealed record WithPolymorphicSlot(OpenBase Value);

    /// <summary>A <c>DateTime</c> with no <see cref="DateTimeKind.Utc"/> — no canonical instant.</summary>
    internal sealed record WithLocalDateTime(DateTime Value);

    /// <summary>A dictionary presented read-only but still in insertion order underneath.</summary>
    internal static IReadOnlyDictionary<Guid, int> GuidMap { get; } =
        new ReadOnlyDictionary<Guid, int>(new Dictionary<Guid, int> { [Guid.Empty] = 1 });
}
