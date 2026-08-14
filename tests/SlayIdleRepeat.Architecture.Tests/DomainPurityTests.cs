using Mono.Cecil;
using Mono.Cecil.Cil;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// `30` §9 — the rules that keep the domain playable in memory.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>M1-12 deleted the sentence that used to be this summary's second half</b>, and it is worth
/// recording what it said because it was the most misleading line in the file: <em>"the subjects M1
/// creates simply yield an empty set today, so each rule turns into a real assertion the moment the
/// type it names appears."</em> True when M0-08 wrote it, false from the day M1 populated the
/// subjects, and sitting on the <em>class</em> — so a reader arriving at any rule in this file was
/// told, before reading it, that the whole file was asleep. Steering <b>S4</b>'s known limit:
/// <em>"an exemption whose reason went stale while still formally valid is not mechanically
/// detectable."</em>
/// </para>
/// <para>
/// 🔒 <b>Every rule here quantifies over real subjects, and each was made to fail on purpose, arm
/// by arm, with the literal output in M1-12's report (steering <b>S1</b>).</b> The counts are
/// deliberately <em>not</em> transcribed here — that is the mistake the deleted sentence made in
/// the other direction, and this milestone made it three times over
/// <c>Core_internal_layering_holds</c>' row count. Read them off the mechanisms that would go red:
/// <c>SubjectSetFloorTests</c>' <c>CoreTypeFloor</c> and its per-name rows, and each rule's own
/// identity floor.
/// </para>
/// <para>
/// ⚠️ <b>What remains subject-less, and it is by design rather than by neglect.</b>
/// <c>Domain_has_no_ambient_time_or_randomness</c>' two <c>IClockPort</c> arms are permanently so
/// <em>by intent</em>: `30` §3 makes that the name which must NEVER appear in <c>Core</c>, so "no
/// subject" is the rule succeeding, not a "not yet".
/// <c>Domain_references_no_port_interface</c>' <c>Application.Ports</c> arm is the same shape —
/// no <c>Core</c> type declares or names one. ⚠️ It is <b>not</b> unreachable, and an earlier draft
/// of this paragraph said it was, on the reasoning that a C# TypeRef needs an AssemblyRef to scope
/// it so the assembly arm would always fire first. That is true only of a port <em>referenced</em>
/// across assemblies; a port <b>copied into</b> <c>Core</c> under <c>namespace
/// SlayIdleRepeat.Application.Ports</c> emits no AssemblyRef at all and fires that arm alone —
/// which is precisely the case the rule's own <c>IsPortShaped</c> clause exists for, six lines
/// below it. Adding a false claim to the paragraph that inventories vacuity was the wrong direction
/// for this task and is corrected here rather than left.
/// The <c>Handlers_and_Rules_are_internal</c> exception list is the last: <c>CombatSimulator</c>
/// (M2) and <c>PowerCalculator</c> (`29` §1) do not exist, so the exemption exempts nothing yet.
/// </para>
/// </remarks>
public sealed class DomainPurityTests
{
    /// <summary>Types that make a signature asynchronous. `30` §9 bans all of them from `Core`.</summary>
    private static readonly string[] AsynchronyTypes =
    {
        "System.Threading.Tasks.Task",
        "System.Threading.Tasks.Task`1",
        "System.Threading.Tasks.ValueTask",
        "System.Threading.Tasks.ValueTask`1",
        "System.Threading.Tasks.TaskCompletionSource",
        "System.Threading.Tasks.TaskCompletionSource`1",
        "System.Threading.CancellationToken",
        "System.Threading.CancellationTokenSource",
        "System.Collections.Generic.IAsyncEnumerable`1",
        "System.Collections.Generic.IAsyncEnumerator`1",
        "System.IAsyncDisposable",
    };

    /// <summary>
    /// `30` §9 — the domain is synchronous: no `Task`, `ValueTask`, `async`,
    /// `CancellationToken` or `IAsyncEnumerable` in any public or private signature in
    /// `SlayIdleRepeat.Core`, nor anywhere in a method body. IL/metadata scan over member
    /// signatures, locals and IL operands, plus the `AsyncStateMachineAttribute` the compiler
    /// stamps on every `async` method.
    /// </summary>
    /// <remarks>
    /// The body scan is the half that was missing. A signature scan sees a method that
    /// RETURNS a `Task`; it does not see one that starts work and drops it —
    /// `_ = Task.Run(Recalculate);` has a `void` signature, no async state machine, and
    /// launches a thread inside a domain `30` §9 requires to be deterministic and
    /// replayable. `Il.ReferencedTypeNames` already walks locals and operands.
    /// </remarks>
    [Fact]
    public void Domain_is_synchronous()
    {
        var banned = new HashSet<string>(AsynchronyTypes, StringComparer.Ordinal);
        var offenders = new List<string>();

        foreach (var type in Domain.CoreTypes)
        {
            offenders.AddRange(
                Il.ReferencedTypeNames(type)
                  .Where(banned.Contains)
                  .Select(n => $"{type.FullName} names {n} somewhere in its members or their bodies"));

            foreach (var field in type.Fields)
            {
                offenders.AddRange(
                    Il.Flatten(field.FieldType)
                      .Where(r => banned.Contains(r.FullName))
                      .Select(r => $"{Il.Describe(field)} : {r.FullName}"));
            }

            foreach (var property in type.Properties)
            {
                offenders.AddRange(
                    Il.Flatten(property.PropertyType)
                      .Where(r => banned.Contains(r.FullName))
                      .Select(r => $"{type.FullName}.{property.Name} : {r.FullName}"));
            }

            foreach (var method in type.Methods)
            {
                offenders.AddRange(
                    Il.SignatureTypes(method).SelectMany(Il.Flatten)
                      .Where(r => banned.Contains(r.FullName))
                      .Select(r => $"{Il.Describe(method)} : {r.FullName}"));

                if (method.CustomAttributes.Any(a =>
                        a.AttributeType.FullName == "System.Runtime.CompilerServices.AsyncStateMachineAttribute"))
                {
                    offenders.Add($"{Il.Describe(method)} is an async method");
                }
            }
        }

        ArchRule.Empty(offenders, "The domain is synchronous — no Task/ValueTask/async/CancellationToken in Core (30 §9).");
    }

    /// <summary>
    /// `30` §9 — the `Core`-scoped form of the ambient-API ban (`14` §8.1), extended to
    /// ban `IClockPort` itself appearing in `Core`. Time enters the domain only as
    /// `GameContext.NowUtc` (`30` §3); a rule that calls a clock is not pure.
    /// </summary>
    [Fact]
    public void Domain_has_no_ambient_time_or_randomness()
    {
        var offenders = BannedApi.Violations(ProductionAssemblies.CoreModule).ToList();

        offenders.AddRange(
            Domain.CoreTypes
                  .Where(t => t.Name.Equals(Domain.ClockPortType, StringComparison.Ordinal))
                  .Select(t => $"{t.FullName} — IClockPort must not exist in Core (30 §9)"));

        offenders.AddRange(
            Domain.CoreTypes
                  .SelectMany(t => Il.ReferencedTypeNames(t).Select(n => (Type: t, Name: n)))
                  .Where(x => x.Name.EndsWith("." + Domain.ClockPortType, StringComparison.Ordinal) ||
                              x.Name.Equals(Domain.ClockPortType, StringComparison.Ordinal))
                  .Select(x => $"{x.Type.FullName} names {x.Name} — Core must not know about a clock port (30 §9)"));

        ArchRule.Empty(offenders, "The domain has no ambient time or randomness, and no clock port (30 §9, 14 §8.1).");
    }

    /// <summary>
    /// `30` §9 — the domain references no port interface: `Core` must not name any type
    /// from `SlayIdleRepeat.Application/Ports/`, nor reference the `Application` assembly.
    /// </summary>
    [Fact]
    public void Domain_references_no_port_interface()
    {
        var offenders = new List<string>();

        offenders.AddRange(
            ProductionAssemblies.CoreModule.AssemblyReferences
                .Where(r => r.Name.StartsWith("SlayIdleRepeat.", StringComparison.Ordinal))
                .Select(r => $"SlayIdleRepeat.Core references assembly '{r.Name}'"));

        foreach (var type in Domain.CoreTypes)
        {
            offenders.AddRange(
                Il.ReferencedTypeNames(type)
                  .Where(n => n.StartsWith(Domain.PortsNamespace + ".", StringComparison.Ordinal) ||
                              n.StartsWith(ProductionAssemblies.ApplicationName + ".", StringComparison.Ordinal))
                  .Select(n => $"{type.FullName} names {n}"));

            // A port copied into Core rather than referenced is the same violation.
            offenders.AddRange(
                Il.ReferencedTypeNames(type)
                  .Where(IsPortShaped)
                  .Select(n => $"{type.FullName} names the port-shaped type {n}"));
        }

        ArchRule.Empty(offenders, "Core names nothing from Application/Ports/ (30 §9).");
    }

    /// <summary>
    /// `30` §9 — every `GameCommand` subtype is handled: no silently unhandled command.
    /// The dispatch surface is `GameRules` plus everything under `Core/Handlers/`
    /// (`30` §11.4, one handler per command); a concrete command no type on that surface
    /// mentions has no way of being applied.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>LIVE over 49 concrete subtypes since M1-02</b>, which landed `14` §2.3's whole registry.
    /// M0-08 wrote this against a subject set that did not exist; M1-06 landed the base and the
    /// dispatch table, which made the rule stop short-circuiting while it still quantified over zero
    /// subtypes; M1-02 filled it. Measured on that branch: a fiftieth command declared without a
    /// dispatch row fails naming it. The early-return arm below is now the guard for an assembly
    /// with no command hierarchy at all, not a "not yet".
    /// <para>
    /// 🔒 <b>M1-09 widened the dispatch surface for the first time, and the sentence that used to end
    /// this remark — "the dispatch surface is <c>GameRules</c> alone until M1-09" — is corrected
    /// rather than left to go stale (steering <b>S4</b>'s known limit).</b> <c>Core/Handlers/</c>
    /// holds <c>BeginSession</c>, so the surface is two types; <c>GameRules</c> still names 48 of the
    /// 49 and renaming it would drop those out of the dispatched set at once.
    /// </para>
    /// <para>
    /// ⚠️ <b>What the widening costs, stated so the name does not overpromise</b> (steering
    /// <b>S1</b>). The claim is <em>"some type on the dispatch surface names this command"</em>, not
    /// <em>"the dispatch table has a row for it"</em>: a command named only by a handler, with no
    /// row, would read as dispatched here. That is unreachable while a handler is only ever named
    /// <em>from</em> a row, and the independent check on the other side is
    /// <c>SlayIdleRepeat.Core.Tests.Commands.CommandVocabularyTests</c>, which pins the registry's 49
    /// wire names against a hand-transcribed list in both directions.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_command_type_is_handled_by_Apply()
    {
        var gameCommand = Domain.FindInCore(Domain.GameCommandType);
        if (gameCommand is null)
        {
            // No command hierarchy AT ALL: the set of unhandled commands is empty and the rule
            // holds. Unreachable on this repository since M1-06 — GameCommand has existed since
            // then and 49 concrete subtypes since M1-02 — and kept as the guard for an assembly
            // that genuinely has none, not as a "not yet".
            ArchRule.Empty(Array.Empty<string>(), UnhandledCommandRule);
            return;
        }

        var commands = Domain.CoreTypes
            .Where(t => !t.IsAbstract && !t.IsInterface && !Domain.IsCompilerGenerated(t))
            .Where(t => Domain.DerivesFrom(t, Domain.GameCommandType))
            .ToArray();

        var dispatchSurface = Domain.CoreTypes
            .Where(t => Il.IsUnder(Il.NamespaceOf(t), Domain.HandlersNamespace) ||
                        t.Name.Equals(Domain.GameRulesType, StringComparison.Ordinal))
            .ToArray();

        var dispatched = new HashSet<string>(
            dispatchSurface.SelectMany(Il.ReferencedTypeNames),
            StringComparer.Ordinal);

        var offenders = commands
            .Where(c => !dispatched.Contains(c.FullName))
            .Select(c => $"{c.FullName} is never named by GameRules or by any type under {Domain.HandlersNamespace}");

        ArchRule.Empty(offenders, UnhandledCommandRule);
    }

    /// <summary>
    /// 🔒 `14` §8.1 / M1 kickoff decision 5 — <c>DeterministicRng</c> is constructed <b>only</b>
    /// inside <c>Core/Rng/</c>. A handler never opens a stream of its own; it draws through the
    /// <c>RunRngScope</c> <c>GameRules.Apply</c> hands it, and <c>Apply</c> folds the scope's final
    /// positions back into the <c>Run</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The failure this closes is silent, and that is why it needs a rule rather than a review.</b>
    /// `14` §8.1 makes the persisted stream position the draw counter. A handler that wrote
    /// <c>new DeterministicRng(run.RunSeed, RngStreams.Dice)</c> would draw perfectly valid,
    /// perfectly deterministic values — from draw 0, every time, with nothing written back. Nothing
    /// throws, no test fails, the numbers look random, and the run replays differently for the rest
    /// of its life. The client/server parity test (`14` §13) would then be comparing two universes
    /// and reporting on neither.
    /// </para>
    /// <para>
    /// 🔒 <b>Scoped to <c>Core/Rng/</c> rather than to the scope type alone</b>, because that is where
    /// the scope lives and where <c>DeterministicRng</c> itself is. What matters is that no
    /// <em>handler</em>, no <em>rule</em> and no <em>aggregate</em> can open a stream — and the
    /// counter-model is <c>Core/Rng/</c>'s to own.
    /// </para>
    /// <para>
    /// ⚠️ It is an <b>IL</b> rule, not an accessibility one. <c>DeterministicRng</c>'s constructor
    /// stays public: `14` §2.4 has the client simulate a battle from a server-issued
    /// <c>battleSeed</c>, and the public seam for that is <c>CombatSimulator</c>. Accessibility could
    /// only stop callers <em>outside</em> the assembly; every caller this rule is about is inside it.
    /// </para>
    /// <para>
    /// 🔒 <b>M1-09 landed the SECOND sanctioned construction site and the floor grew with it.</b>
    /// <c>MetaDrawScope</c> is `14` §8.1's meta regime — <c>Hash64(CommandSeed, s, i)</c> from
    /// <c>i = 0</c> with no persisted counter — and it is the <em>only</em> site of that regime.
    /// ⚠️ The summary above still says "a handler draws through the <c>RunRngScope</c> <c>Apply</c>
    /// hands it" — true of a <c>CommandKind.Run</c> handler and only of one; a meta handler draws
    /// through <c>HandlerInput.MetaDraws</c>, which folds nothing back because there is nothing to
    /// fold.
    /// </para>
    /// <para>
    /// 🔒 <b>The M1/M2 merge landed the THIRD — <c>BattleRngScope</c>, `14` §8.1's combat regime.</b>
    /// M2 built <c>BattleSimulation</c> and <c>EncounterFight</c> on a branch parallel to this rule's
    /// own milestone, and both constructed the combat stream directly; the rule was never wrong, it
    /// simply never ran against that code until the merge, at which point it caught exactly the gap
    /// it exists to catch. All three names are asserted, because a floor naming fewer than three
    /// sanctioned sites is satisfied while one of them stops constructing anything at all — the
    /// <c>Run::_wallet</c> argument this file makes about itself one rule up.
    /// </para>
    /// <para>
    /// 🔒 <b>The floor, pinned by identity</b> (steering S3). The subject set is "construction sites
    /// in <c>Core</c>", which becomes empty the moment a scope stops constructing one — at which
    /// point the rule would report success forever over a domain that had lost a sanctioned draw
    /// path. A count-only floor is satisfied by any construction anywhere, so the assertion names
    /// each scope individually: moving one out of <c>Core/Rng/</c>, or having it stop opening
    /// streams, fails here rather than quietly.
    /// </para>
    /// </remarks>
    [Fact]
    public void DeterministicRng_is_constructed_only_inside_Core_Rng()
    {
        var sites = Il.MethodsWithBodies(ProductionAssemblies.CoreModule)
            .Where(ConstructsADeterministicRng)
            .ToArray();

        // 🔒 ALL THREE sanctioned sites, by identity (steering S3). RunRngScope is 14 §8.1's run
        // regime, MetaDrawScope is its meta one, and BattleRngScope is its combat one; a floor naming
        // fewer than three sanctioned sites stays satisfied while one of them stops constructing
        // anything at all. Asserted separately so a failure names which one went.
        Assert.Contains(
            sites,
            m => m.DeclaringType.Name.Equals(Domain.RunRngScopeType, StringComparison.Ordinal));

        Assert.Contains(
            sites,
            m => m.DeclaringType.Name.Equals(Domain.MetaDrawScopeType, StringComparison.Ordinal));

        Assert.Contains(
            sites,
            m => m.DeclaringType.Name.Equals(Domain.BattleRngScopeType, StringComparison.Ordinal));

        var offenders = sites
            .Where(m => !Il.IsUnder(Il.NamespaceOf(m.DeclaringType), Domain.RngNamespace))
            .Select(m =>
                $"{Il.Describe(m)} constructs a {Domain.DeterministicRngType}. 14 §8.1 makes the PERSISTED " +
                "stream position the draw counter, so a stream opened outside Core/Rng/ draws from " +
                "index 0 with nothing to write its counter back — valid-looking, deterministic, and " +
                "silently unreproducible for the rest of the run. Draw through the RunRngScope " +
                "GameRules.Apply hands the handler; Apply folds the final positions into the Run " +
                "(M1 kickoff decision 5).");

        ArchRule.Empty(
            offenders,
            $"{Domain.DeterministicRngType} is constructed only inside {Domain.RngNamespace} — every in-run " +
            "draw goes through the RunRngScope, and Apply owns the write-back (14 §8.1).");
    }

    /// <summary>
    /// 🔒 `14` §8.1 — the teeth of the predicate above: it must recognise a <c>newobj</c> on the
    /// stream type and <b>refuse</b> a method that merely mentions one.
    /// </summary>
    /// <remarks>
    /// Driven against this assembly's own IL, because the shape that matters — a method that holds a
    /// <c>DeterministicRng</c> without opening one — is exactly what a legitimate future handler
    /// looks like, and a rule that flagged it would be weakened back out within a commit.
    /// </remarks>
    [Fact]
    public void The_stream_construction_check_recognises_a_newobj_and_refuses_a_mention()
    {
        Assert.True(
            ConstructsADeterministicRng(Fixture(nameof(CurrencyEmissionFixtures.OpensAStream))),
            "a method that calls the constructor opens a stream. If this is false the rule above is " +
            "matching nothing and Core/Rng/ is the only place it appears to look.");

        Assert.False(
            ConstructsADeterministicRng(Fixture(nameof(CurrencyEmissionFixtures.OnlyDrawsFromOne))),
            "drawing from a stream the method was HANDED is not opening one — it is precisely what a " +
            "handler is supposed to do with the scope's stream. If this is true the rule forbids the " +
            "sanctioned path and would be deleted rather than obeyed.");
    }

    /// <summary>True when a method body contains a <c>newobj</c> on <c>DeterministicRng</c>.</summary>
    private static bool ConstructsADeterministicRng(MethodDefinition method) =>
        Il.Instructions(method).Any(i =>
            i.OpCode == OpCodes.Newobj &&
            i.Operand is MethodReference reference &&
            reference.DeclaringType.Name.Equals(Domain.DeterministicRngType, StringComparison.Ordinal));

    /// <summary>
    /// 🔒 `30` §9 / §7 — every currency mutation emits `CurrencyChanged`. IL scan: a write
    /// to a currency-carrying field may only happen inside a method that also emits the
    /// event. Construction and rehydration are exempt — they rebuild state rather than
    /// move currency (`30` §11.3).
    /// </summary>
    /// <remarks>
    /// <b>LIVE since M1-04</b>, which declared `Player._wallet` — the first field a currency is
    /// <b>held</b> in (see <see cref="CurrencyFields"/> for why an event does not count). It was
    /// written in M0-08 against a subject set that did not exist yet and passed vacuously until
    /// that commit.
    /// <para>
    /// 🔒 <b>M1-05 added the second subject, <c>Run._wallet</c>, and it is recognised by NAME
    /// ALONE.</b> A run's Gold is a bare <c>long</c>, so the type half of
    /// <see cref="CurrencyFields"/>'s predicate cannot see it; the aggregate is named <c>_wallet</c>
    /// rather than <c>_gold</c> precisely so the name half does. Both floors below are therefore
    /// load-bearing, and the second one more so than the first.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_currency_mutation_emits_CurrencyChanged()
    {
        var currencyFields = CurrencyFields().ToHashSet(StringComparer.Ordinal);

        // 🔒 The floor, not a sentinel (steering S3). This used to be `if (count == 0) { pass; }`,
        // which was right while the set was genuinely empty and became the rule's own blind spot
        // the moment it was not: a change to Il.Flatten, to the Core/Events/ exclusion or to
        // _wallet's declared type would empty the set again and this rule would report success
        // forever, with nothing else in the repository noticing.
        Assert.NotEmpty(currencyFields);
        Assert.Contains(
            currencyFields,
            name => name.Contains("Player::_wallet", StringComparison.Ordinal));

        // 🔒 M1-05's floor row, and it is load-bearing in a way Player's is not. CurrencyFields()
        // recognises Run's Gold ONLY by its field NAME: it is a bare `long`, so the type half of the
        // predicate (CurrencyId-typed, or a type name containing "Wallet") cannot see it at all.
        // Renaming `Run::_wallet` to the more obvious `_gold` would therefore drop the game's only
        // RUN-scoped currency out of this rule's subject set silently — the rule would still be
        // non-empty, still be pointed at Player, and still report success while nothing watched a
        // run's purse. That is steering S3's failure mode arriving through a rename, and this line is
        // what turns it into a build failure.
        Assert.Contains(
            currencyFields,
            name => name.Contains("Run::_wallet", StringComparison.Ordinal));

        var offenders = new List<string>();

        foreach (var method in Il.MethodsWithBodies(ProductionAssemblies.CoreModule))
        {
            var written = Il.Instructions(method)
                .Where(i => i.OpCode == OpCodes.Stfld || i.OpCode == OpCodes.Stsfld)
                .Select(i => (i.Operand as FieldReference)?.FullName)
                .Where(name => name is not null && currencyFields.Contains(name))
                .Select(name => name!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (written.Length == 0 || IsRehydrationOrConstruction(method) || EmitsCurrencyChanged(method))
            {
                continue;
            }

            offenders.AddRange(
                written.Select(field =>
                    $"{Il.Describe(method)} writes {field} without emitting {Domain.CurrencyChangedEvent}"));
        }

        ArchRule.Empty(offenders, CurrencyRule);
    }

    /// <summary>
    /// 🔒 `30` §7 / `30` §9 — a <c>CurrencyChanged</c> that is produced and then <b>dropped at its
    /// call site</b> fails the build: a <c>call</c>/<c>callvirt</c> returning the event, immediately
    /// followed by <c>pop</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>Why this rule exists: <c>Every_currency_mutation_emits_CurrencyChanged</c> cannot see
    /// it.</b> That rule is an IL scan for a <c>newobj</c> on the event in the same method body as
    /// the <c>stfld</c>, and it is satisfied the moment the <em>aggregate</em> constructs one. M1-06
    /// left <c>GameRules.AdvanceTime</c> returning <c>void</c> while <c>Player.AccrueEnergy</c>
    /// returns a <c>CurrencyChanged</c> — so an accrual there would have satisfied `30` §9 in full
    /// while `14` §7.1's economy log and `21` §8.3's <c>income_attribution.csv</c> (risk <b>R10</b>)
    /// never saw the row, with every suite in the repository green. That is carried-forward item 11,
    /// and this is the rule that would have caught it.
    /// </para>
    /// <para>
    /// 🔒 <b>State precisely what it covers, so the name does not overpromise</b> (steering
    /// <b>S1</b>). It covers <em>dropped at the call site</em> — the literal
    /// <c>player.AccrueEnergy(…);</c> statement. It does <b>not</b> prove the event reaches
    /// <c>CommandResult.Events</c>: a method that assigns the return to a local and then
    /// conditionally drops it still passes here, because whole-program dataflow is what that would
    /// need and IL metadata is what this suite has.
    /// <c>GameRulesCatchUpTests</c> in <c>SlayIdleRepeat.Core.Tests</c> is what asserts the event
    /// actually arrives in the result.
    /// </para>
    /// <para>
    /// 🔒 <b>The floor is by identity, not by count</b> (steering <b>S3</b>). A rule whose subject
    /// set is "methods returning <c>CurrencyChanged</c>" could be emptied by a rename, a signature
    /// change or a move, and would then report success forever over a domain where every currency
    /// event was being dropped. The four producers below are named, so emptying the set is a build
    /// failure rather than a silence.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_currency_event_is_never_discarded_at_its_call_site()
    {
        var producers = Il.MethodsWithBodies(ProductionAssemblies.CoreModule)
            .Where(m => NamesTheEvent(m.ReturnType))
            .Select(m => $"{m.DeclaringType.Name}::{m.Name}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // 🔒 The floor (steering S3), asserted as a SET DIFFERENCE rather than one Assert.Contains
        // per name inside a foreach: an assertion in a loop reports only the FIRST producer that
        // went missing, and the interesting failure is the refactor that took several with it.
        // ArchRule.Empty is the file's own idiom for "this set must be empty, and here is why".
        Assert.NotEmpty(producers);
        ArchRule.Empty(
            CurrencyProducerFloor
                .Except(producers, StringComparer.Ordinal)
                .Select(missing =>
                    $"{missing} no longer returns {Domain.CurrencyChangedEvent}, so it has dropped out of " +
                    "the set this rule quantifies over"),
            DiscardFloorRule);

        var offenders = Il.MethodsWithBodies(ProductionAssemblies.CoreModule)
            .Where(DiscardsACurrencyEvent)
            .Select(m =>
                $"{Il.Describe(m)} calls something that returns {Domain.CurrencyChangedEvent} and pops the " +
                "result. 30 §7 makes every currency movement emit an attributed event, and 21 §8.3's " +
                "income_attribution.csv is a query over those events — a row that is constructed and " +
                "dropped satisfies the IL emission rule (the aggregate DID build one) while the report " +
                "that answers risk R10 never sees the movement. Return it, or hand it to whatever " +
                "assembles CommandResult.Events.");

        ArchRule.Empty(offenders, DiscardRule);
    }

    /// <summary>
    /// 🔒 `30` §7 / `30` §9 — the teeth of the rule above, driven against real IL compiled from
    /// <see cref="CurrencyEmissionFixtures"/>: it must recognise a popped return and refuse both a
    /// consumed one and a popped return of something that is not an event.
    /// </summary>
    /// <remarks>
    /// The two negative cases are the point. Without the "consumed" case the predicate could match
    /// every call to a producer and would be weakened back out within a commit —
    /// <c>Player.AccrueEnergy</c> calls <c>SetEnergy</c>, which returns one. Without the
    /// "something else" case it could be matching <c>pop</c> alone, which C# emits for every
    /// discarded return value in the assembly.
    /// </remarks>
    [Fact]
    public void The_discard_check_recognises_a_popped_return_and_refuses_a_consumed_one()
    {
        Assert.True(
            DiscardsACurrencyEvent(Fixture(nameof(CurrencyEmissionFixtures.DropsTheEventItProduced))),
            "a statement that calls a producer and keeps nothing is a discarded currency event — the " +
            "literal `player.AccrueEnergy(…);` shape carried-forward item 11 describes. If this is " +
            "false the rule above is matching nothing and reports success forever.");

        Assert.False(
            DiscardsACurrencyEvent(Fixture(nameof(CurrencyEmissionFixtures.KeepsTheEventItProduced))),
            "reading the event a call produced is not discarding it. If this is true the rule flags " +
            "Player.AccrueEnergy — which calls SetEnergy for its event — and gets deleted rather than " +
            "obeyed.");

        Assert.False(
            DiscardsACurrencyEvent(Fixture(nameof(CurrencyEmissionFixtures.DropsSomethingElse))),
            "popping a long is not popping a CurrencyChanged. If this is true the predicate is keyed " +
            "on the pop alone and would flag every discarded return value in Core.");
    }

    /// <summary>
    /// 🔒 The identity floor under <see cref="A_currency_event_is_never_discarded_at_its_call_site"/>:
    /// the `30` §7 producers whose returns that rule exists to watch.
    /// </summary>
    /// <remarks>
    /// <c>Player.AccrueEnergy</c> is the one M1-08 is about — it is what the catch-up calls, and the
    /// one whose event M1-06's <c>void</c> seam dropped. The other three are named so a rename of
    /// any single producer cannot quietly shrink the set the rule quantifies over.
    /// </remarks>
    private static readonly string[] CurrencyProducerFloor =
    {
        "Player::MoveCurrency",
        "Player::SetEnergy",
        "Player::AccrueEnergy",
        "Run::MoveCurrency",
    };

    /// <summary>True when a method body pops the return value of a call that answers a `CurrencyChanged`.</summary>
    private static bool DiscardsACurrencyEvent(MethodDefinition method) =>
        Il.Instructions(method).Any(IsDiscardedCurrencyReturn);

    /// <summary>
    /// True for a single <c>call</c>/<c>callvirt</c> that returns a <c>CurrencyChanged</c> and whose
    /// very next instruction is <c>pop</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b><c>Next</c>, not "somewhere later".</b> C# compiles a discarded expression statement to
    /// exactly this pair, and widening the window would flag a method that legitimately produced an
    /// event and separately discarded some other value.
    /// </remarks>
    private static bool IsDiscardedCurrencyReturn(Instruction instruction) =>
        (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
        instruction.Operand is MethodReference reference &&
        NamesTheEvent(reference.ReturnType) &&
        instruction.Next is { } next &&
        next.OpCode == OpCodes.Pop;

    /// <summary>
    /// 🔒 `30` §7 / `30` §9 — the teeth of the event exclusion the rule above rests on. It must
    /// recognise the event hierarchy and refuse everything else, or the currency subject set is
    /// being emptied by something other than what the remark claims (steering S3).
    /// </summary>
    /// <remarks>
    /// The exclusion kept <c>CurrencyFields()</c> genuinely empty from M1-03 until M1-04 landed
    /// <c>Player._wallet</c>. It still matters now that the rule is live, for the opposite reason:
    /// an <c>IsDomainEvent</c> that answered <c>true</c> for everything would empty the set again —
    /// and while the empty case used to pass through a `count == 0` sentinel, it now trips the
    /// floor in <c>Every_currency_mutation_emits_CurrencyChanged</c> instead, so this teeth-check
    /// is what says <b>which</b> half broke.
    /// </remarks>
    [Fact]
    public void The_event_exclusion_recognises_the_hierarchy_and_nothing_else()
    {
        Assert.True(
            Domain.IsDomainEvent(Require(Domain.DomainEventType)),
            "DomainEvent is the base of the 30 §7 hierarchy. If this is false the exclusion matches nothing " +
            "and CurrencyChanged's CurrencyId-typed backing field is back in the subject set.");

        Assert.True(
            Domain.IsDomainEvent(Require(Domain.CurrencyChangedEvent)),
            "CurrencyChanged is a DomainEvent under Core/Events/ — the one type this exclusion exists for.");

        Assert.False(
            Domain.IsDomainEvent(Require(Domain.CurrencyIdType)),
            "CurrencyId is a Primitives enum, not an event. An exclusion that swallowed it would swallow " +
            "every currency-carrying type M1-04 declares.");

        Assert.False(
            Domain.IsDomainEvent(Require(Domain.EntitlementsType)),
            "Entitlements sits in the Core root and derives from nothing. This pins that the predicate is " +
            "namespace-scoped and base-typed rather than answering true for whatever it is handed.");

        Assert.False(
            Domain.IsDomainEvent(TypeFixture(nameof(CurrencyEmissionFixtures.DerivesButIsMisplaced))),
            "a type that derives from DomainEvent but does NOT live under Core/Events/ must not be excluded. " +
            "Drop the namespace half and a Core/Model/ aggregate could exempt its own wallet from " +
            "Every_currency_mutation_emits_CurrencyChanged by inheriting from an event — nothing else in this " +
            "suite forbids that inheritance.");
    }

    /// <summary>
    /// 🔒 `30` §7 / `30` §9 — the teeth of the emission half. Driven against real IL compiled
    /// from <see cref="CurrencyEmissionFixtures"/> and read back with Cecil, because the shape
    /// that matters — a method that *reads* an event while mutating a balance — does not exist in
    /// `Core` and must never have to.
    /// </summary>
    /// <remarks>
    /// The negative case is the whole point. Until this branch narrowed it,
    /// <see cref="EmitsCurrencyChanged"/> answered <c>true</c> for
    /// <see cref="CurrencyEmissionFixtures.OnlyReads"/> — <c>Il.OperandTypes</c> yields the
    /// *declaring* type of every member reference, so touching an event counted as emitting one.
    /// </remarks>
    [Fact]
    public void The_emission_check_recognises_a_constructed_event_and_refuses_one_that_is_only_read()
    {
        Assert.True(
            EmitsCurrencyChanged(Fixture(nameof(CurrencyEmissionFixtures.Emits))),
            "a method that constructs a CurrencyChanged emits one. If this is false the rule flags every " +
            "legitimate grant M1-04 onwards and gets weakened back out again.");

        Assert.False(
            EmitsCurrencyChanged(Fixture(nameof(CurrencyEmissionFixtures.OnlyReads))),
            "reading Delta off an event the method was handed is not emitting one. If this is true, a handler " +
            "that debits a balance and inspects any other event satisfies 30 §7 without producing a row for " +
            "21 §8.3's income_attribution.csv.");
    }

    /// <summary>
    /// 🔒 `30` §7 / `30` §9 — the teeth of the <c>init</c>-accessor clause in
    /// <see cref="IsRehydrationOrConstruction"/>. It must cover a record's compiler-generated
    /// <c>init</c> setter and <b>nothing else</b>, or M1-04's wallet has just bought every
    /// currency mutation in the game an exemption.
    /// </summary>
    /// <remarks>
    /// The negative half is the point, and it is driven against real IL: an author-written method
    /// that writes a currency field is still caught, and so is an ordinary <c>set</c> accessor —
    /// which is what stops "make it an ordinary property" from being the workaround.
    /// </remarks>
    [Fact]
    public void The_construction_exemption_covers_a_records_init_accessor_and_nothing_else()
    {
        var snapshotLike = Il.AllTypes(OwnModule)
            .Single(t => t.Name.Equals(nameof(CurrencyEmissionFixtures.SnapshotLike), StringComparison.Ordinal));

        var init = snapshotLike.Methods.Single(m => m.Name.Equals("set_Wallet", StringComparison.Ordinal));

        Assert.True(
            Il.IsInitOnlySetter(init),
            "a positional record's component compiles to an init accessor. If this is false the " +
            "detection is looking for the wrong metadata and the clause below exempts nothing.");

        Assert.True(
            IsRehydrationOrConstruction(init),
            "a record's compiler-generated init accessor is construction: C# admits a call to one " +
            "only while an object is being built. Without this, PlayerSnapshot's own wallet " +
            "component fails Every_currency_mutation_emits_CurrencyChanged.");

        Assert.False(
            IsRehydrationOrConstruction(Fixture(nameof(CurrencyEmissionFixtures.MutatesWithoutEmitting))),
            "an ordinary method that writes a currency field is NOT construction. If this is true " +
            "the rule is exempting the very shape it exists to catch.");

        var ordinary = snapshotLike.Methods.Single(m => m.Name.Equals("set_Loose", StringComparison.Ordinal));

        Assert.False(
            Il.IsInitOnlySetter(ordinary),
            "an ordinary `set` accessor is not an init accessor. If this is true, turning an init " +
            "into a settable property is a free exemption.");

        Assert.False(
            IsRehydrationOrConstruction(ordinary),
            "…and it is therefore not construction either.");

        // 🔒 The clause is a CONJUNCTION — [CompilerGenerated] *and* init-only — and this is the
        // only probe that pins the first half. Without it, deleting `Domain.IsCompilerGenerated(
        // method) &&` from IsRehydrationOrConstruction leaves every test in the repository green:
        // set_Loose is refused by the init-only half and MutatesWithoutEmitting is not a setter at
        // all, so nothing would notice that a HAND-WRITTEN init body had just been exempted.
        var handWritten = Il.AllTypes(OwnModule)
            .Single(t => t.Name.Equals(nameof(CurrencyEmissionFixtures.HandWrittenInit), StringComparison.Ordinal))
            .Methods.Single(m => m.Name.Equals("set_WalletBalance", StringComparison.Ordinal));

        Assert.True(
            Il.IsInitOnlySetter(handWritten),
            "a hand-written `init` accessor carries the same IsExternalInit modifier as a record's.");

        Assert.False(
            Domain.IsCompilerGenerated(handWritten),
            "…and it is NOT compiler-generated, which is the whole distinction the conjunction draws.");

        Assert.False(
            IsRehydrationOrConstruction(handWritten),
            "an author-written init body can run arbitrary code while writing a currency field, so " +
            "it is not the mechanical component-assignment a record's synthesized init is. If this " +
            "is true, `init` has become a keyword that buys an exemption from 30 §7.");
    }

    /// <summary>
    /// 🔒 `30` §11.4 — the teeth of <see cref="Domain.IsCompilerGenerated(TypeDefinition)"/>
    /// walking out to its outermost declaring type.
    /// </summary>
    /// <remarks>
    /// The compiler marks <c>&lt;PrivateImplementationDetails&gt;</c> and does not mark the
    /// <c>__StaticArrayInitTypeSize=N</c> types nested in it, so before the walk the first
    /// <c>Core</c> static array initialiser made
    /// <c>Every_Core_type_lives_under_a_documented_namespace</c> fail over a type no author wrote.
    /// Driven against this assembly's own metadata, which has the same shape for the same reason.
    /// </remarks>
    [Fact]
    public void The_compiler_generated_predicate_reaches_a_type_nested_in_a_generated_one()
    {
        // Driven against SlayIdleRepeat.Core itself, which is where the shape actually occurs:
        // Player.WalletCurrencies compiles to a static array initialiser, and that is what emits
        // <PrivateImplementationDetails> and the __StaticArrayInitTypeSize=N nested inside it.
        var details = Domain.CoreTypes
            .SingleOrDefault(t => t.Name.Equals("<PrivateImplementationDetails>", StringComparison.Ordinal));

        Assert.NotNull(details);
        Assert.NotEmpty(details!.NestedTypes);
        Assert.True(Domain.IsCompilerGenerated(details!), "the container itself carries the attribute");

        foreach (var nested in details!.NestedTypes)
        {
            Assert.False(
                Domain.IsCompilerGenerated((ICustomAttributeProvider)nested),
                $"{nested.Name} does NOT carry [CompilerGenerated] itself — that is the whole reason " +
                "the predicate has to walk outwards. If this ever becomes true the walk is untested.");

            Assert.True(
                Domain.IsCompilerGenerated(nested),
                $"{nested.Name} is nested inside a compiler-generated type, so no author wrote it " +
                "and no author can move it out of namespace ''.");
        }

        Assert.False(
            Domain.IsCompilerGenerated(Require(Domain.CurrencyChangedEvent)),
            "an author-written type is not compiler-generated, walk or no walk.");
    }

    /// <summary>
    /// 🔒 `30` §9 — the load-bearing test. `Core`'s assembly closure is exactly
    /// { `SlayIdleRepeat.Core`, `System.*` } **and** `Core` declares the public `InMemoryGame` that
    /// makes that closure the whole game: playable with no Application, no ports, no fakes, no
    /// adapters (`30` §6).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Three arms, and M1-11 corrected the story about which of them were awake.</b> The
    /// <b>closure</b> arm has been asserting over real metadata since M0 — a <c>PackageReference</c>
    /// or <c>ProjectReference</c> added to <c>SlayIdleRepeat.Core.csproj</c> turns it red whoever
    /// names it. The <b>public-harness</b> arm was dead from M0-08 until M1-11, because it sat behind
    /// <c>harness is not null</c> and there was no <c>InMemoryGame</c>. The <b>presence</b> arm below
    /// is M1-11's addition and is the one that makes this rule's <em>name</em> true: without it,
    /// deleting the harness leaves the rule green over a closure claim about an assembly nobody can
    /// play the game from.
    /// </para>
    /// <para>
    /// 🔒 <b>The precedent is one file over.</b>
    /// <c>AccessibilityBoundaryTests.Apply_is_the_only_public_mutation</c> draws exactly this line:
    /// <em>"<c>GameRules</c> existing WITHOUT an <c>Apply</c> is a violation in its own right — a
    /// rule named <c>Apply_is_the_only_public_mutation</c> that stays silent when <c>Apply</c> is
    /// renamed to <c>Handle</c> promises an invariant it is no longer checking."</em> The same
    /// sentence with two nouns changed is why the arm below exists. <c>GapRegister</c>'s `30` §6
    /// transcription and <c>SubjectSetFloorTests</c>' <c>InMemoryGame</c> row are the second and
    /// third mechanisms over the same fact; this one is here because the rule's own name is the
    /// claim.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_whole_game_is_playable_from_Core_alone()
    {
        var offenders = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(ProductionAssemblies.CoreName);

        while (queue.Count > 0)
        {
            var assemblyName = queue.Dequeue();
            if (!visited.Add(assemblyName))
            {
                continue;
            }

            foreach (var reference in ProductionAssemblies.Module(assemblyName).AssemblyReferences)
            {
                if (Il.IsBclAssembly(reference.Name))
                {
                    continue;
                }

                offenders.Add($"{assemblyName} -> {reference.Name}");

                if (ProductionAssemblies.AllNames.Contains(reference.Name, StringComparer.Ordinal))
                {
                    queue.Enqueue(reference.Name);
                }
            }
        }

        // 🔒 The harness must EXIST, and it must be constructible from outside Core — 30 §6 makes it
        // "the only thing tests and the economy simulator need to construct". That the tools build
        // NOTHING ELSE is ProjectFileTests.The_simulation_tools_reference_Core_only's rule, not this
        // one; what this one owns is that the object they build is there and reachable.
        var harness = Domain.FindInCore(Domain.InMemoryGameType);

        if (harness is null)
        {
            // 🔒 M1-11. Without this arm the rule reports success over the ABSENCE of the very type
            // its name is about — the state it was in from M0-08 until M1-11 — because the closure
            // walk above is a true statement about SlayIdleRepeat.Core whether or not anything in it
            // can play the game. Same shape, same reasoning and the same remedy as
            // Apply_is_the_only_public_mutation's "GameRules declares no Apply" arm.
            offenders.Add(
                $"SlayIdleRepeat.Core declares no '{Domain.InMemoryGameType}'. 30 §6 makes it the " +
                "concrete artefact that makes this rule's claim testable — 'the only thing tests and " +
                "the economy simulator need to construct'. Without it the closure below is a true " +
                "statement about an assembly nobody can play the game from, and this rule's NAME is " +
                "an invariant nothing is checking. It landed in M1-11 under Core/Testing/; if it has " +
                "been moved out of Core or renamed, GapRegister's 30 §6 transcription and " +
                "SubjectSetFloorTests' row fail beside this.");
        }
        else if (!harness.IsPublic)
        {
            offenders.Add($"{harness.FullName} is not public — the harness must be usable from outside Core (30 §6)");
        }
        else if (!harness.Methods.Any(m => m.IsConstructor && m.IsPublic && !m.IsStatic))
        {
            // 🔒 M1 REVIEW. Public-and-uninstantiable passed all three arms: making the constructor
            // internal keeps the type public, keeps the closure exact, and makes `30` §6's actual
            // claim — "the only thing tests and the economy simulator need to CONSTRUCT" — false.
            // tools/BalanceHarness and tools/EconomySim reference Core alone and hold no
            // InternalsVisibleTo grant, so they could no longer build one; ProjectFileTests checks
            // their references, not that the object exists for them.
            offenders.Add(
                $"{harness.FullName} declares no public instance constructor — 30 §6 makes it the " +
                "thing tests and the economy simulator CONSTRUCT, and a public type nobody outside " +
                "Core can instantiate is the closure claim without the artefact. tools/EconomySim " +
                "and tools/BalanceHarness reference Core alone and have no InternalsVisibleTo grant.");
        }

        ArchRule.Empty(
            offenders,
            "The whole game is playable from Core alone: Core's assembly closure is exactly " +
            "{ SlayIdleRepeat.Core, System.* }, and Core declares the public InMemoryGame that makes " +
            "that closure the whole game (30 §9, the load-bearing rule).");
    }

    private const string UnhandledCommandRule =
        "Every GameCommand subtype is handled by Apply — no silently unhandled command (30 §9).";

    private const string CurrencyRule =
        "Every currency mutation emits CurrencyChanged (30 §9, 30 §7).";

    private const string DiscardRule =
        "A CurrencyChanged is never produced and dropped at its call site — 21 §8.3's " +
        "income_attribution.csv is a query over the events that reach CommandResult (30 §7, 30 §9).";

    private const string DiscardFloorRule =
        "The discard rule's subject set still contains every 30 §7 currency producer it was written " +
        "over — a rule that could only find zero producers would report success forever (S3).";

    private static bool IsPortShaped(string typeFullName)
    {
        var simpleName = typeFullName.Split('.', '/').Last();
        return typeFullName.Contains(".Ports.", StringComparison.Ordinal) &&
               simpleName.StartsWith("I", StringComparison.Ordinal) &&
               simpleName.EndsWith("Port", StringComparison.Ordinal);
    }

    /// <summary>
    /// Currency-carrying fields: anything typed by (or generic over) the `CurrencyId`
    /// primitive `30` §7 pins, plus anything named for a wallet or a currency. Both
    /// halves are name-based on purpose — the rule must recognise its subject the day
    /// M1 writes it, without M1 having to opt in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The `30` §7 event hierarchy is excluded, and that exclusion is load-bearing.</b>
    /// A `DomainEvent` is the *emission* of a currency movement, never the place one is
    /// held. Without this skip, M1-03's `CurrencyChanged(int, CurrencyId Id, long, string)`
    /// alone made this set non-empty — which would not have woken the rule up, it would just
    /// have taken away the `count == 0` sentinel that was then the only visible signal the
    /// rule was still asleep. The set stayed meaningfully empty until M1-04 put a currency
    /// on the `Player` aggregate; from that commit the sentinel is a FLOOR instead, and the
    /// skip is what stops an event's backing field from being mistaken for a wallet.
    /// </para>
    /// <para>
    /// ⚠️ <b>Not "because constructors are exempt".</b> Measured, not assumed: with the skip
    /// removed, the three methods writing `CurrencyChanged::&lt;Id&gt;k__BackingField` are
    /// its two constructors *and* `set_Id`, the compiler-generated `init` accessor —
    /// `IsRehydrationOrConstruction` does not exempt that one. It passed only because
    /// `EmitsCurrencyChanged` used to count *touching* the type as emitting it. That
    /// predicate has since been narrowed to production (see
    /// <see cref="EmitsCurrencyChanged"/>), which is what makes this exclusion the thing
    /// actually keeping the set empty rather than a second opinion about it.
    /// </para>
    /// <para>
    /// The predicate is <see cref="Domain.IsDomainEvent"/> — namespace-scoped *and* base-typed,
    /// so a `Core/Model/` aggregate cannot exempt its own wallet by deriving from `DomainEvent`.
    /// Its teeth are shown in
    /// <see cref="The_event_exclusion_recognises_the_hierarchy_and_nothing_else"/>.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> CurrencyFields()
    {
        foreach (var type in Domain.CoreTypes)
        {
            if (Domain.IsDomainEvent(type))
            {
                continue;
            }

            foreach (var field in type.Fields)
            {
                if (field.IsLiteral || field.IsStatic)
                {
                    continue;
                }

                var byType = Il.Flatten(field.FieldType)
                               .Any(r => r.Name.Equals(Domain.CurrencyIdType, StringComparison.Ordinal) ||
                                         r.Name.Contains("Wallet", StringComparison.Ordinal));

                var name = field.Name.Trim('<', '>').Replace("k__BackingField", string.Empty, StringComparison.Ordinal);
                var byName = name.Contains("currenc", StringComparison.OrdinalIgnoreCase) ||
                             name.Contains("wallet", StringComparison.OrdinalIgnoreCase);

                if (byType || byName)
                {
                    yield return field.FullName;
                }
            }
        }
    }

    /// <summary>
    /// Construction and rehydration, which rebuild state rather than move currency (`30` §11.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The <c>init</c>-accessor clause, and why it is not a widening of the rule.</b> A
    /// positional record compiles each component to a compiler-generated <c>init</c> setter, and
    /// M1-04's <c>PlayerSnapshot</c> carries the wallet as
    /// <c>IReadOnlyDictionary&lt;CurrencyId, long&gt;</c> — so <c>set_Wallet</c> writes a
    /// currency-carrying field, is not a constructor, and is not named <c>Rehydrate</c>. The rule
    /// fired on it the moment the first snapshot record existed, exactly as
    /// <c>SubjectSetFloorTests</c> predicted it would for <c>CurrencyChanged.set_Id</c>.
    /// </para>
    /// <para>
    /// It is <b>construction</b> by the language's own definition: C# permits a call to an
    /// <c>init</c> accessor only while an object is being constructed — a constructor, an object
    /// initialiser, or a <c>with</c> expression, all of which produce a <i>new</i> instance rather
    /// than moving a balance. So this clause says the same thing <c>method.IsConstructor</c>
    /// already says, about the other half of how a record is built. `30` §11.3's DTOs are the
    /// documented exemption in <c>Apply_is_the_only_public_mutation</c> for the same reason.
    /// </para>
    /// <para>
    /// ⚠️ Kept as narrow as the metadata allows: <b>compiler-generated</b> and <b><c>init</c>-only</b>,
    /// both. An author-written <c>init</c> body, or an ordinary <c>set</c>, is not exempt — and
    /// neither is any method that merely happens to write a currency field. Proven by
    /// <see cref="The_construction_exemption_covers_a_records_init_accessor_and_nothing_else"/>,
    /// and the rule is proven still live on real production code by removing the emission from
    /// <c>Player.MoveBalance</c>, which turns the build red naming that method and that field.
    /// </para>
    /// </remarks>
    private static bool IsRehydrationOrConstruction(MethodDefinition method) =>
        method.IsConstructor ||
        (Domain.IsCompilerGenerated(method) && Il.IsInitOnlySetter(method)) ||
        method.Name.Equals("Rehydrate", StringComparison.Ordinal) ||
        method.Name.Equals("FromSnapshot", StringComparison.Ordinal);

    /// <summary>
    /// 🔒 Whether a method <b>produces</b> a `CurrencyChanged`: it constructs one, or it calls
    /// something that returns one. Not merely mentions one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ This used to be "any operand type named `CurrencyChanged`", which
    /// <c>Il.OperandTypes</c> yields for the <i>declaring</i> type of every field and method
    /// reference — so reading <c>evt.Delta</c> off an event the method was handed counted as
    /// emitting one. Verified against this very branch: `CurrencyChanged.set_Id` writes a
    /// currency-typed field, is not a constructor, and was exempted by that reading alone.
    /// </para>
    /// <para>
    /// The consequence once M1-04 lands the first wallet is the whole rule: a handler that
    /// debits a balance and happens to inspect some *other* event on the way would satisfy
    /// `30` §7's "every currency movement emits `CurrencyChanged`" without emitting anything.
    /// That is the Critical this rule exists to catch, passing green.
    /// </para>
    /// <para>
    /// A <c>call</c> is counted alongside <c>newobj</c> so a factory or a <c>with</c> expression
    /// still reads as production, and <c>Il.Flatten</c> unwraps a returned collection of events.
    /// </para>
    /// <para>
    /// ⚠️ <b>The residual limit, stated so nobody assumes otherwise.</b> A `Core` method that
    /// *returns* an event without building one — a lookup, a passthrough — still reads as
    /// production. Narrowing further was not done because it could not be demonstrated to bite
    /// against any shape that compiles today (steering S1), and a clause nobody can show working
    /// is the defect this whole file is about. Indexing a `List&lt;CurrencyChanged&gt;` is already
    /// excluded: Cecil hands back the open element method, so the return type reads as the
    /// generic parameter rather than the event.
    /// </para>
    /// </remarks>
    private static bool EmitsCurrencyChanged(MethodDefinition method) =>
        Il.Instructions(method).Any(Produces);

    /// <summary>True when a single instruction constructs a `CurrencyChanged` or returns one.</summary>
    private static bool Produces(Instruction instruction)
    {
        if (instruction.Operand is not MethodReference reference)
        {
            return false;
        }

        if (instruction.OpCode == OpCodes.Newobj)
        {
            return NamesTheEvent(reference.DeclaringType);
        }

        return (instruction.OpCode == OpCodes.Call ||
                instruction.OpCode == OpCodes.Callvirt) &&
               NamesTheEvent(reference.ReturnType);
    }

    /// <summary>True when a type reference is, or wraps, the `CurrencyChanged` event.</summary>
    private static bool NamesTheEvent(TypeReference? reference) =>
        Il.Flatten(reference).Any(r => r.Name.Equals(Domain.CurrencyChangedEvent, StringComparison.Ordinal));

    /// <summary>
    /// The `Core` type with this simple name, or a failure that says which rule went silent —
    /// never a silent <c>null</c> that would make a teeth-check pass over nothing.
    /// </summary>
    private static TypeDefinition Require(string simpleName) =>
        Domain.FindInCore(simpleName)
        ?? throw new InvalidOperationException(
            $"SlayIdleRepeat.Core declares no type named '{simpleName}', so the predicate below is being " +
            "driven against nothing. SubjectSetFloorTests tracks this name for exactly that reason.");

    /// <summary>
    /// This assembly, read back through Cecil so <see cref="EmitsCurrencyChanged"/> can be driven
    /// against real IL rather than a hand-built <c>MethodDefinition</c> that could be wrong in the
    /// same direction as the predicate.
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="SuiteAssembly"/> rather than reading the file a second time:
    /// <c>AccessibilityBoundaryTests</c> drives its exposed-collection fixture the same way, and
    /// two spellings of "read my own metadata" are two places to fix (steering S4).
    /// </remarks>
    private static ModuleDefinition OwnModule => SuiteAssembly.Module;

    /// <summary>One fixture method, by name, out of this assembly's own metadata.</summary>
    private static MethodDefinition Fixture(string name) =>
        FixtureHost().Methods.Single(m => m.Name.Equals(name, StringComparison.Ordinal));

    /// <summary>One fixture type, by name, out of this assembly's own metadata.</summary>
    private static TypeDefinition TypeFixture(string name) =>
        FixtureHost().NestedTypes.Single(t => t.Name.Equals(name, StringComparison.Ordinal));

    private static TypeDefinition FixtureHost() =>
        Il.AllTypes(OwnModule)
          .Single(t => t.Name.Equals(nameof(CurrencyEmissionFixtures), StringComparison.Ordinal));

    /// <summary>
    /// The two IL shapes <see cref="EmitsCurrencyChanged"/> has to tell apart. They live here
    /// rather than in `Core` because the one that matters is a violation, and a violation is
    /// never committed to the domain to prove a rule works.
    /// </summary>
    private static class CurrencyEmissionFixtures
    {
        /// <summary>
        /// Opens a stream — a <c>newobj</c> on `DeterministicRng`. The shape
        /// <see cref="DeterministicRng_is_constructed_only_inside_Core_Rng"/> must catch, and it
        /// lives here rather than in `Core` because outside `Core/Rng/` it IS the violation.
        /// </summary>
        internal static object OpensAStream() => new DeterministicRng(1UL, RngStreams.Dice);

        /// <summary>
        /// Draws from a stream it was handed. The shape the rule must NOT catch: this is exactly
        /// what a handler does with `RunRngScope.Stream(...)`.
        /// </summary>
        internal static uint OnlyDrawsFromOne(DeterministicRng handed) => handed.NextUInt();

        /// <summary>Produces an event — a <c>newobj</c> on `CurrencyChanged`.</summary>
        internal static object Emits() =>
            new CurrencyChanged(0, CurrencyId.CROWNS, 1, "architecture_rule_teeth_check");

        /// <summary>
        /// Only reads one. The IL names `CurrencyChanged` as the declaring type of the property
        /// getter, which is precisely the mention the old predicate accepted as an emission.
        /// </summary>
        internal static long OnlyReads(CurrencyChanged handed) => handed.Delta;

        /// <summary>
        /// 🔒 A producer, standing in for `Player.AccrueEnergy`: a method whose RETURN TYPE is the
        /// event. It is what the two fixtures below call, so the pair differ in exactly one thing —
        /// what they do with the answer.
        /// </summary>
        /// <remarks>
        /// The real producers are `internal` to `SlayIdleRepeat.Core`, and this assembly is not the
        /// one `InternalsVisibleTo` names (`30` §11.3), so the shape has to be reproduced here. The
        /// rule itself reads the real ones straight out of `Core`'s metadata.
        /// </remarks>
        internal static CurrencyChanged ProducesAnEvent() =>
            new(0, CurrencyId.CROWNS, 1, "architecture_rule_teeth_check");

        /// <summary>
        /// 🔴 Calls a producer and keeps nothing — <c>call</c> then <c>pop</c>. The literal
        /// `player.AccrueEnergy(…);` shape carried-forward item 11 describes, and the one
        /// <see cref="A_currency_event_is_never_discarded_at_its_call_site"/> must catch.
        /// </summary>
        /// <remarks>
        /// It lives here rather than in `Core` because it IS the violation, and a violation is never
        /// committed to the domain to prove a rule works.
        /// </remarks>
        internal static void DropsTheEventItProduced()
        {
            ProducesAnEvent();
        }

        /// <summary>
        /// Consumes what the producer answered. The shape the rule must NOT catch: it is what every
        /// legitimate caller does, including `Player.AccrueEnergy`, which calls `SetEnergy` for the
        /// event it returns.
        /// </summary>
        internal static long KeepsTheEventItProduced() => ProducesAnEvent().Delta;

        /// <summary>
        /// Pops a return value that is not an event. The shape that keeps the predicate from being
        /// keyed on the <c>pop</c> alone — C# emits one for every discarded return in the assembly.
        /// </summary>
        internal static void DropsSomethingElse()
        {
            NotAnEvent();
        }

        /// <summary>An ordinary value-returning method, so the call above has something to discard.</summary>
        private static long NotAnEvent() => 1L;

        /// <summary>
        /// Derives from `DomainEvent` while living outside `Core/Events/` — the shape the
        /// namespace half of <see cref="Domain.IsDomainEvent"/> exists to refuse. Nested and
        /// non-public, so it is outside every real subject set in the repository.
        /// </summary>
        internal sealed record DerivesButIsMisplaced(int Sequence) : DomainEvent(Sequence);

        /// <summary>
        /// Writes a currency-carrying field from an ordinary method and emits nothing — the shape
        /// <see cref="IsRehydrationOrConstruction"/>'s <c>init</c> clause must NOT exempt.
        /// </summary>
        /// <remarks>
        /// It lives here rather than in `Core` because it is a violation, and a violation is never
        /// committed to the domain to prove a rule works. ⚠️ It is <b>not</b> in
        /// <c>CurrencyFields()</c>'s subject set and does not claim to be — that set is scoped to
        /// <c>Domain.CoreTypes</c> and skips static fields, and this is a static field in the test
        /// assembly. What it drives is the <b>predicate</b>: <c>IsRehydrationOrConstruction</c>
        /// must answer <c>false</c> for an ordinary method that writes one.
        /// </remarks>
        internal static void MutatesWithoutEmitting() => Loose.WalletBalance = 1;

        /// <summary>The static currency field <see cref="MutatesWithoutEmitting"/> writes.</summary>
        internal static class Loose
        {
            /// <summary>A balance, in a field the rule's predicate recognises.</summary>
            internal static long WalletBalance { get; set; }
        }

        /// <summary>
        /// A positional record shaped like <c>PlayerSnapshot</c> — a compiler-generated <c>init</c>
        /// accessor over a <c>CurrencyId</c>-keyed map — beside an ordinary settable property, so
        /// the exemption can be shown to cover the first and not the second.
        /// </summary>
        internal sealed record SnapshotLike(IReadOnlyDictionary<CurrencyId, long> Wallet)
        {
            /// <summary>An ordinary <c>set</c> accessor: not an init, and therefore not construction.</summary>
            internal long Loose { get; set; }
        }

        /// <summary>
        /// 🔒 A <b>hand-written</b> <c>init</c> accessor over a currency-carrying field — the half
        /// of the exemption's conjunction that only this fixture can pin.
        /// </summary>
        /// <remarks>
        /// It carries the same <c>IsExternalInit</c> modifier a record's synthesized accessor does,
        /// so the init-only half of the check cannot tell the two apart; only
        /// <c>[CompilerGenerated]</c> can. An author-written body may run arbitrary code while
        /// writing the field, which is why it is not the mechanical component-assignment the
        /// exemption is for.
        /// </remarks>
        internal sealed class HandWrittenInit
        {
            private long _walletBalance;

            /// <summary>A balance behind an author-written init accessor.</summary>
            internal long WalletBalance
            {
                get => _walletBalance;
                init => _walletBalance = value;
            }
        }
    }
}
