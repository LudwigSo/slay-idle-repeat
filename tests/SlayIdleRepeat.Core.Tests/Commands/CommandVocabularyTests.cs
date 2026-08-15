using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Commands;

/// <summary>
/// The canonical command registry, pinned as a set against a hand-transcribed literal list, in both
/// directions. The transcription is not derived from the registry itself — a list read off
/// <see cref="GameRules"/> would say the registry is right because the registry says so.
/// </summary>
public sealed class CommandVocabularyTests
{
    /// <summary>
    /// The Run commands table, transcribed by hand in the document's order. <c>START_RUN</c> is
    /// submitted on the player endpoint because no <c>runId</c> exists yet — an exception about the
    /// URL, not the kind.
    /// </summary>
    public static readonly string[] RunCommandWireNames =
    {
        "START_RUN",
        "ROLL_DICE",
        "USE_REROLL",
        "CHOOSE_FORK",
        "RESOLVE_TILE",
        "PICK_PERK",
        "REROLL_DRAFT",
        "SKIP_DRAFT",
        "SHOP_BUY",
        "SHOP_REFRESH",
        "EVENT_CHOOSE",
        "MINIGAME_SUBMIT",
        "CAMPFIRE_CHOOSE",
        "START_BATTLE",
        "CONFIRM_BATTLE_RESULT",
        "REVIVE",
        "USE_CONSUMABLE",
        "END_RUN",
        "ABANDON_RUN",
    };

    /// <summary>The Meta commands table, transcribed by hand in the document's order.</summary>
    public static readonly string[] MetaCommandWireNames =
    {
        "BEGIN_SESSION",
        "SKIP_FTUE",
        "EQUIP",
        "MERGE",
        "ENHANCE",
        "SALVAGE",
        "SPEND_TALENT",
        "RESPEC",
        "LEVEL_PET",
        "ASCEND_PET",
        "EQUIP_PET",
        "EQUIP_MOUNT",
        "CLAIM_QUEST",
        "REROLL_QUEST",
        "CLAIM_AD_REWARD",
        "CLAIM_CALENDAR",
        "CLAIM_INBOX",
        "SPIN_WHEEL",
        "SET_FOCUS",
        "REFORGE_ITEM",
        "RETUNE_ITEM",
        "SAVE_PRESET",
        "APPLY_PRESET",
        "SHOP_PURCHASE",
        "OPEN_CHEST",
        "OPEN_EGG",
        "OPEN_CRATE",
        "UPLOAD_GHOST",
        "START_DUEL",
        "SUBMIT_DUEL",
    };

    private static IReadOnlyDictionary<string, Type> Registry =>
        SlayIdleRepeat.Core.GameRules.CommandTypesByWireName;

    // ------------------------------------------------------------------ the floor under everything

    /// <summary>
    /// The floor under every comparison below: without it, emptying either list makes the set
    /// comparisons hold vacuously.
    /// </summary>
    [Fact]
    public void The_transcription_is_the_nineteen_and_thirty_the_document_lists()
    {
        RunCommandWireNames.Length.ShouldBe(
            19,
            "14 §2.3's run table has 19 rows, counted off the document. A shrunken list makes the " +
            "set comparisons below hold over less than the vocabulary.");

        MetaCommandWireNames.Length.ShouldBe(
            30,
            "14 §2.3's meta table has 30 rows. Its own header says 29 and this repository used to say " +
            "48 commands in total; the M1 kickoff ruled both to be miscounts of a correct table.");

        // Collection ShouldContain compares with EqualityComparer<string>.Default, which is ordinal —
        // the string overload's Case.Insensitive default does not reach here.
        RunCommandWireNames.ShouldContain("SHOP_BUY");
        RunCommandWireNames.ShouldContain("START_RUN");
        MetaCommandWireNames.ShouldContain("SHOP_PURCHASE");
        MetaCommandWireNames.ShouldContain("BEGIN_SESSION");

        Transcribed().Count.ShouldBe(
            49,
            "19 + 30, with no name appearing in both halves. A duplicate across the two tables would " +
            "keep both lengths right while the registry lost a row.");
    }

    // ------------------------------------------------------------- the set, in both directions

    /// <summary>Direction one: every command the document lists is registered.</summary>
    [Fact]
    public void Every_command_the_document_lists_is_registered()
    {
        var missing = Transcribed()
            .Where(name => !Registry.ContainsKey(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        missing.ShouldBeEmpty(
            "14 §2.3's registry is EXHAUSTIVE and its wire names are appendable, never renamed or " +
            "reused (14 §16.2). A listed command with no dispatch row is a request every deployed " +
            "client can send and this server cannot answer.");
    }

    /// <summary>
    /// Direction two, which a count cannot express: nothing is registered that the document does not
    /// list. Catches a rename, which leaves the count unchanged but fails both directions at once.
    /// </summary>
    [Fact]
    public void Nothing_is_registered_that_the_document_does_not_list()
    {
        var transcribed = Transcribed();

        var unlisted = Registry.Keys
            .Where(name => !transcribed.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        unlisted.ShouldBeEmpty(
            "14 §2.3 is exhaustive: 'a command not listed here does not exist'. Adding one is a " +
            "decision recorded in 16 and it lands in that table first.");

        Registry.Count.ShouldBe(
            49,
            "19 + 30. The two set comparisons above are floored by the transcription's own literal " +
            "counts; this is the same floor on the OTHER side, so an emptied registry fails here " +
            "rather than making 'nothing unlisted' trivially true.");
    }

    /// <summary>The comparison is ordinal. A case-insensitive registry would answer for <c>roll_dice</c> too.</summary>
    [Fact]
    public void The_registry_matches_wire_names_ordinally()
    {
        Registry.ContainsKey("ROLL_DICE").ShouldBeTrue();
        Registry.ContainsKey("roll_dice").ShouldBeFalse(
            "a lowercase near-miss is a different string on the wire. A registry built with an " +
            "OrdinalIgnoreCase comparer would resolve it, and the set comparisons above would not notice.");
    }

    // ------------------------------------------------------------------------------ the kinds

    /// <summary>
    /// Every run row is registered <c>CommandKind.Run</c> and every meta row <c>CommandKind.Meta</c>.
    /// The kind decides whether <c>Apply</c> opens a <c>RunRngScope</c> over the run's counters and
    /// whether the run's TTL moves; a run command misfiled as meta gets the run and no scope.
    /// </summary>
    [Fact]
    public void Every_row_is_registered_under_the_kind_its_table_gives_it()
    {
        // Inline floor: an emptied registry would make this loop produce no offenders at all.
        RunCommandWireNames.Length.ShouldBe(19);
        MetaCommandWireNames.Length.ShouldBe(30);
        Registry.Count.ShouldBe(49, "an emptied registry makes the sweep below silent, not red.");

        var offenders = new List<string>();

        foreach (var name in RunCommandWireNames)
        {
            offenders.AddRange(Misclassified(name, CommandKind.Run));
        }

        foreach (var name in MetaCommandWireNames)
        {
            offenders.AddRange(Misclassified(name, CommandKind.Meta));
        }

        offenders.ShouldBeEmpty(
            "the kind decides whether Apply opens a RunRngScope and whether the run's 14 §16.3 TTL " +
            "moves, so it is correctness rather than bookkeeping.");
    }

    /// <summary>
    /// <c>START_RUN</c> is a run command even though it is submitted to the player endpoint.
    /// Classifying it Meta to match its endpoint would hand the command that commits <c>runSeed</c>
    /// no <c>RunRngScope</c>, and leave the run it created ageing off an unadvanced TTL.
    /// </summary>
    [Fact]
    public void START_RUN_is_a_run_command_even_though_it_is_sent_to_the_player_endpoint()
    {
        RegistrationFor("START_RUN").Kind.ShouldBe(CommandKind.Run);
        Registry["START_RUN"].ShouldBe(typeof(StartRunCommand));
    }

    /// <summary><c>SHOP_BUY</c> spends the run's Gold and <c>SHOP_PURCHASE</c> the player's wallet. Two rows, two types, two kinds.</summary>
    [Fact]
    public void The_in_run_shop_and_the_meta_shop_are_two_commands()
    {
        Registry["SHOP_BUY"].ShouldBe(typeof(ShopBuyCommand));
        Registry["SHOP_PURCHASE"].ShouldBe(typeof(ShopPurchaseCommand));

        RegistrationFor("SHOP_BUY").Kind.ShouldBe(CommandKind.Run);
        RegistrationFor("SHOP_PURCHASE").Kind.ShouldBe(CommandKind.Meta);
    }

    // ------------------------------------------------------------------------- the rows themselves

    /// <summary>
    /// Every registered type is a public, concrete, sealed <c>GameCommand</c> declared directly under
    /// <c>SlayIdleRepeat.Core.Commands</c>. Sealed because a subclassable command is one whose
    /// dispatch row does not decide which rule runs.
    /// </summary>
    [Fact]
    public void Every_registered_type_is_a_public_sealed_command_in_the_commands_namespace()
    {
        Registry.Count.ShouldBe(49, "an emptied registry makes the sweep below quantify over nothing.");

        var offenders = Registry
            .OrderBy(row => row.Key, StringComparer.Ordinal)
            .SelectMany(row => Malformed(row.Key, row.Value))
            .ToArray();

        offenders.ShouldBeEmpty(
            "30 §11.2 makes the command hierarchy the wire protocol as well as the input vocabulary, " +
            "and 30 §11.4's namespace list is closed.");
    }

    /// <summary>
    /// Every row is either handled today or names the milestone task that will handle it — the
    /// deferral's only expiry. Written as "handled or owned" so swapping one row to Handled is a
    /// one-line edit here too.
    /// </summary>
    [Fact]
    public void Every_row_is_handled_or_names_the_task_that_will_handle_it()
    {
        var offenders = new List<string>();

        foreach (var (name, type) in Registry.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            var registration = RegistrationFor(name);

            if (registration.IsHandled)
            {
                continue;
            }

            if (registration.DeferredTo is null || !TaskId.IsMatch(registration.DeferredTo))
            {
                offenders.Add(
                    $"'{name}' ({type.Name}) is deferred to '{registration.DeferredTo}', which is not a " +
                    "milestone task id (M3-15, M12-04). Without one, the ILLEGAL_STATE it dispatches to " +
                    "is indistinguishable from a rule that refused the player, and nothing says when the " +
                    "deferral expires.");
            }
        }

        offenders.ShouldBeEmpty();

        Registry.Count(row => !RegistrationFor(row.Key).IsHandled).ShouldBeGreaterThan(
            0,
            "the loop above quantifies over the deferred rows; if none were deferred it would assert " +
            "nothing at all. M1-09 lands the first handler, and this floor is what says so out loud.");
    }

    // -------------------------------------------------------------------- the vocabulary, applied

    /// <summary>
    /// All forty-nine commands are constructible and every one is refused, not thrown, while its
    /// milestone is unbuilt. Builds each type and hands it to <c>GameRules.Apply</c>, so the row
    /// resolves at runtime rather than only in IL.
    /// </summary>
    [Fact]
    public void Every_command_in_the_vocabulary_is_applied_and_refused_rather_than_thrown()
    {
        var deferred = 0;

        foreach (var (name, type) in Registry.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            if (RegistrationFor(name).IsHandled)
            {
                // A handled command's behaviour is its own handler's suite to pin, not this rule's.
                continue;
            }

            var result = SlayIdleRepeat.Core.GameRules.Apply(Worlds.InARun(), Build(type), Worlds.Context);

            result.Accepted.ShouldBeFalse($"'{name}' has no handler yet, so it cannot be accepted.");
            result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE, $"'{name}' is deferred.");
            deferred++;
        }

        // Stated as "the registry minus the handled rows" rather than a literal so the next task to
        // land a handler lowers it by construction, and the number can never drift below what the
        // loop can reach.
        deferred.ShouldBe(
            Registry.Count(row => !RegistrationFor(row.Key).IsHandled),
            "every DEFERRED row of 14 §2.3 is driven here — 30 of the 49 since M3-13 landed the " +
            "REVIVE/END_RUN/ABANDON_RUN handlers. A mismatch means the loop skipped " +
            "a deferred row rather than that the count moved.");

        deferred.ShouldBe(
            30,
            "…and the absolute number, because the assertion above compares the loop against the same " +
            "table it walks and would agree with itself if every row silently became Handled. 14 §2.3 " +
            "is 49 rows and exactly nineteen of them — BEGIN_SESSION (30 §2.3's day cycle), START_RUN " +
            "(02 §2's runSeed commit), MINIGAME_SUBMIT (03 §6's minigame resolution), ROLL_DICE and " +
            "USE_REROLL (04 §§1,3-4), SHOP_BUY/SHOP_REFRESH (03 §7's shop, M3-08), CHOOSE_FORK " +
            "(03 §1.1's junction pause, M3-02), RESOLVE_TILE/EVENT_CHOOSE/CAMPFIRE_CHOOSE " +
            "(03 §2's tile resolvers, M3-03), START_BATTLE/CONFIRM_BATTLE_RESULT (M3-05), " +
            "PICK_PERK/REROLL_DRAFT/SKIP_DRAFT (06 §1, M3-06), and " +
            "REVIVE/END_RUN/ABANDON_RUN (02 §5-6's reward banking and run-end payout, M3-13) — have " +
            "a handler. Lower this by exactly the number of " +
            "rows that become Handled, and never to a " +
            "number the loop cannot reach.");
    }

    /// <summary>
    /// The behavioural half of the kind: every run row refuses a slice with no run, and every meta
    /// row is content with one. The only rule where the kind of all forty-nine rows is observed
    /// rather than read off the registration.
    /// </summary>
    [Fact]
    public void Outside_a_run_every_run_command_is_a_loading_defect_and_every_meta_command_is_not()
    {
        var runRows = 0;
        var metaRows = 0;

        foreach (var (name, type) in Registry.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            var command = Build(type);

            if (RegistrationFor(name).Kind == CommandKind.Run)
            {
                // START_RUN is the one run row that creates the Run itself, so a run-less slice is
                // its natural one and it must not throw here. Every other run row still does.
                if (RegistrationFor(name).OpensRun)
                {
                    // Not Accepted: Build/Sample fills every int with 0, and ChapterId's own floor is
                    // 1, so the generically-built StartRunCommand(0, NORMAL) hits its handler's own
                    // precondition check. This branch pins reaching a REJECTION, not run-less
                    // acceptance — see InMemoryGameTests for the positive acceptance claim.
                    var opened = SlayIdleRepeat.Core.GameRules.Apply(Worlds.OutsideARun(), command, Worlds.Context);

                    opened.Accepted.ShouldBeFalse(
                        $"'{name}' built with Build's generic ChapterId sample of 0, which its own " +
                        "handler refuses (02 §1's chapter floor is 1) — a REJECTION, not the run-less " +
                        "loading defect this rule is about.");
                    opened.Rejection.ShouldBe(
                        RejectionReason.ILLEGAL_STATE,
                        $"'{name}' rejects an out-of-range chapter as the domain-tier catch-all.");
                }
                else
                {
                    Should.Throw<InvalidOperationException>(
                            () => SlayIdleRepeat.Core.GameRules.Apply(Worlds.OutsideARun(), command, Worlds.Context),
                            $"'{name}' is a run command and a slice with no run is a LOADING defect (30 §4.1), " +
                            "not a rejection.")
                        .Message.ShouldContain($"'{name}'", Case.Sensitive);
                }

                runRows++;
                continue;
            }

            // The claim of this arm is "a meta command is sendable outside a run" — what must not
            // happen is the loading defect the run arm above asserts.
            var result = SlayIdleRepeat.Core.GameRules
                .Apply(Worlds.OutsideARun(), command, ContextFor(name));

            if (RegistrationFor(name).IsHandled)
            {
                result.Accepted.ShouldBeTrue(
                    $"'{name}' is a handled meta command, so outside a run it runs its handler — " +
                    "whatever that handler decides is its own suite's business, but reaching it at " +
                    "all is what this rule is about.");
            }
            else
            {
                result.Rejection.ShouldBe(
                    RejectionReason.ILLEGAL_STATE,
                    $"'{name}' is a deferred meta command: sendable outside a run, and refused for " +
                    "its missing milestone rather than for a missing run.");
            }

            metaRows++;
        }

        runRows.ShouldBe(19, "14 §2.3's run table has 19 rows.");
        metaRows.ShouldBe(30, "14 §2.3's meta table has 30 rows.");
    }

    /// <summary>
    /// A real run command with no run in the slice is a loading defect, and the message names the
    /// command by its wire name — the Application layer loaded the wrong slice, not a player asking
    /// for something they cannot have.
    /// </summary>
    [Fact]
    public void A_real_run_command_without_a_run_names_itself_in_the_defect()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.OutsideARun(), new RollDiceCommand(), Worlds.Context));

        thrown.Message.ShouldContain("'ROLL_DICE'", Case.Sensitive);
        thrown.Message.ShouldContain("RUN_NOT_FOUND", Case.Sensitive);
    }

    // ---------------------------------------------------------------------------- the table's guards

    /// <summary>Two commands claiming the same wire name must not silently win; the refusal names both claimants.</summary>
    [Fact]
    public void Two_commands_cannot_both_claim_SHOP_BUY()
    {
        var table = new CommandDispatch()
            .Deferred<ShopBuyCommand>("SHOP_BUY", CommandKind.Run, "M3-08");

        var thrown = Should.Throw<InvalidOperationException>(() =>
            table.Deferred<ShopPurchaseCommand>("SHOP_BUY", CommandKind.Meta, "M4-09"));

        thrown.Message.ShouldContain("The wire name 'SHOP_BUY' is registered twice", Case.Sensitive);
        thrown.Message.ShouldContain(nameof(ShopBuyCommand), Case.Sensitive);
        thrown.Message.ShouldContain(nameof(ShopPurchaseCommand), Case.Sensitive);
    }

    // ------------------------------------------------------------------------- payload value shapes

    /// <summary>
    /// The three list-carrying commands compare by value, which synthesized record equality would
    /// not have done — a record compares an <c>IReadOnlyList&lt;string&gt;</c> member by reference.
    /// </summary>
    [Fact]
    public void The_list_carrying_commands_compare_by_value()
    {
        new SalvageCommand(new[] { "a", "b" })
            .ShouldBe(new SalvageCommand(new List<string> { "a", "b" }));

        new SalvageCommand(new[] { "a", "b" })
            .ShouldNotBe(new SalvageCommand(new[] { "b", "a" }));

        new RetuneItemCommand("i", new[] { "AFX_PEN" }, new[] { "AFX_CRIT_CHANCE" })
            .ShouldBe(new RetuneItemCommand("i", new[] { "AFX_PEN" }, new[] { "AFX_CRIT_CHANCE" }));

        new RetuneItemCommand("i", new[] { "AFX_PEN" }, new[] { "AFX_CRIT_CHANCE" })
            .ShouldNotBe(new RetuneItemCommand("i", new[] { "AFX_CRIT_CHANCE" }, new[] { "AFX_PEN" }),
                "the locks and the wishlist are different fields; swapping them is a different intent.");

        new ClaimInboxCommand(new[] { "m1" }).ShouldBe(new ClaimInboxCommand(new[] { "m1" }));

        new ClaimInboxCommand().ShouldBe(new ClaimInboxCommand());

        new ClaimInboxCommand().ShouldNotBe(
            new ClaimInboxCommand(Array.Empty<string>()),
            "14 §2.3 makes omitted and empty mean the same thing to the RULE, and they are still two " +
            "different things the client sent. Collapsing them here would make the command lie about " +
            "the request 14 §16.3 replays an outcome for.");

        // Ordinal. A culture- or case-insensitive comparison would make two commands equal on one
        // host and unequal on another.
        new SalvageCommand(new[] { "AFX_PEN" }).ShouldNotBe(
            new SalvageCommand(new[] { "afx_pen" }),
            "payload ids compare ordinally, like every other 14 §2.3 identifier in this repository.");
    }

    /// <summary>
    /// The equality that matters is the one reached through the base type, since that is how an
    /// idempotency cache holds these: <c>Dictionary&lt;GameCommand, ...&gt;</c>. A dictionary
    /// round-trip exercises <c>Equals(object?)</c>, <c>==</c> and the base-typed comparison together.
    /// </summary>
    [Fact]
    public void A_list_carrying_command_survives_a_base_typed_dictionary_round_trip()
    {
        var cache = new Dictionary<GameCommand, string>
        {
            [new SalvageCommand(new[] { "a", "b" })] = "outcome",
            [new ClaimInboxCommand()] = "claim-all",
            [new RetuneItemCommand("i", new[] { "x" }, Array.Empty<string>())] = "retune",
        };

        cache[new SalvageCommand(new List<string> { "a", "b" })].ShouldBe("outcome");
        cache[new ClaimInboxCommand()].ShouldBe("claim-all");
        cache[new RetuneItemCommand("i", new[] { "x" }, Array.Empty<string>())].ShouldBe("retune");

        cache.ContainsKey(new SalvageCommand(new[] { "b", "a" })).ShouldBeFalse();

        // The EqualityContract term in the hand-written GetHashCode. Without it these two hash
        // identically — legal, since Equals still tells them apart, but it buckets two different
        // commands together in the very cache the replay cache will be.
        new SalvageCommand(Array.Empty<string>()).GetHashCode()
            .ShouldNotBe(new ClaimInboxCommand(Array.Empty<string>()).GetHashCode());

        ((GameCommand)new SalvageCommand(new[] { "a" }))
            .Equals(new SalvageCommand(new[] { "a" })).ShouldBeTrue();

        (new SalvageCommand(new[] { "a" }) == new SalvageCommand(new[] { "a" })).ShouldBeTrue();
        (new SalvageCommand(new[] { "a" }) != new SalvageCommand(new[] { "z" })).ShouldBeTrue();
    }

    /// <summary>
    /// A command renders identically under every culture, including one whose negative sign is not
    /// <c>-</c>. <c>sv-SE</c> rather than <c>de-DE</c>: German renders a negative integer with an
    /// ordinary hyphen, so a German test would prove nothing.
    /// </summary>
    [Fact]
    public void A_command_renders_identically_under_any_culture()
    {
        var swedish = new System.Globalization.CultureInfo("sv-SE");

        (-1).ToString(swedish).ShouldNotBe(
            (-1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "this assertion is only meaningful if the runtime actually has a Swedish culture. Under " +
            "globalization-invariant mode new CultureInfo(\"sv-SE\") silently returns the invariant " +
            "culture and the comparison below would hold over nothing.");

        var fork = new ChooseForkCommand(-1);

        Render(fork, swedish).ShouldBe(Render(fork, System.Globalization.CultureInfo.InvariantCulture));

        Render(fork, System.Globalization.CultureInfo.InvariantCulture)
            .ShouldContain("BranchIndex = -1", Case.Sensitive);

        Render(fork, swedish).ShouldNotContain(
            "−", Case.Sensitive,
            "U+2212 MINUS SIGN is what sv-SE renders a negative integer with. If it appears here the " +
            "hand-written PrintMembers is gone and the synthesized one is back.");

        // The list payloads, which without a PrintMembers render the wrapper's type name instead of
        // the ids a rejection diagnostic wants.
        new SalvageCommand(new[] { "a", "b" }).ToString()
            .ShouldContain("ItemIds = [a, b]", Case.Sensitive);

        new ClaimInboxCommand().ToString().ShouldContain("MessageIds = null", Case.Sensitive);
    }

    private static string Render(GameCommand command, System.Globalization.CultureInfo culture)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;

        try
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
            return command.ToString();
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>Equal commands hash equally, or a dictionary keyed on one would answer differently from <c>==</c>.</summary>
    [Fact]
    public void Equal_list_carrying_commands_hash_equally()
    {
        new SalvageCommand(new[] { "a", "b" }).GetHashCode()
            .ShouldBe(new SalvageCommand(new[] { "a", "b" }).GetHashCode());

        new RetuneItemCommand("i", new[] { "x" }, Array.Empty<string>()).GetHashCode()
            .ShouldBe(new RetuneItemCommand("i", new[] { "x" }, Array.Empty<string>()).GetHashCode());

        new ClaimInboxCommand().GetHashCode().ShouldBe(new ClaimInboxCommand().GetHashCode());
    }

    /// <summary>
    /// Every list payload is copied on the way in, and the copy does not cast back to the array
    /// behind it — a command whose contents can change after construction is a command whose
    /// idempotency key describes something other than what was applied.
    /// </summary>
    [Fact]
    public void Every_list_payload_is_copied_and_cannot_be_written_through()
    {
        var probes = new (string Name, Func<string[], IReadOnlyList<string>> Build)[]
        {
            (nameof(SalvageCommand.ItemIds), ids => new SalvageCommand(ids).ItemIds),
            (nameof(RetuneItemCommand.LockedAffixIds),
                ids => new RetuneItemCommand("i", ids, Array.Empty<string>()).LockedAffixIds),
            (nameof(RetuneItemCommand.WishlistAffixIds),
                ids => new RetuneItemCommand("i", Array.Empty<string>(), ids).WishlistAffixIds),
            (nameof(ClaimInboxCommand.MessageIds), ids => new ClaimInboxCommand(ids).MessageIds!),
        };

        probes.Length.ShouldBe(4, "14 §2.3 gives SALVAGE, RETUNE_ITEM (twice) and CLAIM_INBOX a list payload.");

        foreach (var (name, build) in probes)
        {
            var callers = new[] { "a", "b" };
            var stored = build(callers);

            callers[0] = "MUTATED";

            stored[0].ShouldBe("a", $"{name} handed back the caller's own array.");

            stored.ShouldNotBeAssignableTo<string[]>(
                $"{name}: a bare array behind an IReadOnlyList<string> casts straight back to " +
                "string[] — the hole M1-05 closed on the aggregates, one indirection out.");

            // ReadOnlyCollection<T> implements IList<T>, so the cast is available and the refusal has
            // to be the setter's rather than the type system's.
            Should.Throw<NotSupportedException>(() => ((IList<string>)stored)[0] = "MUTATED");
        }
    }

    /// <summary>
    /// The list properties are get-only, never <c>init</c> — the one thing stopping a <c>with</c>
    /// expression from handing the command the caller's own array and bypassing the copy, since an
    /// <c>init</c> assignment through <c>with</c> does not re-run the constructor.
    /// </summary>
    [Fact]
    public void A_list_payload_has_no_setter_so_with_cannot_bypass_the_copy()
    {
        var properties = new[]
        {
            typeof(SalvageCommand).GetProperty(nameof(SalvageCommand.ItemIds)),
            typeof(RetuneItemCommand).GetProperty(nameof(RetuneItemCommand.LockedAffixIds)),
            typeof(RetuneItemCommand).GetProperty(nameof(RetuneItemCommand.WishlistAffixIds)),
            typeof(ClaimInboxCommand).GetProperty(nameof(ClaimInboxCommand.MessageIds)),
        };

        properties.ShouldAllBe(p => p != null);
        properties.Length.ShouldBe(4, "the four list payloads of 14 §2.3.");

        foreach (var property in properties)
        {
            property!.SetMethod.ShouldBeNull(
                $"{property.DeclaringType!.Name}.{property.Name} has a setter. An init accessor is " +
                "assignable through `with`, which would hand the command the caller's own array and " +
                "bypass CommandPayload.Copy — the defence this property's remarks claim.");
        }
    }

    /// <summary>A required list payload is a caller defect when it is null, and says which parameter.</summary>
    [Fact]
    public void A_required_list_payload_refuses_null()
    {
        Should.Throw<ArgumentNullException>(() => new SalvageCommand(null!))
            .ParamName.ShouldBe("itemIds");

        Should.Throw<ArgumentNullException>(() => new RetuneItemCommand("i", null!, Array.Empty<string>()))
            .ParamName.ShouldBe("lockedAffixIds");

        Should.Throw<ArgumentNullException>(() => new RetuneItemCommand("i", Array.Empty<string>(), null!))
            .ParamName.ShouldBe("wishlistAffixIds");
    }

    // ------------------------------------------------------------------------------------ helpers

    /// <summary>A milestone task id: <c>M3-15</c>, <c>M12-04</c>. Stricter than a bare milestone — every owner must be a tracker row.</summary>
    private static readonly System.Text.RegularExpressions.Regex TaskId =
        new(@"^M\d{1,2}-\d{2}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The two transcribed halves as one ordinal set.</summary>
    private static HashSet<string> Transcribed() =>
        RunCommandWireNames.Concat(MetaCommandWireNames).ToHashSet(StringComparer.Ordinal);

    private static CommandRegistration RegistrationFor(string wireName) =>
        SlayIdleRepeat.Core.GameRules.RegistrationFor(Registry[wireName])
        ?? throw new InvalidOperationException(
            $"'{wireName}' is in the wire-name index but has no registration, which the index is a " +
            "projection of. The two cannot disagree unless the table has been rebuilt.");

    private static IEnumerable<string> Misclassified(string wireName, CommandKind expected)
    {
        if (!Registry.ContainsKey(wireName))
        {
            // Reported by the set comparison; complaining twice about one fact is two mechanisms.
            yield break;
        }

        var actual = RegistrationFor(wireName).Kind;

        if (actual != expected)
        {
            yield return
                $"'{wireName}' is a {expected} row of 14 §2.3 and is registered {actual}. The kind decides " +
                "whether Apply opens a RunRngScope over the run's 14 §8.1 counters and whether the run's " +
                "14 §16.3 sliding TTL moves — a run command filed as Meta is handed the run with no scope, " +
                "and a meta command filed as Run cannot be sent outside one at all.";
        }
    }

    private static IEnumerable<string> Malformed(string wireName, Type type)
    {
        if (!typeof(GameCommand).IsAssignableFrom(type))
        {
            yield return $"'{wireName}' is registered to {type.FullName}, which is not a GameCommand.";
            yield break;
        }

        if (!type.IsPublic)
        {
            yield return $"{type.FullName} ('{wireName}') is not public — 30 §11.2 makes the command " +
                         "hierarchy the wire protocol as well as the input vocabulary.";
        }

        if (!type.IsSealed || type.IsAbstract)
        {
            yield return $"{type.FullName} ('{wireName}') is not a sealed concrete type. A command that " +
                         "can be subclassed is a command whose dispatch row does not decide which rule runs.";
        }

        if (!string.Equals(type.Namespace, "SlayIdleRepeat.Core.Commands", StringComparison.Ordinal))
        {
            yield return $"{type.FullName} ('{wireName}') is in namespace '{type.Namespace}'. 30 §11.4's " +
                         "list is closed and a sub-namespace is not on it.";
        }
    }

    /// <summary>
    /// The <c>GameContext</c> a command of this wire name may legally be applied with — a
    /// server-issued <c>CommandSeed</c> for the nine seed-bearing rows, <c>null</c> for the other
    /// forty. The seed is a fixed arbitrary constant: these sweeps are about reachability, not
    /// determinism.
    /// </summary>
    private static GameContext ContextFor(string wireName) =>
        CommandSeedPin.SeedBearingMetaCommands.Contains(wireName)
            ? Worlds.Drawing(SweepSeed)
            : Worlds.Context;

    /// <summary>The seed the ⚄ rows are swept with. Arbitrary, fixed, and not a claim about a draw.</summary>
    private const ulong SweepSeed = 0xC0FFEE_1234_5678UL;

    /// <summary>
    /// One instance of a command type, built from its declared constructor. Reflective rather than
    /// forty-nine hand-written <c>new</c> expressions, which would be a second transcription of the
    /// vocabulary that silently stopped driving whichever command it forgot.
    /// </summary>
    private static GameCommand Build(Type commandType)
    {
        // Single, not First: an OrderByDescending(...).First() would silently pick between two if a
        // command ever gained a convenience overload, an unstable tie-break in a rule that drives the
        // whole vocabulary.
        var constructors = commandType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        if (constructors.Length != 1)
        {
            throw new InvalidOperationException(
                $"{commandType.Name} declares {constructors.Length} public constructors. A command is a " +
                "value carrying exactly 14 §2.3's parameters for it, and this builder drives all 49 " +
                "through the one door each of them has. If a second is genuinely wanted, choose here " +
                "deliberately rather than letting a tie-break pick.");
        }

        var constructor = constructors[0];
        var arguments = constructor.GetParameters().Select(Sample).ToArray();

        return (GameCommand)constructor.Invoke(arguments);
    }

    private static object? Sample(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;

        if (type == typeof(int))
        {
            return 0;
        }

        if (type == typeof(bool))
        {
            return false;
        }

        if (type == typeof(string))
        {
            return "x";
        }

        if (type == typeof(DifficultyTier))
        {
            return DifficultyTier.NORMAL;
        }

        if (type == typeof(IReadOnlyList<string>))
        {
            return Array.Empty<string>();
        }

        throw new InvalidOperationException(
            $"{parameter.Member.DeclaringType?.Name}.{parameter.Name} is a {type.Name}, which this " +
            "builder has no sample for. That is not a test to widen on autopilot: a command payload " +
            "type nothing here recognises is a new type in the vocabulary, and 14 §2.3's payload " +
            "sketches plus CommandPayload's remarks are where it has to be justified first.");
    }
}
