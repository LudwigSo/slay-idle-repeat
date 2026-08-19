using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 `05` §5 — the twelve statuses are a catalogue, and no status is a special case in code.
/// </summary>
/// <remarks>
/// <para>
/// `18`'s headnote is explicit that there is <em>"no per-perk, per-talent or per-boss code"</em>, and
/// a status catalogue erodes the same way an elite-identity table does: the first
/// <c>if (statusId == "BURN")</c> in a boss engine or a duel path puts a second, invisible copy of
/// `05` §5 into a place nobody looks. The cost is not hypothetical — five of `05` §5's twelve carry
/// numbers that only <c>content/statuses.json</c> states, and a hard-coded branch is how one of them
/// silently stops being read.
/// </para>
/// <para>
/// 🔒 <b>A closed list of types is allowed to spell a status id, and each is narrowed to the ids its
/// own document rules on by
/// <see cref="Each_exempted_type_names_only_the_one_status_its_document_rules_on"/>.</b>
/// 🔴 <c>StatusIds</c> is where the two ids code names — <c>STUN</c> and <c>FREEZE</c> — are
/// <em>declared</em>; <c>StatusTimeline</c> and <c>BossBuiltIns</c> alias it rather than each holding
/// their own constant, which is what they did through M2.
/// <c>StatusLogId</c> is `05` §7's <c>dataId</c> map, which has to name all twelve because the
/// ordinals are inside <c>LogHash</c> and cannot be derived from a file that may be reordered.
/// <c>StatusTimeline</c> names exactly one — <c>STUN</c> — because `05` §5 gives that status a rule
/// no other status has (<em>"Max 1.5 s per application, with a 3 s immunity window after"</em>,
/// followed by <em>"stun immunity is mandatory"</em>). <c>EnemyCatalogue</c> names exactly one —
/// <c>SUNDER</c> — because `05` §6.1a fixes <c>WARDEN</c>'s on-hit token as that word. Each special
/// case is a document's rather than an implementer's; anything else naming one is the erosion this
/// rule exists to catch.
/// </para>
/// <para>
/// The scan is over <c>ldstr</c> operands rather than over the source text, so a comment or an XML
/// remark naming a status — and the remarks in this very file do — is not a hit, while a switch arm
/// or an equality test is.
/// </para>
/// <para>
/// ⚠️ <b>Test assemblies are deliberately out of scope.</b> <c>StatusCatalogueTests</c> asserts all
/// twelve ids by name, which is exactly what a transcription test is for.
/// </para>
/// <para>
/// 🔒 A <b>new file</b>, not an edit to M0-08's rule files or to <c>Infrastructure/Domain.cs</c>:
/// M1-12 is in flight on exactly those, and patching a component another agent is scheduled to touch
/// is how M0 broke thirty tests (steering S12). The namespace constant is therefore declared here,
/// and <see cref="The_namespace_this_rule_governs_is_the_one_under_Rules_Combat"/> pins it against
/// <c>Domain</c>'s so the restatement cannot go stale.
/// </para>
/// </remarks>
public sealed class StatusCatalogueRuleTests
{
    /// <summary>`05` §5's home — restated here rather than added to <c>Domain</c>. See the remarks.</summary>
    internal const string StatusNamespace = "SlayIdleRepeat.Core.Rules.Combat.Status";

    /// <summary>
    /// 🔒 `05` §5's twelve ids, transcribed. The subject of the scan below.
    /// </summary>
    /// <remarks>
    /// ⚠️ A <see cref="List{T}"/> initialiser, not <c>[ … ]</c> and not <c>new[] { … }</c> — the
    /// global-namespace synthesis trap <c>CombatCaps.CappedStats</c> records. It applies to this
    /// assembly too.
    /// </remarks>
    private static readonly IReadOnlyList<string> StatusIds = new List<string>
    {
        "BURN", "POISON", "BLEED", "FREEZE", "STUN", "WEAKEN",
        "SUNDER", "SPORE", "RAGE", "WARD", "HASTE", "REGEN",
    };

    /// <summary>
    /// The three types the documents give a reason to name a status id. See the class remarks.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The third entry was not planned — the rule found it on its first run, against code that
    /// was already there.</b> <c>EnemyCatalogue.ParseOnHit</c> reads
    /// <c>content/enemies/enemies.json</c>'s per-archetype <c>onHit</c> token, whose vocabulary is
    /// <c>SUNDER</c> / <c>BIOME_STATUS</c> / <c>NONE</c>. That is `05` §6.1a's own three-token
    /// archetype vocabulary, which happens to spell one of its tokens with a status's name because
    /// §6.1a fixes <em>"wherever a <c>WARDEN</c> appears … its on-hit debuff"</em> as <c>SUNDER</c>
    /// — a documented fact about the archetype, not a branch on a status. Exempted deliberately, with
    /// the reason, rather than by loosening the rule; and it is the first evidence that the rule is
    /// not vacuous, because it fired before anything was planted for it.
    /// </remarks>
    /// <remarks>
    /// 🔴 <b>The fourth entry is M2-12's, and it was added only after the catalogue route was looked
    /// for and found not to exist.</b> `17` §1 states a rule about two named statuses —
    /// <em>"Bosses are immune to <c>STUN</c> and <c>FREEZE</c> in phase 3"</em> — and `17` §11 makes
    /// it universal: <em>"implemented once, applied to all bosses"</em>. So <c>BossBuiltIns</c>
    /// authors two <c>IMMUNE_STATUS</c> effects in code, and their <c>statusId</c> is the pair `17`
    /// §1 fixes.
    /// <para>
    /// The three alternatives were each worse, and each is recorded so nobody re-derives them.
    /// <b>(a) Derive the pair from the catalogue</b> — the only property that separates
    /// <c>STUN</c> and <c>FREEZE</c> from `05` §5's other three <c>DEBUFF</c> rows is
    /// <em>"basis is <c>NONE</c> or the stat is <c>ASPD</c>"</em>, which is a taxonomy no document
    /// states and which a thirteenth status would silently join (`16` R6). <b>(b) Author the pair as
    /// data</b> — `05` §5's table is the catalogue's file and a <c>bossPhase3Immune</c> column there
    /// would be `17` §1's rule living inside `05` §5's document; the boss content directory that
    /// <em>would</em> be its home is M2-13's and is empty today. <b>(c) Take the ids from the
    /// caller</b> — that removes `17` §1's fact from production entirely, which is the hole S6
    /// forbids rather than the branch this rule forbids.
    /// </para>
    /// <para>
    /// 🔒 What is left is exactly <c>EnemyCatalogue</c>'s shape: <b>a document fixing named statuses
    /// for a named feature</b>, not an implementer branching on one. It is admitted the same way and
    /// narrowed the same way — see
    /// <see cref="Each_exempted_type_names_only_the_one_status_its_document_rules_on"/>, which pins
    /// it to those two ids and nothing else.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// 🔴 <b>The fifth entry is M2 review's, and it is the one place the two code-named ids live.</b>
    /// <c>StatusTimeline</c> and <c>BossBuiltIns</c> each declared their own <c>const</c> spelling
    /// <c>"STUN"</c>, each documented as the single naming — same assembly, same namespace root, no
    /// visibility barrier. <c>StatusIds</c> now holds <c>STUN</c> and <c>FREEZE</c> and both of those
    /// types alias it. It is admitted for the reason they are: `05` §5 gives <c>STUN</c> a rule of
    /// its own and `17` §1 names the phase-3 immunity pair, so the ids are documents' rather than an
    /// implementer's.
    /// <para>
    /// ⚠️ It is <b>not</b> a transcription of the twelve, deliberately — it names only the two a
    /// sentence fixes by name, so the rule above still catches the thirteenth branch. And because
    /// C# inlines a <c>const</c> at its use site, aliasing changes no <c>ldstr</c>: both consumers
    /// still appear to the scan below naming exactly what they named before, and
    /// <see cref="Each_exempted_type_names_only_the_one_status_its_document_rules_on"/> still narrows
    /// them. <c>StatusIds</c> itself has no method bodies and so emits none.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyList<string> Permitted = new List<string>
    {
        StatusNamespace + ".StatusIds",
        StatusNamespace + ".StatusLogId",
        StatusNamespace + ".StatusTimeline",
        Domain.CombatRulesNamespace + ".Enemies.EnemyCatalogue",
        Domain.CombatRulesNamespace + ".Bosses.BossBuiltIns",

        // ⚠️ Admitted for a COLLISION, not for a status. The perk catalogue's category tokens
        // include BLEED and POISON, which are spelled exactly like two status ids and are not them:
        // the parser is mapping a perk's category, and the elements are named after the ailments
        // they build around. The scan compares string literals and cannot tell the two vocabularies
        // apart, so the exemption is narrowed to those two ids below — a THIRD status id appearing
        // in this type is still an offender, which is the only part of the rule that could catch a
        // real per-status branch creeping into a content reader.
        Domain.ContentNamespace + ".Perks.PerkCatalogue",
    };

    /// <summary>
    /// 🔒 `05` §5 — no type in <b>Core</b> outside the catalogue names one of the twelve statuses.
    /// </summary>
    [Fact]
    public void No_status_id_is_named_in_code_outside_the_catalogue()
    {
        var offenders = new List<string>();

        foreach (var type in Domain.CoreTypes.Where(t => !Domain.IsCompilerGenerated(t)))
        {
            var owner = Owner(type);
            if (Permitted.Contains(owner, StringComparer.Ordinal))
            {
                continue;
            }

            foreach (var method in Il.AllMethods(type))
            {
                foreach (var instruction in Il.Instructions(method))
                {
                    if (instruction.OpCode != OpCodes.Ldstr ||
                        instruction.Operand is not string literal ||
                        !StatusIds.Contains(literal, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    offenders.Add(
                        $"{Il.Describe(method)} names the status '{literal}' — 05 §5's table is the " +
                        "catalogue and 18's headnote forbids per-content code. Read it through " +
                        "StatusCatalogue, or say here why this status is a documented special case " +
                        $"as STUN is. Permitted: {string.Join(", ", Permitted)}.");
                }
            }
        }

        ArchRule.Empty(
            offenders,
            "05 §5: the twelve statuses are data. No type in Core outside the catalogue names one.");
    }

    /// <summary>
    /// 🔒 `05` §5, `05` §6.1a and `17` §1 — each exempted type names <b>only the statuses</b> its own
    /// document rules on, and no others.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The second probe, and the rule above cannot make this claim.</b> That rule allows three
    /// types to name a status; on its own it would let any of them grow a twelve-arm switch with
    /// nothing going red — the exact shape `18`'s headnote forbids, hidden inside the types that are
    /// allowed to name anything at all. Review found the first version narrowed only
    /// <c>StatusTimeline</c> while <c>EnemyCatalogue</c>'s exemption was argued for one id
    /// (<c>SUNDER</c>) and granted for twelve. Each exemption is now narrowed to the id its own
    /// document fixes: `05` §5 gives <c>STUN</c> a rule of its own (the 1.5 s cap and the mandatory
    /// immunity window), and `05` §6.1a fixes <c>WARDEN</c>'s on-hit token as <c>SUNDER</c>.
    /// </remarks>
    [Fact]
    public void Each_exempted_type_names_only_the_one_status_its_document_rules_on()
    {
        // The three exemptions the documents argue for, each with exactly the ids it argues for.
        // 🔒 A SET rather than a single id, because `17` §1's sentence names two — and the set is
        //    still closed, so a third id inside any of these types is an offender.
        var narrowed = new List<(string Owner, IReadOnlyList<string> Permitted)>
        {
            (StatusNamespace + ".StatusTimeline", new List<string> { "STUN" }),
            (Domain.CombatRulesNamespace + ".Enemies.EnemyCatalogue", new List<string> { "SUNDER" }),
            (Domain.CombatRulesNamespace + ".Bosses.BossBuiltIns", new List<string> { "FREEZE", "STUN" }),
            (Domain.ContentNamespace + ".Perks.PerkCatalogue", new List<string> { "BLEED", "POISON" }),
        };

        var offenders = new List<string>();

        foreach (var (owner, permitted) in narrowed)
        {
            var type = Domain.CoreTypes.SingleOrDefault(
                t => Owner(t).Equals(owner, StringComparison.Ordinal));

            if (type is null)
            {
                offenders.Add(
                    $"'{owner}' is exempted from the status-id scan and does not exist — an " +
                    "exemption for a type that is gone can never expire (steering S4).");
                continue;
            }

            var named = Il.AllMethods(type)
                .SelectMany(Il.Instructions)
                .Where(i => i.OpCode == OpCodes.Ldstr && i.Operand is string s &&
                            StatusIds.Contains(s, StringComparer.Ordinal))
                .Select(i => (string)i.Operand!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();

            var beyond = named.Where(id => !permitted.Contains(id, StringComparer.Ordinal)).ToArray();

            if (beyond.Length == 0)
            {
                continue;
            }

            offenders.Add(
                $"{owner} is exempted for {string.Join(" and ", permitted.Select(p => $"'{p}'"))} " +
                $"and also names: {string.Join(", ", beyond)}. 05 §5 gives STUN a rule of its own " +
                "(the 1.5 s cap and the mandatory immunity window), 05 §6.1a fixes WARDEN's on-hit " +
                "token as SUNDER, 17 §1 makes bosses immune to STUN and FREEZE in phase 3, and " +
                "PerkCatalogue's two are perk-CATEGORY tokens that collide with status ids rather " +
                "than being them. Every other status is a row in the catalogue.");
        }

        ArchRule.Empty(
            offenders,
            "05 §5 / 05 §6.1a: each type exempted from the status-id scan names only the one status " +
            "its own document rules on.");
    }

    /// <summary>
    /// `23` §6 — the S3 floor. The scan reaches the statuses, the catalogue and something that
    /// actually spells an id.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Without this the rule above is trivially satisfiable.</b> Move the status types out of
    /// the namespace, or let <c>Domain.CoreTypes</c> come back empty, and
    /// <c>No_status_id_is_named_in_code_outside_the_catalogue</c> reports success over nothing — while
    /// `05` §5 is free to be a switch statement again. The floor is stated over three things that
    /// would each have to survive: the namespace reaches types, the permitted list names types that
    /// exist, and the scan finds the twelve ids somewhere.
    /// </remarks>
    [Fact]
    public void The_rules_subject_set_is_the_one_they_were_written_against()
    {
        var subjects = Il.TypesUnder(ProductionAssemblies.CoreModule, StatusNamespace)
                         .Where(t => !Domain.IsCompilerGenerated(t))
                         .ToArray();

        (subjects.Length >= StatusTypeFloor).ShouldBeTrue(
            $"types under {StatusNamespace}: found {subjects.Length}, floor is {StatusTypeFloor}. " +
            "05 §5's catalogue, its cadence, its stun window and its timeline live there; an empty " +
            "or shrunken set means they have moved and this file's rules govern nothing. If this " +
            "shrank on purpose, lower the floor in the same commit and say why.");

        foreach (var permitted in Permitted)
        {
            (Domain.CoreTypes.Any(t => Owner(t).Equals(permitted, StringComparison.Ordinal))).ShouldBeTrue(
                $"'{permitted}' is exempted from the status-id rule and does not exist. An exemption " +
                "for a type that is gone is an exemption that can never expire, and the rule it " +
                "loosens is looser for nothing (steering S4).");
        }

        // 🔒 The scan really does find the ids. A `ldstr` filter that matched nothing anywhere would
        // satisfy the offender rule and prove the opposite of what it claims.
        var found = Domain.CoreTypes
            .Where(t => !Domain.IsCompilerGenerated(t))
            .SelectMany(Il.AllMethods)
            .SelectMany(Il.Instructions)
            .Where(i => i.OpCode == OpCodes.Ldstr && i.Operand is string s &&
                        StatusIds.Contains(s, StringComparer.Ordinal))
            .Select(i => (string)i.Operand!)
            .Distinct(StringComparer.Ordinal)
            .Count();

        (found >= StatusIds.Count).ShouldBeTrue(
            $"the ldstr scan found {found} of 05 §5's {StatusIds.Count} status ids in Core. It is " +
            "supposed to find all twelve in StatusLogId — fewer means the map has stopped naming " +
            "them, and a scan that matches nothing reports success over every switch statement " +
            "somebody adds next.");
    }

    /// <summary>
    /// `30` §11.4 — the namespace this file restates is really beneath the one <c>Domain</c>
    /// declares.
    /// </summary>
    /// <remarks>
    /// The restatement exists because M1-12 holds <c>Domain.cs</c> (steering S12). Without this pin, a
    /// rename there would leave every rule in this file governing a namespace that no longer exists,
    /// silently. The idiom is <c>IntraRulesLayeringRuleTests</c>'.
    /// </remarks>
    [Fact]
    public void The_namespace_this_rule_governs_is_the_one_under_Rules_Combat()
    {
        (Il.IsUnder(StatusNamespace, Domain.CombatRulesNamespace)).ShouldBeTrue(
            $"{StatusNamespace} is no longer beneath {Domain.CombatRulesNamespace}, so every rule in " +
            "this file governs a namespace that does not exist.");

        (Domain.IsPermittedCoreNamespace(StatusNamespace)).ShouldBeTrue(
            $"{StatusNamespace} is not a permitted 30 §11.4 Core namespace.");
    }

    /// <summary>
    /// Types under <c>Rules/Combat/Status/</c> on the commit these rules landed:
    /// <c>StatusKind</c>, <c>StatusPotencyBasis</c>, <c>StatusDefinition</c>, <c>StatusCatalogue</c>,
    /// <c>StatusCadence</c>, <c>StunWindow</c>, <c>StatusInstance</c>, <c>ActorStatuses</c>,
    /// <c>StatusLogId</c> and <c>StatusTimeline</c> — 10, plus whatever record plumbing the compiler
    /// emits. The floor is well below that so adding a type is not a test edit.
    /// </summary>
    private const int StatusTypeFloor = 6;

    private static string Owner(TypeDefinition type) => type.FullName.Replace('/', '.');
}
