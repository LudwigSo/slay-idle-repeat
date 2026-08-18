using Mono.Cecil;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 `30` §11.2 / `05` §1.1 — the containment of M7-06b's hero surface: <b>the BUILD and the FIGHT
/// are exported, the DERIVATION is not.</b>
/// </summary>
/// <remarks>
/// <para>
/// The hero's stat block could not be built at any accessibility until M4-16 authored the
/// derivation, and could not be reached from outside <c>Core</c> until this widening — so a run
/// entering a battle could never produce the fight it was standing in, and the loop stopped at
/// <em>fight</em>. The widening that fixed it is two names: <c>HeroBuild</c>, whose consumer is the
/// client's Hero and Inventory screens, and <c>RunBattle</c>, whose consumer is the Application
/// layer's <c>SimulatePendingBattleUseCase</c>.
/// </para>
/// <para>
/// ⚠️ <b>Nothing in the existing suite would notice that narrowness being lost.</b>
/// <c>Handlers_and_Rules_are_internal</c> is satisfied the moment a name lands in
/// <c>Domain.PublicRuleTypes</c>, and <c>PublicRuleTypeFloorTests</c> asks only whether the listed
/// names resolve and are public. A public <c>StatAggregation</c> "so the client can preview a
/// build", or a <c>Simulate(BattlePlan)</c> overload "so a screen can stage a fight", would pass
/// every rule in this repository and would hand the outside world the machinery that decides what a
/// hero is and what a fight does.
/// </para>
/// <para>
/// ⚠️ <b>C# closes one half of this on its own, and these rules do not take credit for it.</b> A
/// public member naming an <c>internal</c> type does not compile, so while the derivation stays
/// internal no member CAN name it and the arms below mentioning <c>StatAggregation</c> or
/// <c>BattlePlan</c> are unreachable. They are the other half of a pair: they become reachable
/// exactly when <see cref="The_hero_derivations_machinery_stays_internal"/> has already gone red.
/// What the closure rule catches on its own is a member naming an ALREADY-PUBLIC <c>Core</c> type
/// outside the permitted set — a <c>DeterministicRng</c> parameter, a <c>Run</c> or <c>Player</c>
/// aggregate — which compiles fine and which nothing else in the suite would see.
/// </para>
/// <para>
/// 🔒 A <b>new file</b>, on <c>BoardViewSurfaceRuleTests</c>' precedent and for its reason: the names
/// below are this rule's own subject set rather than <c>Infrastructure/Domain.cs</c>'s, and every one
/// of them is floored BY NAME (steering S3), so a rename cannot empty the set.
/// </para>
/// </remarks>
public sealed class HeroBattleSurfaceRuleTests
{
    /// <summary>The namespaces the two exported types live in.</summary>
    private const string StatsNamespace = "SlayIdleRepeat.Core.Rules.Stats";

    /// <inheritdoc cref="StatsNamespace"/>
    private const string CombatNamespace = "SlayIdleRepeat.Core.Rules.Combat";

    /// <summary>
    /// The derivation's machinery: the types that DECIDE what a hero's numbers are and what a fight
    /// does with them. Every one must stay <c>internal</c>.
    /// </summary>
    /// <remarks>
    /// Named individually rather than "everything under <c>Rules/Stats/</c> except the exported
    /// ones", because that phrasing is satisfied by an empty directory. Each name is floored on its
    /// own, so a rename is a failure rather than a silent shrink of the subject set (steering S3).
    /// </remarks>
    private static readonly (string Name, string Reason)[] Machinery =
    {
        ("StatAggregation", "05 §1.1's ten-step order itself — it DECIDES what every stat in the game is"),
        ("AggregatedStats", "the aggregate's own record, carrying the battle pipeline's heal ceiling; the build publishes the three facts a reader needs instead"),
        ("StatAggregationSeams", "the aggregation's refusal policy; a caller choosing it chooses which faults are silent"),
        ("HeroBaseCurve", "05 §2's base curve — the block every build starts from"),
        ("StatCaps", "the ceilings applied after aggregation; a caller holding them can aggregate uncapped"),
        ("CombatCaps", "the reader that builds those ceilings out of content"),
        ("GearStatDerivation", "08 §3's item stat generation, which is what an item IS"),
        ("GearCatalogue", "the twenty-four base items and their slot coefficients"),
        ("LoadoutRules", "what may be worn where; the build resolves a loadout, it does not let a caller redefine one"),
        ("SetBonusCatalogue", "08 §3.2's set breakpoints"),
        ("EffectSourceSet", "the collection step — a caller assembling one composes a hero out of effects nothing authored"),
        ("GearEffectSource", "the gear half of that collection"),
        ("EncounterFight", "the non-boss composition; CombatSimulator.SimulateEncounter is the door"),
        ("BossFight", "the boss composition; CombatSimulator.SimulateBossFight is the door"),
        ("BattlePlan", "the simulator's internal face — a caller holding one can author a roster, its caps and its seams"),
        ("ActorPlan", "one actor of that roster, including the BaseStats slot this task exists to fill correctly"),
        ("BattleSimulation", "the tick loop"),
    };

    /// <summary>
    /// The two names M7-06b exports, with the namespace each must live under.
    /// </summary>
    /// <remarks>
    /// Restated here rather than filtered out of <c>Domain.PublicRuleTypes</c>: that list is the
    /// exemption arm of another rule and holds the combat simulator's and the board view's closures
    /// too, so deriving this set from it would make these rules quietly follow whatever anybody adds
    /// there — the drift they exist to catch.
    /// </remarks>
    private static readonly (string Name, string Namespace)[] HeroSurface =
    {
        ("HeroBuild", StatsNamespace),
        ("RunBattle", CombatNamespace),
    };

    /// <summary>
    /// The <c>Core</c> types a public member of the two may name: everything that was already public
    /// before this widening and that the two genuinely take or hand back.
    /// </summary>
    /// <remarks>
    /// Enumerated rather than "anything already public", which is the argument that would let the
    /// aggregates in. <c>Player</c> and <c>Run</c> are public types with public getters (`30` §11.2)
    /// and are deliberately absent: the public doors take ROWS, so an outside caller never needs to
    /// hold an aggregate — and a public overload taking one would be an overload no outside assembly
    /// could call, since neither aggregate has a public factory returning itself.
    /// </remarks>
    private static readonly string[] AlreadyPublicInputs =
    {
        "SlayIdleRepeat.Core.Content.ContentSnapshot",
        "SlayIdleRepeat.Core.Content.Effects.EffectDefinition",
        "SlayIdleRepeat.Core.Model.Gear.GearInstance",
        "SlayIdleRepeat.Core.Model.Snapshots.PlayerSnapshot",
        "SlayIdleRepeat.Core.Model.Snapshots.RunSnapshot",
        StatsNamespace + ".ActorStats",
        CombatNamespace + ".SimulationResult",
    };

    /// <summary>
    /// Types a public hero or battle member may never name, whatever <see cref="AlreadyPublicInputs"/>
    /// is later widened to.
    /// </summary>
    private static readonly (string FullName, string Reason)[] NeverInTheHeroSurface =
    {
        ("SlayIdleRepeat.Core.Rng.DeterministicRng",
            "14 §8.1's draw stream. It is already public — this is not about its accessibility but " +
            "about the battle surface not HANDING one out: a caller holding a fight's stream can " +
            "steer the draws the fight is decided by, which is the whole of 14 §9's anti-cheat."),
        ("SlayIdleRepeat.Core.Rng.RngStreams",
            "the stream registry. A battle member naming it would offer a caller the choice of which " +
            "stream a fight comes off, which the seed derivation fixes to one."),
        ("SlayIdleRepeat.Core.Model.Player",
            "the player aggregate. The public doors take rows; an aggregate parameter is one no " +
            "outside assembly can construct, so it would be a public API in name only."),
        ("SlayIdleRepeat.Core.Model.Run",
            "the run aggregate, for the same reason — and a public member handing one BACK would " +
            "hand out an object whose internal mutators are one InternalsVisibleTo away."),
    };

    /// <summary>
    /// 🔒 `30` §11.2 — the hero derivation's machinery stays <c>internal</c>. M7-06b's widening
    /// exports the <b>build</b> and the <b>fight</b>, not the rules that decide either.
    /// </summary>
    /// <remarks>
    /// Floored by NAME rather than by count (steering S3): a name that resolves to nothing is an
    /// offender in its own right, so renaming <c>StatAggregation</c> cannot quietly empty this rule's
    /// subject set and leave the aggregation order public and unwatched.
    /// </remarks>
    [Fact]
    public void The_hero_derivations_machinery_stays_internal()
    {
        var offenders = new List<string>();

        foreach (var (name, reason) in Machinery)
        {
            var matches = Domain.CoreTypes
                .Where(t => t.Name.Equals(name, StringComparison.Ordinal))
                .ToArray();

            if (matches.Length == 0)
            {
                offenders.Add(
                    $"'{name}' resolves to no Core type. This rule is stated over it by identity — {reason} " +
                    "— so a rename empties its share of the subject set and the type it names could go " +
                    "public unwatched. Rename the entry in the same commit, or delete it and say why the " +
                    "type no longer exists.");

                continue;
            }

            offenders.AddRange(
                matches
                    .Where(t => t.IsPublic)
                    .Select(t =>
                        $"{t.FullName} is public. It is derivation MACHINERY — {reason} — and the point of " +
                        "M7-06b's widening of 30 §11.2's public surface was to export the hero's BUILD and " +
                        "the run's FIGHT, so a screen can show a stat block it cannot recompute differently " +
                        "and a use case can produce a LogHash it cannot forge. If this type genuinely has " +
                        "to leave Core, that is a decision for a kickoff, not a keyword."));
        }

        ArchRule.Empty(
            offenders,
            "30 §11.2: the hero derivation's machinery stays internal — M7-06b exports the build and the " +
            "fight, not the rules that decide either (05 §1.1).");
    }

    /// <summary>
    /// 🔒 `30` §11.2 — the public hero surface's signature closure names nothing else: every
    /// <c>Core</c> type reached from a public member of the two is one that was already public and
    /// is enumerated here.
    /// </summary>
    /// <remarks>
    /// This is the rule that fires the day somebody adds an overload taking the <c>Player</c>
    /// aggregate, or a member handing back the internal aggregate under a public wrapper. It is
    /// <em>not</em> what stops a public <c>HeroBuild.Aggregated</c> — the compiler does, per the
    /// class remarks — and saying otherwise would count the language's work as this rule's.
    /// </remarks>
    [Fact]
    public void The_public_hero_surface_names_nothing_beyond_its_enumerated_inputs()
    {
        var offenders = new List<string>();
        var surface = ResolveSurface(offenders);
        var permitted = HeroSurface
            .Select(entry => entry.Namespace + "." + entry.Name)
            .Concat(AlreadyPublicInputs)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var type in surface)
        {
            foreach (var (member, reference) in PublicSignatureTypes(type))
            {
                var resolved = Domain.CoreTypes.FirstOrDefault(
                    t => t.FullName.Equals(reference.FullName, StringComparison.Ordinal));

                if (resolved is null || permitted.Contains(resolved.FullName))
                {
                    continue;
                }

                offenders.Add(
                    $"{type.FullName}.{member} names {resolved.FullName}, which is neither one of M7-06b's " +
                    "two exported types nor one of the already-public inputs they were widened over. " +
                    "30 §11.2's public surface is ENUMERATED (R16): a member that reaches a further Core " +
                    "type widens what the outside world can hold without anybody deciding to widen it. " +
                    "Project it into a shape the surface already names, or make the member internal.");
            }
        }

        ArchRule.Empty(
            offenders,
            "30 §11.2: the hero build and the run's fight name only their own two types plus the " +
            "already-public rows, stats and effects they were widened over.");
    }

    /// <summary>
    /// 🔒 `30` §11.2 / `14` §8.1 / `14` §9 — the public battle surface exposes no draw and no
    /// aggregate: no public member of the two names a <c>DeterministicRng</c>, the stream registry,
    /// or either aggregate.
    /// </summary>
    /// <remarks>
    /// ⚠️ All four banned types are <b>already public</b>, so this is not a claim about their
    /// accessibility — it is a claim about this surface not handing one out. Stated separately from
    /// the closure rule and not folded into it: that rule's permitted set is a list somebody can
    /// edit, and "it was already public anyway" is exactly the argument that would get one added to
    /// it. This list is not an allowlist and has no such escape hatch.
    /// </remarks>
    [Fact]
    public void The_public_battle_surface_hands_out_no_draw_stream_and_no_aggregate()
    {
        var offenders = new List<string>();
        var surface = ResolveSurface(offenders);

        foreach (var type in surface)
        {
            foreach (var (member, reference) in PublicSignatureTypes(type))
            {
                var hit = NeverInTheHeroSurface.FirstOrDefault(
                    b => b.FullName.Equals(reference.FullName, StringComparison.Ordinal));

                if (hit.FullName is not null)
                {
                    offenders.Add(
                        $"{type.FullName}.{member} names {reference.FullName} — {hit.Reason} The fight a run " +
                        "is standing in is a function of its committed row and nothing else; a member " +
                        "that takes the draw or the aggregate makes it something a caller can steer.");
                }
            }
        }

        // 🔒 The bans have to be findable at all. Every one of the four names a type that exists and
        // is public today, so a name misspelled into something unmatchable would leave this rule
        // green over a surface it never checked.
        offenders.AddRange(
            NeverInTheHeroSurface
                .Where(ban => Domain.CoreTypes.All(
                    t => !t.FullName.Equals(ban.FullName, StringComparison.Ordinal)))
                .Select(ban =>
                    $"'{ban.FullName}' resolves to no Core type, so banning it from this surface bans " +
                    "nothing. Either the type was renamed — rename the entry in the same commit — or the " +
                    "entry was never spelled correctly and this rule has been passing for free."));

        ArchRule.Empty(
            offenders,
            "30 §11.2 / 14 §8.1: the hero build and the run's fight are read-only compositions — they " +
            "hand out no draw stream and no aggregate.");
    }

    /// <summary>
    /// The two exported types, with the S3 floor that each resolves to a public <c>Core</c> type
    /// under the namespace it belongs to.
    /// </summary>
    /// <remarks>
    /// By NAMED MEMBER rather than by count: the two rules above are "no member of set S does X", so
    /// an empty S passes forever — and S is empty exactly when the hero surface has been renamed away
    /// or never landed, which is the state this file must not report success over.
    /// </remarks>
    private static IReadOnlyList<TypeDefinition> ResolveSurface(List<string> offenders)
    {
        var resolved = new List<TypeDefinition>(HeroSurface.Length);

        foreach (var (name, ns) in HeroSurface)
        {
            var type = Domain.CoreTypes.FirstOrDefault(
                t => t.Name.Equals(name, StringComparison.Ordinal) && Il.IsUnder(Il.NamespaceOf(t), ns));

            if (type is null)
            {
                offenders.Add(
                    $"'{name}' resolves to no Core type under {ns}. M7-06b's public hero surface is these " +
                    "two and nothing else, so a missing one leaves every rule in this file quantifying " +
                    "over less than it was written against (steering S3).");

                continue;
            }

            if (!type.IsPublic)
            {
                offenders.Add(
                    $"{type.FullName} is in the exported hero surface but is not public. Its named consumers " +
                    "are separate assemblies with no InternalsVisibleTo grant, so an internal member of the " +
                    "two is a surface nothing outside Core can reach at all — and every rule below then " +
                    "governs a type nobody can call.");

                continue;
            }

            resolved.Add(type);
        }

        return resolved;
    }

    /// <summary>
    /// Every type named by a public member's signature: property types, public field types, and the
    /// return and parameter types of every public method and constructor.
    /// </summary>
    /// <remarks>
    /// ⚠️ Properties are walked as PROPERTIES rather than through their accessors, and that is
    /// load-bearing. An auto-property's <c>get</c> carries <c>[CompilerGenerated]</c>, so the
    /// method-filter this suite normally uses would skip every property and leave the rules above
    /// quantifying over the methods alone.
    /// </remarks>
    private static IEnumerable<(string Member, TypeReference Reference)> PublicSignatureTypes(TypeDefinition type)
    {
        foreach (var property in type.Properties.Where(p => p.GetMethod is { IsPublic: true }))
        {
            foreach (var reference in Il.Flatten(property.PropertyType))
            {
                yield return (property.Name, reference);
            }
        }

        foreach (var field in type.Fields.Where(f => f.IsPublic && !Domain.IsCompilerGenerated(f)))
        {
            foreach (var reference in Il.Flatten(field.FieldType))
            {
                yield return (field.Name, reference);
            }
        }

        foreach (var method in type.Methods.Where(m => m.IsPublic && !Domain.IsCompilerGenerated(m)))
        {
            foreach (var reference in Il.SignatureTypes(method).SelectMany(Il.Flatten))
            {
                yield return (method.Name, reference);
            }
        }
    }
}
