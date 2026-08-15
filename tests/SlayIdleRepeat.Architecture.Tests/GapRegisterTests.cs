using System.Text.RegularExpressions;
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
    /// 🔒 `30` §7 / `23` §6 — the converse of the rule above: every deferral names a subject some
    /// transcribed specification actually enumerates. The analogue of <c>test-suites.json</c>'s
    /// rule 5, and what stops <see cref="GapRegister.Deferred"/> and
    /// <see cref="GapRegister.Surfaces"/> drifting apart as M1-06 and M4 add to them.
    /// </summary>
    /// <remarks>
    /// An entry the undeclared direction cannot see is an exemption that can never be satisfied:
    /// authoring the subject expires it (<see cref="GapRegister.Expired"/> catches that), but
    /// nothing would ever have demanded the subject in the first place. That is a comment with a
    /// milestone id on it, which is what this register exists instead of.
    /// </remarks>
    [Fact]
    public void Every_deferral_names_a_subject_some_specification_enumerates()
    {
        ArchRule.Empty(
            GapRegister.Unanchored(GapRegister.Surfaces, GapRegister.Deferred),
            "Every GapRegister entry defers a subject a transcribed specification enumerates (23 §6, 30 §7).");
    }

    /// <summary>
    /// `23` §6 — the teeth of the anchoring direction, both halves: silent on an entry the
    /// transcription enumerates, loud on one it does not (`30` §7).
    /// </summary>
    [Fact]
    public void The_anchoring_check_fires_on_a_deferral_no_transcription_enumerates()
    {
        var surface = new GapRegister.SpecifiedSurface(
            "30 §7", Domain.EventsNamespace, new[] { "TileResolved" });

        var anchored = new GapRegister.Gap(
            "TileResolved", "M3-04", "TileType", "a reason long enough to be worth falsifying.");

        GapRegister.Unanchored(new[] { surface }, new[] { anchored }).ShouldBeEmpty();

        var floating = new GapRegister.Gap(
            "AnEventNoSpecAsksFor", "M3-04", "TileType", "a reason long enough to be worth falsifying.");

        GapRegister.Unanchored(new[] { surface }, new[] { floating })
            .ShouldHaveSingleItem()
            .ShouldContain("no GapRegister.Surfaces transcription enumerates it", Case.Sensitive);
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
            Domain.CurrencyChangedEvent, "M9-01", "TileType", "a reason long enough to be worth falsifying.");

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
            "30 §7", Domain.EventsNamespace, new[] { "TileResolved" });

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
            "30 §7", Domain.EventsNamespace, new[] { "TileResolved" });

        var entry = new GapRegister.Gap(
            "TileResolved", "M3-04", "TileType", "a reason long enough to be worth falsifying.");

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
        var noSubject = new GapRegister.Gap("  ", "M3-04", "TileType", "a reason long enough to be worth falsifying.");
        GapRegister.Malformed(new[] { noSubject })
            .ShouldHaveSingleItem()
            .ShouldContain("names no subject", Case.Sensitive);

        var noOwner = new GapRegister.Gap("X", "later", "TileType", "a reason long enough to be worth falsifying.");
        GapRegister.Malformed(new[] { noOwner })
            .ShouldHaveSingleItem()
            .ShouldContain("not a milestone task id", Case.Sensitive);

        var noPredicate = new GapRegister.Gap("X", "M3-04", "  ", "a reason long enough to be worth falsifying.");
        GapRegister.Malformed(new[] { noPredicate })
            .ShouldHaveSingleItem()
            .ShouldContain("nothing can expire it", Case.Sensitive);

        var noReason = new GapRegister.Gap("X", "M3-04", "TileType", "later");
        GapRegister.Malformed(new[] { noReason })
            .ShouldHaveSingleItem()
            .ShouldContain("no written reason worth falsifying", Case.Sensitive);

        var wellFormed = new GapRegister.Gap("X", "M3-04", "TileType", "a reason long enough to be worth falsifying.");
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

        // 🔒 M1-05. The same floor for the two transcriptions that milestone added, and for the same
        // reason: a trimmed list does not fail, it quietly stops asking. Both counts are literals,
        // not the lists' own lengths.
        var runContents = GapRegister.Surfaces.Single(
            s => s.Citation.StartsWith("30 §4 (the Run-contents row", StringComparison.Ordinal));

        runContents.Subjects.Count.ShouldBe(
            5,
            "30 §4's Run row enumerates ten things; M1-05 built five (position, HP, run Gold, RNG " +
            "stream positions, per-run ad uses) and this transcription is the other five. Five plus " +
            "five is the row — if this shrinks, the arithmetic in GapRegister.Surfaces' remarks stops " +
            "adding up and the dropped item is deferred by nobody.");

        runContents.Namespace.ShouldBe(Domain.ModelNamespace);

        var runStateMachine = GapRegister.Surfaces.Single(
            s => s.Citation.StartsWith("02 §1.1", StringComparison.Ordinal));

        runStateMachine.Subjects.Count.ShouldBe(
            1,
            "02 §1.1's state machine is deferred as exactly one subject, RunPhase — the state SET is " +
            "M3-05's ruling, not a list to transcribe here. An empty transcription would silently " +
            "un-defer the SchemaVersion bump that entry exists to price.");

        runStateMachine.Namespace.ShouldBe(Domain.PrimitivesNamespace);

        // 🔒 M1-02. The floor under `14` §2.3's transcription, and it is the one in this file where
        // the number is itself contested: the table's own header says "Meta commands (29)" and this
        // repository used to say 48 commands. The M1 kickoff ruled both to be miscounts of a correct
        // table, so 49 is a LITERAL here — taken from counting the document's rows, never from the
        // transcription's own Count, which cannot notice itself being trimmed.
        //
        // ⚠️ A count alone would be satisfied by 49 WRONG names, which is why it is not the only
        // guard: every name in that transcription must resolve to a real type under Core/Commands/
        // or Undeclared fires, and the wire names those types register under are pinned as a SET, in
        // both directions, by SlayIdleRepeat.Core.Tests.CommandVocabularyTests.
        var commandRegistry = GapRegister.Surfaces.Single(
            s => s.Citation.StartsWith("14 §2.3", StringComparison.Ordinal));

        commandRegistry.Subjects.Count.ShouldBe(
            49,
            "14 §2.3's registry is 19 run commands plus 30 meta commands, and it is EXHAUSTIVE — 'a " +
            "command not listed here does not exist'. A transcription that shrank would stop asking " +
            "about the rows it dropped, and deleting a command type would then be silent.");

        commandRegistry.Namespace.ShouldBe(Domain.CommandsNamespace);

        commandRegistry.Subjects.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            49,
            "a duplicated name would keep the count at 49 while one row went untranscribed.");

        // 🔒 A shape check on the hand-written list, and it is load-bearing rather than tidy.
        // MEASURED: replacing "AbandonRunCommand" with "CommandPayload" passed 58/58 — the count
        // held, Distinct held, and Undeclared stayed silent because CommandPayload really is
        // authored under Core/Commands/. So a row could leave the transcription by being swapped for
        // one of the two non-command types in that namespace, and deleting the command would then be
        // invisible. GameCommand is the other. This is a claim about the LIST, not about the code —
        // it derives nothing from the assembly.
        commandRegistry.Subjects
            .Where(s => !s.EndsWith("Command", StringComparison.Ordinal) ||
                        s.Equals(Domain.GameCommandType, StringComparison.Ordinal))
            .ShouldBeEmpty(
                "every subject of 14 §2.3's transcription is a concrete command type, so each name " +
                "ends in 'Command' and none of them is the abstract base. Core/Commands/ also holds " +
                "GameCommand and CommandPayload, and either would satisfy the count and the " +
                "undeclared check while quietly taking a real row's place.");

        // 🔒 M1-08. The floor under `30` §2.3's transcription, on the M1-02 and M1-05 pattern and
        // for the reason those two record: a literal, never the transcription's own Count.
        //
        // ⚠️ It is NOT redundant with the unanchored direction, which is the first thing a reader
        // checks. Unanchored fires on a subject that leaves this list while its Deferred entry
        // stays — so trimming the transcription ALONE is already red. What nothing else catches is
        // the edit that removes a subject from BOTH lists at once: Expired is silent (the type
        // still does not exist), Undeclared is silent (nothing asks for it any more), Unanchored is
        // silent (no entry is left dangling), and the boundary 30 §2.3 specifies is then deferred
        // by nobody with the whole suite green. That is the shape this file's other floors exist
        // for, and it is the one direction a register cannot notice about itself.
        var catchUpBoundaries = GapRegister.Surfaces.Single(
            s => s.Citation.StartsWith("30 §2.3", StringComparison.Ordinal));

        catchUpBoundaries.Subjects.Count.ShouldBe(
            3,
            "30 §2.3 enumerates five catch-up boundaries — Energy regeneration accrual, the 05:00 UTC " +
            "daily resets, weekly boundaries, Plus expiry, event-window state. M1-08 BUILT three of " +
            "them (the accrual, the daily reset mechanism with the ad caps, dungeon entries and wheel " +
            "free spin that hang off it, and the weekly boundary), RULED Plus expiry off (12 §2.1 puts " +
            "entitlement on the session, so there is no aggregate state to roll forward), and this " +
            "transcription is the remainder: the two items inside the daily-reset parenthetical that " +
            "act on state nobody has authored — quest expiry and daily-shop stock expiry — plus the " +
            "whole of event-window state. So every one of the five is accounted for: three built, one " +
            "ruled off, one deferred outright, and the deferred half of a built one. If this shrinks, " +
            "the dropped boundary is deferred by nobody.");

        catchUpBoundaries.Namespace.ShouldBe(Domain.ModelNamespace);

        // 🔒 M1-11. The floor under `30` §6's transcription, on the same pattern and for the same
        // reason: a literal, never the transcription's own Count.
        //
        // ⚠️ THIS ONE GUARDS THE MOST EXPENSIVE SILENCE IN THE REPOSITORY, which is why it is here
        // as well as in SubjectSetFloorTests and — since M1-11 — inside the rule itself.
        // DomainPurityTests.The_whole_game_is_playable_from_Core_alone reported success when
        // InMemoryGame was ABSENT for the whole of M0-08..M1-11; that rule now has a presence arm,
        // and this transcription is the second mechanism over the same fact. Trimming it would
        // remove one of the two things that demand the harness exist, with Expired silent (neither
        // name is deferred), Unanchored silent (no entry dangles) and the whole suite green.
        var harness = GapRegister.Surfaces.Single(
            s => s.Citation.StartsWith("30 §6", StringComparison.Ordinal));

        harness.Subjects.Count.ShouldBe(
            2,
            "30 §6's table names exactly two types — InMemoryGame and VirtualClock — and both are " +
            "authored, so this entry works only in the undeclared direction. If it shrinks, deleting " +
            "the harness stops being a build failure and 30 §9's load-bearing rule goes back to " +
            "passing over nothing.");

        harness.Namespace.ShouldBe(Domain.TestingNamespace);

        // 🔒 And the shape check, for the reason the `14` §2.3 list carries one: the count and the
        // undeclared direction are both satisfied by ANY two types authored under Core/Testing/, so
        // a name could leave this list by being swapped for a helper that happened to land there.
        // These two are named by IDENTITY (steering S3) because they are not interchangeable —
        // The_whole_game_is_playable_from_Core_alone keys on the first by name, through
        // Domain.InMemoryGameType.
        harness.Subjects.ShouldBe(
            new[] { Domain.InMemoryGameType, "VirtualClock" },
            ignoreOrder: true,
            "30 §6 names these two and no others; a count-only floor is satisfied by whatever pair " +
            "replaced them.");
    }

    /// <summary>
    /// 🔒 `23` §6 / `14` §2.3, steering <b>S4</b> — every milestone task a
    /// <c>CommandDispatch.Deferred</c> row names is a real task row in
    /// <c>IMPLEMENTATION_TRACKER.md</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>A dispatch row is a declared exception, and its owner is its only expiry</b> — which is
    /// what makes it this file's business rather than the domain suite's. M1-02 wrote into
    /// <c>GameRules</c> that the 49 owners were "read off <c>IMPLEMENTATION_TRACKER.md</c>'s task
    /// rows rather than inferred, because a wrong owner is a deferral that expires at the wrong
    /// time". Nothing checked that claim until this rule; a wrong owner is the S4 failure this
    /// milestone has already hit twice (M0's <c>knownEmpty</c> naming M1-09 for a port suite, and
    /// eight <c>SubjectSetFloorTests</c> markers naming the wrong task).
    /// </para>
    /// <para>
    /// ⚠️ It reads the <b>raw</b> source rather than <c>SourceText</c>, whose whole job is to blank
    /// string literals — and the owner <em>is</em> a string literal. It also cannot live in
    /// <c>SlayIdleRepeat.Core.Tests</c>, which the M1 kickoff keeps hermetic: reading a repository
    /// file there would be the first crack in "nothing in <c>Core</c> loads from disk". This suite
    /// already reads the tree.
    /// </para>
    /// <para>
    /// 🔒 <b>Both sets are floored by identity</b> (steering S3). A regex that stopped matching would
    /// empty either side and leave the comparison trivially true, which is the one way this rule
    /// could go quiet.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_deferred_command_names_a_task_the_tracker_declares()
    {
        var dispatch = File.ReadAllText(
            Path.Combine(RepoLayout.SrcRoot, "SlayIdleRepeat.Core", "GameRules.cs"));

        var owners = Regex.Matches(dispatch, @"\.Deferred<(?<command>\w+)>\(""(?<wire>[A-Z0-9_]+)"", CommandKind\.(?:Run|Meta), ""(?<owner>[^""]+)""\)")
            .Select(m => (Wire: m.Groups["wire"].Value, Owner: m.Groups["owner"].Value))
            .ToArray();

        // 🔒 M1-09 lowered the deferred floor from 49 to 48 by exactly the one row that became
        // Handled — BEGIN_SESSION — M3-15 lowered it again to 47 by START_RUN, M3-03c lowered it
        // again to 46 by MINIGAME_SUBMIT, and M3-04 lowers it again to 44 by ROLL_DICE and
        // USE_REROLL, adding the Handled floor beside it rather than only editing the number. That
        // is the difference between "44 rows are deferred" and "44 are deferred AND the other five
        // are handled": the sentence this replaces would have been satisfied just as well by a row
        // that was DELETED, which is the failure 14 §2.3's registry ("a command not listed here
        // does not exist") most needs a rule to notice. The two are asserted, and then their sum.
        var handled = Regex.Matches(dispatch, @"\.Handled<(?<command>\w+)>\(""(?<wire>[A-Z0-9_]+)"", CommandKind\.(?:Run|Meta), (?<handler>[\w.]+)(?:,\s*\w+:\s*\w+)?\)")
            .Select(m => m.Groups["wire"].Value)
            .ToArray();

        owners.Length.ShouldBe(
            44,
            "14 §2.3's registry is 19 run + 30 meta, and 44 of the 49 rows are Deferred since M3-04 " +
            "landed the ROLL_DICE and USE_REROLL handlers. If this is 0 the pattern has stopped " +
            "matching the dispatch table and the comparison below holds over nothing; if it shrinks, " +
            "either a row went away or a row became Handled — in which case lower this by exactly " +
            "that many and raise the Handled floor by the same.");

        handled.ShouldBe(
            new[] { "BEGIN_SESSION", "START_RUN", "MINIGAME_SUBMIT", "ROLL_DICE", "USE_REROLL" },
            ignoreOrder: true,
            "the Handled rows, by IDENTITY rather than by count (steering S3): a count-only floor is " +
            "satisfied by whatever handler replaced the one this names. 30 §2.3's BEGIN_SESSION was " +
            "the first; 02 §2's START_RUN (M3-15) the second; 03 §6's MINIGAME_SUBMIT (M3-03c) the " +
            "third; 04 §§1,3-4's ROLL_DICE and USE_REROLL (M3-04) the fourth and fifth.");


        (owners.Length + handled.Length).ShouldBe(
            49,
            "…and the sum, because the two floors above are separately satisfiable while a row goes " +
            "missing entirely. 14 §2.3 says the registry is EXHAUSTIVE — 'a command not listed here " +
            "does not exist' — so 49 is the number the table has, whatever the split.");

        var tracker = File.ReadAllText(Path.Combine(RepoLayout.RepoRoot, "IMPLEMENTATION_TRACKER.md"));

        var declared = Regex.Matches(tracker, @"^\| (?<id>M\d{1,2}-\d{2}) \|", RegexOptions.Multiline)
            .Select(m => m.Groups["id"].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(declared);
        Assert.Contains("M1-02", declared, StringComparer.Ordinal);
        Assert.Contains("M3-15", declared, StringComparer.Ordinal);
        Assert.DoesNotContain("M9-99", declared, StringComparer.Ordinal);

        var offenders = owners
            .Where(row => !declared.Contains(row.Owner))
            .Select(row =>
                $"'{row.Wire}' is deferred to '{row.Owner}', which is not a task row in " +
                "IMPLEMENTATION_TRACKER.md. A deferral whose owner does not exist expires when nobody " +
                "is looking: the row keeps rejecting with ILLEGAL_STATE and no milestone is on the " +
                "hook for it. Name the task that actually builds the system behind the command.");

        ArchRule.Empty(
            offenders,
            "Every deferred command names a milestone task the tracker declares (steering S4).");
    }

    /// <summary>
    /// 🔒 `30` §11.4 — <c>Core/Handlers/</c> holds <b>handlers</b>: every top-level type under it is
    /// named by a <c>CommandDispatch.Handled</c> row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>Found by M1-09's architecture review, and it is the "what can a future author do that
    /// nothing would catch" question answered.</b>
    /// <c>DomainPurityTests.Every_command_type_is_handled_by_Apply</c> computes its dispatch surface
    /// as <em>everything under <c>Core/Handlers/</c></em> plus <c>GameRules</c>. So a NON-handler type
    /// placed there — a shared daily-block helper, a <c>HandlerResult</c> factory — silently widens
    /// that surface, and any command it happens to name reads as dispatched with no row behind it.
    /// Nothing constrained the composition of that directory at all; the constraint was only ever
    /// implicit in it being empty.
    /// </para>
    /// <para>
    /// ⚠️ It is here rather than in <c>DomainPurityTests</c> because it reads the dispatch table's
    /// <b>source</b>, which is this file's mechanism (see the rule above for why the raw text and not
    /// <c>SourceText</c>). It was briefly written <em>inside</em> that rule and moved out: an
    /// assertion whose failure is not described by the test's name is the same defect as a name that
    /// promises more than its assertion delivers (steering <b>S1</b>), just pointing the other way.
    /// </para>
    /// <para>
    /// 🔒 Floored by <c>Assert.NotEmpty</c> on the declared set: <c>Core/Handlers/</c> emptying would
    /// otherwise satisfy this forever, which is exactly the state it was in before M1-09.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_type_under_Core_Handlers_is_a_handler_the_dispatch_table_names()
    {
        var dispatch = File.ReadAllText(
            Path.Combine(RepoLayout.SrcRoot, "SlayIdleRepeat.Core", "GameRules.cs"));

        // 🔒 …AND THE HANDLERS THEMSELVES, which M1-09's architecture review found nothing was
        // watching. Every_command_type_is_handled_by_Apply computes its dispatch surface as
        // "Core/Handlers/ ∪ GameRules", so a NON-handler type placed under Core/Handlers/ — a shared
        // daily-block helper, a HandlerResult factory — silently widens that surface, and any command
        // it happens to name reads as dispatched. Nothing constrained the composition of that
        // directory at all. The regex above already captures the handler group; it was being
        // discarded.
        var handlers = Regex.Matches(dispatch, @"\.Handled<(?<command>\w+)>\(""(?<wire>[A-Z0-9_]+)"", CommandKind\.(?:Run|Meta), (?<handler>[\w.]+)(?:,\s*\w+:\s*\w+)?\)")
            .Select(m => m.Groups["handler"].Value.Split('.')[0])
            .ToHashSet(StringComparer.Ordinal);

        var underHandlers = Domain.CoreTypesUnder(Domain.HandlersNamespace)
            .Where(t => t.DeclaringType is null && !Domain.IsCompilerGenerated(t))
            .Select(t => t.Name)
            .ToArray();

        Assert.NotEmpty(underHandlers);
        ArchRule.Empty(
            underHandlers.Where(name => !handlers.Contains(name))
                .Select(name =>
                    $"'{name}' is declared under {Domain.HandlersNamespace} but no CommandDispatch.Handled " +
                    "row names it. 30 §11.4 makes that directory 'one handler per command', and " +
                    "DomainPurityTests.Every_command_type_is_handled_by_Apply treats EVERYTHING under it " +
                    "as dispatch surface — so a type there that is not a handler widens the set of " +
                    "commands that rule considers handled, for free. Put shared helpers in Rules/ " +
                    "(computation) or on the aggregate (state), not here."),
            "Every type under Core/Handlers/ is a handler the dispatch table names (30 §11.4).");
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

        GapRegister.IsPresentInCore("TileType").ShouldBeFalse(
            "TileType is M3-03's. If this is true, either it has arrived — in which case the TileResolved " +
            "entry is stale — or the lookup matches names it should not.");

        GapRegister.IsAuthoredUnder(Domain.EventsNamespace, Domain.CurrencyChangedEvent).ShouldBeTrue(
            "M1-03 authored it under Core/Events/. If this is false the undeclared direction would demand a " +
            "register entry for an event that already exists.");

        GapRegister.IsAuthoredUnder(Domain.EventsNamespace, "TileResolved").ShouldBeFalse(
            "nothing has authored it. If this is true, the namespace filter is matching by something other " +
            "than the name and the undeclared direction is silent.");

        GapRegister.IsAuthoredUnder(Domain.EventsNamespace, Domain.CurrencyIdType).ShouldBeFalse(
            "CurrencyId exists, but in Primitives — so this pins that the check is namespace-scoped rather " +
            "than an assembly-wide lookup wearing a namespace argument.");
    }
}
