using System.Globalization;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// One authored script: the <see cref="BossScript"/> and its own effect set, plus the columns that
/// are not part of the script itself.
/// </summary>
/// <param name="Script">The script, exactly as <see cref="BossEncounterBuilder.Build"/> takes it.</param>
/// <param name="Effects">
/// The script's own effects, by id — the only scope a mechanic id or a <c>RANDOM_OUTCOME</c> row
/// resolves in.
/// </param>
/// <param name="Chapter">The chapter, or <c>null</c> for the FTUE row.</param>
/// <param name="FixedPower">The fixed authored power, on the FTUE row only.</param>
/// <param name="FixedLevel">The fixed authored level, on the FTUE row only.</param>
/// <param name="TelegraphSeconds">
/// Every authored wind-up, as <c>(phase, effectId, lead)</c> — a list keyed on the mechanic, not a
/// dictionary keyed on the effect, because one effect can be a mechanic of more than one phase block
/// with a different lead each time; a dictionary keyed by effect id would collapse those to one.
/// </param>
internal sealed record BossScriptEntry(
    BossScript Script,
    IReadOnlyDictionary<string, EffectDefinition> Effects,
    int? Chapter,
    double? FixedPower,
    int? FixedLevel,
    IReadOnlyList<(int Phase, string EffectId, double Lead)> TelegraphSeconds);

/// <summary>
/// <c>content/bosses/bosses.json</c>, read.
/// </summary>
/// <remarks>
/// <para>
/// Follows <see cref="Enemies.EnemyCatalogue"/>'s precedent: <c>internal</c>, one <c>Read</c> over a
/// <see cref="ContentSnapshot"/>, every value through a typed reader, pointers written out per block.
/// </para>
/// <para>
/// Nothing here has a default: every value goes through <see cref="ContentSnapshot"/>'s readers,
/// which throw <see cref="MissingContentException"/> on an absent pointer and
/// <see cref="UnauthorisedTunableException"/> on a <c>null</c>. Optional keys are read through
/// <see cref="ContentSnapshot.IsAuthorised"/> and carried as <c>null</c>.
/// </para>
/// <para>
/// No per-boss branch here and no boss is named anywhere in this file — it is one loop over
/// whatever rows the document declares.
/// </para>
/// </remarks>
internal sealed record BossCatalogue(
    IReadOnlyList<BossScriptEntry> Scripts,
    double Crit,
    double CritDamage,
    double Dodge,
    double Lifesteal)
{
    /// <summary>The snapshot-relative path of the document.</summary>
    internal const string Document = "content/bosses/bosses.json";

    /// <summary>The baseline CRIT every boss shares.</summary>
    internal const string CritPointer = Document + "#/secondaryStats/crit";

    /// <summary>The baseline CDMG.</summary>
    internal const string CritDamagePointer = Document + "#/secondaryStats/critDamage";

    /// <summary>The baseline DODGE.</summary>
    internal const string DodgePointer = Document + "#/secondaryStats/dodge";

    /// <summary>
    /// The baseline LIFESTEAL — always zero; the one boss that has lifesteal carries it as a phase
    /// mechanic, never a base stat.
    /// </summary>
    internal const string LifestealPointer = Document + "#/secondaryStats/lifesteal";

    /// <summary>The table of campaign rows plus the FTUE row.</summary>
    internal const string ScriptsPointer = Document + "#/scripts";

    /// <summary>One row of the table.</summary>
    internal static string ScriptPointer(int script) =>
        $"{ScriptsPointer}/{script.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>One row's four coefficients.</summary>
    internal static string CoefficientsPointer(int script) => ScriptPointer(script) + "/coefficients";

    /// <summary>One script's own effect set — the only scope an effect id resolves in.</summary>
    internal static string EffectsPointer(int script) => ScriptPointer(script) + "/effects";

    /// <summary>One authored effect of one script.</summary>
    internal static string EffectPointer(int script, int effect) =>
        $"{EffectsPointer(script)}/{effect.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>One script's three phase blocks.</summary>
    internal static string PhasesPointer(int script) => ScriptPointer(script) + "/phases";

    /// <summary>One phase block.</summary>
    internal static string PhasePointer(int script, int phase) =>
        $"{PhasesPointer(script)}/{phase.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>One phase block's mechanics.</summary>
    internal static string MechanicsPointer(int script, int phase) =>
        PhasePointer(script, phase) + "/mechanics";

    /// <summary>One mechanic — an effect id and its wind-up.</summary>
    internal static string MechanicPointer(int script, int phase, int mechanic) =>
        $"{MechanicsPointer(script, phase)}/{mechanic.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>One <c>RANDOM_OUTCOME</c> row of one effect.</summary>
    internal static string OutcomePointer(int script, int effect, int outcome) =>
        $"{EffectPointer(script, effect)}/outcomes/{outcome.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Reads the whole document.</summary>
    /// <param name="content">The loaded, schema-validated content snapshot.</param>
    /// <exception cref="MissingContentException">A pointer is absent.</exception>
    /// <exception cref="UnauthorisedTunableException">A value is <c>null</c> where one is required.</exception>
    /// <exception cref="ContentTypeMismatchException">
    /// A value is of the wrong kind, or names a token outside a closed vocabulary.
    /// </exception>
    internal static BossCatalogue Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var rows = content.Read(ScriptsPointer);
        var scripts = new List<BossScriptEntry>(rows.Items.Count);

        for (var i = 0; i < rows.Items.Count; i++)
        {
            scripts.Add(ReadScript(content, i));
        }

        return new BossCatalogue(
            scripts,
            content.ReadDouble(CritPointer),
            content.ReadDouble(CritDamagePointer),
            content.ReadDouble(DodgePointer),
            content.ReadDouble(LifestealPointer));
    }

    /// <summary>One script by its id.</summary>
    /// <exception cref="KeyNotFoundException">The catalogue holds no script with that id.</exception>
    internal BossScriptEntry Of(string bossId)
    {
        foreach (var entry in Scripts)
        {
            if (string.Equals(entry.Script.Id, bossId, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        throw new KeyNotFoundException(
            $"content/bosses/bosses.json declares no 17 §1.2 row for '{bossId}'. It declares: " +
            string.Join(", ", Scripts.Select(s => s.Script.Id)));
    }

    // ------------------------------------------------------------------ block readers

    private static BossScriptEntry ReadScript(ContentSnapshot content, int script)
    {
        var pointer = ScriptPointer(script);
        RequireKnownKeys(content, pointer, KnownScriptKeys, "script");

        var effects = ReadEffects(content, script);
        var leads = new List<(int, string, double)>();
        var phases = ReadPhases(content, script, leads);

        var chapterPointer = pointer + "/chapter";
        var fixedPowerPointer = pointer + "/fixedPower";
        var fixedLevelPointer = pointer + "/fixedLevel";
        var addsPointer = pointer + "/addsPowerFraction";

        return new BossScriptEntry(
            new BossScript
            {
                Id = content.ReadText(pointer + "/id"),
                Coefficients = ReadCoefficients(content, script),

                // null where the boss authors no SUMMON, rather than a plausible-looking default.
                AddsPowerFraction = content.IsAuthorised(addsPointer)
                    ? content.ReadDouble(addsPointer)
                    : null,
                Phases = phases,
            },
            effects,

            // Chapter is empty for the FTUE row, which carries fixed power/level instead.
            content.IsAuthorised(chapterPointer) ? content.ReadInt32(chapterPointer) : null,
            content.IsAuthorised(fixedPowerPointer) ? content.ReadDouble(fixedPowerPointer) : null,
            content.IsAuthorised(fixedLevelPointer) ? content.ReadInt32(fixedLevelPointer) : null,
            leads);
    }

    private static BossCoefficients ReadCoefficients(ContentSnapshot content, int script)
    {
        var pointer = CoefficientsPointer(script);

        return new BossCoefficients(
            content.ReadDouble(pointer + "/hp"),
            content.ReadDouble(pointer + "/atk"),
            content.ReadDouble(pointer + "/def"),
            content.ReadDouble(pointer + "/aspd"));
    }

    private static IReadOnlyDictionary<string, EffectDefinition> ReadEffects(
        ContentSnapshot content, int script)
    {
        var rows = content.Read(EffectsPointer(script));
        var effects = new Dictionary<string, EffectDefinition>(rows.Items.Count, StringComparer.Ordinal);

        for (var i = 0; i < rows.Items.Count; i++)
        {
            var effect = ReadEffect(content, script, i);
            effects[effect.Id] = effect;
        }

        return effects;
    }

    private static IReadOnlyList<BossPhaseBlock> ReadPhases(
        ContentSnapshot content, int script, List<(int, string, double)> leads)
    {
        var rows = content.Read(PhasesPointer(script));
        var blocks = new List<BossPhaseBlock>(rows.Items.Count);

        for (var i = 0; i < rows.Items.Count; i++)
        {
            RequireKnownKeys(content, PhasePointer(script, i), KnownPhaseKeys, "phase block");

            var phase = content.ReadInt32(PhasePointer(script, i) + "/phase");
            var mechanicRows = content.Read(MechanicsPointer(script, i));
            var mechanics = new List<BossMechanic>(mechanicRows.Items.Count);

            for (var m = 0; m < mechanicRows.Items.Count; m++)
            {
                var pointer = MechanicPointer(script, i, m);
                RequireKnownKeys(content, pointer, KnownMechanicKeys, "mechanic");

                var effectId = content.ReadText(pointer + "/effectId");
                var leadPointer = pointer + "/telegraphSeconds";
                var lead = content.IsAuthorised(leadPointer) ? content.ReadDouble(leadPointer) : (double?)null;

                if (lead is { } seconds)
                {
                    leads.Add((phase, effectId, seconds));
                }

                mechanics.Add(new BossMechanic(effectId, lead));
            }

            blocks.Add(new BossPhaseBlock { Phase = phase, Mechanics = mechanics });
        }

        return blocks;
    }

    /// <summary>
    /// Maps one authored effect. Every key is handled and an unknown one throws — see
    /// <see cref="RequireKnownKeys"/>.
    /// </summary>
    private static EffectDefinition ReadEffect(ContentSnapshot content, int script, int index)
    {
        var pointer = EffectPointer(script, index);
        RequireKnownKeys(content, pointer, KnownEffectKeys, "effect");

        var statPointer = pointer + "/stat";
        var valuePointer = pointer + "/value";
        var valueModePointer = pointer + "/valueMode";
        var statusIdPointer = pointer + "/statusId";
        var archetypePointer = pointer + "/archetype";
        var maxAlivePointer = pointer + "/maxAlive";
        var chargesPointer = pointer + "/charges";
        var targetPointer = pointer + "/target";
        var triggerPointer = pointer + "/trigger";
        var durationPointer = pointer + "/duration";
        var stackingPointer = pointer + "/stacking";
        var outcomesPointer = pointer + "/outcomes";
        var newFacePointer = pointer + "/newFace";
        var faceIndexPointer = pointer + "/faceIndex";

        return new EffectDefinition
        {
            Id = content.ReadText(pointer + "/id"),
            Op = ParseOp(content.ReadText(pointer + "/op"), pointer + "/op"),
            Stat = content.IsAuthorised(statPointer)
                ? ReadStat(content.ReadText(statPointer), statPointer)
                : null,
            Value = content.IsAuthorised(valuePointer) ? content.ReadDouble(valuePointer) : null,
            ValueMode = content.IsAuthorised(valueModePointer)
                ? ParseValueMode(content.ReadText(valueModePointer), valueModePointer)
                : null,
            StatusId = content.IsAuthorised(statusIdPointer) ? content.ReadText(statusIdPointer) : null,
            Archetype = content.IsAuthorised(archetypePointer) ? content.ReadText(archetypePointer) : null,
            MaxAlive = content.IsAuthorised(maxAlivePointer) ? content.ReadInt32(maxAlivePointer) : null,
            Charges = content.IsAuthorised(chargesPointer) ? content.ReadInt32(chargesPointer) : null,
            Target = content.IsAuthorised(targetPointer)
                ? ParseTarget(content.ReadText(targetPointer), targetPointer)
                : null,
            Trigger = content.IsAuthorised(triggerPointer) ? ReadTrigger(content, triggerPointer) : null,
            Duration = content.IsAuthorised(durationPointer) ? ReadDuration(content, durationPointer) : null,
            Stacking = content.IsAuthorised(stackingPointer) ? ReadStacking(content, stackingPointer) : null,
            Outcomes = content.IsAuthorised(outcomesPointer)
                ? ReadOutcomes(content, script, index)
                : null,
            NewFace = content.IsAuthorised(newFacePointer)
                ? new DieFaceSpec(content.ReadText(newFacePointer + "/kind"))
                : null,
            FaceIndex = content.IsAuthorised(faceIndexPointer)
                ? ReadFaceIndex(content, faceIndexPointer)
                : null,
        };
    }

    private static EffectTrigger ReadTrigger(ContentSnapshot content, string pointer)
    {
        var interval = pointer + "/interval";
        var startDelay = pointer + "/startDelay";
        var phase = pointer + "/phase";
        var threshold = pointer + "/threshold";
        var once = pointer + "/once";
        var cooldown = pointer + "/cooldown";
        var everyNth = pointer + "/everyNth";
        var chance = pointer + "/chance";

        return new EffectTrigger
        {
            Kind = ParseTriggerKind(content.ReadText(pointer + "/kind"), pointer + "/kind"),
            Interval = content.IsAuthorised(interval) ? content.ReadDouble(interval) : null,
            StartDelay = content.IsAuthorised(startDelay) ? content.ReadDouble(startDelay) : null,
            Phase = content.IsAuthorised(phase) ? content.ReadInt32(phase) : null,
            Threshold = content.IsAuthorised(threshold) ? content.ReadDouble(threshold) : null,
            Once = content.IsAuthorised(once) ? content.ReadBoolean(once) : null,
            Cooldown = content.IsAuthorised(cooldown) ? content.ReadDouble(cooldown) : null,
            EveryNth = content.IsAuthorised(everyNth) ? content.ReadInt32(everyNth) : null,
            Chance = content.IsAuthorised(chance) ? content.ReadDouble(chance) : null,
        };
    }

    private static EffectDuration ReadDuration(ContentSnapshot content, string pointer)
    {
        var seconds = pointer + "/seconds";
        var until = pointer + "/until";

        return new EffectDuration
        {
            Scope = ParseDurationScope(content.ReadText(pointer + "/scope"), pointer + "/scope"),
            Seconds = content.IsAuthorised(seconds) ? content.ReadDouble(seconds) : null,
            Until = content.IsAuthorised(until)
                ? ParseTerminator(content.ReadText(until), until)
                : null,
        };
    }

    private static EffectStacking ReadStacking(ContentSnapshot content, string pointer)
    {
        var maxStacks = pointer + "/maxStacks";
        var refresh = pointer + "/refreshOnReapply";

        return new EffectStacking
        {
            Mode = ParseStackingMode(content.ReadText(pointer + "/mode"), pointer + "/mode"),
            MaxStacks = content.IsAuthorised(maxStacks) ? content.ReadInt32(maxStacks) : null,
            RefreshOnReapply = content.IsAuthorised(refresh) ? content.ReadBoolean(refresh) : null,
        };
    }

    private static IReadOnlyList<RandomOutcomeEntry> ReadOutcomes(
        ContentSnapshot content, int script, int effect)
    {
        var rows = content.Read(EffectPointer(script, effect) + "/outcomes");
        var outcomes = new List<RandomOutcomeEntry>(rows.Items.Count);

        for (var i = 0; i < rows.Items.Count; i++)
        {
            var pointer = OutcomePointer(script, effect, i);

            outcomes.Add(new RandomOutcomeEntry(
                content.ReadText(pointer + "/effectId"),
                content.ReadDouble(pointer + "/weight")));
        }

        return outcomes;
    }

    /// <summary>The face selector: the <c>PLAYER_CHOICE</c> token, or a 1-6 index.</summary>
    private static DieFaceIndex ReadFaceIndex(ContentSnapshot content, string pointer)
    {
        if (content.Read(pointer).Kind != ContentValueKind.Text)
        {
            return DieFaceIndex.At(content.ReadInt32(pointer));
        }

        var token = content.ReadText(pointer);

        return string.Equals(token, DieFaceIndex.PlayerChoiceToken, StringComparison.Ordinal)
            ? DieFaceIndex.PlayerChoice
            : throw new ContentTypeMismatchException(
                pointer, ContentValueKind.Text,
                $"'{token}', which is not a face selector 18 §7.9 states. It offers " +
                $"'{DieFaceIndex.PlayerChoiceToken}' or a face index of " +
                $"{DieFaceIndex.MinFace.ToString(CultureInfo.InvariantCulture)}-" +
                $"{DieFaceIndex.MaxFace.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// Refuses a key this reader does not map, at every level of the file — a misspelled key (e.g.
    /// <c>telegraphTicks</c> for <c>telegraphSeconds</c>) is read through
    /// <see cref="ContentSnapshot.IsAuthorised"/> the same as an absent one, so it would otherwise be
    /// dropped in silence rather than caught.
    /// </summary>
    /// <exception cref="ContentTypeMismatchException">A key is not one this reader maps.</exception>
    private static void RequireKnownKeys(
        ContentSnapshot content, string pointer, HashSet<string> known, string what)
    {
        foreach (var name in content.Read(pointer).MemberNames)
        {
            if (known.Contains(name))
            {
                continue;
            }

            throw new ContentTypeMismatchException(
                pointer, ContentValueKind.Object,
                $"an authored {what} whose keys this reader maps — it carries '{name}', which it does " +
                "not. Silently dropping the key would leave every case stated over this catalogue " +
                "asserting over a script that is not the one on disk. Map the key, or take it out of " +
                "the data");
        }
    }

    // ------------------------------------------------------------------ vocabulary parsing

    private static EffectOp ParseOp(string name, string pointer) =>
        Enum.TryParse<EffectOp>(name, ignoreCase: false, out var op) && Enum.IsDefined(op)
            ? op
            : throw new ContentTypeMismatchException(
                pointer, ContentValueKind.Text,
                $"'{name}', which is not one of 18 §2's operations. The vocabulary is closed: a new op " +
                "is 18 §10's extension procedure, not a data edit");

    private static StatSelector ReadStat(string token, string pointer) => token switch
    {
        "ALL_COMBAT" => StatSelector.AllCombat,
        "HIGHEST_PCT_BONUS" => StatSelector.HighestPctBonus,
        _ => Enum.TryParse<StatId>(token, ignoreCase: false, out var stat) && Enum.IsDefined(stat)
            ? StatSelector.Of(stat)
            : throw new ContentTypeMismatchException(
                pointer, ContentValueKind.Text,
                $"'{token}', which is neither one of 05 §1's stats nor one of 18 §2.1's two selectors " +
                "(ALL_COMBAT, HIGHEST_PCT_BONUS)"),
    };

    private static ValueMode ParseValueMode(string name, string pointer) =>
        Enum.TryParse<ValueMode>(name, ignoreCase: false, out var mode) && Enum.IsDefined(mode)
            ? mode
            : throw new ContentTypeMismatchException(
                pointer, ContentValueKind.Text,
                $"'{name}', which is not one of 18 §2.2's value modes " +
                $"({string.Join(", ", Enum.GetNames<ValueMode>())})");

    private static EffectTarget ParseTarget(string name, string pointer) =>
        Enum.TryParse<EffectTarget>(name, ignoreCase: false, out var target) && Enum.IsDefined(target)
            ? target
            : throw new ContentTypeMismatchException(
                pointer, ContentValueKind.Text,
                $"'{name}', which is not one of 18 §5's targets " +
                $"({string.Join(", ", Enum.GetNames<EffectTarget>())})");

    private static TriggerKind ParseTriggerKind(string name, string pointer) =>
        Enum.TryParse<TriggerKind>(name, ignoreCase: false, out var kind) && Enum.IsDefined(kind)
            ? kind
            : throw new ContentTypeMismatchException(
                pointer, ContentValueKind.Text,
                $"'{name}', which is not one of 18 §3's trigger kinds. The set is closed: a new kind is " +
                "18 §10's extension procedure, not a data edit");

    private static DurationScope ParseDurationScope(string name, string pointer) =>
        Enum.TryParse<DurationScope>(name, ignoreCase: false, out var scope) && Enum.IsDefined(scope)
            ? scope
            : throw new ContentTypeMismatchException(
                pointer, ContentValueKind.Text,
                $"'{name}', which is not one of 18 §6's six duration scopes " +
                $"({string.Join(", ", Enum.GetNames<DurationScope>())})");

    private static DurationTerminator ParseTerminator(string name, string pointer) =>
        Enum.TryParse<DurationTerminator>(name, ignoreCase: false, out var until) && Enum.IsDefined(until)
            ? until
            : throw new ContentTypeMismatchException(
                pointer, ContentValueKind.Text,
                $"'{name}', which is not one of 18 §6's duration terminators " +
                $"({string.Join(", ", Enum.GetNames<DurationTerminator>())})");

    private static StackingMode ParseStackingMode(string name, string pointer) =>
        Enum.TryParse<StackingMode>(name, ignoreCase: false, out var mode) && Enum.IsDefined(mode)
            ? mode
            : throw new ContentTypeMismatchException(
                pointer, ContentValueKind.Text,
                $"'{name}', which is not one of 18 §6's stacking modes " +
                $"({string.Join(", ", Enum.GetNames<StackingMode>())})");

    // ------------------------------------------------------------------ the mapped key sets
    //
    // Static auto-properties, not static readonly fields: a mutable-static-state test exempts the
    // compiler-generated backing field of a get-only auto-property but not a plain static field.
    //
    // A List<T> initialiser, not [ … ]: a collection expression synthesises a helper in the global
    // namespace, which the namespace-boundary test fails the build on.

    /// <summary>Every key <see cref="ReadEffect"/> maps.</summary>
    private static HashSet<string> KnownEffectKeys { get; } = new(
        new List<string>
        {
            "id", "op", "stat", "value", "valueMode", "statusId", "archetype", "maxAlive",
            "charges", "target", "trigger", "duration", "stacking", "outcomes", "newFace",
            "faceIndex",
        },
        StringComparer.Ordinal);

    /// <summary>Every key a script row may carry. <c>_doc</c> is prose and is read by nothing.</summary>
    private static HashSet<string> KnownScriptKeys { get; } = new(
        new List<string>
        {
            "id", "_doc", "chapter", "coefficients", "addsPowerFraction", "fixedPower", "fixedLevel",
            "effects", "phases",
        },
        StringComparer.Ordinal);

    /// <summary>Every key a phase block may carry.</summary>
    private static HashSet<string> KnownPhaseKeys { get; } = new(
        new List<string> { "phase", "mechanics" }, StringComparer.Ordinal);

    /// <summary>Every key a mechanic may carry.</summary>
    private static HashSet<string> KnownMechanicKeys { get; } = new(
        new List<string> { "effectId", "telegraphSeconds" }, StringComparer.Ordinal);
}
