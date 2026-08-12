using System.Globalization;
using System.Reflection;
using System.Text.Json;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// 🔒 M2-13's authored <c>game-data/content/bosses/bosses.json</c>, read into the
/// <see cref="BossScript"/> shape <see cref="BossEncounterBuilder"/> consumes.
/// </summary>
/// <remarks>
/// <para>
/// ═══ 🔒 <b>WHY THE REAL FILE AND NOT A FIXTURE</b> ═══
/// </para>
/// <para>
/// The claim these cases exist to support is that the <b>authored</b> data satisfies the encounter
/// builder's eight authoring rules — A1-A5, T1-T3 and O1 — and produces the numbers `17` states. A
/// hand-written fixture would prove that some script satisfies them, which is what
/// <c>BossEncounterBuilderTests</c> already proves. So this reads the shipped file, embedded from
/// <c>game-data/</c> by the project file rather than copied into this tree.
/// </para>
/// <para>
/// ⚠️ <b>This is a test-side reader and deliberately not a content loader.</b> It maps only the keys
/// M2-13's data actually authors, and it throws on anything it does not recognise rather than
/// ignoring it — a silently dropped key would make every case here pass over a script that is not
/// the one on disk. The production route is the content pipeline, which validates the same file
/// against <c>effect.schema.json</c>; this suite is the other half, and neither substitutes for the
/// other.
/// </para>
/// <para>
/// 🔒 <b>Nothing here is boss code.</b> `17` §11's <em>"zero bespoke boss code"</em> is a claim about
/// the engine: there is no per-boss branch in <c>SlayIdleRepeat.Core</c> and this file adds none —
/// it is one loop over nine rows, with no boss named anywhere in it.
/// </para>
///
/// <para>
/// ═══ 🔴 <b>THIS READER HAS A SUCCESSOR, AND IT IS THE NEXT TASK — RECORDED, NOT DISCOVERED LATER</b> ═══
/// </para>
/// <para>
/// <c>EnemyFixtures</c> and <c>StatFixtures</c> both state this suite's convention in prose: Core.Tests
/// <em>"has no JSON reader and no content loader"</em>, so a shipped file's numbers are restated as a
/// fixture here and the transcription is asserted separately in <c>SlayIdleRepeat.Application.Tests</c>,
/// which reads the real document. <b>This file reaches the opposite conclusion from the same
/// premise</b>, and the reason is narrow: what these cases are about <em>is</em> the shipped script —
/// that the authored data satisfies <see cref="BossEncounterBuilder"/>'s eight authoring rules — and a
/// restated fixture proves that of the fixture, not of the data.
/// </para>
/// <para>
/// 🔴 <b>The proper resolution is a <c>BossCatalogue.Read(ContentSnapshot)</c> in
/// <c>Core/Rules/Combat/Bosses/</c>, on <c>EnemyCatalogue</c>'s and <c>StatusCatalogue</c>'s exact
/// precedent</b> — Core already owns that responsibility for <c>enemies.json</c> and
/// <c>statuses.json</c>. It is not written here because M2-13 authors data against an engine that
/// already exists, and because <b>M2-16a needs that mapping anyway</b>: its balance harness must build
/// a boss encounter from this file, and <c>tools/BalanceHarness</c> is pinned to Core with no package
/// references, so it can reach neither this reader nor the Application pipeline. When
/// <c>BossCatalogue</c> lands, this file is <b>deleted</b> and its cases re-pointed at it; it is not
/// to be kept alongside as a second mapping.
/// </para>
/// <para>
/// ⚠️ <b>Two ways it already disagrees with the pipeline, stated so neither is discovered by a
/// symptom.</b> (1) <see cref="JsonDocument"/> silently keeps the <em>last</em> of two duplicate keys;
/// <c>JsonContentReader</c> exists partly to report that as a duplicate-key finding. (2)
/// <see cref="Optional"/> returns the element for an authored <c>null</c>, so a numeric read on one
/// throws, where the pipeline classifies it as an unauthorised hole and every rule skips it — which is
/// the load-bearing convention of the whole content layer. Neither bites today: <c>bosses.json</c>
/// authors no duplicate keys and no nulls, the second of which is pinned by name in
/// <c>RealDataNegativeCaseTests</c>' per-file hole census.
/// </para>
/// </remarks>
internal static class AuthoredBossScripts
{
    /// <summary>The logical name the project file embeds the authored data under.</summary>
    private const string ResourceName = "bosses.json";

    /// <summary>One authored script: the <see cref="BossScript"/> and its own effect set.</summary>
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
    /// (<c>BOSS_CINDERMAW_SMOULDER_BURN</c> is named by all three of Cindermaw's). Keyed by effect id,
    /// two blocks naming the same effect with different leads would collapse to one entry, last one
    /// winning — so a wind-up could vanish from every census over this without a single assertion
    /// moving.
    /// </para>
    /// </param>
    internal sealed record Authored(
        BossScript Script,
        IReadOnlyDictionary<string, EffectDefinition> Effects,
        int? Chapter,
        double? FixedPower,
        int? FixedLevel,
        IReadOnlyList<(int Phase, string EffectId, double Lead)> TelegraphSeconds);

    private static readonly Lazy<IReadOnlyList<Authored>> LazyAll = new(Read);

    /// <summary>Every authored script, in the order the file declares them.</summary>
    internal static IReadOnlyList<Authored> All => LazyAll.Value;

    /// <summary>`17` §1.2's baseline secondaries, authored once for all nine rows.</summary>
    internal static (double Crit, double CritDamage, double Dodge, double Lifesteal) SecondaryStats =>
        LazySecondaries.Value;

    private static readonly Lazy<(double, double, double, double)> LazySecondaries = new(ReadSecondaries);

    /// <summary>One script by its `17` §1.2 id.</summary>
    /// <param name="bossId">e.g. <c>BOSS_DICELORD</c>.</param>
    internal static Authored Of(string bossId) =>
        All.FirstOrDefault(a => string.Equals(a.Script.Id, bossId, StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            $"the authored data declares no '{bossId}'. It declares: " +
            string.Join(", ", All.Select(a => a.Script.Id)));

    private static JsonElement Root()
    {
        var stream = typeof(AuthoredBossScripts).GetTypeInfo().Assembly
                         .GetManifestResourceStream(ResourceName)
                     ?? throw new InvalidOperationException(
                         $"'{ResourceName}' is not embedded in this assembly. The project file embeds " +
                         "game-data/content/bosses/bosses.json under that logical name; without it every " +
                         "case in this namespace would be asserting over nothing.");

        using (stream)
        {
            using var document = JsonDocument.Parse(stream);

            return document.RootElement.Clone();
        }
    }

    private static (double, double, double, double) ReadSecondaries()
    {
        var secondaries = Root().GetProperty("secondaryStats");

        return (
            secondaries.GetProperty("crit").GetDouble(),
            secondaries.GetProperty("critDamage").GetDouble(),
            secondaries.GetProperty("dodge").GetDouble(),
            secondaries.GetProperty("lifesteal").GetDouble());
    }

    private static IReadOnlyList<Authored> Read()
    {
        var authored = new List<Authored>();

        foreach (var script in Root().GetProperty("scripts").EnumerateArray())
        {
            var effects = new Dictionary<string, EffectDefinition>(StringComparer.Ordinal);

            foreach (var effect in script.GetProperty("effects").EnumerateArray())
            {
                var read = ReadEffect(effect);
                effects[read.Id] = read;
            }

            var phases = new List<BossPhaseBlock>();
            var leads = new List<(int, string, double)>();

            foreach (var block in script.GetProperty("phases").EnumerateArray())
            {
                var mechanics = new List<BossMechanic>();
                var phase = block.GetProperty("phase").GetInt32();

                foreach (var mechanic in block.GetProperty("mechanics").EnumerateArray())
                {
                    RequireKnownKeys(mechanic, KnownMechanicKeys, "mechanic");

                    var effectId = mechanic.GetProperty("effectId").GetString()!;
                    var lead = Optional(mechanic, "telegraphSeconds")?.GetDouble();

                    if (lead is { } seconds)
                    {
                        leads.Add((phase, effectId, seconds));
                    }

                    mechanics.Add(new BossMechanic(effectId, lead));
                }

                RequireKnownKeys(block, KnownPhaseKeys, "phase block");

                phases.Add(new BossPhaseBlock { Phase = phase, Mechanics = mechanics });
            }

            RequireKnownKeys(script, KnownScriptKeys, "script");

            authored.Add(new Authored(
                new BossScript
                {
                    Id = script.GetProperty("id").GetString()!,
                    Coefficients = ReadCoefficients(script.GetProperty("coefficients")),
                    AddsPowerFraction = Optional(script, "addsPowerFraction")?.GetDouble(),
                    Phases = phases,
                },
                effects,
                Optional(script, "chapter")?.GetInt32(),
                Optional(script, "fixedPower")?.GetDouble(),
                Optional(script, "fixedLevel")?.GetInt32(),
                leads));
        }

        return authored;
    }

    private static BossCoefficients ReadCoefficients(JsonElement row) =>
        new(
            row.GetProperty("hp").GetDouble(),
            row.GetProperty("atk").GetDouble(),
            row.GetProperty("def").GetDouble(),
            row.GetProperty("aspd").GetDouble());

    /// <summary>
    /// 🔒 Maps one authored effect. Every key M2-13's data uses is handled and an unknown one throws
    /// — see the class remarks for why silence would be worse than a failure here.
    /// </summary>
    private static EffectDefinition ReadEffect(JsonElement effect)
    {
        RequireKnownKeys(effect, KnownEffectKeys, "effect");

        return new EffectDefinition
        {
            Id = effect.GetProperty("id").GetString()!,
            Op = Enum.Parse<EffectOp>(effect.GetProperty("op").GetString()!),
            Stat = Optional(effect, "stat") is { } stat ? ReadStat(stat.GetString()!) : null,
            Value = Optional(effect, "value")?.GetDouble(),
            ValueMode = Optional(effect, "valueMode") is { } mode
                ? Enum.Parse<ValueMode>(mode.GetString()!)
                : null,
            StatusId = Optional(effect, "statusId")?.GetString(),
            Archetype = Optional(effect, "archetype")?.GetString(),
            MaxAlive = Optional(effect, "maxAlive")?.GetInt32(),
            Charges = Optional(effect, "charges")?.GetInt32(),
            Target = Optional(effect, "target") is { } target
                ? Enum.Parse<EffectTarget>(target.GetString()!)
                : null,
            Trigger = Optional(effect, "trigger") is { } trigger ? ReadTrigger(trigger) : null,
            Duration = Optional(effect, "duration") is { } duration ? ReadDuration(duration) : null,
            Stacking = Optional(effect, "stacking") is { } stacking ? ReadStacking(stacking) : null,
            Outcomes = Optional(effect, "outcomes") is { } outcomes ? ReadOutcomes(outcomes) : null,
            NewFace = Optional(effect, "newFace") is { } face
                ? new DieFaceSpec(face.GetProperty("kind").GetString()!)
                : null,
            FaceIndex = Optional(effect, "faceIndex") is { } index
                ? index.ValueKind == System.Text.Json.JsonValueKind.String
                    ? DieFaceIndex.PlayerChoice
                    : DieFaceIndex.At(index.GetInt32())
                : null,
        };
    }

    private static StatSelector ReadStat(string token) =>
        token switch
        {
            "ALL_COMBAT" => StatSelector.AllCombat,
            "HIGHEST_PCT_BONUS" => StatSelector.HighestPctBonus,
            _ => StatSelector.Of(Enum.Parse<StatId>(token)),
        };

    private static EffectTrigger ReadTrigger(JsonElement trigger) =>
        new()
        {
            Kind = Enum.Parse<TriggerKind>(trigger.GetProperty("kind").GetString()!),
            Interval = Optional(trigger, "interval")?.GetDouble(),
            StartDelay = Optional(trigger, "startDelay")?.GetDouble(),
            Phase = Optional(trigger, "phase")?.GetInt32(),
            Threshold = Optional(trigger, "threshold")?.GetDouble(),
            Once = Optional(trigger, "once")?.GetBoolean(),
            Cooldown = Optional(trigger, "cooldown")?.GetDouble(),
            EveryNth = Optional(trigger, "everyNth")?.GetInt32(),
            Chance = Optional(trigger, "chance")?.GetDouble(),
        };

    private static EffectDuration ReadDuration(JsonElement duration) =>
        new()
        {
            Scope = Enum.Parse<DurationScope>(duration.GetProperty("scope").GetString()!),
            Seconds = Optional(duration, "seconds")?.GetDouble(),
            Until = Optional(duration, "until") is { } until
                ? Enum.Parse<DurationTerminator>(until.GetString()!)
                : null,
        };

    private static EffectStacking ReadStacking(JsonElement stacking) =>
        new()
        {
            Mode = Enum.Parse<StackingMode>(stacking.GetProperty("mode").GetString()!),
            MaxStacks = Optional(stacking, "maxStacks")?.GetInt32(),
            RefreshOnReapply = Optional(stacking, "refreshOnReapply")?.GetBoolean(),
        };

    private static IReadOnlyList<RandomOutcomeEntry> ReadOutcomes(JsonElement outcomes)
    {
        var rows = new List<RandomOutcomeEntry>();

        foreach (var row in outcomes.EnumerateArray())
        {
            rows.Add(new RandomOutcomeEntry(
                row.GetProperty("effectId").GetString()!,
                row.GetProperty("weight").GetDouble()));
        }

        return rows;
    }

    private static JsonElement? Optional(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var found) ? found : null;

    /// <summary>
    /// 🔒 Refuses a key this reader does not map, at <b>every</b> level of the file.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>It covers the script, the phase block and the mechanic as well as the effect, and that
    /// is the point.</b> Everything below is read through <see cref="Optional"/>, which answers
    /// <c>null</c> for a key that is absent <em>and</em> for one that is misspelled — so a
    /// <c>telegraphTicks</c> where <c>telegraphSeconds</c> was meant, or a new script-level field,
    /// would be dropped in silence and every case in this namespace would go on asserting over a
    /// script that is not the one on disk. That is precisely the vacuous pass the suite exists to
    /// avoid, so the guard has to reach as far as the reader does.
    /// </remarks>
    private static void RequireKnownKeys(JsonElement owner, HashSet<string> known, string what)
    {
        foreach (var member in owner.EnumerateObject())
        {
            if (known.Contains(member.Name))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"an authored {what} carries the key '{member.Name}', which this reader does not map. " +
                "Silently dropping it would leave these cases asserting over a script that is not the " +
                "one on disk. Map the key, or take it out of the data.");
        }
    }

    /// <summary>
    /// Every key <see cref="ReadEffect"/> maps. A <see cref="List{T}"/> initialiser rather than
    /// <c>[ … ]</c>, on <c>BossBuiltIns.All</c>'s precedent: a collection expression synthesises a
    /// helper in the global namespace, which the namespace rule fails the build on.
    /// </summary>
    private static readonly HashSet<string> KnownEffectKeys = new(
        new List<string>
        {
            "id", "op", "stat", "value", "valueMode", "statusId", "archetype", "maxAlive",
            "charges", "target", "trigger", "duration", "stacking", "outcomes", "newFace",
            "faceIndex",
        },
        StringComparer.Ordinal);

    /// <summary>Every key a script row may carry. <c>_doc</c> is prose and is read by nothing.</summary>
    private static readonly HashSet<string> KnownScriptKeys = new(
        new List<string>
        {
            "id", "_doc", "chapter", "coefficients", "addsPowerFraction", "fixedPower", "fixedLevel",
            "effects", "phases",
        },
        StringComparer.Ordinal);

    /// <summary>Every key a phase block may carry.</summary>
    private static readonly HashSet<string> KnownPhaseKeys = new(
        new List<string> { "phase", "mechanics" }, StringComparer.Ordinal);

    /// <summary>Every key a mechanic may carry.</summary>
    private static readonly HashSet<string> KnownMechanicKeys = new(
        new List<string> { "effectId", "telegraphSeconds" }, StringComparer.Ordinal);

    /// <summary>Formats a double the way the engine's own refusals do, for assertion messages.</summary>
    internal static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
