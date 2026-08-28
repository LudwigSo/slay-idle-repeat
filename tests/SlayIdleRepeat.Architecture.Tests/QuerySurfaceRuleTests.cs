using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Shouldly;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 The register of `30` §12.4's cross-player read models, and the convention every one of them
/// takes when it is finally built: a view model out, a declared staleness budget, a stated routing,
/// and no aggregate on the way past.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a register and not a comment.</b> None of the six views exists — all six belong to M12,
/// M13 and M14 — so a rule quantified over "the read models in the assembly" would hold over an
/// empty set forever (steering <b>S3</b>), and would still hold on the day the first one landed
/// wearing whatever shape its author chose. The transcription below is a closed, hand-copied list
/// of the document's own table, so the rule has six subjects today and fires the moment one of
/// them arrives.
/// </para>
/// <para>
/// <b>It fails in three directions.</b> A transcribed view that is neither authored nor deferred is
/// reported; an authored view that breaks the convention is reported; and a deferral whose view now
/// exists is reported, which is what forces the entry out on the commit that discharges it rather
/// than at some later kickoff. Modelled on <see cref="GapRegister"/>, which does the same for
/// `30` §7.
/// </para>
/// <para>
/// ⚠️ <b>The known limit, stated so nobody assumes otherwise.</b> What is decidable here is the
/// shape: the view arrived, the budget disagrees with the document, the owner is missing. What is
/// not decidable is whether a budget is still the RIGHT budget for the view — that is a product
/// decision, re-read at each milestone kickoff against `30` §12.4 itself.
/// </para>
/// </remarks>
public sealed class QuerySurfaceRuleTests
{
    private const string QueriesNamespace = "SlayIdleRepeat.Application.Queries";
    private const string OwnStateNamespace = "SlayIdleRepeat.Application.UseCases";
    private const string AggregateNamespace = "SlayIdleRepeat.Core.Model";
    private const string ViewInterfaceName = "IReadModelView";
    private const string QueryInterfaceName = "IReadModelQuery`1";
    private const string RoutingEnumName = "ReadRouting";
    private const string PrimaryRouting = "Primary";
    private const string OwnStateViewName = "OwnStateView";
    private const string RunStateQueryName = "RunStateQuery";
    private const string BudgetProperty = "StalenessBudget";
    private const string RoutingProperty = "Routing";

    /// <summary>A milestone task id: <c>M14</c>, or <c>M12-05</c>.</summary>
    private static readonly Regex TaskId = new(@"^M\d{1,2}(-\d{2})?$", RegexOptions.Compiled);

    /// <summary>One row of `30` §12.4's table: the view's name and the staleness the document allows it.</summary>
    private sealed record ReadModel(string View, TimeSpan Budget);

    /// <summary>A read model that has deliberately not been built, and the task that builds it.</summary>
    private sealed record Deferral(string View, string Owner, string Why);

    /// <summary>The document this register transcribes, and the heading its table sits under.</summary>
    private const string DesignDocument = "30_DOMAIN_MODEL.md";

    private const string ViewTableHeading = "### 12.4 The read models";

    /// <summary>
    /// The one row of that table which is not a read model, named so the count below is a count of
    /// views. The document calls it "fully async": nothing reads it, so it declares no budget.
    /// </summary>
    private static readonly string[] NotAReadModel = { "Analytics / PostHog" };

    /// <summary>
    /// 🔒 `30` §12.4's six views, transcribed by hand from the document's table. A list derived from
    /// the code would say the code is complete because the code says so — so it is derived from
    /// neither, and <see cref="Untranscribed"/> resolves every name and every number here against the
    /// document itself.
    /// </summary>
    private static readonly ReadModel[] Views =
    {
        new("LadderView", TimeSpan.FromMinutes(10)),
        new("GhostCandidates", TimeSpan.FromMinutes(10)),
        new("GuildRosterView", TimeSpan.FromMinutes(1)),
        new("GuildBossView", TimeSpan.FromSeconds(30)),
        new("GuildBrowserView", TimeSpan.FromMinutes(5)),
        new("EventLeaderboardView", TimeSpan.FromMinutes(10)),
    };

    /// <summary>
    /// 🔒 Every one of the six, deferred with the tracker row that owns it. Each entry expires by
    /// itself: authoring the view fails the rule below on the commit that adds it.
    /// </summary>
    private static readonly Deferral[] Deferred =
    {
        new("LadderView", "M12-05",
            "The leaderboard read model itself — the tracker's M12-05 row is 'window-function rank, " +
            "10-min cache', which is this view and its budget in the tracker's own words, and it is " +
            "the owner PortCatalogue already gives ILeaderboardRepository. There is no rating to rank " +
            "before M12-04 records one, so a view authored now would page over an empty table."),

        new("GhostCandidates", "M12-03",
            "The tracker's M12-03 row reads 'Matchmaking: candidate selection + band widening, B3 " +
            "fairness rule', and 30 §12.4 makes the B3 rule (24 §4.10) this view's own obligation at " +
            "selection time. Owned there rather than at M12-01, which stores ghosts: a candidate SET " +
            "is a selection, and an owner that resolves to the wrong real row is the failure this " +
            "kind of register is weakest against."),

        new("GuildRosterView", "M14-01",
            "Membership plus the contribution ledger. The tracker's M14-01 row authors the guild " +
            "aggregate, its schema and IGuildRepository, without which a roster has nothing to " +
            "project; the contribution half is M14-03's and arrives against the same repository."),

        new("GuildBossView", "M14-04",
            "A projection of the damage ledger, and the tracker's M14-04 row is the Guild Boss in " +
            "full — HP pool, server-authoritative damage, the weekly cycle. The 30 s budget is what " +
            "makes the atomic-increment write path (30 §5) affordable, so the view and the ledger " +
            "have to be designed together."),

        new("GuildBrowserView", "M14-01",
            "A rollup of guild activity for a player choosing one to join. It is M14-01's with the " +
            "join policies it browses against: a browser that lists guilds a policy would refuse is " +
            "worse than no browser, and the policies do not exist until that row lands."),

        new("EventLeaderboardView", "M13-02",
            "Event scores, paid by percentile band (26 §3.2). The tracker's M13-02 row authors the " +
            "three archetypes including EVENT_SCORE_RUSH and its 'data-driven scoring formula', " +
            "which is what a score IS — before it there is nothing to rank. Not M12-05's: that row " +
            "ranks player ratings, and an event's board is scoped to one package's window."),
    };

    /// <summary>Every type in the built <c>SlayIdleRepeat.Application</c> assembly.</summary>
    private static readonly IReadOnlyList<Type> ApplicationTypes = ProductionAssemblies.Application.GetTypes();

    /// <summary>
    /// 🔒 `30` §12.4 / §12.5 — every read model the design enumerates is authored to its transcribed
    /// budget or carried by a deferral with an owning milestone task.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The count is the literal <b>6</b> rather than the transcription's own length: a count taken
    /// from the list cannot notice the list being trimmed, which is the one edit a self-referential
    /// floor would survive (steering S3).
    /// </para>
    /// <para>
    /// <b>The numbers are resolved, not quoted (steering S9).</b> Every transcribed name and budget
    /// is read back out of the document's own table, in both directions: a budget edited here to
    /// something the document does not say fails, and a seventh view added to the document that
    /// nobody transcribed fails too. Without that, the transcription is an assertion about itself.
    /// </para>
    /// <para>
    /// <b>The convention half is driven against a real subject.</b> No cross-player view exists, so
    /// <see cref="BreaksTheConvention"/> — the part of this rule that actually decides whether an
    /// authored view is well formed — would otherwise be code nothing has ever run. The last check
    /// below points the register at <c>OwnStateView</c>, which breaks the convention in three ways
    /// at once, so the enforcement is shown to bite without a violation ever being committed.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_read_model_the_design_enumerates_is_authored_or_declared_deferred()
    {
        Views.Length.ShouldBe(
            6,
            "30 §12.4's table has six view rows. A transcription that shrank would stop asking about " +
            "the row it dropped, and nothing else in this repository enumerates them.");

        Views.Select(v => v.View).Distinct(StringComparer.Ordinal).Count().ShouldBe(
            6, "a duplicated name would keep the count at six while one row went untranscribed.");

        ArchRule.Empty(
            Untranscribed(Views),
            "Every transcribed read model is one 30 §12.4 names, at the budget 30 §12.4 spells for it.");

        ArchRule.Empty(
            Unbuilt(Views, Deferred, QueriesNamespace),
            "Every 30 §12.4 read model is authored to its budget or declared deferred with an owner.");

        // The teeth, driven against crafted input so the rule is shown to bite without a violation
        // ever being committed — the same construction GapRegisterTests uses.
        Unbuilt(
                new[] { new ReadModel("AViewNoMilestoneOwns", TimeSpan.FromMinutes(1)) },
                Array.Empty<Deferral>(),
                QueriesNamespace)
            .ShouldHaveSingleItem()
            .ShouldContain("no deferral names it", Case.Sensitive);

        Unbuilt(
                new[] { Views[0] },
                new[] { new Deferral("LadderView", "soon", "a reason long enough to be worth falsifying.") },
                QueriesNamespace)
            .ShouldHaveSingleItem()
            .ShouldContain("not a milestone task id", Case.Sensitive);

        var authoredButWrong = Unbuilt(
            new[] { new ReadModel(OwnStateViewName, TimeSpan.FromMinutes(10)) },
            Array.Empty<Deferral>(),
            OwnStateNamespace);

        authoredButWrong.ShouldNotBeEmpty(
            "OwnStateView is authored, tolerates no staleness rather than the ten minutes asked for " +
            "here, hands two persisted rows out on its public surface and is reached through no query " +
            "port — so the convention half of this rule must report it. A convention nothing has ever " +
            "been measured against is a paragraph, not a rule, and the first real view would land " +
            "wearing whatever shape its author chose.");

        authoredButWrong.Any(offence => offence.Contains("no deferral names it", StringComparison.Ordinal))
            .ShouldBeFalse(
                "the authored branch is the one under test: an offender reading 'no deferral names it' " +
                "would mean the lookup never found the type, and the check above would prove only that " +
                "an unknown name is unknown.");
    }

    /// <summary>
    /// 🔒 `30` §12.4 — a deferral does not outlive the view that discharges it. The commit that
    /// authors the first cross-player view fails here until its entry is removed, which is what makes
    /// this register fire on arrival instead of passing forever (`23` §6).
    /// </summary>
    [Fact]
    public void A_deferred_read_model_does_not_outlive_the_view_that_discharges_it()
    {
        ArchRule.Empty(
            Expired(Deferred),
            "Every deferred 30 §12.4 read model is still unbuilt (23 §6, steering S4).");

        Expired(new[] { new Deferral(OwnStateViewName, "M12-05", "a reason long enough to be worth falsifying.") })
            .ShouldHaveSingleItem()
            .ShouldContain(
                "already declares",
                Case.Sensitive,
                "OwnStateView exists today, so a deferral naming it is exactly the state this rule " +
                "catches — driven here rather than committed.");

        Expired(new[] { Deferred[0] }).ShouldBeEmpty(
            "the silent half: a check that flagged everything would also 'prove' it has teeth.");
    }

    /// <summary>
    /// 🔒 `30` §12.8 / `14` §2.3a — the player's own state is a write-model read on the primary, and
    /// is not a read model. <c>OwnStateView</c> tolerates no staleness, <c>RunStateQuery</c> names the
    /// primary, and neither wears the cross-player convention.
    /// </summary>
    /// <remarks>
    /// Both subjects are looked up <b>by name and required to be found</b>: a rename would otherwise
    /// empty the subject set and take the whole claim quiet with it.
    /// </remarks>
    [Fact]
    public void The_players_own_state_is_read_from_the_write_model_on_the_primary()
    {
        var view = Required(OwnStateViewName);
        var query = Required(RunStateQueryName);
        var viewInterface = Required(ViewInterfaceName);
        var queryInterface = Required(QueryInterfaceName);

        Budget(view).ShouldBe(
            TimeSpan.Zero,
            "a player must see their own accepted command in the very next read — the client has " +
            "already animated it (30 §12.2). Every other view may declare a budget; this one cannot.");

        Routing(query).ShouldBe(
            Enum.Parse(Required(RoutingEnumName), PrimaryRouting),
            "GET /run/{runId}/state is a write-model read (30 §12.8): served from a replica it would " +
            "answer a reconnecting client with state older than the command it is reconnecting after.");

        viewInterface.IsAssignableFrom(view).ShouldBeFalse(
            "the write-model read is not a read model. Wearing " + ViewInterfaceName + " would put " +
            "the player's own state under a convention whose whole premise is that staleness is " +
            "tolerable.");

        ImplementsOpen(query, queryInterface).ShouldBeFalse(
            "the same, from the query side: an " + QueryInterfaceName + " defaults to the replica, " +
            "and a type that inherits that default has already lost the argument 30 §12.2 makes.");
    }

    /// <summary>Every transcribed row the document does not corroborate, and every view it names that nobody transcribed.</summary>
    /// <remarks>
    /// Both directions matter. A budget mistyped here would otherwise govern the code while the
    /// document said something else, and a seventh view added to `30` §12.4 would arrive with no
    /// deferral, no owner and nothing asking about it.
    /// </remarks>
    private static IReadOnlyList<string> Untranscribed(IReadOnlyList<ReadModel> views)
    {
        var documented = DocumentedViews();
        var offenders = new List<string>();

        foreach (var model in views)
        {
            if (!documented.TryGetValue(model.View, out var spelling))
            {
                offenders.Add(
                    $"'{model.View}' is transcribed here and {DesignDocument}'s read-model table names " +
                    "no such view. A transcription of a table that does not say this is a rule " +
                    "enforcing a document nobody wrote.");
                continue;
            }

            if (SpelledBudget(spelling) != model.Budget)
            {
                offenders.Add(
                    $"'{model.View}' is transcribed at {model.Budget} and {DesignDocument} spells " +
                    $"'{spelling}'. The cache honours this number and the UI states it, so the two " +
                    "drifting apart is the product quietly disagreeing with its own specification.");
            }
        }

        var transcribed = views.Select(v => v.View).ToHashSet(StringComparer.Ordinal);

        offenders.AddRange(
            documented.Keys
                .Where(view => !transcribed.Contains(view) && !NotAReadModel.Contains(view, StringComparer.Ordinal))
                .Select(view =>
                    $"{DesignDocument}'s read-model table names '{view}' and nothing here transcribes " +
                    "it. A view the design added after this register was written is a view with no " +
                    "owner, no budget anyone checks and no commit that has to notice it arriving."));

        return offenders;
    }

    /// <summary>The read-model table's own rows: the view's name against the staleness the document spells.</summary>
    private static IReadOnlyDictionary<string, string> DocumentedViews()
    {
        var path = Path.Combine(RepoLayout.RepoRoot, "game-design", DesignDocument);
        var lines = File.ReadAllLines(path);
        var heading = Array.FindIndex(lines, line => line.StartsWith(ViewTableHeading, StringComparison.Ordinal));

        if (heading < 0)
        {
            throw new InvalidOperationException(
                $"'{path}' carries no section headed '{ViewTableHeading}'. Every name and number this " +
                "register transcribes is resolved against that table, and a lookup that found nothing " +
                "would take the whole resolution quiet (steering S9).");
        }

        var rows = lines
            .Skip(heading + 1)
            .TakeWhile(line => !line.StartsWith("### ", StringComparison.Ordinal))
            .Select(line => line.Split('|'))
            .Where(cells => cells.Length >= 5)
            .Select(cells => (View: Cell(cells[1]), Staleness: Cell(cells[3])))
            .Where(row => row.View.Length > 0 && row.View != "View" && !row.View.All(c => c == '-'))
            .ToDictionary(row => row.View, row => row.Staleness, StringComparer.Ordinal);

        return rows.Count > 0
            ? rows
            : throw new InvalidOperationException(
                $"'{path}' has a '{ViewTableHeading}' section with no table rows under it. An empty " +
                "lookup would report every transcribed view as absent, or — read the other way — " +
                "corroborate none of them.");
    }

    /// <summary>One markdown table cell, stripped of the document's code ticks and emphasis.</summary>
    private static string Cell(string raw) => raw.Trim().Trim('`', '*', ' ');

    /// <summary>The document's spelling of a staleness budget as a duration, or <c>null</c> when it names none.</summary>
    private static TimeSpan? SpelledBudget(string spelling)
    {
        var value = spelling.Split(' ');

        if (value.Length != 2 || !int.TryParse(value[0], NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
        {
            return null;
        }

        return value[1] switch
        {
            "min" => TimeSpan.FromMinutes(amount),
            "s" => TimeSpan.FromSeconds(amount),
            _ => null,
        };
    }

    /// <summary>Every transcribed view that is neither authored to its budget nor properly deferred.</summary>
    /// <remarks>
    /// Takes the lists and the namespace as parameters rather than reading the fields, so the rule
    /// above can drive it with crafted input and prove it bites — the construction
    /// <c>GapRegister.Expired</c> uses. The namespace is a parameter for the same reason: the
    /// convention checks have no authored subject in <c>Queries</c> today, and pointing them at a
    /// namespace that does hold one is the only way to run them at all.
    /// </remarks>
    private static IReadOnlyList<string> Unbuilt(
        IEnumerable<ReadModel> views, IEnumerable<Deferral> deferrals, string queriesNamespace)
    {
        var declared = deferrals.ToArray();
        var offenders = new List<string>();

        foreach (var model in views)
        {
            var authored = ApplicationTypes.FirstOrDefault(
                t => t.Name.Equals(model.View, StringComparison.Ordinal) &&
                     string.Equals(t.Namespace, queriesNamespace, StringComparison.Ordinal));

            if (authored is not null)
            {
                offenders.AddRange(BreaksTheConvention(authored, model));
                continue;
            }

            var deferral = declared.FirstOrDefault(d => d.View.Equals(model.View, StringComparison.Ordinal));

            if (deferral is null)
            {
                offenders.Add(
                    $"30 §12.4 specifies '{model.View}'. It is not authored under {queriesNamespace}, and " +
                    "no deferral names it. An undeclared read model is indistinguishable from one " +
                    "nobody noticed: either author it, or add an entry naming the task that will.");
                continue;
            }

            if (!TaskId.IsMatch(deferral.Owner ?? string.Empty))
            {
                offenders.Add(
                    $"'{model.View}' names owner '{deferral.Owner}', which is not a milestone task id " +
                    "(M14, M12-05). An entry with no owner has no expiry a reader can check.");
            }

            if (string.IsNullOrWhiteSpace(deferral.Why) || deferral.Why.Length < 40)
            {
                offenders.Add(
                    $"'{model.View}' carries no written reason worth falsifying. The reason going stale " +
                    "while the view is still absent is the one case CI cannot catch; the text is what a " +
                    "human re-reads at the next kickoff.");
            }
        }

        return offenders;
    }

    /// <summary>Every deferral whose view now exists anywhere in the Application assembly.</summary>
    private static IReadOnlyList<string> Expired(IEnumerable<Deferral> deferrals) =>
        deferrals
            .Where(d => ApplicationTypes.Any(t => t.Name.Equals(d.View, StringComparison.Ordinal)))
            .Select(d =>
                $"'{d.View}' is declared deferred to {d.Owner}, but SlayIdleRepeat.Application already " +
                "declares it. Delete the entry in the commit that authored the view — a deferral that " +
                "outlives its subject is an exemption nobody is checking.")
            .ToArray();

    /// <summary>Everything an authored view gets wrong about `30` §12.4 and §12.5.</summary>
    private static IEnumerable<string> BreaksTheConvention(Type authored, ReadModel model)
    {
        var viewInterface = ApplicationTypes.FirstOrDefault(t => t.Name.Equals(ViewInterfaceName, StringComparison.Ordinal));
        var queryInterface = ApplicationTypes.FirstOrDefault(t => t.Name.Equals(QueryInterfaceName, StringComparison.Ordinal));

        if (viewInterface is null || queryInterface is null)
        {
            yield return
                $"'{model.View}' is authored, but {QueriesNamespace} declares no {ViewInterfaceName}/" +
                $"{QueryInterfaceName} for it to take. The convention this rule enforces does not exist.";
            yield break;
        }

        if (!viewInterface.IsAssignableFrom(authored))
        {
            yield return
                $"'{model.View}' does not implement {ViewInterfaceName}, so it declares no staleness " +
                "budget and the UI has nothing to state (30 §12.5 Q4).";
        }
        else if (Budget(authored) is { } budget && budget != model.Budget)
        {
            yield return
                $"'{model.View}' declares a budget of {budget}, and 30 §12.4 states {model.Budget}. " +
                "The cache and the sentence shown to the player both follow the code, so the document " +
                "and the product would then disagree with nobody noticing.";
        }

        foreach (var aggregate in PublicSurface(authored)
                     .Where(t => t.Namespace is { } ns && Under(ns, AggregateNamespace))
                     .Select(t => t.FullName ?? t.Name)
                     .Distinct(StringComparer.Ordinal))
        {
            yield return
                $"'{model.View}' exposes '{aggregate}' on its public surface. A query port returns VIEW " +
                "MODELS, never aggregates (30 §13 amending 23 §4.2) — an aggregate handed out of a read " +
                "is a rule one call away from a query.";
        }

        if (!ApplicationTypes.Any(t => ClosesOver(t, queryInterface, authored)))
        {
            yield return
                $"'{model.View}' is reached through no {QueryInterfaceName}. A view with no port " +
                "declares no routing and no budget at the seam a caller actually holds (30 §12.5 Q4).";
        }
    }

    /// <summary>The value of a type's static <c>StalenessBudget</c>, or <c>null</c> when it declares none.</summary>
    private static TimeSpan? Budget(Type type) =>
        type.GetProperty(BudgetProperty, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as TimeSpan?;

    /// <summary>The value of a type's static <c>Routing</c>, or <c>null</c> when it declares none.</summary>
    private static object? Routing(Type type) =>
        type.GetProperty(RoutingProperty, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

    /// <summary>Every type named on a type's public members: property, method and constructor signatures.</summary>
    private static IEnumerable<Type> PublicSurface(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            foreach (var named in Flatten(property.PropertyType))
            {
                yield return named;
            }
        }

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            foreach (var named in Flatten(method.ReturnType).Concat(method.GetParameters().SelectMany(p => Flatten(p.ParameterType))))
            {
                yield return named;
            }
        }

        foreach (var named in type.GetConstructors().SelectMany(c => c.GetParameters()).SelectMany(p => Flatten(p.ParameterType)))
        {
            yield return named;
        }
    }

    /// <summary>A type and every type its generic arguments and element types name.</summary>
    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments().SelectMany(Flatten))
            {
                yield return argument;
            }
        }

        if (type.HasElementType && type.GetElementType() is { } element)
        {
            foreach (var named in Flatten(element))
            {
                yield return named;
            }
        }
    }

    /// <summary>True when <paramref name="ns"/> is <paramref name="prefix"/> or a namespace below it.</summary>
    private static bool Under(string ns, string prefix) =>
        ns.Equals(prefix, StringComparison.Ordinal) ||
        ns.StartsWith(prefix + ".", StringComparison.Ordinal);

    /// <summary>True when <paramref name="candidate"/> implements the open generic interface at all.</summary>
    private static bool ImplementsOpen(Type candidate, Type openInterface) =>
        candidate.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == openInterface);

    /// <summary>True when <paramref name="candidate"/> implements the open interface closed over <paramref name="view"/>.</summary>
    private static bool ClosesOver(Type candidate, Type openInterface, Type view) =>
        candidate.GetInterfaces().Any(
            i => i.IsGenericType &&
                 i.GetGenericTypeDefinition() == openInterface &&
                 i.GetGenericArguments()[0] == view);

    /// <summary>The Application type with this simple name, which the rules above require to exist.</summary>
    private static Type Required(string simpleName) =>
        ApplicationTypes.FirstOrDefault(t => t.Name.Equals(simpleName, StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            $"SlayIdleRepeat.Application declares no type named '{simpleName}'. Every rule in this file " +
            "keys on it by name, so a rename or a deletion must fail here rather than emptying a subject " +
            "set and going quiet (steering S3).");
}
