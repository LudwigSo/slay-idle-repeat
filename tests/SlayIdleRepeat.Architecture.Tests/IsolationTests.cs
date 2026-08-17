using System.Text.RegularExpressions;
using Mono.Cecil;
using Shouldly;
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
    /// is a semantic question — that stays a `Core.Tests` obligation.
    /// <para>
    /// 🔒 <b>No longer vacuous.</b> It needed two things: <c>Entitlements</c>, which M1-07
    /// declared, and a type under <c>Core/Rules/</c> to look inside, which M1-10 landed as
    /// <c>Core/Rules/Economy/</c>. Both are here, and M1-10 proved the rule bites by naming
    /// <c>Entitlements</c> from an energy rule on purpose and capturing the failure.
    /// </para>
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
    /// (`if (mode == PremiumMode)`); that remains a review obligation.
    /// <para>
    /// ⚠️ <b>M1 REVIEW — this said "vacuous today: no `.cs` file under `src/` names any of these
    /// flags", and that stopped being true at M1-07</b>, which declared
    /// <c>Entitlements.HasPlus</c>. The SOURCE arm is still subject-less (no <c>if</c> names the
    /// flag), but the IL arm now has real subjects — <c>get_HasPlus</c> and the constructor both
    /// read the member — so the rule is <em>more</em> live than its own remark claimed. Second-order
    /// hazard worth knowing before it surprises somebody: <c>EntitlementBranchesInIl</c> treats any
    /// <c>FieldReference</c> operand as a read, so adding a validating <c>if</c> to
    /// <c>Entitlements</c>' own constructor would make this rule fire on
    /// <c>Entitlements..ctor</c>. That is the loud direction, but it is not obvious from the
    /// message.</para>
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
            // The licensed readers will branch on the entitlement — that is what they are
            // licensed for. The IL backstop exempts them by exact type name; the grep exempts
            // the file of that exact name, so the two halves license the same closed set rather
            // than one contradicting the other. Exact filename, so AdGrantCapRuleHelpers.cs and
            // SavePresetHelpers.cs are not exempt.
            .Where(file => !IsLicensedEntitlementReaderFile(file));

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

    /// <summary>The exact name of the rule `30` §3 licenses to read the entitlement.</summary>
    internal const string AdGrantCapRuleName = "AdGrantCapRule";

    /// <summary>The exact type the handler `14` §16.2 licenses to read the entitlement.</summary>
    internal const string PresetAllowanceHandlerType = "SlayIdleRepeat.Core.Handlers.SavePreset";

    /// <summary>
    /// 🔒 The <b>closed</b> list of sites the entitlement ban is lifted for, with what each decides
    /// and which document requires the decision to be inside the domain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>It was one name and had to become two, and that is a documented contradiction rather
    /// than a relaxation.</b> `30` §3 says the domain may read the entitlement <em>only</em> for the
    /// ad-reward auto-grant cap. `14` §16.2 classifies <c>NOT_ENTITLED</c> as a <b>domain</b>-tier
    /// rejection — this repository's own <c>RejectionReasons.TierOf</c> already agrees, which means
    /// <c>GameRules.Apply</c> is the only thing allowed to return it — and §16.2 gives it exactly one
    /// worked example: <em>"a Plus-gated operation without Plus (e.g. preset slot 4+, `09` §2.1)"</em>.
    /// A domain-tier value the domain is forbidden to compute cannot both be true. The design set
    /// names the second reader; the architecture rule's prose had not caught up.
    /// </para>
    /// <para>
    /// 🔒 <b>Enumerated, on <c>StatefulRuleTypeRuleTests.Stateful</c>'s precedent and for its
    /// reason.</b> The alternative — widening the predicate to "any handler may read Plus" — is how
    /// the entitlement leaks into a stat, a rate or a drop, which is the thing `12` §3.2 exists to
    /// prevent. Enumerated, the third one takes a diff, and the diff is where the question gets
    /// asked. Matched by EXACT name, never <c>Contains</c>: see <see cref="IsAdGrantCapRule"/>'s
    /// remarks for the near-miss that motivated it.
    /// </para>
    /// <para>
    /// ⚠️ <b>What each is licensed for is written here and enforced nowhere.</b> Nothing stops
    /// <c>SavePreset</c> from reading <c>HasPlus</c> to decide something other than a slot number;
    /// that stays a review obligation, exactly as a predicate renamed into something neutral does.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// 🔒 <b>Matched by FULL type name and by exact repo-relative PATH, never by simple name.</b> The
    /// grep arm sweeps every <c>.cs</c> file under <c>src/</c> and <c>tools/</c> and the IL arm sweeps
    /// the whole of <c>Core</c> and <c>Application</c>, so a bare simple name would exempt
    /// <em>any</em> <c>SavePreset</c> anywhere — a presenter, an endpoint, a DTO, a use case. That
    /// never bit while the list held one distinctive name; <c>SavePreset</c> is exactly the name three
    /// other layers would reach for. <see cref="AdGrantCapRuleName"/> stays a simple name only because
    /// M15-03 has not chosen where it lives, and its own remarks record that.
    /// </remarks>
    internal static readonly IReadOnlyList<(string Name, string Licenses)> EntitlementReaders = new[]
    {
        (AdGrantCapRuleName,
            "30 §3 — the ad-reward auto-grant cap. A cap of 30 for Plus and 10 otherwise IS a " +
            "conditional, and 12 §2's second grant (every rewarded placement becomes a one-tap " +
            "CLAIM at the same daily cap) is what makes it one. M15-03 authors it."),
        (PresetAllowanceHandlerType,
            "14 §16.2 — the preset slot allowance, and §16.2's own worked example of NOT_ENTITLED. " +
            "12 §2 grants Plus unlimited loadout presets and ads.json#/plus/freePresets authors the " +
            "free three; 12 §2.2 keeps presets beyond the allowance READ-ONLY rather than deleted, so " +
            "the read is confined to SAVE_PRESET and APPLY_PRESET must never make it. M4-10 authors it."),
    };

    /// <summary>
    /// X-02 / `30` §3 / `14` §16.2 — 🔒 the exemption list itself is closed, reasoned, and points at
    /// something real. The floor under <see cref="No_entitlement_branch_outside_a_composition_root"/>'s
    /// own exemptions (steering S3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A list of names has the failure mode <c>PublicRuleTypeFloorTests</c> records: a name that
    /// resolves to nothing exempts nothing, and nothing else notices, because a stale exemption makes
    /// the rule <em>stricter</em> rather than quieter.
    /// </para>
    /// <para>
    /// 🔒 <b>And an entry whose type exists but no longer READS the entitlement is the other half —
    /// the S4 direction, an exemption that has been satisfied.</b> The moment a refactor moves the
    /// allowance decision out of the domain (which is the better design and may well happen), the
    /// exemption stops excusing anything and sits here pre-armed for whatever lands on that name
    /// next. So every entry that resolves must be shown to be load-bearing: its IL must actually read
    /// an entitlement member. That is checked with the same machinery
    /// <see cref="EntitlementBranchesInIl"/> already uses, so the two cannot disagree about what a
    /// read is.
    /// </para>
    /// <para>
    /// <see cref="AdGrantCapRuleName"/> is exempt from the resolution floor and from the
    /// load-bearing check alike, because M15-03 has not written it — the same shape
    /// <c>PublicRuleTypeFloorTests</c> uses for its own pending name.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_licensed_entitlement_reader_that_exists_still_reads_the_entitlement()
    {
        EntitlementReaders.Count.ShouldBe(
            2,
            "the exemption list is closed. Two is 30 §3's ad-reward cap plus 14 §16.2's preset " +
            "allowance, and 30 §3's own table now names both; a third is a decision that belongs in " +
            "a diff with a reason, and this assertion is what forces the diff to be read.");

        EntitlementReaders
            .Where(reader => string.IsNullOrWhiteSpace(reader.Licenses) || reader.Licenses.Length < 40)
            .Select(reader => $"'{reader.Name}' carries no written licence worth falsifying.")
            .ShouldBeEmpty("an exemption with no stated reason has nothing to re-read at a kickoff.");

        var declared = EntitlementReaders.Select(reader => reader.Name).ToArray();

        declared.ShouldContain(
            PresetAllowanceHandlerType,
            "the preset allowance handler must be named by its FULL type name. A bare simple name " +
            "would exempt any 'SavePreset' anywhere under src/ or tools/ — a presenter, an endpoint, " +
            "a DTO — which is a far wider licence than 14 §16.2 gives.");

        var offenders = new List<string>();

        foreach (var (reader, _) in EntitlementReaders)
        {
            var type = FindLicensedReader(reader);

            if (type is null)
            {
                // Not written yet. Only AdGrantCapRule may be in that state; anything else naming a
                // type that does not exist is an exemption excusing nothing.
                if (!reader.Equals(AdGrantCapRuleName, StringComparison.Ordinal))
                {
                    offenders.Add(
                        $"'{reader}' is licensed to read the entitlement and resolves to no type in " +
                        "Core or Application. An exemption that excuses nothing is pre-armed for " +
                        "whatever lands on that name next — test-suites.json rule 5's failure mode.");
                }

                continue;
            }

            if (!ReadsAnEntitlementFlag(type))
            {
                offenders.Add(
                    $"'{reader}' exists and reads no entitlement flag, so its exemption from " +
                    "12 §3.2's ban is SATISFIED and must be deleted — together with its row in " +
                    "30 §3's table. An exemption that outlives what it excused stops describing " +
                    "anything and starts hiding the next one (steering S4).");
            }
        }

        ArchRule.Empty(
            offenders,
            "Every licensed entitlement reader that exists still reads the entitlement (S4, 30 §3).");
    }

    /// <summary>The type a licensed reader names, in <c>Core</c> or <c>Application</c>, or <c>null</c>.</summary>
    private static TypeDefinition? FindLicensedReader(string reader)
    {
        foreach (var module in new[] { ProductionAssemblies.CoreModule, ProductionAssemblies.ApplicationModule })
        {
            foreach (var type in module.GetTypes())
            {
                var matches = reader.Contains('.', StringComparison.Ordinal)
                    ? type.FullName.Equals(reader, StringComparison.Ordinal)
                    : type.Name.Equals(reader, StringComparison.Ordinal);

                if (matches)
                {
                    return type;
                }
            }
        }

        return null;
    }

    /// <summary>Whether any method of a type reads an entitlement flag, by the same IL test the ban uses.</summary>
    private static bool ReadsAnEntitlementFlag(TypeDefinition type) =>
        type.Methods
            .Where(method => method.HasBody)
            .SelectMany(Il.Instructions)
            .Select(instruction => instruction.Operand switch
            {
                MethodReference member => member.Name,
                FieldReference field => field.Name,
                _ => null,
            })
            .Any(name => name is not null && EntitlementFlag.IsMatch(StripAccessorPrefix(name)));

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

    /// <summary>The repo-relative source file a licensed reader is written in, or <c>null</c> for a name with no home yet.</summary>
    /// <remarks>
    /// Derived from the full type name rather than listed beside it, so the grep arm and the IL arm
    /// can never license two different things. An entry that is a bare simple name — M15-03's, until
    /// it lands — maps to no path and is matched by file stem instead, which is the narrowest form
    /// available for a type nobody has placed yet.
    /// </remarks>
    private static string? SourcePathOf(string reader)
    {
        var lastDot = reader.LastIndexOf('.');

        if (lastDot < 0)
        {
            return null;
        }

        var namespaceName = reader[..lastDot];
        var typeName = reader[(lastDot + 1)..];

        // SlayIdleRepeat.Core.Handlers -> SlayIdleRepeat.Core/Handlers: the project name is the first
        // three segments and everything after it is a directory.
        var segments = namespaceName.Split('.');
        var project = string.Join('.', segments.Take(2));
        var directories = segments.Skip(2);

        return Path.Combine(
            new[] { "src", project }.Concat(directories).Append(typeName + ".cs").ToArray());
    }

    /// <summary>Whether a source file is one of <see cref="EntitlementReaders"/>' own.</summary>
    private static bool IsLicensedEntitlementReaderFile(string file)
    {
        var relative = RepoLayout.Relative(file).Replace('/', Path.DirectorySeparatorChar);
        var stem = Path.GetFileNameWithoutExtension(file);

        foreach (var (reader, _) in EntitlementReaders)
        {
            var path = SourcePathOf(reader);

            var matches = path is null
                ? stem.Equals(reader, StringComparison.Ordinal)
                : relative.Equals(path, StringComparison.Ordinal);

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a type is one of <see cref="EntitlementReaders"/>.</summary>
    /// <remarks>By full name where the entry has one, by simple name where it does not — see <see cref="SourcePathOf"/>.</remarks>
    private static bool IsLicensedEntitlementReader(TypeDefinition type)
    {
        foreach (var (reader, _) in EntitlementReaders)
        {
            var matches = reader.Contains('.', StringComparison.Ordinal)
                ? type.FullName.Equals(reader, StringComparison.Ordinal)
                : type.Name.Equals(reader, StringComparison.Ordinal);

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

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
            if (IsLicensedEntitlementReader(method.DeclaringType))
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
                $"game (12 §3.2, 23 §7.2). Only {string.Join(" and ", EntitlementReaders.Select(r => r.Name))} " +
                "may read the entitlement — see IsolationTests.EntitlementReaders for what each is " +
                "licensed for and why.";
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
