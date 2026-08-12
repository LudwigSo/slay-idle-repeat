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
    /// <param name="TelegraphSeconds">Each mechanic's authored wind-up, by effect id, where it has one.</param>
    internal sealed record Authored(
        BossScript Script,
        IReadOnlyDictionary<string, EffectDefinition> Effects,
        int? Chapter,
        double? FixedPower,
        int? FixedLevel,
        IReadOnlyDictionary<string, double> TelegraphSeconds);

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
            var leads = new Dictionary<string, double>(StringComparer.Ordinal);

            foreach (var block in script.GetProperty("phases").EnumerateArray())
            {
                var mechanics = new List<BossMechanic>();

                foreach (var mechanic in block.GetProperty("mechanics").EnumerateArray())
                {
                    var effectId = mechanic.GetProperty("effectId").GetString()!;
                    var lead = Optional(mechanic, "telegraphSeconds")?.GetDouble();

                    if (lead is { } seconds)
                    {
                        leads[effectId] = seconds;
                    }

                    mechanics.Add(new BossMechanic(effectId, lead));
                }

                phases.Add(new BossPhaseBlock
                {
                    Phase = block.GetProperty("phase").GetInt32(),
                    Mechanics = mechanics,
                });
            }

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
        foreach (var member in effect.EnumerateObject())
        {
            if (!Known.Contains(member.Name))
            {
                throw new InvalidOperationException(
                    $"the authored effect '{effect.GetProperty("id").GetString()}' carries the key " +
                    $"'{member.Name}', which this reader does not map. Silently dropping it would " +
                    "leave these cases asserting over a script that is not the one on disk. Map the " +
                    "key, or take it out of the data.");
            }
        }

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
    /// Every key <see cref="ReadEffect"/> maps. A <see cref="List{T}"/> initialiser rather than
    /// <c>[ … ]</c>, on <c>BossBuiltIns.All</c>'s precedent: a collection expression synthesises a
    /// helper in the global namespace, which the namespace rule fails the build on.
    /// </summary>
    private static readonly HashSet<string> Known = new(
        new List<string>
        {
            "id", "op", "stat", "value", "valueMode", "statusId", "archetype", "maxAlive",
            "charges", "target", "trigger", "duration", "stacking", "outcomes", "newFace",
        },
        StringComparer.Ordinal);

    /// <summary>Formats a double the way the engine's own refusals do, for assertion messages.</summary>
    internal static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
