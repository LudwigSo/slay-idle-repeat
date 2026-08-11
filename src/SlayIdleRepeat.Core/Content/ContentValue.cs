namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// One immutable node of loaded content: object, array, text, number, boolean, or
/// <see cref="ContentValueKind.Unauthorised"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is `Core`'s own value tree, deliberately free of any serialisation type. `14` §6:
/// <em>"Loading JSON is I/O and belongs in an adapter; reading content is a rule."</em>
/// `Core` references nothing (`23` §2.1), so there is no <c>System.Text.Json</c> here and no
/// attribute, converter or reader leaks in from the load path.
/// </para>
/// <para>
/// 🔒 <b>Ordering is pinned.</b> Object members are stored ordinal-sorted by name and arrays
/// keep their document order. That is what makes the version stamp reproducible across
/// machines and runtimes — see <c>ContentHashing</c> — and it is load-bearing for `14` §16.6.
/// </para>
/// </remarks>
public sealed class ContentValue : IEquatable<ContentValue>
{
    /// <summary>🔒 The <c>null</c> of the data files: "the design docs do not authorise a value here".</summary>
    public static ContentValue Unauthorised => throw new NotImplementedException();

    /// <summary>The boolean <c>true</c>.</summary>
    public static ContentValue True => throw new NotImplementedException();

    /// <summary>The boolean <c>false</c>.</summary>
    public static ContentValue False => throw new NotImplementedException();

    /// <summary>An object with no members.</summary>
    public static ContentValue EmptyObject => throw new NotImplementedException();

    /// <summary>An array with no items.</summary>
    public static ContentValue EmptyArray => throw new NotImplementedException();

    /// <summary>The kind this value holds.</summary>
    public ContentValueKind Kind => throw new NotImplementedException();

    /// <summary>🔒 True when the design docs do not authorise a value here.</summary>
    public bool IsUnauthorised => Kind == ContentValueKind.Unauthorised;

    /// <summary>Member names, ordinal-sorted. Empty for anything but an object.</summary>
    public IReadOnlyList<string> MemberNames => throw new NotImplementedException();

    /// <summary>Array items in document order. Empty for anything but an array.</summary>
    public IReadOnlyList<ContentValue> Items => throw new NotImplementedException();

    /// <summary>Creates a text value.</summary>
    public static ContentValue Text(string value) => throw new NotImplementedException();

    /// <summary>Creates a number value. <see cref="decimal"/> so the load path never rounds.</summary>
    public static ContentValue Number(decimal value) => throw new NotImplementedException();

    /// <summary>Creates a boolean value.</summary>
    public static ContentValue Boolean(bool value) => throw new NotImplementedException();

    /// <summary>Creates an object. Members are sorted ordinally by name; a duplicate name throws.</summary>
    public static ContentValue Object(IEnumerable<KeyValuePair<string, ContentValue>> members) =>
        throw new NotImplementedException();

    /// <summary>Creates an array, preserving item order.</summary>
    public static ContentValue Array(IEnumerable<ContentValue> items) => throw new NotImplementedException();

    /// <summary>Looks a member up by name. False for a non-object or an absent name.</summary>
    public bool TryGetMember(string name, out ContentValue? value) => throw new NotImplementedException();

    /// <summary>Reads this value as text.</summary>
    public string AsText() => throw new NotImplementedException();

    /// <summary>Reads this value as an exact decimal.</summary>
    public decimal AsNumber() => throw new NotImplementedException();

    /// <summary>Reads this value as a double.</summary>
    public double AsDouble() => throw new NotImplementedException();

    /// <summary>Reads this value as a 32-bit integer. Throws when it has a fractional part.</summary>
    public int AsInt32() => throw new NotImplementedException();

    /// <summary>Reads this value as a 64-bit integer. Throws when it has a fractional part.</summary>
    public long AsInt64() => throw new NotImplementedException();

    /// <summary>Reads this value as a boolean.</summary>
    public bool AsBoolean() => throw new NotImplementedException();

    /// <inheritdoc/>
    public bool Equals(ContentValue? other) => throw new NotImplementedException();

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ContentValue);

    /// <inheritdoc/>
    public override int GetHashCode() => throw new NotImplementedException();

    /// <inheritdoc/>
    public override string ToString() => throw new NotImplementedException();
}
