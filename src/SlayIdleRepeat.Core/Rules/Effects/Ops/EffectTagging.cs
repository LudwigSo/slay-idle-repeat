using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>The two tag vocabularies, read apart: an effect's own author labels (in which <c>drawback</c> is reserved) and a status's tag group.</summary>
/// <remarks>
/// <para>
/// A type rather than two string comparisons: both vocabularies are lower-snake strings, and left as
/// bare strings a <c>REMOVE_STATUS</c> authored with <c>"drawback"</c> could either clear the wrong
/// marker or let a status label slip through a ward-bypass check, silently absorbing a perk drawback
/// into a shield. <see cref="AuthorTag"/> and <see cref="StatusTag"/> cannot be passed to each
/// other's methods.
/// </para>
/// <para>This class reads; it decides nothing about wards — the op passes <see cref="IsSelfInflictedCost"/> along so the tag is read in exactly one place.</para>
/// </remarks>
internal static class EffectTagging
{
    /// <summary>The effect's author labels, typed.</summary>
    /// <remarks>Duplicates are not rejected here — the schema's <c>uniqueItems</c> already states that rule.</remarks>
    internal static IEnumerable<AuthorTag> AuthorTags(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        foreach (var tag in effect.Tags)
        {
            yield return new AuthorTag(tag);
        }
    }

    /// <summary>Whether the effect carries the reserved <c>drawback</c> label.</summary>
    /// <remarks>
    /// An indexed loop rather than <c>AuthorTags(effect).Any(…)</c>: this runs per actor per tick
    /// inside a tight combat budget, and the iterator plus LINQ enumerator are two allocations for a
    /// scan of a list that's almost always one element long.
    /// </remarks>
    internal static bool IsDrawback(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        var tags = effect.Tags;
        for (var i = 0; i < tags.Count; i++)
        {
            if (string.Equals(tags[i], AuthorTag.Drawback.Value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether this effect's damage is a self-inflicted cost that must reach HP through the ward pool untouched.</summary>
    /// <remarks>
    /// Two conditions, both required: the tag, and that the damage is self-inflicted. A
    /// <c>drawback</c>-tagged effect pointed at an enemy is an author label on an offensive clause,
    /// not a cost, and letting it skip the enemy's wards would hand every cursed perk armour
    /// penetration nobody wrote.
    /// </remarks>
    internal static bool IsSelfInflictedCost(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        return IsDrawback(effect) && EffectDefaults.TargetOf(effect) == EffectTarget.SELF;
    }

    /// <summary>The status tag group a <see cref="EffectOp.REMOVE_STATUS"/> clears, or <c>null</c> where it names a single <c>statusId</c> instead.</summary>
    internal static StatusTag? StatusTagOf(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        return effect.StatusTag;
    }
}
