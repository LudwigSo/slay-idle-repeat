using Mono.Cecil;
using Shouldly;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using SlayIdleRepeat.Core.Model.Snapshots;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 `30` §11.2 / `23` §2.0a — <c>GameRules.Apply</c> is the only path that produces a NEW
/// persisted state. Nothing outside <c>SlayIdleRepeat.Core</c> builds or edits a
/// <see cref="PlayerSnapshot"/> or a <see cref="RunSnapshot"/>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Why this rule did not exist and now has to.</b> <c>Apply_is_the_only_public_mutation</c>
/// and <c>Handlers_and_Rules_are_internal</c> together make an Application-layer write path
/// uncompilable — every mutator on <c>Player</c> and the run aggregate is <c>internal</c>, and
/// <c>InternalsVisibleTo</c> names <c>Core.Tests</c> alone. Both rules quantify over <c>Core</c>.
/// The snapshots are the one hole they leave: `30` §11.3 makes them <b>public positional
/// records</b> on purpose, so an adapter can hand one to <c>Rehydrate</c> — which also makes
/// <c>loaded with { LegendXp = loaded.LegendXp + 10_000 }</c> compile in any assembly in the
/// solution.
/// </para>
/// <para>
/// Before M5 that shape had nowhere to go: there was no repository to save it to. M5-05 landed
/// <c>IPlayerRepository</c>/<c>IRunStateStore</c> and M5-04 the unit of work, and an edited
/// snapshot handed to <c>SaveAsync</c> is now a complete, durable, server-authoritative write that
/// no rule in this suite sees, no handler authored, no <c>CurrencyChanged</c> accompanies and no
/// state hash disagrees with — because the hash is computed over whatever was saved. It is
/// measured clean today (nothing outside <c>Core</c> names either constructor or either clone),
/// which is exactly when a rule is cheap to add.
/// </para>
/// <para>
/// ⚠️ <b>What this closes and what it does not</b> (steering S18's habit, applied to an IL rule
/// with no <c>const</c> in it). Closed: the <c>newobj</c> of either snapshot's constructor,
/// object-initializer syntax over it, and the <c>with</c>-expression — which the compiler emits as
/// a call to the record's generated <see cref="CloneMethod"/> and nothing else, so the init-only
/// setters need no arm of their own. <b>Not</b> closed: construction through reflection. That is
/// not an oversight — <c>SnapshotCodec</c> deserialises both records with
/// <c>System.Text.Json</c>'s reflection binder, which leaves no call site at all, and it is the
/// intended way stored bytes become a snapshot again. The day that codec moves to a source-
/// generated <c>JsonSerializerContext</c>, the generated partial WILL emit a constructor call in
/// <c>Application</c> and this rule will fire on it: the repair then is an exemption naming the
/// generated type, not a weakening of the subject set.
/// </para>
/// </remarks>
public sealed class SnapshotWritePathRuleTests
{
    /// <summary>The generated method a <c>with</c>-expression calls. The only IL trace the syntax leaves.</summary>
    private const string CloneMethod = "<Clone>$";

    /// <summary>
    /// 🔒 The persisted records, by full name — an <b>identity</b> floor rather than a count
    /// (steering S3). "Every record under <c>Core/Model/Snapshots/</c>" is satisfied by a namespace
    /// that has been renamed out from under the rule; these two names are the ones a repository
    /// round-trips, and each is resolved below so a rename fails here rather than going quiet.
    /// </summary>
    private static readonly string[] PersistedSnapshots =
    {
        "SlayIdleRepeat.Core.Model.Snapshots.PlayerSnapshot",
        "SlayIdleRepeat.Core.Model.Snapshots.RunSnapshot",
    };

    /// <summary>
    /// 🔒 `30` §11.2 — no production assembly outside <c>Core</c> constructs or edits a persisted
    /// snapshot.
    /// </summary>
    [Fact]
    public void No_assembly_outside_Core_builds_or_edits_a_persisted_snapshot()
    {
        var subjects = ProductionAssemblies.AllNames
            .Where(name => !name.Equals(ProductionAssemblies.CoreName, StringComparison.Ordinal))
            .ToArray();

        subjects.ShouldContain(
            ProductionAssemblies.ApplicationName,
            "the Application layer is the assembly this rule exists for — it is the one that holds " +
            "both a loaded snapshot and the repository to save it back to. A subject list that has " +
            "lost it reports success over the only place the bypass is reachable today.");

        subjects.ShouldContain(
            ProductionAssemblies.ServerName,
            "the server composition root reaches the same repositories directly, so it can write a " +
            "snapshot no use case produced.");

        var offenders = SnapshotWrites(
            subjects.SelectMany(name => Il.AllTypes(ProductionAssemblies.Module(name))));

        ArchRule.Empty(
            offenders,
            "GameRules.Apply is the only producer of a new persisted state (30 §11.2): nothing " +
            "outside Core builds or edits a PlayerSnapshot or a RunSnapshot. A snapshot edited " +
            "outside Core and handed to IPlayerRepository.SaveAsync is a durable, authoritative " +
            "write with no command, no handler, no domain event and no rule behind it — and the " +
            "state hash is computed over whatever was saved, so nothing downstream disagrees " +
            "either. Issue a command through Apply and persist what it returns.");
    }

    /// <summary>
    /// `30` §11.2 — the teeth under the rule above (steering S1): both arms driven against real IL,
    /// one from <c>Core</c> and one from this suite's own fixture, plus a negative control that
    /// must stay silent.
    /// </summary>
    /// <remarks>
    /// The construction arm is proven against <c>Core</c> itself, which is the honest positive
    /// control — <c>Player.ToSnapshot</c> really does <c>newobj PlayerSnapshot</c>, so a predicate
    /// that found nothing there is a predicate that would find nothing anywhere. The edit arm has
    /// no subject in the repository at all, by construction: it is the shape that must never be
    /// committed to production, so the fixture is compiled into this assembly and read back as
    /// metadata (<see cref="SuiteAssembly"/>) rather than asserted about in prose.
    /// </remarks>
    [Fact]
    public void The_snapshot_write_path_rule_fires_on_a_construction_and_on_an_edit()
    {
        foreach (var name in PersistedSnapshots)
        {
            var resolved = Il.AllTypes(ProductionAssemblies.CoreModule)
                .SingleOrDefault(t => t.FullName.Equals(name, StringComparison.Ordinal));

            resolved.ShouldNotBeNull(
                $"'{name}' does not resolve in {ProductionAssemblies.CoreName}. The rule is stated " +
                "over these two names, so a renamed or moved snapshot silently narrows it to the " +
                "one that is left. Move the name here in the commit that moves the type.");

            resolved.Methods.Select(m => m.Name).ShouldContain(
                CloneMethod,
                $"'{name}' carries no {CloneMethod}, so it is no longer a record and the " +
                "with-expression arm below governs nothing. If the type became a class, this rule " +
                "needs its setter arm written before this line is deleted.");
        }

        var construction = SnapshotWrites(Il.AllTypes(ProductionAssemblies.CoreModule));

        construction.ShouldContain(
            o => o.Contains("SlayIdleRepeat.Core.Model.Player.", StringComparison.Ordinal) &&
                 o.Contains("PlayerSnapshot..ctor", StringComparison.Ordinal),
            "the Player aggregate builds a PlayerSnapshot with newobj — the construction arm must " +
            "see it. Pinned by identity rather than by count: 'Core produces some offender' is " +
            "cleared by any record construction anywhere in the assembly.");

        construction.ShouldContain(
            o => o.Contains("SlayIdleRepeat.Core.Handlers.StartRun.", StringComparison.Ordinal) &&
                 o.Contains("RunSnapshot..ctor", StringComparison.Ordinal),
            "START_RUN builds the RunSnapshot, and it is the second name PersistedSnapshots lists " +
            "— without this arm the whole rule could narrow to the player record and stay green.");

        var edit = SnapshotWrites(new[] { SuiteAssembly.Type(nameof(SnapshotWritePathFixtures)) });

        edit.ShouldContain(
            o => o.Contains(nameof(SnapshotWritePathFixtures.RenamedWithoutApply), StringComparison.Ordinal) &&
                 o.Contains(CloneMethod, StringComparison.Ordinal),
            "the with-expression arm must see RenamedWithoutApply. If it does not, the compiler no " +
            "longer lowers `with` to a " + CloneMethod + " call and this rule is closing nothing.");

        edit.ShouldNotContain(
            o => o.Contains(nameof(SnapshotWritePathFixtures.OnlyReads), StringComparison.Ordinal),
            "OnlyReads calls a snapshot getter and nothing else. A predicate that fires on it would " +
            "fire on every projection in the repository, and the rule would be reverted rather than " +
            "obeyed.");
    }

    /// <summary>
    /// Every method that constructs or clones one of <see cref="PersistedSnapshots"/>, named
    /// caller-first so the failure points at the write and not at the record.
    /// </summary>
    private static IReadOnlyList<string> SnapshotWrites(IEnumerable<TypeDefinition> types) =>
        types.SelectMany(Il.AllMethods)
             .SelectMany(method => Il.Instructions(method)
                 .Select(instruction => instruction.Operand as MethodReference)
                 .Where(operand => operand is not null &&
                                   PersistedSnapshots.Contains(
                                       operand.DeclaringType.FullName, StringComparer.Ordinal) &&
                                   (operand.Name.Equals(".ctor", StringComparison.Ordinal) ||
                                    operand.Name.Equals(CloneMethod, StringComparison.Ordinal)))
                 .Select(operand =>
                     $"{Il.Describe(method)} calls {operand!.DeclaringType.FullName}.{operand.Name}."))
             .Distinct(StringComparer.Ordinal)
             .OrderBy(o => o, StringComparer.Ordinal)
             .ToArray();
}

/// <summary>
/// The two shapes the rule above is driven against, compiled here rather than committed to
/// <c>Core</c> or to any production assembly.
/// </summary>
/// <remarks>
/// Neither member is invoked: what is asserted about them is their IL, read back through
/// <see cref="SuiteAssembly"/>. A member the compiler failed to emit therefore fails the teeth test
/// loudly instead of leaving it green over nothing.
/// </remarks>
internal static class SnapshotWritePathFixtures
{
    /// <summary>The bypass: a durable field changed with no command, no handler and no event.</summary>
    internal static PlayerSnapshot RenamedWithoutApply(PlayerSnapshot snapshot) =>
        snapshot with { DisplayName = "renamed without Apply" };

    /// <summary>The negative control: reading a snapshot is what every projection does.</summary>
    internal static string OnlyReads(PlayerSnapshot snapshot) => snapshot.DisplayName;
}
