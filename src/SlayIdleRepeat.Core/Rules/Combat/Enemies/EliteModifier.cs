using System.Globalization;

namespace SlayIdleRepeat.Core.Rules.Combat.Enemies;

/// <summary>
/// The eight Elite Modifiers. Exactly one is drawn per Elite encounter.
/// </summary>
/// <remarks>
/// Elite modifiers are shown on the pre-battle banner so the player can read the threat before it
/// starts; every row carries a localisation key for that.
/// </remarks>
internal enum EliteModifier
{
    /// <summary>+50% ATK below 40% HP.</summary>
    ENRAGED = 1,

    /// <summary>+80% DEF, -20% ASPD.</summary>
    ARMORED = 2,

    /// <summary>35% Lifesteal.</summary>
    VAMPIRIC = 3,

    /// <summary>Explodes on death for 15% of hero Max HP.</summary>
    VOLATILE = 4,

    /// <summary>Starts with a <c>WARD</c> equal to 30% Max HP.</summary>
    SHIELDED = 5,

    /// <summary>+60% ASPD.</summary>
    SWIFT = 6,

    /// <summary>Applies a run-scoped curse on victory unless killed within 20 s.</summary>
    CURSED = 7,

    /// <summary>25% thorns.</summary>
    REFLECTIVE = 8,
}

/// <summary>
/// One row of the modifier list — the authored numbers, keyed by name.
/// </summary>
/// <remarks>
/// <para>
/// Parameters are a named map rather than embedded effect JSON: the content pipeline cannot validate
/// cross-schema effect references today, so embedded effect JSON would ship unvalidated. The
/// mapping to effects is one table, not a branch per modifier.
/// </para>
/// <para>
/// <see cref="CurseId"/> is <c>null</c> for <see cref="EliteModifier.CURSED"/> and absent for every
/// other row — the curse catalogue does not exist yet, and a fabricated id that looks precise is
/// worse than a missing one. <see cref="RequireCurseId"/> is the loud failure that hole earns.
/// </para>
/// </remarks>
/// <param name="Id">Which of the eight modifiers this is.</param>
/// <param name="DisplayName">The name on the pre-battle banner, as a localisation key.</param>
/// <param name="Parameters">
/// The authored numbers for this modifier, keyed by name. A <c>*Mult</c> key is a <c>STAT_MULT</c>
/// multiplier: the value is the multiplier, so +80% DEF is <c>1.8</c>.
/// </param>
/// <param name="CurseId">The curse <see cref="EliteModifier.CURSED"/> applies, or <c>null</c>.</param>
internal sealed record EliteModifierRow(
    EliteModifier Id,
    string DisplayName,
    IReadOnlyDictionary<string, double> Parameters,
    string? CurseId)
{
    /// <summary>One authored parameter.</summary>
    /// <exception cref="KeyNotFoundException">No parameter of that name is authored for this modifier.</exception>
    internal double Parameter(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return Parameters.TryGetValue(name, out var value)
            ? value
            : throw new KeyNotFoundException(
                $"05 §6.2 states no '{name}' for {Id}. It states " +
                $"{string.Join(", ", Parameters.Keys.OrderBy(k => k, StringComparer.Ordinal))}. " +
                "Reading a parameter a modifier does not have would silently apply zero, which for a " +
                "multiplier deletes the stat it scales.");
    }

    /// <summary>The curse this modifier applies, or a throw naming the hole.</summary>
    /// <exception cref="InvalidOperationException">No curse id is authorised, true of every row today.</exception>
    internal string RequireCurseId() =>
        CurseId ?? throw new InvalidOperationException(
            $"05 §6.2 says {Id} 'applies a run-scoped curse' and names none, and content/curses/ is " +
            "empty, so content/enemies/enemies.json carries null. 16 R6: never fill a hole with a " +
            "plausible value. The milestone that authors the curse catalogue rules on which curse " +
            "this is.");

    /// <inheritdoc />
    public override string ToString() =>
        $"{Id} ({string.Join(", ", Parameters
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={p.Value.ToString("R", CultureInfo.InvariantCulture)}"))})";
}
