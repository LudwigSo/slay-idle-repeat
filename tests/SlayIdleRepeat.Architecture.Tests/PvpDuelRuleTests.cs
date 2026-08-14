using Mono.Cecil;
using Mono.Cecil.Cil;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 <b>"Is this a Ghost Duel?" is <em>one</em> fact in <c>Core</c>, declared in three enumerated
/// places and read from nowhere else</b> — `05` §3.3, `11` §4.3 and `18` §4's <c>IS_PVP</c>.
/// </summary>
/// <remarks>
/// <para>
/// ═══ <b>WHY THIS RULE EXISTS</b> ═══
/// </para>
/// <para>
/// `05` §3.3 gives a duel six divergences from a PvE fight — targeting, initiative, <c>ON_KILL</c>,
/// three condition reads, the <c>IS_PVP</c> skip and `11` §4.3's duration and tie — and every one of
/// them keys on the same question. <c>CombatRules</c>' own remarks already refuse a second answer to
/// it in prose: a <c>bool AttackerFirst</c> beside <c>IsPvp</c> <em>"would be a second statement of
/// 'is this a duel'"</em>. Prose is not a rule. This is.
/// </para>
/// <para>
/// The failure it guards is specific and quiet. Two flags agree on the day they are written and
/// diverge on the day one caller sets one of them — at which point a fight is a duel for
/// <c>ON_KILL</c> and a PvE fight for initiative, produces a perfectly valid log, and is rejected by
/// `11` §6's tamper check as though the player had cheated. It is the same defect
/// <c>BattleRoster</c> was extracted to prevent one layer down, where <c>ENEMY_COUNT</c> and
/// <c>ALL_ENEMIES</c> had each been given their own copy of "the actors hostile to the holder".
/// </para>
/// <para>
/// 🔒 <b>Three declarations, and each is load-bearing rather than a copy.</b>
/// <c>CombatRules.IsPvp</c> is the fight's own bound and the only one a caller sets;
/// <c>EffectEvaluationContext.IsPvp</c> is what `18` §4's evaluator reads, and it is a value type
/// built per pass rather than a second source of truth; <c>TriggerOccurrence.IsPvp</c> is the same
/// for `18` §3's moments. `05` §3.3's rulings are spread over all three layers, and the loop copies
/// the one bound into the other two at construction. A <b>fourth</b> is not another layer — there is
/// no fourth layer — it is a divergence waiting to happen.
/// </para>
/// <para>
/// ⚠️ <b>WHAT THIS RULE CANNOT SEE, listed rather than implied.</b>
/// </para>
/// <list type="bullet">
///   <item>A flag that <b>names neither PvP nor a duel</b>. A <c>bool AttackerFirst</c> on
///   <c>CombatRules</c> is precisely the design <c>CombatRules</c>' remarks refuse and precisely the
///   one this scan misses, because the scan keys on the name. The remark is still the primary
///   mechanism for that shape; the rule closes the spellings an author reaching for "another duel
///   flag" would actually type.</item>
///   <item>A duel bit smuggled as something other than a <see cref="bool"/> — an <c>enum Mode</c>, an
///   <c>int</c>. The declaration arm below tests the type as well as the name, so such a member is
///   not reported. That is deliberate: reporting every <c>Pvp</c>-named number would fire on
///   `11` §4.3's <c>pvpMaxFightSeconds</c>, which is a duration and must live in data.</item>
///   <item>Anything outside <c>Core</c>. `30` §11.1 puts every decision there, and `05` §3.3's
///   divergences are decisions.</item>
/// </list>
/// <para>
/// 🔒 A <b>new file</b>, not an edit to M0-08's rule files or to <c>Infrastructure/Domain.cs</c>:
/// M1-12 holds the latter (steering S12).
/// </para>
/// </remarks>
public sealed class PvpDuelRuleTests
{
    /// <summary>The member name `18` §4's <c>IS_PVP</c> is spelled with, in all three declarations.</summary>
    private const string Flag = "IsPvp";

    /// <summary>
    /// 🔒 The three <b>members</b> allowed to declare it — the fight's bound, and the two
    /// per-evaluation values the loop copies it into.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Member-qualified, not type-qualified, and the difference is the rule.</b> At type
    /// granularity a genuinely wrong fourth flag is invisible whenever it lands on a type already in
    /// the list — <c>bool IsDuelInitiative</c> on <c>CombatRules</c>, <c>bool InDuel</c> on
    /// <c>EffectEvaluationContext</c> — which is the same shape the rule was written for, only spelled
    /// with a duel word in it. Qualifying by member makes <em>"not a fourth"</em> true as stated.
    /// </remarks>
    private static readonly string[] PermittedDeclarations =
    {
        "SlayIdleRepeat.Core.Rules.Combat.CombatRules.IsPvp",
        "SlayIdleRepeat.Core.Rules.Effects.EffectEvaluationContext.IsPvp",
        "SlayIdleRepeat.Core.Rules.Effects.Triggers.TriggerOccurrence.IsPvp",
    };

    /// <summary>The declaring types of <see cref="PermittedDeclarations"/>, for the reader rule.</summary>
    /// <remarks>
    /// Derived rather than listed a second time — two lists of the same three types is the defect this
    /// whole file is about, and it would be an odd one to commit inside it.
    /// </remarks>
    private static readonly string[] PermittedReadTargets =
        PermittedDeclarations
            .Select(name => name[..name.LastIndexOf('.')])
            .ToArray();

    /// <summary>
    /// Name fragments that mark a member as answering "is this a duel". Matched case-insensitively,
    /// so <c>IsPvP</c>, <c>isPvp</c> and <c>InDuel</c> are all caught.
    /// </summary>
    private static readonly string[] DuelWords = { "pvp", "duel", "ghost" };

    /// <summary>
    /// 🔒 The one PvP-named boolean in <c>Core</c> that is <b>not</b> `05` §3.3's switch, enumerated
    /// with its reason rather than filtered out by a pattern.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>FeatureFlags.PvpEnabled</c> is `14` §14's <b>kill switch</b>: whether the PvP feature is
    /// live for this build at all. `05` §3.3's <c>IsPvp</c> is whether <em>this fight</em> is a duel.
    /// They answer different questions at different layers and neither can be derived from the other
    /// — a duel simulated on a build whose kill switch is thrown is still a duel, and `11` §6's
    /// server re-run must produce the same log either way.
    /// </para>
    /// <para>
    /// 🔒 <b>Enumerated rather than pattern-excluded, and it is the more dangerous of the two.</b>
    /// The confusion this rule is really about is a <c>Rules/</c> type reaching for the kill switch to
    /// answer "is this a duel" — which would make the simulator's behaviour depend on remote config,
    /// so a flag flip mid-season would change every duel's outcome and `11` §6 would start rejecting
    /// honest logs by the thousand.
    /// <see cref="The_kill_switch_of_14_section_14_never_reaches_the_simulator"/> is that half.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// 🔒 <b>Matched at TYPE granularity, unlike <see cref="PermittedDeclarations"/>, and deliberately.</b>
    /// `14` §14 catalogues kill switches and will legitimately grow more of them; requiring each new
    /// one to be enumerated here would be friction on an unrelated task for no safety. What must not
    /// grow is `05` §3.3's switch, and that is the list qualified by member. <c>FeatureFlags</c>' own
    /// member count is separately pinned by its tests.
    /// </remarks>
    private static readonly string[] UnrelatedPvpFlags = { KillSwitchType };

    /// <summary>
    /// 🔒 `05` §3.3 / `18` §4 — every boolean member in <c>Core</c> that answers <em>"is this a Ghost
    /// Duel?"</em> is one of the three enumerated declarations. Not a fourth, and not fewer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stated as a <b>set equality</b> rather than as a subset, which is what gives it an S3 floor and
    /// an S4 expiry in one assertion. A subset rule goes silently green if all three declarations are
    /// renamed away — at which point `05` §3.3's divergences are keyed on something this suite has
    /// never heard of. An equality fails in both directions, so the enumeration cannot rot.
    /// </para>
    /// <para>
    /// The scan reads properties, fields and record positional parameters alike: a positional record
    /// parameter compiles to a property plus a backing field, and both carry the name.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_duel_flag_is_declared_in_exactly_the_three_enumerated_places()
    {
        var declared = DuelFlagDeclarations()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();

        var expected = PermittedDeclarations.Concat(UnrelatedPvpFlags)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();

        if (declared.SequenceEqual(expected, StringComparer.Ordinal))
        {
            return;
        }

        var extra = declared.Except(expected, StringComparer.Ordinal)
            .Select(t =>
                $"{t} is a boolean member naming a duel. `05` §3.3's divergences already key on " +
                "CombatRules.IsPvp; a second flag agrees on the day it is written and diverges on the " +
                "day one caller sets one of them, at which point a fight is a duel for ON_KILL and a " +
                "PvE fight for initiative and 11 §6 rejects an honest log.");

        // 🔒 The two lists fail for different reasons, so they say different things (steering S2). The
        // kill switch disappearing is not "05 §3.3's switch has moved".
        var missing = expected.Except(declared, StringComparer.Ordinal)
            .Select(t => UnrelatedPvpFlags.Contains(t, StringComparer.Ordinal)
                ? $"{t} no longer declares a PvP-named boolean. It was `14` §14's kill switch and the " +
                  "reason this rule carries an exemption at all; with it gone the exemption governs " +
                  "nothing, and The_kill_switch_of_14_section_14_never_reaches_the_simulator is " +
                  "scanning for a type that does not exist. Remove both together, or update both."
                : $"{t} is gone. Either it was renamed — in which case this rule and the reader rule " +
                  "below are now quantifying over an enumeration that matches nothing — or `05` §3.3's " +
                  "switch has moved and this list has to move with it, in the same commit.");

        ArchRule.Empty(
            extra.Concat(missing),
            "05 §3.3's 'is this a Ghost Duel' is one fact, declared in three enumerated places (18 §4).");
    }

    /// <summary>
    /// 🔒 `05` §3.3 — every read of the duel flag anywhere in <c>Core</c> resolves to one of those
    /// three declarations, so no layer answers `18` §4's question for itself.
    /// </summary>
    /// <remarks>
    /// The declaration rule alone would be satisfied by a type that recomputed duel-ness from
    /// something else and never declared a flag at all — a roster shape, a side count, an actor kind.
    /// This one is stated over the <b>reads</b>, and its floor is that there are some: a rule whose
    /// subject set is every <c>IsPvp</c> load in the assembly reports success over an assembly that
    /// has stopped loading it, which is the shape steering S3 exists for.
    /// </remarks>
    [Fact]
    public void Every_reader_of_the_duel_flag_reads_one_of_those_three()
    {
        var reads = DuelFlagReads().ToArray();

        var offenders = reads
            .Where(r => !PermittedReadTargets.Contains(r.DeclaringType, StringComparer.Ordinal))
            .Select(r =>
                $"{r.Reader} reads {r.DeclaringType}.{Flag}, which is not one of `05` §3.3's three " +
                "enumerated declarations. A fourth statement of 'is this a duel' is a divergence " +
                "waiting for its first disagreeing caller.");

        ArchRule.Empty(
            offenders,
            "05 §3.3's duel flag is read from CombatRules and the two per-evaluation values it is " +
            "copied into, and from nowhere else (18 §4).");

        if (reads.Length >= ReadSiteFloor)
        {
            return;
        }

        throw new ArchitectureRuleViolationException(
            "05 §3.3's duel flag is still read by the layers that implement the duel (23 §6).",
            new[]
            {
                $"found {reads.Length} read(s) of '{Flag}' in Core, floor is {ReadSiteFloor}. `05` §3.3 " +
                "names six divergences and `11` §4.3 two more; if almost nothing reads the flag, the " +
                "duel rules are keyed on something else and the rule above is passing over an empty " +
                "set. If a reader was legitimately removed, lower the floor in the same commit and say " +
                "why in the message.",
            });
    }

    /// <summary>
    /// The number of <c>IsPvp</c> reads in <c>Core</c> when this rule landed, less headroom for
    /// ordinary refactoring. See <see cref="Every_reader_of_the_duel_flag_reads_one_of_those_three"/>.
    /// </summary>
    private const int ReadSiteFloor = 8;

    /// <summary>
    /// 🔒 `14` §14 / `11` §6 — the PvP <b>kill switch</b> never reaches <c>Rules/</c>. Whether a fight
    /// is a duel is `05` §3.3's <c>CombatRules.IsPvp</c>; whether the feature is live is remote
    /// config, and the simulator must not be able to tell.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The consequence if it could is severe and delayed. `11` §6 has the server re-run the identical
    /// deterministic sim and compare <c>LogHash</c>; `14` §2.4 has the client run the same assembly.
    /// A simulator that read remote config would produce a different log on either side of a flag
    /// flip — so an operator turning PvP off and on again would invalidate every duel in flight, and
    /// the mismatch reports as <em>players cheating</em> rather than as an ops action. The same
    /// argument is why `30` §11.1 puts every decision in <c>Core</c> and why
    /// <c>IsolationTests</c> keeps entitlements out of the rules.
    /// </para>
    /// <para>
    /// Stated over the whole of <c>Rules/</c> rather than only <c>Rules.Combat/</c>: `29`'s power
    /// model grades the simulator's own output, so a kill switch reaching it would move
    /// <c>PlayerPower</c> as well and `05` §9's <c>A10</c> assertion would drift with remote config.
    /// The floor beneath it is the scanned type count — a rule stated over a namespace filter is one
    /// rename from vacuous (steering S3).
    /// </para>
    /// </remarks>
    [Fact]
    public void The_kill_switch_of_14_section_14_never_reaches_the_simulator()
    {
        var module = ProductionAssemblies.Module(ProductionAssemblies.CoreName);

        // Compiler-generated types filtered out for the reason IntraRulesLayeringRuleTests and
        // Every_Core_type_lives_under_a_documented_namespace both do it: closure display classes and
        // iterator state machines would inflate the floor below with types nobody wrote.
        var rules = Il.TypesUnder(module, Domain.RulesNamespace)
            .Where(t => !Domain.IsCompilerGenerated(t))
            .ToArray();

        var offenders = rules
            .Where(t => Il.ReferencedTypeNames(t).Contains(KillSwitchType, StringComparer.Ordinal))
            .Select(t =>
                $"{t.FullName} names {KillSwitchType}. `14` §14's kill switch says whether the PvP " +
                "FEATURE is live; `05` §3.3's CombatRules.IsPvp says whether THIS FIGHT is a duel. A " +
                "simulator that could read the first would produce a different log on either side of " +
                "a flag flip, and `11` §6 would report an ops action as a thousand players cheating.");

        ArchRule.Empty(
            offenders,
            "14 §14's PvP kill switch is unreachable from Core's Rules — the duel is decided by " +
            "05 §3.3's CombatRules, never by remote config (11 §6).");

        if (rules.Length >= RulesTypeFloor)
        {
            return;
        }

        throw new ArchitectureRuleViolationException(
            "The rule above still quantifies over the simulator (23 §6).",
            new[]
            {
                $"found {rules.Length} type(s) under {Domain.RulesNamespace}, floor is {RulesTypeFloor}. " +
                "An empty or shrunken set means Rules/ has been renamed or relocated and this rule is " +
                "reporting success over nothing.",
            });
    }

    /// <summary>`14` §14's kill-switch holder — the type `05`'s simulator must never name.</summary>
    private const string KillSwitchType = "SlayIdleRepeat.Core.FeatureFlags";

    /// <summary>
    /// A floor well under the type count of <c>Core.Rules</c> on the commit this rule landed, so
    /// ordinary refactoring is not a test edit and a rename is still a failure.
    /// </summary>
    private const int RulesTypeFloor = 50;

    /// <summary>
    /// Every boolean member in <c>Core</c> whose name names a duel, as
    /// <c>Namespace.Type.Member</c> — or as <c>Namespace.Type</c> for the `14` §14 kill-switch holder,
    /// which is matched at type granularity (<see cref="UnrelatedPvpFlags"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Properties and fields both, because a positional record parameter compiles to one of each — and
    /// the backing field is <b>compiler-generated</b>, so it is excluded here and the property is what
    /// the rule reports. Without that exclusion every record member would be counted twice, under two
    /// spellings, and the second would never match an enumerated name.
    /// </para>
    /// <para>
    /// 🔴 Compiler-generated <em>types</em> are excluded for the reason this repository has hit three
    /// times: a lambda over a local named <c>isPvp</c> hoists it onto a <c>&lt;&gt;c__DisplayClass</c>
    /// field of that name, which would be reported as a rogue fourth declaration in a file nobody
    /// wrote.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> DuelFlagDeclarations()
    {
        var module = ProductionAssemblies.Module(ProductionAssemblies.CoreName);

        foreach (var type in Il.AllTypes(module).Where(t => !Domain.IsCompilerGenerated(t)))
        {
            var members = type.Properties
                .Where(p => NamesADuel(p.Name) && IsBoolean(p.PropertyType))
                .Select(p => p.Name)
                .Concat(type.Fields
                    .Where(f => !Domain.IsCompilerGenerated(f) &&
                                NamesADuel(f.Name) &&
                                IsBoolean(f.FieldType))
                    .Select(f => f.Name));

            foreach (var member in members)
            {
                yield return UnrelatedPvpFlags.Contains(type.FullName, StringComparer.Ordinal)
                    ? type.FullName
                    : $"{type.FullName}.{member}";
            }
        }
    }

    /// <summary>Every read of a duel flag in <c>Core</c>, as (reader, declaring type).</summary>
    /// <remarks>
    /// Both spellings are read: <c>callvirt get_IsPvp</c> for a property, and <c>ldfld IsPvp</c> for
    /// the compiler-generated backing field a record's own members load directly. Reads made by the
    /// declaring type's generated members — <c>Equals</c>, <c>GetHashCode</c>, <c>ToString</c>,
    /// <c>&lt;Clone&gt;$</c> — are excluded: a record synthesises those over every one of its
    /// properties, so counting them would let the floor be satisfied by a type nothing uses.
    /// </remarks>
    private static IEnumerable<(string Reader, string DeclaringType)> DuelFlagReads()
    {
        var module = ProductionAssemblies.Module(ProductionAssemblies.CoreName);

        foreach (var method in Il.MethodsWithBodies(module))
        {
            foreach (var instruction in Il.Instructions(method))
            {
                var declaring = ReadOfADuelFlag(instruction);

                if (declaring is null ||
                    declaring.Equals(method.DeclaringType.FullName, StringComparison.Ordinal))
                {
                    continue;
                }

                yield return (Il.Describe(method), declaring);
            }
        }
    }

    /// <summary>The declaring type of the duel flag this instruction loads, or <c>null</c>.</summary>
    private static string? ReadOfADuelFlag(Instruction instruction) => instruction.Operand switch
    {
        MethodReference getter
            when instruction.OpCode.Code is Code.Call or Code.Callvirt &&
                 getter.Name.Equals("get_" + Flag, StringComparison.Ordinal)
            => getter.DeclaringType.FullName,

        FieldReference field
            when instruction.OpCode.Code is Code.Ldfld or Code.Ldflda or Code.Ldsfld &&
                 NamesTheFlag(field.Name)
            => field.DeclaringType.FullName,

        _ => null,
    };

    /// <summary>
    /// The compiler spells a positional record's backing field <c>&lt;IsPvp&gt;k__BackingField</c>,
    /// so the field name is matched by containment rather than by equality.
    /// </summary>
    private static bool NamesTheFlag(string name) =>
        name.Contains(Flag, StringComparison.Ordinal);

    private static bool NamesADuel(string name) =>
        DuelWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase));

    /// <summary><see cref="bool"/> or <see cref="Nullable{T}"/> of it.</summary>
    private static bool IsBoolean(TypeReference type) =>
        type.FullName.Equals("System.Boolean", StringComparison.Ordinal) ||
        type.FullName.Equals("System.Nullable`1<System.Boolean>", StringComparison.Ordinal);
}
