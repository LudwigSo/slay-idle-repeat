using System.Collections.ObjectModel;

namespace SlayIdleRepeat.Core.Tests.Model.Snapshots;

/// <summary>
/// The shapes <c>CanonicalStateWriter</c> must <b>refuse</b>, one record per shape.
/// </summary>
/// <remarks>
/// 🔒 `14` §16.6: *"No unordered container is ever hashed as-is."* The writer enforces that
/// by dispatching over a <b>closed allowlist</b> with no <c>IEnumerable</c> fallback, so a
/// container with no defined order — and a scalar with no pinned encoding — has nowhere to
/// land except a hard failure. These records are what proves the allowlist is still closed:
/// if one of them ever starts hashing, some fallback branch grew back.
/// </remarks>
internal static class UnsupportedSnapshots
{
    /// <summary>A set has no order at all — the exact container §16.6 forbids.</summary>
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

    /// <summary><c>float</c> is not <c>double</c>; §16.6 pins the 8-byte pattern of a double.</summary>
    internal sealed record WithSingle(float Value);

    /// <summary><c>decimal</c> has no IEEE-754 bit pattern and no rounding rule here.</summary>
    internal sealed record WithDecimal(decimal Value);

    /// <summary>A <c>char</c> is neither the string rule nor the integer rule.</summary>
    internal sealed record WithChar(char Value);

    /// <summary><c>Guid</c> has no pinned byte order in §16.6 — its own layout is famously mixed-endian.</summary>
    internal sealed record WithGuid(Guid Value);

    /// <summary>A <c>TimeSpan</c> is not a timestamp; §16.6 pins instants, not durations.</summary>
    internal sealed record WithTimeSpan(TimeSpan Value);

    /// <summary><c>object</c> defers the encoding decision to runtime, which is not an encoding.</summary>
    internal sealed record WithObject(object Value);

    /// <summary>A plain class has no reflection-guaranteed declaration order.</summary>
    internal sealed class NotARecord
    {
        /// <summary>A value.</summary>
        public int First { get; init; }

        /// <summary>Another value.</summary>
        public int Second { get; init; }
    }

    /// <summary>A record whose field is a plain class, so the refusal is reached by descent.</summary>
    internal sealed record WithPlainClass(NotARecord Nested);

    /// <summary>A record that owns two public constructors: "the" primary one is ambiguous.</summary>
    internal sealed record AmbiguousConstructors
    {
        /// <summary>The positional constructor.</summary>
        public AmbiguousConstructors(int Value) => this.Value = Value;

        /// <summary>A second public constructor, which is what makes the order ambiguous.</summary>
        public AmbiguousConstructors(int value, int ignored) => Value = value + ignored;

        /// <summary>A value.</summary>
        public int Value { get; init; }
    }

    /// <summary>A record with no fields at all — nothing to serialise is not a state.</summary>
    internal sealed record Empty;

    /// <summary>
    /// 🔒 A positional record carrying a public property <b>outside</b> its primary constructor —
    /// the one shape whose fields the writer could silently drop.
    /// </summary>
    /// <remarks>
    /// The field list is the constructor's parameter list, so <c>RevivesUsed</c> would contribute
    /// zero bytes: <c>{ RevivesUsed = 0 }</c> and <c>{ RevivesUsed = 99 }</c> would share a
    /// <c>stateHash</c> while record equality correctly reported them different, and
    /// <c>CanonicalFieldOrder</c> — asking the same question — would never pin the field at all.
    /// <c>record</c> + <c>{ get; init; }</c> is exactly what an optional member of
    /// <c>PlayerSnapshot</c>/<c>RunSnapshot</c> reaches for, so the refusal is the guard rail that
    /// has to exist before those records do.
    /// </remarks>
    internal sealed record WithPropertyOutsideTheConstructor(int SchemaVersion, int ChapterId)
    {
        /// <summary>The field outside the primary constructor.</summary>
        public int RevivesUsed { get; init; }
    }

    /// <summary>
    /// 🔴 A positional record carrying a public <b>field</b> outside its primary constructor — the
    /// same defect as <see cref="WithPropertyOutsideTheConstructor"/> through a door M0-07 left
    /// open.
    /// </summary>
    /// <remarks>
    /// <c>CanonicalProperties</c> compared <c>GetProperties()</c> against the parameter list and
    /// never looked at <c>GetFields()</c>, so this shape — <c>public int RevivesUsed;</c>, one
    /// keyword-pair away from the record above and the first thing a hand-written DTO reaches for —
    /// was accepted as a canonical record and its field contributed <b>zero bytes</b>.
    /// <c>{ RevivesUsed = 0 }</c> and <c>{ RevivesUsed = 99 }</c> shared a <c>stateHash</c> while
    /// record equality correctly reported them different, and the <c>SchemaVersion</c> field-order
    /// pin never saw the field at all. Latent since M0-07 and harmless only while no snapshot
    /// record existed; M1-04 authors the first one, so M1-04 closes it.
    /// </remarks>
    internal sealed record WithPublicField(int SchemaVersion, int ChapterId)
    {
        /// <summary>The public field the writer must refuse rather than silently drop.</summary>
#pragma warning disable CA1051, SA1401 // A public field is the defect under test.
        public int RevivesUsed;
#pragma warning restore CA1051, SA1401
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
