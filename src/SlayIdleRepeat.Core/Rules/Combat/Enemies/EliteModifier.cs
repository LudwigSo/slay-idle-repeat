using System.Globalization;

namespace SlayIdleRepeat.Core.Rules.Combat.Enemies;

/// <summary>
/// 🔒 `05` §6.2 — the eight Elite Modifiers. Exactly one is drawn per Elite encounter.
/// </summary>
/// <remarks>
/// `05` §6.2 also states the presentation obligation, which is a rule and not a nicety:
/// <em>"Elite modifiers are shown on the pre-battle banner. The player must be able to read the
/// threat before it starts."</em> Every row therefore carries a localisation key.
/// </remarks>
internal enum EliteModifier
{
    /// <summary>`05` §6.2 — +50% ATK below 40% HP.</summary>
    ENRAGED = 1,

    /// <summary>`05` §6.2 — +80% DEF, −20% ASPD.</summary>
    ARMORED = 2,

    /// <summary>`05` §6.2 — 35% Lifesteal.</summary>
    VAMPIRIC = 3,

    /// <summary>`05` §6.2 — explodes on death for 15% of hero Max HP.</summary>
    VOLATILE = 4,

    /// <summary>`05` §6.2 — starts with a <c>WARD</c> equal to 30% Max HP.</summary>
    SHIELDED = 5,

    /// <summary>`05` §6.2 — +60% ASPD.</summary>
    SWIFT = 6,

    /// <summary>`05` §6.2 — applies a run-scoped curse on victory unless killed within 20 s.</summary>
    CURSED = 7,

    /// <summary>`05` §6.2 — 25% thorns.</summary>
    REFLECTIVE = 8,
}

/// <summary>
/// 🔒 One row of `05` §6.2's modifier list — the authored numbers, keyed by name.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Why the parameters are a named map rather than embedded `18` §1 effect JSON, recorded
/// because it looks like the wrong answer.</b> `18`'s headnote is emphatic that there is no
/// per-perk, per-talent or per-boss code, and the natural reading is that a modifier should ship as
/// authored effect JSON. The content pipeline cannot validate that today:
/// <c>JsonSchemaValidator</c> resolves same-document pointers only, so
/// <c>enemies.schema.json</c> cannot <c>$ref</c> <c>effect.schema.json</c>; and
/// <c>EffectSchemaTests.No_other_schema_restates_the_effect_vocabulary</c> fails any second schema
/// that enumerates the op set. Embedded effect JSON would therefore ship <b>unvalidated</b>, which
/// is the one failure class `14` §6's build-time check exists to remove. The numbers are authored;
/// the mapping to `18` §1 effects is one table, not a branch per modifier — and the constraint is
/// recorded as errata, because M2-13's boss mechanics meet it identically.
/// </para>
/// <para>
/// 🔒 <see cref="CurseId"/> is <c>null</c> for <see cref="EliteModifier.CURSED"/> and absent for
/// every other row. `05` §6.2 says <em>"applies a run-scoped curse"</em> and names none, and
/// <c>content/curses/</c> is empty — <c>16</c> R6: a fabricated id that looks precise is worse than
/// a missing one. <see cref="RequireCurseId"/> is the loud failure that hole earns.
/// </para>
/// </remarks>
/// <param name="Id">Which of `05` §6.2's eight modifiers this is.</param>
/// <param name="DisplayName">`05` §6.2 — the name on the pre-battle banner, as a localisation key.</param>
/// <param name="Parameters">
/// `05` §6.2's authored numbers for this modifier, keyed by name. A <c>*Mult</c> key is a
/// <c>STAT_MULT</c> multiplier: the value <em>is</em> the multiplier, so +80% DEF is <c>1.8</c>.
/// </param>
/// <param name="CurseId">The curse <see cref="EliteModifier.CURSED"/> applies, or <c>null</c>.</param>
internal sealed record EliteModifierRow(
    EliteModifier Id,
    string DisplayName,
    IReadOnlyDictionary<string, double> Parameters,
    string? CurseId)
{
    /// <summary>One authored parameter.</summary>
    /// <exception cref="KeyNotFoundException">
    /// `05` §6.2 states no parameter of that name for this modifier.
    /// </exception>
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
    /// <exception cref="InvalidOperationException">
    /// `05` §6.2 authorises no curse id, which is true of every row today.
    /// </exception>
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
