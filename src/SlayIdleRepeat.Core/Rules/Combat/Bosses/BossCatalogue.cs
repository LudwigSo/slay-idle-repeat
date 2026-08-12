using System.Globalization;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// 🔒 One authored script: the <see cref="BossScript"/> and its own effect set, plus the three
/// `17` §1.2 columns that are not part of the script itself.
/// </summary>
/// <param name="Script">The script, exactly as <see cref="BossEncounterBuilder.Build"/> takes it.</param>
/// <param name="Effects">
/// 🔒 The script's OWN effects, by `18` §8 id — the only scope a mechanic id or a
/// <c>RANDOM_OUTCOME</c> row resolves in, and there is no wider one.
/// </param>
/// <param name="Chapter">`17` §1.2's Ch column, or <c>null</c> for the FTUE row.</param>
/// <param name="FixedPower">`17` §1.2's <c>power = 900</c>, on the FTUE row only.</param>
/// <param name="FixedLevel">`17` §1.2's <c>Level = 1</c>, on the FTUE row only.</param>
/// <param name="TelegraphSeconds">
/// 🔴 Every authored wind-up, as <c>(phase, effectId, lead)</c> — a <b>list keyed on the
/// mechanic</b>, not a dictionary keyed on the effect.
/// <para>
/// T1 and T3 govern a <em>mechanic</em>, and one effect can be a mechanic of more than one block
/// (one authored script names the same on-hit effect in all three of its blocks). Keyed by effect
/// id, two blocks naming the same effect with different leads would collapse to one entry, last one
/// winning — so a wind-up could vanish from every census over this without a single assertion
/// moving.
/// </para>
/// </param>
internal sealed record BossScriptEntry(
    BossScript Script,
    IReadOnlyDictionary<string, EffectDefinition> Effects,
    int? Chapter,
    double? FixedPower,
    int? FixedLevel,
    IReadOnlyList<(int Phase, string EffectId, double Lead)> TelegraphSeconds);

/// <summary>
/// 🔒 <c>content/bosses/bosses.json</c>, read — `17` §1.2's nine rows and `17` §2-9's mechanics.
/// </summary>
/// <remarks>
/// <para>
/// The precedent this file follows point for point is
/// <see cref="Enemies.EnemyCatalogue"/>: <c>internal</c>, one <c>Read</c> over a
/// <see cref="ContentSnapshot"/>, every value through a typed reader, pointers written out per
/// block so that <c>grep bosses.json</c> over <c>src/</c> finds every read of the file.
/// </para>
/// <para>
/// 🔒 <b>Nothing here has a default.</b> Every value goes through <see cref="ContentSnapshot"/>'s
/// readers, which throw <see cref="MissingContentException"/> on an absent pointer and
/// <see cref="UnauthorisedTunableException"/> on a <c>null</c>. `18` §1's optional keys — the ones
/// `18` itself authors as absent on some effects — are read through
/// <see cref="ContentSnapshot.IsAuthorised"/> and carried as <c>null</c>; a <c>null</c> there is the
/// documents authorising no value, which is the one thing the content layer never coerces.
/// </para>
/// <para>
/// 🔴 <b>This type replaced a test-side <c>System.Text.Json</c> reader, and it fixes two divergences
/// that reader recorded against itself.</b> (1) <c>JsonDocument</c> silently keeps the LAST of two
/// duplicate keys; a <see cref="ContentValue"/> object refuses a duplicate outright, and the load
/// path that builds one is required to detect the duplicate before it gets there. (2) that reader's
/// <c>Optional</c> returned the element for an authored <c>null</c>, so a numeric read on one threw
/// a serialisation error; here an authored <c>null</c> is
/// <see cref="ContentValueKind.Unauthorised"/>, so an optional key reads as <c>null</c> and a
/// required one throws <see cref="UnauthorisedTunableException"/> by name.
/// </para>
/// <para>
/// 🔒 <b>Nothing here is boss code.</b> `17` §11's <em>"zero bespoke boss code"</em> is a claim about
/// the engine: there is no per-boss branch here and no boss is named anywhere in this file — it is
/// one loop over whatever rows the document declares.
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

    /// <summary>`17` §1.2 — the baseline CRIT every boss shares.</summary>
    internal const string CritPointer = Document + "#/secondaryStats/crit";

    /// <summary>`17` §1.2 — the baseline CDMG, which R21's ×3 is computed from.</summary>
    internal const string CritDamagePointer = Document + "#/secondaryStats/critDamage";

    /// <summary>`17` §1.2 — the baseline DODGE.</summary>
    internal const string DodgePointer = Document + "#/secondaryStats/dodge";

    /// <summary>
    /// `17` §1.2 — the baseline LIFESTEAL. 🔒 Zero: the one boss that has lifesteal carries it as a
    /// phase mechanic, <em>"never a base stat"</em>.
    /// </summary>
    internal const string LifestealPointer = Document + "#/secondaryStats/lifesteal";

    /// <summary>`17` §1.2's table — the eight campaign rows and the FTUE row.</summary>
    internal const string ScriptsPointer = Document + "#/scripts";

    /// <summary>One row of `17` §1.2's table.</summary>
    internal static string ScriptPointer(int script) =>
        $"{ScriptsPointer}/{script.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>`17` §1.2's four coefficients for one row.</summary>
    internal static string CoefficientsPointer(int script) => ScriptPointer(script) + "/coefficients";

    /// <summary>One script's own effect set — the only scope an effect id resolves in.</summary>
    internal static string EffectsPointer(int script) => ScriptPointer(script) + "/effects";

    /// <summary>One authored effect of one script.</summary>
    internal static string EffectPointer(int script, int effect) =>
        $"{EffectsPointer(script)}/{effect.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>One script's three `17` §1 phase blocks.</summary>
    internal static string PhasesPointer(int script) => ScriptPointer(script) + "/phases";

    /// <summary>One phase block.</summary>
    internal static string PhasePointer(int script, int phase) =>
        $"{PhasesPointer(script)}/{phase.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>One phase block's mechanics.</summary>
    internal static string MechanicsPointer(int script, int phase) =>
        PhasePointer(script, phase) + "/mechanics";

    /// <summary>One mechanic — an effect id and `17` §1's wind-up.</summary>
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

    /// <summary>One script by its `17` §1.2 id.</summary>
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

                // 🔒 null where the boss authors no SUMMON. An adds fraction on a boss with no adds
                // is data nothing reads, and a plausible-looking one is exactly the hole the null
                // convention exists to keep greppable.
                AddsPowerFraction = content.IsAuthorised(addsPointer)
                    ? content.ReadDouble(addsPointer)
                    : null,
                Phases = phases,
            },
            effects,

            // 17 §1.2's Ch column is empty for the FTUE row, and its power and level are the fixed
            // authored inputs that row carries instead. Three optional columns, three holes, none
            // of them coerced to a chapter, a power or a level nobody authored.
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
    /// 🔒 Maps one authored effect. Every key the data uses is handled and an unknown one throws —
    /// see <see cref="RequireKnownKeys"/> for why silence would be worse than a failure here.
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

    /// <summary>
    /// `18` §7.9's face selector: the <c>PLAYER_CHOICE</c> token, or a 1-6 index. The kind decides,
    /// so a token nobody authored cannot become face 0.
    /// </summary>
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
    /// 🔒 Refuses a key this reader does not map, at <b>every</b> level of the file.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>It covers the script, the phase block and the mechanic as well as the effect, and that
    /// is the point.</b> Everything below is read through
    /// <see cref="ContentSnapshot.IsAuthorised"/>, which answers <c>false</c> for a key that is
    /// absent <em>and</em> for one that is misspelled — so a <c>telegraphTicks</c> where
    /// <c>telegraphSeconds</c> was meant, or a new script-level field, would be dropped in silence
    /// and every case stated over this catalogue would go on asserting over a script that is not the
    /// one on disk. <c>bosses.json</c> is schema-governed, and this reader still refuses what it does
    /// not understand: the schema says the data is well formed, not that this mapping is complete.
    /// </remarks>
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
    // ⚠️ Static AUTO-PROPERTIES, not static readonly fields: BossEngineRuleTests.
    // The_boss_engine_holds_no_mutable_static_state fails a static field of a mutable type, and
    // exempts the compiler-generated backing field of a get-only auto-property — which is
    // BossBuiltIns.All's and BossTelegraphs.DamagingOps' shape, and the precedent this follows.
    //
    // ⚠️ A List<T> initialiser, not [ … ] and not new[] { … }: a collection expression synthesises a
    // helper in the GLOBAL namespace, which AccessibilityBoundaryTests fails the build on.

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
