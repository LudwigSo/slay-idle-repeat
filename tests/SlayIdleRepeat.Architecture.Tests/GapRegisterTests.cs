using Shouldly;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// `23` §6 / `30` §7 — the rules that keep <see cref="GapRegister"/> honest. A declared exception
/// must expire by itself (steering S4), and a register that cannot fail is a comment.
/// </summary>
/// <remarks>
/// Three directions, each with its own teeth-check driven against crafted input so the rule is
/// shown to bite without a violation ever being committed: <b>stale</b> (the type it waits for
/// arrived), <b>undeclared</b> (a specified subject nobody claimed), and <b>vacuous</b> (the
/// register's own subject sets, and the two presence predicates every rule here rests on).
/// </remarks>
public sealed class GapRegisterTests
{
    /// <summary>
    /// 🔒 `30` §7 / `23` §6 — no deferral outlives the type that gives it meaning. The moment
    /// <c>DieFace</c>, <c>TileType</c>, <c>GearInstance</c>, <c>LuckService</c> or <c>GuildId</c>
    /// lands in <c>Core</c>, its entry is wrong and this fails.
    /// </summary>
    [Fact]
    public void No_deferral_outlives_the_type_that_gives_it_meaning()
    {
        ArchRule.Empty(
            GapRegister.Expired(GapRegister.Deferred),
            "Every GapRegister entry still describes something that has not arrived (23 §6, steering S4).");
    }

    /// <summary>
    /// 🔒 `30` §7 / `23` §6 — every subject the design documents enumerate is either authored in
    /// its namespace or declared deferred with an owner. This is the direction that turns the
    /// register from a list of things someone remembered into a check on the whole specified
    /// surface.
    /// </summary>
    [Fact]
    public void Every_subject_the_design_docs_enumerate_is_authored_or_declared_deferred()
    {
        ArchRule.Empty(
            GapRegister.Undeclared(GapRegister.Surfaces, GapRegister.Deferred),
            "Every subject the specs enumerate is authored or declared deferred (30 §7, 23 §6).");
    }

    /// <summary>
    /// `23` §6 — every entry names an owning milestone task, a predicate type and a written reason.
    /// An exemption with no milestone has no expiry; one with no reason has nothing to falsify.
    /// </summary>
    [Fact]
    public void Every_register_entry_names_an_owning_task_a_predicate_and_a_reason()
    {
        ArchRule.Empty(
            GapRegister.Malformed(GapRegister.Deferred),
            "Every GapRegister entry is well formed: owner, predicate type, written reason (23 §6, steering S4).");
    }

    /// <summary>
    /// 🔒 `23` §6 / `30` §7 — the teeth of the stale direction. Driven with two entries that have
    /// expired in the two different ways, over types that really do exist today.
    /// </summary>
    /// <remarks>
    /// <c>CurrencyId</c> (M1-01) and <c>CurrencyChanged</c> (M1-03) are both in <c>Core</c> right
    /// now, so an entry claiming to be waiting for either of them is exactly the state this rule
    /// exists to catch — and it is caught here without one being committed to
    /// <see cref="GapRegister.Deferred"/>.
    /// </remarks>
    [Fact]
    public void The_stale_check_fires_on_an_arrived_predicate_type_and_on_an_authored_subject()
    {
        var waitingForSomethingThatArrived = new GapRegister.Gap(
            "SomethingUnwritten", "M9-01", Domain.CurrencyIdType, "a reason long enough to be worth falsifying.");

        GapRegister.Expired(new[] { waitingForSomethingThatArrived })
            .ShouldHaveSingleItem()
            .ShouldContain("now exists in SlayIdleRepeat.Core", Case.Sensitive);

        var subjectAlreadyAuthored = new GapRegister.Gap(
            Domain.CurrencyChangedEvent, "M9-01", "DieFace", "a reason long enough to be worth falsifying.");

        GapRegister.Expired(new[] { subjectAlreadyAuthored })
            .ShouldHaveSingleItem()
            .ShouldContain("Delete the entry", Case.Sensitive);
    }

    /// <summary>
    /// `23` §6 — the negative half of the stale check: it is silent while the type is genuinely
    /// absent. A check that flagged everything would also "prove" it has teeth.
    /// </summary>
    [Fact]
    public void The_stale_check_is_silent_while_the_type_it_waits_for_is_absent()
    {
        var stillWaiting = new GapRegister.Gap(
            "SomethingUnwritten", "M9-01", "SomethingElseUnwritten", "a reason long enough to be worth falsifying.");

        GapRegister.Expired(new[] { stillWaiting }).ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 `30` §7 / `23` §6 — the teeth of the undeclared direction, and the one usually skipped.
    /// A specified subject that is neither authored nor carried by an entry is reported.
    /// </summary>
    [Fact]
    public void The_undeclared_check_fires_on_a_specified_subject_that_nobody_claimed()
    {
        var surface = new GapRegister.SpecifiedSurface(
            "30 §7", Domain.EventsNamespace, new[] { "DiceRolled" });

        GapRegister.Undeclared(new[] { surface }, Array.Empty<GapRegister.Gap>())
            .ShouldHaveSingleItem()
            .ShouldContain("GapRegister.Deferred does not carry it", Case.Sensitive);
    }

    /// <summary>
    /// `30` §7 / `23` §6 — the negative half: an authored subject and a declared one are both
    /// silent. Without this the rule could be satisfied by a predicate that never matches anything,
    /// which would make the register unsatisfiable rather than strict.
    /// </summary>
    [Fact]
    public void The_undeclared_check_is_silent_on_an_authored_subject_and_on_a_declared_one()
    {
        var authored = new GapRegister.SpecifiedSurface(
            "30 §7", Domain.EventsNamespace, new[] { Domain.CurrencyChangedEvent });

        GapRegister.Undeclared(new[] { authored }, Array.Empty<GapRegister.Gap>()).ShouldBeEmpty();

        var deferred = new GapRegister.SpecifiedSurface(
            "30 §7", Domain.EventsNamespace, new[] { "DiceRolled" });

        var entry = new GapRegister.Gap(
            "DiceRolled", "M3-04", "DieFace", "a reason long enough to be worth falsifying.");

        GapRegister.Undeclared(new[] { deferred }, new[] { entry }).ShouldBeEmpty();
    }

    /// <summary>
    /// `23` §6 — the teeth of the well-formedness check, one crafted entry per way of being
    /// malformed. Every branch of the predicate is driven: a branch no entry reaches could return
    /// "well formed" for everything and no test here would notice.
    /// </summary>
    [Fact]
    public void The_wellformedness_check_fires_on_a_missing_subject_owner_predicate_or_reason()
    {
        var noSubject = new GapRegister.Gap("  ", "M3-04", "DieFace", "a reason long enough to be worth falsifying.");
        GapRegister.Malformed(new[] { noSubject })
            .ShouldHaveSingleItem()
            .ShouldContain("names no subject", Case.Sensitive);

        var noOwner = new GapRegister.Gap("X", "later", "DieFace", "a reason long enough to be worth falsifying.");
        GapRegister.Malformed(new[] { noOwner })
            .ShouldHaveSingleItem()
            .ShouldContain("not a milestone task id", Case.Sensitive);

        var noPredicate = new GapRegister.Gap("X", "M3-04", "  ", "a reason long enough to be worth falsifying.");
        GapRegister.Malformed(new[] { noPredicate })
            .ShouldHaveSingleItem()
            .ShouldContain("nothing can expire it", Case.Sensitive);

        var noReason = new GapRegister.Gap("X", "M3-04", "DieFace", "later");
        GapRegister.Malformed(new[] { noReason })
            .ShouldHaveSingleItem()
            .ShouldContain("no written reason worth falsifying", Case.Sensitive);

        var wellFormed = new GapRegister.Gap("X", "M3-04", "DieFace", "a reason long enough to be worth falsifying.");
        GapRegister.Malformed(new[] { wellFormed }).ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 `23` §6 / `30` §7 — the tripwire against the register's own permanent vacuity
    /// (steering S3). <see cref="GapRegister.Expired"/> quantifies over the entries and
    /// <see cref="GapRegister.Undeclared"/> over the transcriptions; empty either one and both
    /// rules above hold forever over nothing.
    /// </summary>
    /// <remarks>
    /// The `30` §7 count is stated as the literal <b>6</b>, not as the transcription's own length:
    /// a count taken from the list cannot notice the list being trimmed, which is the one edit a
    /// self-referential floor would survive.
    /// </remarks>
    [Fact]
    public void The_registers_own_subject_sets_are_non_empty_and_cover_30_section_7()
    {
        GapRegister.Deferred.ShouldNotBeEmpty(
            "an empty register makes No_deferral_outlives_the_type_that_gives_it_meaning vacuous. If every " +
            "gap really has closed, that is a milestone worth writing down — delete this rule deliberately, " +
            "in the commit that closes the last one.");

        GapRegister.Surfaces.ShouldNotBeEmpty(
            "an empty transcription list makes the undeclared direction vacuous, and that direction is the " +
            "whole reason this register is more than a comment.");

        var domainEvents = GapRegister.Surfaces.Single(s => s.Citation == "30 §7");

        domainEvents.Subjects.Count.ShouldBe(
            6,
            "30 §7's code block declares exactly six events. A transcription that shrank would stop asking " +
            "about the ones it dropped, and nothing else in this repository enumerates them.");

        domainEvents.Namespace.ShouldBe(Domain.EventsNamespace);
    }

    /// <summary>
    /// 🔒 `23` §6 — the other half of the vacuity check: the two presence predicates every rule
    /// here rests on actually distinguish a type that exists from one that does not.
    /// </summary>
    /// <remarks>
    /// This is the failure the rules above cannot announce about themselves. An
    /// <see cref="GapRegister.IsPresentInCore"/> that always answered <c>false</c> would make the
    /// stale direction silent forever; an <see cref="GapRegister.IsAuthoredUnder"/> that always
    /// answered <c>true</c> would make the undeclared direction silent forever. Both are name
    /// lookups against `30` §11.4's namespaces, so both go quiet on a rename rather than going red.
    /// </remarks>
    [Fact]
    public void The_presence_predicates_recognise_a_type_that_exists_and_one_that_does_not()
    {
        GapRegister.IsPresentInCore(Domain.CurrencyIdType).ShouldBeTrue(
            "CurrencyId landed in M1-01. If this is false the lookup is broken and every deferral looks live.");

        GapRegister.IsPresentInCore("DieFace").ShouldBeFalse(
            "DieFace is M3-04's. If this is true, either it has arrived — in which case the DiceRolled entry " +
            "is stale — or the lookup matches names it should not.");

        GapRegister.IsAuthoredUnder(Domain.EventsNamespace, Domain.CurrencyChangedEvent).ShouldBeTrue(
            "M1-03 authored it under Core/Events/. If this is false the undeclared direction would demand a " +
            "register entry for an event that already exists.");

        GapRegister.IsAuthoredUnder(Domain.EventsNamespace, "DiceRolled").ShouldBeFalse(
            "nothing has authored it. If this is true, the namespace filter is matching by something other " +
            "than the name and the undeclared direction is silent.");

        GapRegister.IsAuthoredUnder(Domain.EventsNamespace, Domain.CurrencyIdType).ShouldBeFalse(
            "CurrencyId exists, but in Primitives — so this pins that the check is namespace-scoped rather " +
            "than an assembly-wide lookup wearing a namespace argument.");
    }
}
