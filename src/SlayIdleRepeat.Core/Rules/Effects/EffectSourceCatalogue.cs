namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// One row of `18` §8 step 1's source list.
/// </summary>
/// <param name="Kind">The source.</param>
/// <param name="Phrase">
/// 🔒 The words `18` §8 step 1 uses for it, exactly. The catalogue's rows are joined back into the
/// document's own sentence and compared with it, so a row that drifts, moves or disappears fails
/// against the spec rather than against a second transcription of it.
/// </param>
/// <param name="OwningMilestone">
/// The milestone that lands the data model this source reads. <c>null</c> where the source can
/// already yield effects today.
/// </param>
/// <param name="PendingSubject">
/// 🔒 The <c>Core</c> type whose <b>arrival</b> means this source can be wired — the name its
/// <c>SubjectSetFloorTests.Pending</c> entry is keyed on, so that each absent source expires by
/// itself (steering S4). <c>null</c> for a source that is not pending.
/// </param>
internal sealed record EffectSourceRow(
    EffectSourceKind Kind, string Phrase, string? OwningMilestone, string? PendingSubject)
{
    /// <summary>True where no data model exists for this source yet.</summary>
    internal bool IsPending => OwningMilestone is not null;
}

/// <summary>
/// 🔒 `18` §8 <b>step 1</b> — <em>"collect all active effects from"</em> — as a declared, floored
/// inventory of ten sources.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>NONE of the ten has a data model, and none of them is stubbed.</b> Gear, affixes and set
/// bonuses are M4-03; talents M4-06; pet auras M4-07; the mount M4-08; run buffs M3-08; shrine buffs
/// and curses M3-11; perks M3-07. Steering S6 forbids filling a hole with a plausible value, and a
/// plausible <c>GearItem</c> here would be ten invented shapes that seven later milestones would each
/// have to find and delete. So the correct end state today is exactly what this is: <b>a collector
/// that enumerates ten declared sources, none of which a real build can yet fill.</b> What CAN fill a
/// slot is <c>ListEffectSource</c> — the degenerate implementation `05` §9's balance harness and the
/// tests hand synthetic builds through — which is why the pipeline is testable end to end today
/// without a single stub of gear, of a perk or of a draft.
/// </para>
/// <para>
/// 🔒 <b>What is declared instead of stubbed.</b> A source is a name, a position in §8 step 1's
/// order, the milestone that lands it and the type whose arrival discharges the deferral. The
/// <em>shape</em> a source hands over is <see cref="IEffectSource"/> — a list of
/// <see cref="Content.Effects.EffectDefinition"/> and nothing else — which is the narrowest thing
/// every one of the ten can satisfy, because it is what §8 step 1 says they contribute. M4-03 does
/// not have to fit its gear model to a guess made here; it has to project it to a list of effects,
/// which it must produce anyway.
/// </para>
/// <para>
/// 🔒 <b>Two floors, and they watch different things (steering S3).</b>
/// <c>EffectSourceCatalogueTests.The_ten_sources_are_18_8_step_1_in_its_own_order</c> rebuilds §8 step
/// 1's sentence from <see cref="EffectSourceRow.Phrase"/> and compares it with the literal quotation,
/// so a dropped or reordered row fails against the <em>document</em>. Ten
/// <c>SubjectSetFloorTests.Pending</c> entries — one per absent source, keyed on
/// <see cref="EffectSourceRow.PendingSubject"/> — fail on the day that source's type
/// <em>arrives</em>, which is when somebody has to come back here and wire it.
/// </para>
/// <para>
/// ⚠️ <b>The pending subject names are inferences, recorded as such</b>, on the precedent of
/// <c>DurationScopes.OutlivesTheBattle</c>'s <c>RunController</c> entry. Each is the type the tracker
/// row for that milestone describes (M4-03's <em>"gear instance schema"</em> → <c>GearItem</c>, and
/// so on). If the owning milestone picks another name, the correct action is to <b>rename</b> the
/// <c>Pending</c> entry rather than delete it — the subject being tracked is "this source now has
/// something to collect from", not the string. The inbound path is stated here, in production code,
/// because a note addressed to M4-03 is worthless in a test file M4-03 will never open. And the two
/// halves are no longer joined by prose alone: <c>EffectSourceDeferralRuleTests</c> reads these ten
/// literals out of this type's IL and fails when one of them is tracked by no register entry.
/// </para>
/// <para>
/// ⚠️ <b>The ten names use two spellings, and that is a per-source guess rather than a convention
/// claim.</b> Three carry <c>Definition</c> (<c>PetDefinition</c>, <c>MountDefinition</c>,
/// <c>PerkDefinition</c>) because `07` and `06` call those things definitions in prose and
/// <see cref="Content.Effects.EffectDefinition"/> is the repo's one precedent; seven do not, because
/// `08` §2 calls a gear item an item and `09` calls a talent a node. If a milestone's real name
/// differs, <b>rename the register entry</b> — the guess being wrong is expected and cheap, the
/// deferral going untracked is neither.
/// </para>
/// </remarks>
internal static class EffectSourceCatalogue
{
    /// <summary>
    /// 🔒 `18` §8 step 1's source list, verbatim — the string
    /// <see cref="Rows"/> is checked against.
    /// </summary>
    internal const string Step1SourceList =
        "gear → affixes → set bonuses → talents → pet auras → mount → run buffs → shrine buffs → " +
        "curses → perks (in draft order)";

    /// <summary>The separator `18` §8 step 1 writes between its sources.</summary>
    internal const string PhraseSeparator = " → ";

    /// <summary>
    /// 🔒 The ten sources, in `18` §8 step 1's order.
    /// </summary>
    /// <remarks>
    /// ⚠️ Built with <c>new EffectSourceRow[] { … }</c> rather than a collection expression. A
    /// <c>[…]</c> targeting <see cref="IReadOnlyList{T}"/> makes Roslyn synthesise a list type into
    /// the <b>global</b> namespace without <c>CompilerGeneratedAttribute</c>, which
    /// <c>AccessibilityBoundaryTests.Every_Core_type_lives_under_a_documented_namespace</c> reports as
    /// an undocumented `30` §11.4 namespace. M2-01, M2-06 and M2-03 each hit a different shape of it.
    /// </remarks>
    internal static IReadOnlyList<EffectSourceRow> Rows { get; } = new EffectSourceRow[]
    {
        new(EffectSourceKind.GEAR, "gear", "M4-03", "GearItem"),
        new(EffectSourceKind.AFFIXES, "affixes", "M4-03", "GearAffix"),
        new(EffectSourceKind.SET_BONUSES, "set bonuses", "M4-03", "SetBonus"),
        new(EffectSourceKind.TALENTS, "talents", "M4-06", "TalentNode"),
        new(EffectSourceKind.PET_AURAS, "pet auras", "M4-07", "PetDefinition"),
        new(EffectSourceKind.MOUNT, "mount", "M4-08", "MountDefinition"),
        new(EffectSourceKind.RUN_BUFFS, "run buffs", "M3-08", "RunBuff"),
        new(EffectSourceKind.SHRINE_BUFFS, "shrine buffs", "M3-11", "ShrineBuff"),
        new(EffectSourceKind.CURSES, "curses", "M3-11", "Curse"),
        new(EffectSourceKind.PERKS, "perks (in draft order)", "M3-07", "PerkDefinition"),
    };

    /// <summary>The row for one source.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The kind is outside `18` §8 step 1's ten.</exception>
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
