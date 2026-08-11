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
    /// The shapes an entitlement branch takes. `12` §3.2 is explicit: there is no
    /// <c>if (isSubscriber)</c> anywhere in the game — the Plus promise is an adapter swap.
    /// </summary>
    private static readonly Regex EntitlementBranch = new(
        @"\b(if|while)\s*\(\s*!?\s*[\w\.\?]*\b(isSubscriber|IsSubscriber|hasPlus|HasPlus|isPlus|IsPlus|hasAds|HasAds|hasSubscription|HasSubscription)\b",
        RegexOptions.Compiled);

    /// <summary>
    /// A ternary on the same predicate is the same branch wearing a hat.
    /// </summary>
    private static readonly Regex EntitlementTernary = new(
        @"\b(isSubscriber|IsSubscriber|hasPlus|HasPlus|isPlus|IsPlus|hasAds|HasAds|hasSubscription|HasSubscription)\s*\?",
        RegexOptions.Compiled);

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
        var offenders = Domain.CoreTypesUnder(Domain.RulesNamespace)
            .Where(t => !IsAdGrantCapRule(t))
            .SelectMany(t => Il.ReferencedTypeNames(t).Select(n => (Type: t, Name: n)))
            .Where(x => SimpleName(x.Name).Equals(Domain.EntitlementsType, StringComparison.Ordinal))
            .Select(x => $"{x.Type.FullName} names {x.Name} — entitlement may not reach a rule (30 §3)");

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
    /// Coverage: a source grep over every `.cs` file under `src/`, with comments and string
    /// literals stripped, matching an `if`/`while`/ternary whose predicate names a
    /// subscription or ad-entitlement flag. It covers the shapes the documents name and the
    /// obvious synonyms. It cannot cover a branch whose predicate has been renamed into
    /// something neutral (`if (mode == PremiumMode)`), nor one computed through a helper —
    /// those remain a review obligation. Vacuous today: `Core`, `Application` and every
    /// adapter contain no `.cs` files yet.
    /// </remarks>
    [Fact]
    public void No_entitlement_branch_outside_a_composition_root()
    {
        var compositionRoots = ProductionAssemblies.CompositionRootNames
            .Select(root => Path.Combine(RepoLayout.ProjectDirectory(root), "Composition"))
            .ToArray();

        var offenders =
            from file in RepoLayout.SourceFiles(RepoLayout.SrcRoot)
            where !compositionRoots.Any(root => file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            let source = SourceText.Read(file)
            from hit in source.Hits(EntitlementBranch).Concat(source.Hits(EntitlementTernary))
            select $"{hit}  [entitlement branch outside a composition root]";

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

    /// <summary>
    /// The one rule `30` §3 licenses to read the entitlement: the ad-reward auto-grant cap.
    /// Recognised by name so M1 can write it without the test having to be relaxed.
    /// </summary>
    private static bool IsAdGrantCapRule(TypeDefinition type) =>
        type.Name.Contains("AdCap", StringComparison.OrdinalIgnoreCase) ||
        type.Name.Contains("AdGrant", StringComparison.OrdinalIgnoreCase) ||
        type.Name.Contains("AdReward", StringComparison.OrdinalIgnoreCase);

    private static string SimpleName(string typeFullName) => typeFullName.Split('.', '/').Last();
}
