using System.Text.RegularExpressions;
using Mono.Cecil;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// X-02 — entitlement and guild isolation. Neither rule is written out in `23` §6 or
/// `30` §9; both are named in the milestone's overarching task as part of this suite's
/// remit and are sourced from `30` §3, `12` §3.2, `23` §7.2 and `27` §8/§11.
/// </summary>
public sealed class IsolationTests
{
    /// <summary>
    /// The entitlement flags themselves. `12` §3.2 is explicit: there is no
    /// <c>if (isSubscriber)</c> anywhere in the game — the Plus promise is an adapter swap.
    /// </summary>
    private static readonly Regex EntitlementFlag = new(
        @"\b(isSubscriber|hasPlus|isPlus|hasAds|hasSubscription)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// A decision keyword and the parenthesised condition that follows it, matched with the
    /// condition's contents left entirely open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The predecessor of this rule anchored the flag to the opening parenthesis —
    /// <c>if\s*\(\s*!?\s*[\w\.\?]*\bHasPlus\b</c> — which reads any condition more complex
    /// than a single member access as not-a-branch. <c>if (chapter &gt; 3 &amp;&amp;
    /// ctx.Entitlements.HasPlus)</c> breaks that anchor at the <c>&gt;</c>; so does
    /// <c>if (!player.IsTrial || player.HasPlus)</c>. And <c>switch</c> was not in the
    /// keyword list at all, so <c>switch (player.HasPlus)</c> was invisible — which is
    /// exactly the "Plus alters a drop" that `12` §3.2 forbids.
    /// </para>
    /// <para>
    /// Nesting is bounded to two levels of parentheses, which covers every real condition;
    /// the IL backstop below is what covers the shapes no grep can reach.
    /// </para>
    /// </remarks>
    private static readonly Regex DecisionCondition = new(
        @"\b(if|while|switch)\s*\((?<condition>(?:[^()]|\((?:[^()]|\([^()]*\))*\))*)\)",
        RegexOptions.Compiled);

    /// <summary>
    /// A ternary on the same predicate is the same branch wearing a hat.
    /// </summary>
    private static readonly Regex EntitlementTernary = new(
        @"\b(isSubscriber|hasPlus|isPlus|hasAds|hasSubscription)\s*\?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Conditional-branch opcodes — what "the code decided something" looks like in IL.</summary>
    private static readonly HashSet<string> ConditionalBranchOpCodes = new(StringComparer.Ordinal)
    {
        "brtrue", "brtrue.s", "brfalse", "brfalse.s",
        "beq", "beq.s", "bne.un", "bne.un.s",
        "switch",
    };

    /// <summary>
    /// X-02 / `30` §3 — 🔒 the domain may read `HasPlus` only to resolve ad-reward
    /// auto-grant caps, never to alter a stat, a rate or a drop. `Entitlements` must be
    /// unreachable from `Core/Rules/` — including the power computation of `29` §3 — with
    /// the single exception of the ad-cap rule that `30` §3 licenses.
    /// </summary>
    /// <remarks>
    /// Structural coverage: this asserts no type under `Core/Rules/` names `Entitlements`.
    /// It cannot prove the ad-cap rule reads only the cap, because "which field it reads"
    /// is a semantic question — that stays a `Core.Tests` obligation. Vacuous until M1
    /// declares `Entitlements` (`30` §3).
    /// </remarks>
    [Fact]
    public void Entitlements_are_unreachable_from_the_rules_and_the_power_computation()
    {
        var rules = Domain.CoreTypesUnder(Domain.RulesNamespace).ToArray();

        var offenders = rules
            .Where(t => !IsAdGrantCapRule(t))
            .SelectMany(t => Il.ReferencedTypeNames(t).Select(n => (Type: t, Name: n)))
            .Where(x => SimpleName(x.Name).Equals(Domain.EntitlementsType, StringComparison.Ordinal))
            .Select(x => $"{x.Type.FullName} names {x.Name} — entitlement may not reach a rule (30 §3)")
            .ToList();

        // `30` §3 licenses ONE rule, singular. The exemption is from the whole Entitlements
        // ban, so a second exempted type is a second place where Plus can reach a drop rate.
        var exempted = rules.Where(IsAdGrantCapRule).ToArray();
        if (exempted.Length > 1)
        {
            offenders.Add(
                $"{exempted.Length} types are exempt as the ad-grant cap rule " +
                $"[{string.Join(", ", exempted.Select(t => t.FullName))}]. 30 §3 licenses exactly one, and the " +
                "exemption is from the entire Entitlements ban — every extra one is another place Plus can " +
                "alter a stat, a rate or a drop.");
        }

        ArchRule.Empty(
            offenders,
            "Entitlements are unreachable from Core/Rules/ and from the power computation (X-02, 30 §3, 29 §3).");
    }

    /// <summary>
    /// X-02 / `12` §3.2 / `23` §7.2 — 🔒 no <c>if (isSubscriber)</c> / <c>if (hasAds)</c>
    /// branch outside a composition root. The Plus promise is an adapter swap, chosen once
    /// in `Server/Composition/` or `Client/Composition/`, not a condition sprinkled through
    /// the game.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two independent mechanisms. A source grep over every `.cs` file under `src/` and
    /// `tools/`, comments and string literals stripped, matching a decision keyword whose
    /// whole parenthesised condition names a subscription or ad-entitlement flag, plus the
    /// ternary form. And an IL backstop over `Core` and `Application` for the shape a grep
    /// cannot see — `var plus = Check(ctx); if (plus)` — where the flag is read and the
    /// method then branches.
    /// </para>
    /// <para>
    /// Neither can cover a predicate renamed into something neutral
    /// (`if (mode == PremiumMode)`); that remains a review obligation. Vacuous today: no
    /// `.cs` file under `src/` names any of these flags.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_entitlement_branch_outside_a_composition_root()
    {
        var compositionRoots = ProductionAssemblies.CompositionRootNames
            .Select(root => Path.Combine(RepoLayout.ProjectDirectory(root), "Composition") + Path.DirectorySeparatorChar)
            .ToArray();

        var offenders = new List<string>();

        var files = RepoLayout.ProductionSourceRoots
            .Where(Directory.Exists)
            .SelectMany(RepoLayout.SourceFiles)
            // Ordinal, not OrdinalIgnoreCase. CI runs on ubuntu-24.04, where the filesystem
            // is case-sensitive: `Server/composition/Foo.cs` and `Server/Composition/Foo.cs`
            // are two different files there, and an ignore-case prefix would exempt the first
            // as though it were the second — silently widening the one exemption this rule has.
            .Where(file => !compositionRoots.Any(root => file.StartsWith(root, StringComparison.Ordinal)))
            // The one rule `30` §3 licenses to read the entitlement will branch on it — a cap
            // of 30 for Plus and 10 otherwise IS a conditional. The IL backstop already
            // exempts it by exact type name; the grep exempts the file of that exact name, so
            // the two halves license the same single thing rather than one contradicting the
            // other. Exact filename, so AdGrantCapRuleHelpers.cs is not exempt.
            .Where(file => !Path.GetFileNameWithoutExtension(file).Equals(AdGrantCapRuleName, StringComparison.Ordinal));

        foreach (var file in files)
        {
            var source = SourceText.Read(file);

            offenders.AddRange(
                DecisionCondition.Matches(source.Stripped)
                    .Where(m => EntitlementFlag.IsMatch(m.Groups["condition"].Value))
                    .Select(m => $"{RepoLayout.Relative(file)}: {Condense(m.Value)}  " +
                                 "[entitlement branch outside a composition root]"));

            offenders.AddRange(
                source.Hits(EntitlementTernary)
                      .Select(hit => $"{hit}  [entitlement ternary outside a composition root]"));
        }

        offenders.AddRange(EntitlementBranchesInIl(ProductionAssemblies.CoreModule, "Core"));
        offenders.AddRange(EntitlementBranchesInIl(ProductionAssemblies.ApplicationModule, "Application"));

        ArchRule.Empty(
            offenders,
            "No entitlement branch outside Server/Composition/ or Client/Composition/ (X-02, 12 §3.2, 23 §7.2).");
    }

    /// <summary>
    /// X-02 / `27` §11 / `30` §5 — guild state is unreachable from the combat path.
    /// The combat simulation runs from a stat block and a seed (`05` §9); reaching a guild
    /// aggregate from it would put the game's only contended write on the hottest path.
    /// </summary>
    [Fact]
    public void Guild_state_is_unreachable_from_the_combat_path()
    {
        var combatTypes = Domain.CoreTypesUnder(Domain.CombatRulesNamespace)
            .Concat(Domain.CoreTypes.Where(t => t.Name.Contains("Combat", StringComparison.Ordinal) ||
                                                t.Name.Contains("Battle", StringComparison.Ordinal)))
            .Distinct();

        var offenders = combatTypes
            .SelectMany(t => Il.ReferencedTypeNames(t).Select(n => (Type: t, Name: n)))
            .Where(x => IsGuildType(x.Name))
            .Select(x => $"{x.Type.FullName} names guild state {x.Name} — the combat path must not reach a guild (27 §11)");

        ArchRule.Empty(
            offenders,
            "Guild state is unreachable from the combat path (X-02, 27 §11, 30 §5).");
    }

    /// <summary>
    /// X-02 / `27` §8 / `11` §6 — guild state is unreachable from the ghost snapshot.
    /// A duel is fought against a stored snapshot of a player; guild membership,
    /// contribution history and phrase posts are personal data (`27` §11 GDPR) and have no
    /// business travelling with it.
    /// </summary>
    [Fact]
    public void Guild_state_is_unreachable_from_the_ghost_snapshot()
    {
        var snapshotTypes = Domain.CoreTypes
            .Where(t => t.Name.Contains("Ghost", StringComparison.Ordinal))
            .Concat(Domain.CoreTypesUnder(Domain.SnapshotsNamespace)
                          .Where(t => t.Name.Contains("Ghost", StringComparison.Ordinal)))
            .Distinct();

        var offenders = snapshotTypes
            .SelectMany(t => Il.ReferencedTypeNames(t).Select(n => (Type: t, Name: n)))
            .Where(x => IsGuildType(x.Name))
            .Select(x => $"{x.Type.FullName} carries guild state {x.Name} — the ghost snapshot must not (27 §8)");

        ArchRule.Empty(
            offenders,
            "Guild state is unreachable from the ghost snapshot (X-02, 27 §8).");
    }

    /// <summary>
    /// `30` §5 — 🔒 the domain never returns a mutated guild: `GuildView` in `WorldSlice`
    /// is read-only. Guild effects leave the domain as a `GuildContribution` intent that
    /// the Application layer applies as an atomic increment (`27` §11).
    /// </summary>
    [Fact]
    public void GuildView_is_a_read_only_projection()
    {
        var view = Domain.FindInCore(Domain.GuildViewType);
        if (view is null)
        {
            ArchRule.Empty(Array.Empty<string>(), GuildViewRule);
            return;
        }

        var offenders = view.Properties
            .Where(p => p.SetMethod is not null && !p.SetMethod.IsPrivate)
            .Select(p => $"{view.FullName}.{p.Name} has a settable accessor")
            .Concat(view.Fields
                .Where(f => f.IsPublic && !f.IsInitOnly && !f.IsLiteral && !Domain.IsCompilerGenerated(f))
                .Select(f => $"{Il.Describe(f)} is a mutable public field"));

        ArchRule.Empty(offenders, GuildViewRule);
    }

    private const string GuildViewRule =
        "GuildView is a read-only projection — the domain never returns a mutated guild (30 §5, 27 §11).";

    private static bool IsGuildType(string typeFullName) =>
        typeFullName.StartsWith(Domain.GuildModelNamespace + ".", StringComparison.Ordinal) ||
        SimpleName(typeFullName).StartsWith("Guild", StringComparison.Ordinal);

    /// <summary>The exact name of the one rule `30` §3 licenses to read the entitlement.</summary>
    internal const string AdGrantCapRuleName = "AdGrantCapRule";

    /// <summary>
    /// The one rule `30` §3 licenses to read the entitlement: the ad-reward auto-grant cap.
    /// Recognised by exact name so M1 can write it without the test having to be relaxed.
    /// </summary>
    /// <remarks>
    /// 🔒 EXACT, not <c>Contains</c>. The predecessor matched any type name containing
    /// "AdCap", "AdGrant" or "AdReward" case-insensitively — which also matches
    /// <c>He<b>adCap</b>Rule</c>, and, far worse, would have exempted a plausible
    /// <c>AdRewardDropRule</c>. The exemption is from the ENTIRE <c>Entitlements</c> ban,
    /// not merely from the cap lookup, so a type that fell inside it could read
    /// <c>HasPlus</c> and double a drop rate with `30` §3 and `12` §3.2 both green.
    /// </remarks>
    private static bool IsAdGrantCapRule(TypeDefinition type) =>
        type.Name.Equals(AdGrantCapRuleName, StringComparison.Ordinal);

    private static string SimpleName(string typeFullName) => typeFullName.Split('.', '/').Last();

    /// <summary>Collapses a multi-line matched condition onto one line for a readable failure.</summary>
    private static string Condense(string text) =>
        Regex.Replace(text, @"\s+", " ").Trim();

    /// <summary>
    /// Methods that both read an entitlement flag and then branch. The grep cannot see this
    /// shape because the flag and the <c>if</c> are on different lines and in different
    /// expressions: <c>var plus = Check(ctx); if (plus)</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately coarse — "loads the flag AND contains a conditional branch" rather than a
    /// dataflow proof that the branch consumes the flag. A rule of this kind is allowed to
    /// fail falsely (the fix is to move the decision to a composition root, which is where
    /// `23` §7.2 wants it anyway) and is never allowed to pass emptily. The single licensed
    /// reader, <c>AdGrantCapRule</c>, is excluded by exact name.
    /// </remarks>
    private static IEnumerable<string> EntitlementBranchesInIl(ModuleDefinition module, string label)
    {
        foreach (var method in Il.MethodsWithBodies(module))
        {
            if (method.DeclaringType.Name.Equals(AdGrantCapRuleName, StringComparison.Ordinal))
            {
                continue;
            }

            var instructions = Il.Instructions(method).ToArray();

            var read = instructions
                .Select(i => i.Operand switch
                {
                    MethodReference m => m.Name,
                    FieldReference f => f.Name,
                    _ => null,
                })
                .Where(name => name is not null && EntitlementFlag.IsMatch(StripAccessorPrefix(name!)))
                .Select(name => StripAccessorPrefix(name!))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (read.Length == 0)
            {
                continue;
            }

            if (!instructions.Any(i => ConditionalBranchOpCodes.Contains(i.OpCode.Name)))
            {
                continue;
            }

            yield return
                $"[{label}] {Il.Describe(method)} reads {string.Join(", ", read)} and then branches — " +
                $"the Plus promise is an adapter swap chosen in a composition root, not a condition in the " +
                $"game (12 §3.2, 23 §7.2). Only {AdGrantCapRuleName} may read the entitlement (30 §3).";
        }
    }

    /// <summary>Turns <c>get_HasPlus</c> / <c>&lt;HasPlus&gt;k__BackingField</c> back into <c>HasPlus</c>.</summary>
    private static string StripAccessorPrefix(string memberName)
    {
        var name = memberName;

        if (name.StartsWith("get_", StringComparison.Ordinal) || name.StartsWith("set_", StringComparison.Ordinal))
        {
            name = name[4..];
        }

        return name.Trim('<', '>').Replace("k__BackingField", string.Empty, StringComparison.Ordinal);
    }
}
