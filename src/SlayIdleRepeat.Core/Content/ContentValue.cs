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
/// machines and runtimes — see <c>ContentHashing</c>. The content stamp may be carried inside a
/// snapshot, so a non-deterministic one would move <c>stateHash</c>; it is a separate encoding
/// from `14` §16.6's <c>CanonicalStateWriter</c>, which never sees a <see cref="ContentValue"/>,
/// and the two never share bytes.
/// </para>
/// </remarks>
public sealed class ContentValue : IEquatable<ContentValue>
{
    private static readonly string[] NoNames = [];
    private static readonly ContentValue[] NoValues = [];

    private readonly string? _text;
    private readonly decimal _number;
    private readonly bool _boolean;
    private readonly string[] _names;
    private readonly ContentValue[] _memberValues;
    private readonly ContentValue[] _items;

    private ContentValue(
        ContentValueKind kind,
        string? text,
        decimal number,
        bool boolean,
        string[] names,
        ContentValue[] memberValues,
        ContentValue[] items)
    {
        Kind = kind;
        _text = text;
        _number = number;
        _boolean = boolean;
        _names = names;
        _memberValues = memberValues;
        _items = items;
        MemberNames = System.Array.AsReadOnly(names);
        Items = System.Array.AsReadOnly(items);
    }

    /// <summary>🔒 The <c>null</c> of the data files: "the design docs do not authorise a value here".</summary>
    public static ContentValue Unauthorised { get; } =
        new(ContentValueKind.Unauthorised, null, 0m, false, NoNames, NoValues, NoValues);

    /// <summary>The boolean <c>true</c>.</summary>
    public static ContentValue True { get; } =
        new(ContentValueKind.Boolean, null, 0m, true, NoNames, NoValues, NoValues);

    /// <summary>The boolean <c>false</c>.</summary>
    public static ContentValue False { get; } =
        new(ContentValueKind.Boolean, null, 0m, false, NoNames, NoValues, NoValues);

    /// <summary>An object with no members.</summary>
    public static ContentValue EmptyObject { get; } =
        new(ContentValueKind.Object, null, 0m, false, NoNames, NoValues, NoValues);

    /// <summary>An array with no items.</summary>
    public static ContentValue EmptyArray { get; } =
        new(ContentValueKind.Array, null, 0m, false, NoNames, NoValues, NoValues);

    /// <summary>The kind this value holds.</summary>
    public ContentValueKind Kind { get; }

    /// <summary>🔒 True when the design docs do not authorise a value here.</summary>
    public bool IsUnauthorised => Kind == ContentValueKind.Unauthorised;

    /// <summary>Member names, ordinal-sorted. Empty for anything but an object.</summary>
    /// <remarks>
    /// 🔒 A wrapper, not the backing array. An <c>IReadOnlyList&lt;T&gt;</c> that <em>is</em> a
    /// <c>string[]</c> can be cast back and written through, and this type's immutability is what
    /// the version stamp rests on. Built once in the constructor rather than per read, because
    /// these two are walked in tight loops by the validator and the hasher.
    /// </remarks>
    public IReadOnlyList<string> MemberNames { get; }

    /// <summary>Array items in document order. Empty for anything but an array.</summary>
    /// <remarks>🔒 A wrapper, for the same reason as <see cref="MemberNames"/>.</remarks>
    public IReadOnlyList<ContentValue> Items { get; }

    /// <summary>Creates a text value.</summary>
    public static ContentValue Text(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ContentValue(ContentValueKind.Text, value, 0m, false, NoNames, NoValues, NoValues);
    }

    /// <summary>Creates a number value. <see cref="decimal"/> so the load path never rounds.</summary>
    public static ContentValue Number(decimal value) =>
        new(ContentValueKind.Number, null, value, false, NoNames, NoValues, NoValues);

    /// <summary>Creates a boolean value.</summary>
    public static ContentValue Boolean(bool value) => value ? True : False;

    /// <summary>Creates an object. Members are sorted ordinally by name; a duplicate name throws.</summary>
    public static ContentValue Object(IEnumerable<KeyValuePair<string, ContentValue>> members)
    {
        ArgumentNullException.ThrowIfNull(members);

        var ordered = members.OrderBy(m => m.Key, StringComparer.Ordinal).ToArray();
        if (ordered.Length == 0)
        {
            return EmptyObject;
        }

        var names = new string[ordered.Length];
        var values = new ContentValue[ordered.Length];
        for (var i = 0; i < ordered.Length; i++)
        {
            if (i > 0 && string.Equals(ordered[i].Key, ordered[i - 1].Key, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Member '{ordered[i].Key}' was supplied twice. One of the two values would " +
                    "vanish silently, which is exactly what 14 §6's duplicate-id rule forbids.",
                    nameof(members));
            }

            names[i] = ordered[i].Key;
            values[i] = ordered[i].Value
                ?? throw new ArgumentException($"Member '{ordered[i].Key}' has a null ContentValue. " +
                    "An unauthorised value is ContentValue.Unauthorised, never a null reference.",
                    nameof(members));
        }

        return new ContentValue(ContentValueKind.Object, null, 0m, false, names, values, NoValues);
    }

    /// <summary>Creates an array, preserving item order.</summary>
    public static ContentValue Array(IEnumerable<ContentValue> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var materialised = items.ToArray();
        if (materialised.Length == 0)
        {
            return EmptyArray;
        }

        foreach (var item in materialised)
        {
            if (item is null)
            {
                throw new ArgumentException(
                    "An array item is a null reference. An unauthorised value is " +
                    "ContentValue.Unauthorised, never a null reference.",
                    nameof(items));
            }
        }

        return new ContentValue(ContentValueKind.Array, null, 0m, false, NoNames, NoValues, materialised);
    }

    /// <summary>Looks a member up by name. False for a non-object or an absent name.</summary>
    public bool TryGetMember(string name, out ContentValue? value)
    {
        var index = System.Array.BinarySearch(_names, name, StringComparer.Ordinal);
        if (index < 0)
        {
            value = null;
            return false;
        }

        value = _memberValues[index];
        return true;
    }

    /// <summary>Reads this value as text.</summary>
    /// <param name="reference">Names the value in a failure message; has no other effect.</param>
    public string AsText(string? reference = null)
    {
        Require(ContentValueKind.Text, nameof(ContentValueKind.Text), reference);
        return _text!;
    }

    /// <summary>Reads this value as an exact decimal.</summary>
    /// <param name="reference">Names the value in a failure message; has no other effect.</param>
    public decimal AsNumber(string? reference = null)
    {
        Require(ContentValueKind.Number, nameof(ContentValueKind.Number), reference);
        return _number;
    }

    /// <summary>Reads this value as a double.</summary>
    /// <param name="reference">Names the value in a failure message; has no other effect.</param>
    public double AsDouble(string? reference = null) => (double)AsNumber(reference);

    /// <summary>Reads this value as a 32-bit integer. Throws when it has a fractional part.</summary>
    /// <param name="reference">Names the value in a failure message; has no other effect.</param>
    public int AsInt32(string? reference = null)
    {
        var value = AsInt64(reference);
        return value is >= int.MinValue and <= int.MaxValue
            ? (int)value
            : throw new ContentTypeMismatchException(Describe(reference), Kind, "Int32");
    }

    /// <summary>Reads this value as a 64-bit integer. Throws when it has a fractional part.</summary>
    /// <param name="reference">Names the value in a failure message; has no other effect.</param>
    public long AsInt64(string? reference = null)
    {
        var value = AsNumber(reference);
        return decimal.Truncate(value) == value
            ? (long)value
            : throw new ContentTypeMismatchException(Describe(reference), Kind, "Int64");
    }

    /// <summary>Reads this value as a boolean.</summary>
    /// <param name="reference">Names the value in a failure message; has no other effect.</param>
    public bool AsBoolean(string? reference = null)
    {
        Require(ContentValueKind.Boolean, nameof(ContentValueKind.Boolean), reference);
        return _boolean;
    }

    /// <inheritdoc/>
    public bool Equals(ContentValue? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || other.Kind != Kind)
        {
            return false;
        }

        switch (Kind)
        {
            case ContentValueKind.Unauthorised:
                return true;
            case ContentValueKind.Boolean:
                return _boolean == other._boolean;
            case ContentValueKind.Text:
                return string.Equals(_text, other._text, StringComparison.Ordinal);
            case ContentValueKind.Number:
                // Value equality, not representation equality: 1.5 and 1.500 are one number.
                return _number == other._number;
            case ContentValueKind.Array:
                if (_items.Length != other._items.Length)
                {
                    return false;
                }

                for (var i = 0; i < _items.Length; i++)
                {
                    if (!_items[i].Equals(other._items[i]))
                    {
                        return false;
                    }
                }

                return true;
            case ContentValueKind.Object:
                if (_names.Length != other._names.Length)
                {
                    return false;
                }

                for (var i = 0; i < _names.Length; i++)
                {
                    if (!string.Equals(_names[i], other._names[i], StringComparison.Ordinal) ||
                        !_memberValues[i].Equals(other._memberValues[i]))
                    {
                        return false;
                    }
                }

                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ContentValue);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add((int)Kind);

        switch (Kind)
        {
            case ContentValueKind.Boolean:
                hash.Add(_boolean);
                break;
            case ContentValueKind.Text:
                hash.Add(_text, StringComparer.Ordinal);
                break;
            case ContentValueKind.Number:
                // decimal.GetHashCode is scale-independent, so 1.5 and 1.500 agree with Equals.
                hash.Add(_number);
                break;
            case ContentValueKind.Array:
                foreach (var item in _items)
                {
                    hash.Add(item);
                }

                break;
            case ContentValueKind.Object:
                for (var i = 0; i < _names.Length; i++)
                {
                    hash.Add(_names[i], StringComparer.Ordinal);
                    hash.Add(_memberValues[i]);
                }

                break;
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override string ToString() => Kind switch
    {
        ContentValueKind.Unauthorised => "null (unauthorised)",
        ContentValueKind.Boolean => _boolean ? "true" : "false",
        ContentValueKind.Text => $"\"{_text}\"",
        ContentValueKind.Number => _number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ContentValueKind.Array => $"[{_items.Length} item(s)]",
        _ => $"{{{string.Join(", ", _names)}}}",
    };

    private void Require(ContentValueKind expected, string expectedName, string? reference)
    {
        if (Kind == expected)
        {
            return;
        }

        throw IsUnauthorised
            ? new UnauthorisedTunableException(Describe(reference))
            : new ContentTypeMismatchException(Describe(reference), Kind, expectedName);
    }

    private string Describe(string? reference) => reference ?? ToString();
}
