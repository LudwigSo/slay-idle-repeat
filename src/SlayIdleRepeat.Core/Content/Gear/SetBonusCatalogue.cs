using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Content.Gear;

/// <summary>
/// One set breakpoint's grant: how many pieces it needs, and the effects it hands over — or the
/// authored statement that nobody has written them.
/// </summary>
/// <param name="Pieces">The piece count this row fires at.</param>
/// <param name="Effects">
/// The effects the breakpoint grants, or <see langword="null"/> where the design set describes the
/// bonus in prose and authors no effect for it. Never empty: an empty list would say the breakpoint
/// grants nothing, which is a different claim from "nobody wrote it down".
/// </param>
internal readonly record struct SetBonusRow(int Pieces, IReadOnlyList<EffectDefinition>? Effects);

/// <summary>
/// The four SS sets' 2/4/6-piece bonuses, read out of <c>content/sets/sets.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A set is its family axis.</b> There is one set per axis, an SS item's family already resolves
/// to one, and the drop tables author a null set id because the design set names no set-id
/// vocabulary — so the axis is the key here and no second identifier is introduced.
/// </para>
/// <para>
/// 🔒 <b>The piece counts are cross-checked against the authored breakpoint ladder</b> rather than
/// restated. This is the only layer that can see both documents, and a bonus authored at a piece
/// count the ladder does not contain would be a bonus <see cref="Rules.Gear.SetBonusResolver"/> can
/// never report as met — a set effect that reads as authored and can never fire.
/// </para>
/// <para>
/// ⚠️ <b>The effect reader here is narrow on purpose, and it refuses rather than ignores.</b> It maps
/// the keys a set bonus actually authors and throws on any other, so a key it does not handle fails
/// loudly instead of being silently dropped into an effect that then does something other than what
/// the document says. Widening it is a deliberate edit; the boss catalogue's own reader is the wide
/// one, and it lives in a layer this one may not name.
/// </para>
/// </remarks>
internal sealed class SetBonusCatalogue
{
    /// <summary>The document the set bonuses live in.</summary>
    internal const string DocumentPath = "content/sets/sets.json";

    /// <summary>The four sets.</summary>
    internal const string SetsReference = DocumentPath + "#/sets";

    /// <summary>The keys one authored set-bonus effect may carry.</summary>
    /// <remarks>
    /// Closed, and checked. The whole effect vocabulary is much wider; what a set bonus has ever
    /// needed is this, and an authored key outside it is a mechanic nobody has taught this reader.
    /// </remarks>
    private static readonly string[] KnownEffectKeys =
        ["id", "op", "stat", "value", "valueMode", "trigger", "condition", "target"];

    /// <summary>The keys one authored set-bonus trigger may carry.</summary>
    /// <remarks>
    /// 🔒 <b>The nested half of the same guard, and it is not optional.</b> A trigger carries
    /// other parameters this reader does not map — <c>chance</c>, <c>cooldown</c>, <c>interval</c>
    /// and the rest — so without this, checking only the effect's own keys leaves the lossiness one
    /// level down. <c>once</c> is mapped for exactly the case this remark used to warn about:
    /// Ironvow's six-piece is a once-per-battle save on an <c>ON_LETHAL</c> trigger, and dropping
    /// the key silently would turn one save per fight into one every time the hero would die.
    /// </remarks>
    private static readonly string[] KnownTriggerKeys = ["kind", "everyNth", "once"];

    private readonly IReadOnlyDictionary<GearFamilyAxis, IReadOnlyList<SetBonusRow>> _bonuses;

    private SetBonusCatalogue(IReadOnlyDictionary<GearFamilyAxis, IReadOnlyList<SetBonusRow>> bonuses) =>
        _bonuses = bonuses;

    /// <summary>How many sets the document authors.</summary>
    internal int SetCount => _bonuses.Count;

    /// <summary>One set's breakpoint rows, in the order the document authors them.</summary>
    /// <param name="set">The set, named by its family axis.</param>
    /// <returns>Its rows.</returns>
    /// <exception cref="MissingContentException">The document authors no row for this set.</exception>
    internal IReadOnlyList<SetBonusRow> Bonuses(GearFamilyAxis set) =>
        _bonuses.TryGetValue(set, out var rows)
            ? rows
            : throw new MissingContentException(
                SetsReference,
                $"The set catalogue authors nothing for the {set} axis. Every family axis is a set, so " +
                "an axis with no row is a set an equipped loadout can reach and nothing can grant.");

    /// <summary>The effects a loadout wearing this many pieces of one set is granted.</summary>
    /// <remarks>
    /// Every breakpoint at or below the count, not only the highest — the tiers escalate rather than
    /// replace. A breakpoint whose effects are unauthored contributes nothing and is not an error:
    /// the null is the design set's own statement, and refusing here would make a partially authored
    /// set unwearable.
    /// </remarks>
    /// <param name="set">The set, named by its family axis.</param>
    /// <param name="pieces">How many pieces of it are worn.</param>
    /// <returns>The granted effects, in breakpoint order. Empty when none has been reached or authored.</returns>
    /// <exception cref="MissingContentException">The document authors no row for this set.</exception>
    internal IReadOnlyList<EffectDefinition> Granted(GearFamilyAxis set, int pieces)
    {
        var granted = new List<EffectDefinition>();

        foreach (var row in Bonuses(set))
        {
            if (pieces >= row.Pieces && row.Effects is { } effects)
            {
                granted.AddRange(effects);
            }
        }

        return granted.AsReadOnly();
    }

    /// <summary>Reads the set bonuses.</summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <param name="breakpoints">The authored piece-count ladder every row must sit on.</param>
    /// <returns>The catalogue.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="ContentTypeMismatchException">A leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static SetBonusCatalogue Read(ContentSnapshot content, IReadOnlyList<int> breakpoints)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(breakpoints);

        var array = content.Read(SetsReference);

        if (array.Kind != ContentValueKind.Array || array.Items.Count == 0)
        {
            throw new InvalidTunableException(
                SetsReference, "The set catalogue is a non-empty array, one row per family axis.");
        }

        var sets = new Dictionary<GearFamilyAxis, IReadOnlyList<SetBonusRow>>(array.Items.Count);

        for (var i = 0; i < array.Items.Count; i++)
        {
            var pointer = SetsReference + "/" + AuthoredToken.Render(i);
            var axis = AuthoredToken.Parse<GearFamilyAxis>(content, pointer + "/familyAxis", "a family axis");

            if (!sets.TryAdd(axis, ReadBonuses(content, pointer + "/bonuses", breakpoints)))
            {
                throw new InvalidTunableException(
                    pointer + "/familyAxis",
                    $"The {axis} axis is authored twice, so which of the two sets a piece belongs to " +
                    "depends on which row a reader reaches first.");
            }
        }

        return new SetBonusCatalogue(sets);
    }

    private static IReadOnlyList<SetBonusRow> ReadBonuses(
        ContentSnapshot content, string reference, IReadOnlyList<int> breakpoints)
    {
        var array = content.Read(reference);

        if (array.Kind != ContentValueKind.Array || array.Items.Count == 0)
        {
            throw new InvalidTunableException(
                reference, "A set authors at least one breakpoint row, and this one authors none.");
        }

        var rows = new SetBonusRow[array.Items.Count];

        for (var i = 0; i < rows.Length; i++)
        {
            var pointer = reference + "/" + AuthoredToken.Render(i);
            var pieces = content.ReadInt32(pointer + "/pieces");

            if (!breakpoints.Contains(pieces))
            {
                throw new InvalidTunableException(
                    pointer + "/pieces",
                    $"A bonus is authored at {AuthoredToken.Render(pieces)} pieces, which is not one of " +
                    "the authored breakpoints. Only a breakpoint on that ladder is ever reported as " +
                    "met, so this bonus reads as authored and can never fire.");
            }

            if (i > 0 && pieces <= rows[i - 1].Pieces)
            {
                throw new InvalidTunableException(
                    pointer + "/pieces",
                    "A set's bonuses are authored in ascending piece order, so a reader can grant them " +
                    "in the order the tiers escalate rather than sorting them itself.");
            }

            rows[i] = new SetBonusRow(pieces, ReadEffects(content, pointer + "/effects"));
        }

        // Every breakpoint carries a row, not merely every row a breakpoint. A set that dropped one
        // would load cleanly and be indistinguishable from a set whose bonus is unauthored — and the
        // whole document is built on telling those two apart, so the reader has to as well.
        if (rows.Length != breakpoints.Count)
        {
            throw new InvalidTunableException(
                reference,
                $"The set authors {AuthoredToken.Render(rows.Length)} bonus row(s) against " +
                $"{AuthoredToken.Render(breakpoints.Count)} authored breakpoint(s). A missing row is a " +
                "breakpoint the loadout can reach and this document says nothing about, which reads " +
                "exactly like one whose bonus nobody has written — and those are different facts.");
        }

        return Array.AsReadOnly(rows);
    }

    private static IReadOnlyList<EffectDefinition>? ReadEffects(ContentSnapshot content, string reference)
    {
        var value = content.Read(reference);

        if (value.IsUnauthorised)
        {
            return null;
        }

        if (value.Kind != ContentValueKind.Array || value.Items.Count == 0)
        {
            throw new InvalidTunableException(
                reference,
                "An authored breakpoint grants at least one effect. An empty list says the breakpoint " +
                "grants nothing, which no set does — the way to say 'nobody has written this one' is " +
                "null, and that is a different fact.");
        }

        var effects = new EffectDefinition[value.Items.Count];

        for (var i = 0; i < effects.Length; i++)
        {
            effects[i] = ReadEffect(content, reference + "/" + AuthoredToken.Render(i));
        }

        return Array.AsReadOnly(effects);
    }

    private static EffectDefinition ReadEffect(ContentSnapshot content, string pointer)
    {
        RequireKnownKeys(content, pointer, KnownEffectKeys, "a set bonus");

        var statPointer = pointer + "/stat";
        var valuePointer = pointer + "/value";
        var valueModePointer = pointer + "/valueMode";
        var targetPointer = pointer + "/target";
        var triggerPointer = pointer + "/trigger";

        return new EffectDefinition
        {
            Id = content.ReadText(pointer + "/id"),
            Op = AuthoredToken.Parse<EffectOp>(content, pointer + "/op", "an effect operation"),
            Stat = content.IsAuthorised(statPointer)
                ? StatSelector.Of(AuthoredToken.Parse<StatId>(content, statPointer, "one of the stats"))
                : null,
            Value = content.IsAuthorised(valuePointer) ? content.ReadDouble(valuePointer) : null,
            ValueMode = content.IsAuthorised(valueModePointer)
                ? AuthoredToken.Parse<ValueMode>(content, valueModePointer, "a value mode")
                : null,
            Target = content.IsAuthorised(targetPointer)
                ? AuthoredToken.Parse<EffectTarget>(content, targetPointer, "an effect target")
                : null,
            Trigger = content.IsAuthorised(triggerPointer) ? ReadTrigger(content, triggerPointer) : null,

            // An authored gate is read as the tree it writes — the conditional standing-effect
            // bucket made a gated set bonus a legal authored shape (Ironvow's four-piece is an
            // attacker-gated standing DR), and `condition: null` stays the vocabulary's canonical
            // ungated.
            Condition = content.IsAuthorised(pointer + "/condition")
                ? ConditionContentReader.ReadContextGate(
                    content, pointer + "/condition", "a set bonus's condition")
                : null,
        };
    }

    private static EffectTrigger ReadTrigger(ContentSnapshot content, string pointer)
    {
        RequireKnownKeys(content, pointer, KnownTriggerKeys, "a set bonus's trigger");

        return new EffectTrigger
        {
            Kind = AuthoredToken.Parse<TriggerKind>(content, pointer + "/kind", "a trigger kind"),
            EveryNth = content.IsAuthorised(pointer + "/everyNth")
                ? content.ReadInt32(pointer + "/everyNth")
                : null,
            Once = content.IsAuthorised(pointer + "/once")
                ? content.ReadBoolean(pointer + "/once")
                : null,
        };
    }

    /// <summary>Refuses an authored key this reader does not map.</summary>
    /// <remarks>
    /// The difference between a narrow reader and a lossy one. Without this, authoring a
    /// <c>duration</c> or a <c>stacking</c> block on a set bonus would load cleanly and grant a
    /// permanent, unstacked effect instead — the document and the game disagreeing with everything
    /// green.
    /// </remarks>
    private static void RequireKnownKeys(
        ContentSnapshot content, string pointer, IReadOnlyList<string> known, string what)
    {
        foreach (var name in content.Read(pointer).MemberNames)
        {
            if (!known.Contains(name, StringComparer.Ordinal))
            {
                throw new InvalidTunableException(
                    pointer + "/" + name,
                    $"'{name}' is a key this reader does not map, so authoring it would change nothing " +
                    "about the effect the game builds while changing what the document says. The keys " +
                    $"{what} may carry are {string.Join(", ", known)}; teaching it a new one is a " +
                    "deliberate edit here.");
            }
        }
    }
}
