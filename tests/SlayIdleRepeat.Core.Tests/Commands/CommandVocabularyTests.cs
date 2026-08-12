using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Commands;

/// <summary>
/// 🔒 `14` §2.3 — the canonical command registry, pinned as a <b>set</b> against a hand-transcribed
/// literal list, in both directions.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The set, not the count, and the difference is the whole file.</b> Forty-nine <em>wrong</em>
/// names also count forty-nine. A count pin catches "someone dropped <c>REVIVE</c>" only if nobody
/// added anything in the same commit, and it never catches a rename — which on this table is a wire
/// break: `14` §16.2's forward-compatibility rule ("appendable, never renamed or reused") applies
/// to the registry, so a renamed row is a command every deployed client stops being able to send.
/// </para>
/// <para>
/// 🔒 <b>The literal lists are transcribed from the document, not derived from the registry</b>, and
/// that is what makes the comparison mean anything: a list read off <see cref="GameRules"/> would
/// say the registry is right because the registry says so. Counted off `14` §2.3 before the
/// vocabulary was written — <b>19 run rows and 30 meta rows</b>, against a table header that says
/// "Meta commands (29)" and a tracker that used to say 48. The M1 kickoff (2026-08-11) ruled both to
/// be miscounts of a correct table: errata, not a scope change.
/// </para>
/// <para>
/// ⚠️ <b>Every rule here is floored</b> (steering <b>S3</b>). Two empty sets compare equal, so the
/// transcription's own size and identity are asserted first, as literals: a set comparison over an
/// emptied list is the failure this file exists to prevent, not a mechanism it may rest on.
/// </para>
/// </remarks>
public sealed class CommandVocabularyTests
{
    /// <summary>
    /// 🔒 `14` §2.3's <b>Run commands (19)</b> table, transcribed by hand in the document's order.
    /// </summary>
    /// <remarks>
    /// The endpoint is <c>POST /run/{runId}/command</c> with the sequence per run —
    /// <c>START_RUN</c> excepted, which is submitted on the player endpoint because no
    /// <c>runId</c> exists yet. That exception is about the URL and not about the kind; see
    /// <see cref="START_RUN_is_a_run_command_even_though_it_is_sent_to_the_player_endpoint"/>.
    /// </remarks>
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

    /// <summary>
    /// 🔒 `14` §2.3's <b>Meta commands</b> table, transcribed by hand in the document's order. Thirty
    /// rows, under a header that says twenty-nine.
    /// </summary>
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
    /// 🔒 Steering <b>S3</b> — the floor under every comparison below: the transcription is the
    /// right <b>size</b> and carries the right <b>members</b>, both asserted as literals.
    /// </summary>
    /// <remarks>
    /// Without this, emptying either list would make the two set comparisons hold vacuously and this
    /// whole file would go green over a registry it had stopped reading. The identity anchors are
    /// chosen for what they catch: <c>SHOP_BUY</c> and <c>SHOP_PURCHASE</c> are the pair `14` §2.3
    /// warns are distinct, and <c>START_RUN</c> is the row whose classification is the easiest to get
    /// wrong.
    /// </remarks>
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

    /// <summary>
    /// 🔒 `14` §2.3 — <b>direction one</b>: every command the document lists is registered. A row
    /// dropped from the dispatch table fails here naming it.
    /// </summary>
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
    /// 🔒 `14` §2.3 — <b>direction two</b>, and the one a count pin cannot express: nothing is
    /// registered that the document does not list. <em>"A command not listed here does not
    /// exist."</em>
    /// </summary>
    /// <remarks>
    /// This is the half that catches a rename: <c>ROLL_DICE</c> spelled <c>ROLLDICE</c> leaves the
    /// count at 49 and fails both directions at once, naming the old name here and the new one above.
    /// Adding a command is a decision logged in `16` and landed in `14` §2.3 <em>first</em>; this
    /// rule is what makes the order mandatory rather than customary.
    /// </remarks>
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
            "the two directions above are set comparisons, and a set comparison cannot see a wire " +
            "name registered twice under two types — CommandDispatch refuses that at type " +
            "initialisation, and this is the assertion that would notice if it stopped.");
    }

    /// <summary>
    /// 🔒 The comparison is <b>ordinal</b>. A case-insensitive registry would answer for
    /// <c>roll_dice</c>, and `14` §2.3's ids are compared byte for byte on the wire.
    /// </summary>
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
    /// 🔒 `14` §2.3 / `30` §3 — every run row is registered <c>CommandKind.Run</c> and every meta row
    /// <c>CommandKind.Meta</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The kind is correctness, not bookkeeping.</b> It decides whether <c>Apply</c> opens a
    /// <c>RunRngScope</c> over the run's `14` §8.1 counters and whether the run's `14` §16.3 sliding
    /// TTL moves. M1-06 found the hole it protects: a <c>CommandKind.Meta</c> handler <em>is</em>
    /// dispatched with a run in the slice, so a run command misfiled as meta would be handed the run
    /// and no scope — able to reach the counters with nothing folding them back.
    /// </remarks>
    [Fact]
    public void Every_row_is_registered_under_the_kind_its_table_gives_it()
    {
        var offenders = new List<string>();

        foreach (var name in RunCommandWireNames)
        {
            offenders.AddRange(Misclassified(name, CommandKind.Run));
        }

        foreach (var name in MetaCommandWireNames)
        {
            offenders.AddRange(Misclassified(name, CommandKind.Meta));
        }

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 The one row worth naming on its own: <c>START_RUN</c> is a <b>run</b> command even though
    /// `14` §2.3 submits it to the <b>player</b> endpoint.
    /// </summary>
    /// <remarks>
    /// The exception in the document is about the URL — there is no <c>runId</c> yet, so the server
    /// allocates one and the run's sequence starts at 1. Classifying the command <c>Meta</c> to match
    /// its endpoint would hand the one command that commits <c>runSeed</c> (`02` §2) no
    /// <c>RunRngScope</c> at all, and would leave the run it created ageing off a `14` §16.3 TTL that
    /// nothing had advanced. Pinned separately from the table sweep above because the sweep would
    /// pass just as happily with both halves of the mistake made together.
    /// </remarks>
    [Fact]
    public void START_RUN_is_a_run_command_even_though_it_is_sent_to_the_player_endpoint()
    {
        RegistrationFor("START_RUN").Kind.ShouldBe(CommandKind.Run);
        Registry["START_RUN"].ShouldBe(typeof(StartRunCommand));
    }

    /// <summary>
    /// 🔒 `14` §2.3 flags them as distinct and they are: <c>SHOP_BUY</c> spends the run's Gold
    /// (`03` §7, milestone assumption <b>A3</b>) and <c>SHOP_PURCHASE</c> the player's wallet
    /// (`10` §5). Two rows, two types, two kinds.
    /// </summary>
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
    /// 🔒 `30` §11.2 / `30` §11.4 — every registered type is a <b>public, concrete, sealed</b>
    /// <c>GameCommand</c> declared directly under <c>SlayIdleRepeat.Core.Commands</c>.
    /// </summary>
    /// <remarks>
    /// Public because `30` §11.2 makes the hierarchy the wire protocol as well as the input
    /// vocabulary. Sealed because a command that could be subclassed would be a command whose
    /// dispatch row does not decide which rule runs. And the namespace is asserted <b>exactly</b>:
    /// `30` §11.4's list is closed, a sub-namespace is not on it, and
    /// <c>Every_Core_type_lives_under_a_documented_namespace</c> is what would notice — from the
    /// other suite, which is a worse place to find it than here.
    /// </remarks>
    [Fact]
    public void Every_registered_type_is_a_public_sealed_command_in_the_commands_namespace()
    {
        var offenders = Registry
            .OrderBy(row => row.Key, StringComparer.Ordinal)
            .SelectMany(row => Malformed(row.Key, row.Value))
            .ToArray();

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 Every row is either handled today or names the milestone task that will handle it — the
    /// deferral's only expiry.
    /// </summary>
    /// <remarks>
    /// All forty-nine are <c>Deferred</c> on this commit, including <c>BEGIN_SESSION</c>, whose
    /// handler is M1-09's. The rule is written as "handled <em>or</em> owned" rather than "all
    /// deferred" so that M1-09 swapping one row to <c>Handled</c> is a one-line edit here too —
    /// and so this does not become a count nobody may change.
    /// </remarks>
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
    /// 🔒 `30` §2.1's <b>P3</b> over the <b>real</b> table: all forty-nine commands are constructible
    /// and every one of them is <em>refused</em> — not thrown — while its milestone is unbuilt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the end-to-end half of <c>Every_command_type_is_handled_by_Apply</c>. That rule reads
    /// metadata and says a dispatch row <em>names</em> each type; this drives each type through
    /// <c>GameRules.Apply</c> and says the row actually resolves — a row registered under a type the
    /// runtime never produces would satisfy the IL scan and fail here.
    /// </para>
    /// <para>
    /// The slice carries a run, which is legal for both kinds: a run command needs one, and a meta
    /// command is dispatched perfectly happily with one (a player can open the shop without leaving).
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_command_in_the_vocabulary_is_applied_and_refused_rather_than_thrown()
    {
        var applied = 0;

        foreach (var (name, type) in Registry.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            var result = SlayIdleRepeat.Core.GameRules.Apply(Worlds.InARun(), Build(type), Worlds.Context);

            result.Accepted.ShouldBeFalse($"'{name}' has no handler yet, so it cannot be accepted.");
            result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE, $"'{name}' is deferred.");
            applied++;
        }

        applied.ShouldBe(49, "every row is driven, or this rule is quantifying over less than the vocabulary.");
    }

    /// <summary>
    /// 🔒 A real run command with no run in the slice is a <b>loading defect</b>, and the message
    /// names the command by its wire name.
    /// </summary>
    /// <remarks>
    /// M1-06 pinned this against a fixture; this pins it against the vocabulary, which is where it
    /// will actually be hit. `14` §16.2's <c>RUN_NOT_FOUND</c> is transport tier, so a run command
    /// that reached the domain without its run is the Application layer loading the wrong slice
    /// (`30` §4.1) rather than a player asking for something they cannot have.
    /// </remarks>
    [Fact]
    public void A_real_run_command_without_a_run_names_itself_in_the_defect()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.OutsideARun(), new RollDiceCommand(), Worlds.Context));

        thrown.Message.ShouldContain("'ROLL_DICE'", Case.Sensitive);
        thrown.Message.ShouldContain("RUN_NOT_FOUND", Case.Sensitive);
    }

    // ---------------------------------------------------------------------------- the table's guards

    /// <summary>
    /// 🔒 `14` §2.3 is <b>one</b> vocabulary — two commands claiming <c>SHOP_BUY</c> must not
    /// silently win. Driven against two of the real forty-nine rather than fixtures.
    /// </summary>
    /// <remarks>
    /// M1-06 proved the guard against fixture types. This proves it against the shape it would
    /// actually take: a second row for a name the registry already carries, added by a task that did
    /// not read the table first. The refusal names <b>both</b> claimants, so the reader does not have
    /// to grep for the other one.
    /// </remarks>
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
    /// 🔒 `14` §3.2 — the three list-carrying commands compare <b>by value</b>, which the synthesized
    /// record equality would not have done.
    /// </summary>
    /// <remarks>
    /// <see cref="GameCommand"/>'s own remarks make value equality the contract that lets a repeated
    /// command replay its stored outcome. A record compares an <c>IReadOnlyList&lt;string&gt;</c>
    /// member by reference, so without the hand-written <c>Equals</c> these three would be the only
    /// commands in the registry for which that promise silently did not hold — and the two lists on
    /// <c>RETUNE_ITEM</c> make it the sharpest case.
    /// </remarks>
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
    }

    /// <summary>
    /// 🔒 Equal commands hash equally, or a dictionary keyed on one would answer differently from
    /// <c>==</c>.
    /// </summary>
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
    /// 🔒 A list payload is <b>copied</b> on the way in, so a caller cannot rewrite the command after
    /// it was built — and the copy does not cast back to the array behind it.
    /// </summary>
    /// <remarks>
    /// The same hole M1-05 closed on the aggregates: a bare <c>string[]</c> handed out through an
    /// <c>IReadOnlyList&lt;string&gt;</c> casts straight back. A command whose contents can change
    /// after construction is a command whose `14` §16.3 idempotency key describes something other
    /// than what was applied.
    /// </remarks>
    [Fact]
    public void A_list_payload_is_copied_and_cannot_be_written_through()
    {
        var callers = new[] { "a", "b" };
        var command = new SalvageCommand(callers);

        callers[0] = "MUTATED";

        command.ItemIds[0].ShouldBe("a");

        command.ItemIds.ShouldNotBeAssignableTo<string[]>(
            "a bare array behind an IReadOnlyList<string> casts straight back to string[] — the hole " +
            "M1-05 closed on the aggregates, one indirection out.");

        // ⚠️ ReadOnlyCollection<T> DOES implement IList<T>, so the cast is available and the refusal
        // has to be the setter's rather than the type system's. Asserted, not assumed.
        Should.Throw<NotSupportedException>(() => ((IList<string>)command.ItemIds)[0] = "MUTATED");
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

    private static readonly System.Text.RegularExpressions.Regex TaskId =
        new(@"^M\d{1,2}(-\d{2})?$", System.Text.RegularExpressions.RegexOptions.Compiled);

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
    /// One instance of a command type, built from its declared constructor.
    /// </summary>
    /// <remarks>
    /// Reflective rather than a hand-written list of forty-nine <c>new</c> expressions, and that is
    /// the point: a hand-written list is a second transcription of the vocabulary, and the day it
    /// went out of date it would silently stop driving whichever command it had forgotten. The values
    /// are deliberately trivial — no rule reads a payload today, and one that did would be tested
    /// against its own fixtures rather than these.
    /// </remarks>
    private static GameCommand Build(Type commandType)
    {
        var constructor = commandType
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

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
