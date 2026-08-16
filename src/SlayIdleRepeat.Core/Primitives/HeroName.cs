namespace SlayIdleRepeat.Core.Primitives;

/// <summary>A hero name that has passed the name rule — length, characters, and both word lists.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>Public getter, <c>internal</c> constructor, and that is the seam.</b> The aggregate's
/// rename takes this type and not a <c>string</c>, so a caller cannot hand it a name nobody checked:
/// the only way to obtain one is <c>Rules.Hero.HeroNameRule.Validate</c>, which is where the word
/// lists are consulted. A <c>Rename(string)</c> beside it would make the filter advisory, and an
/// advisory filter is the one that gets skipped by the third caller.
/// </para>
/// <para>
/// A sealed class rather than a <c>readonly record struct</c>, unlike <see cref="PlayerId"/> and
/// <see cref="GearInstanceId"/>: those validate inside a property initialiser, which
/// <c>default(T)</c> never runs, and each carries a paragraph about the hole that leaves. This type
/// cannot validate itself at all — the check needs the loaded word lists — so a struct here would
/// have a <c>default</c> whose text is <c>null</c> and which no constructor ever refused. A class has
/// no <c>default</c> but <c>null</c>, and every caller already handles that.
/// </para>
/// <para>
/// <see cref="Value"/> is the name <em>as the player typed it</em>. Nothing normalised, case-folded
/// or trimmed it: normalisation exists to decide whether the name is allowed, never to decide what
/// the name is, and a filter that quietly rewrote a name would show the player something they did not
/// choose.
/// </para>
/// </remarks>
public sealed class HeroName
{
    /// <summary>The only constructor. <c>internal</c>; the name has already been checked by the rule that calls it.</summary>
    internal HeroName(string value) => Value = value;

    /// <summary>The name, exactly as the player typed it.</summary>
    public string Value { get; }

    /// <summary>The name, so a log line reads it rather than the type's shape.</summary>
    public override string ToString() => Value;
}
