namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// One row of the source list step 1 collects effects from.
/// </summary>
/// <param name="Kind">The source.</param>
/// <param name="Phrase">
/// The exact word(s) the design doc uses for it — rows are joined back into that sentence and
/// compared against it, so a drifted or reordered row fails against the spec.
/// </param>
/// <param name="OwningMilestone">
/// The milestone that lands the data model this source reads. <c>null</c> where the source can
/// already yield effects today.
/// </param>
/// <param name="PendingSubject">
/// The <c>Core</c> type whose arrival means this source can be wired. <c>null</c> for a source that
/// is not pending.
/// </param>
internal sealed record EffectSourceRow(
    EffectSourceKind Kind, string Phrase, string? OwningMilestone, string? PendingSubject)
{
    /// <summary>True where no data model exists for this source yet.</summary>
    internal bool IsPending => OwningMilestone is not null;
}

/// <summary>The ten sources step 1 collects effects from, as a declared, floored inventory.</summary>
/// <remarks>
/// <para>
/// Three of the ten are wired — gear, affixes and set bonuses, which the hero build reads off the
/// equipped loadout. The other seven have no data model yet, and none is stubbed: a plausible shape
/// here would be seven invented types that later milestones would each have to find and delete. What
/// fills one of those slots today is <c>ListEffectSource</c>, the synthetic-build implementation the
/// balance harness and tests use, which is why the pipeline was testable end to end before any real
/// source existed.
/// </para>
/// <para>
/// A source hands over <see cref="IEffectSource"/> — a bare list of
/// <see cref="Content.Effects.EffectDefinition"/> — the narrowest shape every one of the ten can
/// satisfy, since that's all the design doc asks them to contribute.
/// </para>
/// <para>
/// The pending subject names are inferred from each milestone's tracker description, not confirmed.
/// If a milestone's real name differs, rename the register entry rather than delete it — the thing
/// being tracked is "this source now has something to collect from", not the string.
/// </para>
/// </remarks>
internal static class EffectSourceCatalogue
{
    /// <summary>The source list, verbatim — the string <see cref="Rows"/> is checked against.</summary>
    internal const string Step1SourceList =
        "gear → affixes → set bonuses → talents → pet auras → mount → run buffs → shrine buffs → " +
        "curses → perks (in draft order)";

    /// <summary>The separator between sources in that phrase.</summary>
    internal const string PhraseSeparator = " → ";

    /// <summary>The ten sources, in collection order.</summary>
    /// <remarks>
    /// Built with an explicit array rather than a collection expression: targeting
    /// <see cref="IReadOnlyList{T}"/> with a collection-expression literal makes Roslyn synthesise an
    /// undocumented type into the global namespace, which the namespace-boundary test flags.
    /// </remarks>
    internal static IReadOnlyList<EffectSourceRow> Rows { get; } = new EffectSourceRow[]
    {
        // The three gear sources are WIRED. GearEffectSource, GearAffixEffectSource and
        // SetBonusEffectSource read the equipped loadout, so these rows carry neither an owning
        // milestone nor an expiry subject — and clearing them is what lowers the floor the deferral
        // rule quantifies over, in the same commit as the register entries they were keyed on.
        new(EffectSourceKind.GEAR, "gear", null, null),
        new(EffectSourceKind.AFFIXES, "affixes", null, null),
        new(EffectSourceKind.SET_BONUSES, "set bonuses", null, null),
        new(EffectSourceKind.TALENTS, "talents", "M4-06", "TalentNode"),
        new(EffectSourceKind.PET_AURAS, "pet auras", "M4-07", "PetDefinition"),
        new(EffectSourceKind.MOUNT, "mount", "M4-08", "MountDefinition"),
        new(EffectSourceKind.RUN_BUFFS, "run buffs", "M3-08", "RunBuff"),
        new(EffectSourceKind.SHRINE_BUFFS, "shrine buffs", "M3-11", "ShrineBuff"),
        new(EffectSourceKind.CURSES, "curses", "M3-11", "Curse"),
        new(EffectSourceKind.PERKS, "perks (in draft order)", "M3-07", "PerkDefinition"),
    };

    /// <summary>The row for one source.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The kind is outside the ten declared sources.</exception>
    internal static EffectSourceRow RowFor(EffectSourceKind kind)
    {
        foreach (var row in Rows)
        {
            if (row.Kind == kind)
            {
                return row;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(kind),
            kind,
            "18 §8 step 1 names ten sources and this is not one of them. Adding an eleventh means " +
            "adding it to EffectSourceKind, to this catalogue and to 18 §8 — the three-in-one-commit " +
            "rule 18 §10 states for the op vocabulary applies to the source list for the same reason.");
    }
}
