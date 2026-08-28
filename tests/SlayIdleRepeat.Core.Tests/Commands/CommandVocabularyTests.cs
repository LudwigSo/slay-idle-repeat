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
    /// <summary>14 §2.3's Run table, transcribed by hand in the document's order.</summary>
    /// <remarks>
    /// 22 rows. SHOP_LEAVE and SHRINE_CHOOSE were added to the registry, each carrying a tile choice
    /// no existing command could express; USE_REROLL and DICE_FORGE_CHOOSE were then removed with the
    /// reroll and the die's special faces (55 -> 53); and USE_FIXED_DIE and CHOOSE_FIXED_DIE arrived
    /// with the fixed dice that replaced them (53 -> 55) — a second way to MOVE, and the one door
    /// every grant site's number choice is answered by.
    /// </remarks>
    public static readonly string[] RunCommandWireNames =
    {
        "START_RUN",
        "ROLL_DICE",
        "USE_FIXED_DIE",
        "CHOOSE_FIXED_DIE",
        "CHOOSE_FORK",
        "RESOLVE_TILE",
        "PICK_PERK",
        "REROLL_DRAFT",
        "SKIP_DRAFT",
        "SHOP_BUY",
        "SHOP_REFRESH",
        "SHOP_LEAVE",
        "SHRINE_CHOOSE",
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

    /// <summary>14 §2.3's Meta table, transcribed by hand in the document's order.</summary>
    public static readonly string[] MetaCommandWireNames =
    {
        "BEGIN_SESSION",
        "SKIP_FTUE",
        "EQUIP",
        "UNEQUIP",
        "MERGE",
        "ENHANCE",
        "SALVAGE",
        "LOCK_ITEM",
        "SET_AUTO_SALVAGE_RULES",
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

    /// <summary>
    /// 🔒 The closed list of handled rows whose generically-built payload is a legal command that is
    /// not a legal MOVE. <c>Build</c> samples every <c>int</c> as 0 and every id as one the sample
    /// player does not own, so: the two preset rows refuse slot 0 (07 §4 numbers slots from 1), the
    /// item-naming rows (forge trio, EQUIP, LOCK_ITEM) answer NOT_OWNED, and UNEQUIP names a legal
    /// slot the sample player is wearing nothing in (ILLEGAL_STATE). Every other handled row must
    /// still accept, so a row cannot quietly start refusing everything.
    /// </summary>
    private static readonly string[] RowsBuildCannotSatisfy =
    [
        "MERGE", "ENHANCE", "SALVAGE", "SAVE_PRESET", "APPLY_PRESET", "EQUIP", "UNEQUIP", "LOCK_ITEM",
    ];

    /// <summary>
    /// 🔒 The closed list of handled META rows for which a slice this fixture builds is a LOADING
    /// defect rather than a legal move — the meta-tier counterpart of the run rows' missing-run
    /// throw, and asserted the same way.
    /// </summary>
    /// <remarks>
    /// <c>CLAIM_INBOX</c> reads the inbox projection, and <c>Worlds.OutsideARun()</c> carries none.
    /// A handler that read a missing projection as an empty one would tell a player with unclaimed
    /// compensation that they have nothing to collect, so it throws — and this list is what stops
    /// that throw being mistaken for the row having no handler at all.
    /// </remarks>
    private static readonly string[] RowsNeedingAProjection = ["CLAIM_INBOX"];

    // ------------------------------------------------------------- the set, in both directions

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

    [Fact]
    public void Nothing_is_registered_that_the_document_does_not_list()
    {
        var transcribed = Transcribed();

        var unlisted = Registry.Keys
            .Where(name => !transcribed.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        unlisted.ShouldBeEmpty(
            "14 §2.3 is exhaustive: 'a command not listed here does not exist'.");

        Registry.Count.ShouldBe(
            55,
            "22 run + 33 meta. The literal floors both set comparisons above: an emptied registry " +
            "would otherwise make 'nothing unlisted' trivially true.");
    }

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
    /// The kind decides whether <c>Apply</c> opens a <c>RunRngScope</c> over the run's counters and
    /// whether the run's TTL moves; a run command misfiled as meta gets the run and no scope.
    /// </summary>
    [Fact]
    public void Every_row_is_registered_under_the_kind_its_table_gives_it()
    {
        RunCommandWireNames.Length.ShouldBe(
            22, "14 §2.3's run table, counted off the document.");
        MetaCommandWireNames.Length.ShouldBe(33, "14 §2.3's meta table, counted off the document.");
        Registry.Count.ShouldBe(55, "an emptied registry makes the sweep below silent, not red.");

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

    /// <summary><c>SHOP_BUY</c> spends the run's Gold and <c>SHOP_PURCHASE</c> the player's wallet. Two rows, two types, two kinds.</summary>
    [Fact]
    public void The_in_run_shop_and_the_meta_shop_are_two_commands()
    {
        Registry["SHOP_BUY"].ShouldBe(typeof(ShopBuyCommand));
        Registry["SHOP_PURCHASE"].ShouldBe(typeof(ShopPurchaseCommand));

        RegistrationFor("SHOP_BUY").Kind.ShouldBe(CommandKind.Run);
        RegistrationFor("SHOP_PURCHASE").Kind.ShouldBe(CommandKind.Meta);
    }

    // -------------------------------------------------------------------- the vocabulary, applied

    /// <summary>
    /// Every deferred command is constructible and refused, not thrown. Builds each type and hands
    /// it to <c>GameRules.Apply</c>, so the row resolves at runtime rather than only in IL.
    /// </summary>
    [Fact]
    public void Every_command_in_the_vocabulary_is_applied_and_refused_rather_than_thrown()
    {
        var deferred = 0;

        foreach (var (name, type) in Registry.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            if (RegistrationFor(name).IsHandled)
            {
                continue;
            }

            var result = SlayIdleRepeat.Core.GameRules.Apply(Worlds.InARun(), Build(type), Worlds.Context);

            result.Accepted.ShouldBeFalse($"'{name}' has no handler yet, so it cannot be accepted.");
            result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE, $"'{name}' is deferred.");
            deferred++;
        }

        deferred.ShouldBe(
            Registry.Count(row => !RegistrationFor(row.Key).IsHandled),
            "a mismatch means the loop skipped a deferred row rather than that the count moved.");

        deferred.ShouldBe(
            22,
            "the absolute number, because the assertion above compares the loop against the same " +
            "table it walks and would agree with itself if every row silently became Handled. Lower " +
            "this by exactly the number of rows that gain a handler. M5-08 did exactly that for " +
            "CLAIM_INBOX: 23 -> 22.");
    }

    /// <summary>
    /// The behavioural half of the kind: every run row refuses a slice with no run, and every meta
    /// row is content with one.
    /// </summary>
    [Fact]
    public void Outside_a_run_every_run_command_is_a_loading_defect_and_every_meta_command_is_not()
    {
        var runRows = 0;
        var metaRows = 0;
        var handledAndAccepted = new List<string>();
        var handledAndRefused = new List<string>();

        RowsBuildCannotSatisfy.Length.ShouldBe(
            8, "the exemption is closed; a ninth row that stops accepting must take a diff here.");

        RowsNeedingAProjection.Length.ShouldBe(
            1,
            "the projection exemption is closed too: a second row that starts throwing here must " +
            "take a diff, or a handler that began faulting on an ordinary slice would read as " +
            "'this row needs a projection'.");

        foreach (var (name, type) in Registry.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            var command = Build(type);

            if (RegistrationFor(name).Kind == CommandKind.Run)
            {
                // START_RUN is the one run row that creates the Run itself, so a run-less slice is
                // its natural one. Build's ChapterId sample of 0 is below 02 §1's floor of 1, so it
                // reaches a domain-tier REJECTION rather than run-less acceptance — the positive
                // acceptance claim lives in InMemoryGameTests.
                if (RegistrationFor(name).OpensRun)
                {
                    var opened = SlayIdleRepeat.Core.GameRules.Apply(Worlds.OutsideARun(), command, Worlds.Context);

                    opened.Accepted.ShouldBeFalse(
                        $"'{name}' built with chapter 0, which its own handler refuses.");
                    opened.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
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

            if (RowsNeedingAProjection.Contains(name, StringComparer.Ordinal))
            {
                Should.Throw<InvalidOperationException>(
                        () => SlayIdleRepeat.Core.GameRules
                            .Apply(Worlds.OutsideARun(), command, ContextFor(command)),
                        $"'{name}' reads a projection this slice does not carry, which is a LOADING " +
                        "defect (30 §4.1) at the meta tier — not a rejection, and not an empty answer.")
                    .Message.ShouldContain($"{name} was dispatched", Case.Sensitive);

                metaRows++;
                continue;
            }

            var result = SlayIdleRepeat.Core.GameRules
                .Apply(Worlds.OutsideARun(), command, ContextFor(command));

            if (RegistrationFor(name).IsHandled)
            {
                if (RowsBuildCannotSatisfy.Contains(name, StringComparer.Ordinal))
                {
                    RejectionReasons.IsDomainTier(result.Rejection!.Value).ShouldBeTrue(
                        $"'{name}' is exempt from the acceptance claim because Build's generic " +
                        "payload is not a legal move for it — but it must still have REACHED its " +
                        "handler and answered a DOMAIN-tier value.");

                    handledAndRefused.Add(name);
                }
                else
                {
                    result.Accepted.ShouldBeTrue(
                        $"'{name}' is a handled meta command, so outside a run it runs its handler. " +
                        $"If Build's payload is genuinely illegal for '{name}', add it to " +
                        $"{nameof(RowsBuildCannotSatisfy)} with the reason.");

                    handledAndAccepted.Add(name);
                }
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

        runRows.ShouldBe(
            22,
            "14 §2.3's run table: 22 rows again after two removals and two arrivals — see RunCommandWireNames.");
        metaRows.ShouldBe(33, "14 §2.3's meta table has 33 rows.");

        // Both sides by identity: the tier assertion above is satisfied by a table in which every
        // handled row refuses AND by one in which every handled row accepts.
        handledAndAccepted.ShouldBe(
            new[] { "BEGIN_SESSION", "SET_AUTO_SALVAGE_RULES" },
            ignoreOrder: true,
            "the handled meta rows whose generic payload is legal — BEGIN_SESSION names no item, no " +
            "slot and no id, and SET_AUTO_SALVAGE_RULES is handed the empty rule list, which 08 §4.3 " +
            "makes a legal filter meaning 'sweep nothing'. If either stops accepting, the arm above " +
            "has stopped proving that a handled meta command is reached at all.");

        handledAndRefused.ShouldBe(
            RowsBuildCannotSatisfy,
            ignoreOrder: true,
            "the exemption list and the observed set are the SAME set, so a row cannot be excused " +
            "without also being seen to refuse.");
    }

    /// <summary>
    /// A run command with no run in the slice is a loading defect naming the command by its wire
    /// name — the Application layer loaded the wrong slice, not a player asking for the impossible.
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

    /// <summary>
    /// Internal seam on purpose: the shipped table is built once and cannot be made to collide from
    /// any public entry point, so only a fresh <c>CommandDispatch</c> can exercise the guard.
    /// </summary>
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
    /// The list-carrying commands compare by value, which synthesized record equality would not do —
    /// a record compares an <c>IReadOnlyList&lt;string&gt;</c> member by reference.
    /// </summary>
    [Fact]
    public void The_list_carrying_commands_compare_by_value()
    {
        new SalvageCommand(Items("a", "b"))
            .ShouldBe(new SalvageCommand(new List<GearInstanceId>(Items("a", "b"))));

        new SalvageCommand(Items("a", "b"))
            .ShouldNotBe(new SalvageCommand(Items("b", "a")));

        new RetuneItemCommand(Item("i"), new[] { "AFX_PEN" }, new[] { "AFX_CRIT_CHANCE" })
            .ShouldBe(new RetuneItemCommand(Item("i"), new[] { "AFX_PEN" }, new[] { "AFX_CRIT_CHANCE" }));

        new RetuneItemCommand(Item("i"), new[] { "AFX_PEN" }, new[] { "AFX_CRIT_CHANCE" })
            .ShouldNotBe(new RetuneItemCommand(Item("i"), new[] { "AFX_CRIT_CHANCE" }, new[] { "AFX_PEN" }),
                "the locks and the wishlist are different fields; swapping them is a different intent.");

        new ClaimInboxCommand(new[] { "m1" }).ShouldBe(new ClaimInboxCommand(new[] { "m1" }));

        new ClaimInboxCommand().ShouldBe(new ClaimInboxCommand());

        new ClaimInboxCommand().ShouldNotBe(
            new ClaimInboxCommand(Array.Empty<string>()),
            "14 §2.3 makes omitted and empty mean the same thing to the RULE, and they are still two " +
            "different things the client sent. Collapsing them here would make the command lie about " +
            "the request 14 §16.3 replays an outcome for.");

        new SalvageCommand(Items("AFX_PEN")).ShouldNotBe(
            new SalvageCommand(Items("afx_pen")),
            "payload ids compare ordinally, like every other 14 §2.3 identifier in this repository.");
    }

    /// <summary>
    /// The equality that matters is the one reached through the base type, since that is how an
    /// idempotency cache holds these: a dictionary round-trip exercises <c>Equals(object?)</c>,
    /// <c>==</c> and <c>GetHashCode</c> together.
    /// </summary>
    [Fact]
    public void A_list_carrying_command_survives_a_base_typed_dictionary_round_trip()
    {
        var cache = new Dictionary<GameCommand, string>
        {
            [new SalvageCommand(Items("a", "b"))] = "outcome",
            [new ClaimInboxCommand()] = "claim-all",
            [new RetuneItemCommand(Item("i"), new[] { "x" }, Array.Empty<string>())] = "retune",
        };

        cache[new SalvageCommand(new List<GearInstanceId>(Items("a", "b")))].ShouldBe("outcome");
        cache[new ClaimInboxCommand()].ShouldBe("claim-all");
        cache[new RetuneItemCommand(Item("i"), new[] { "x" }, Array.Empty<string>())].ShouldBe("retune");

        cache.ContainsKey(new SalvageCommand(Items("b", "a"))).ShouldBeFalse();

        new SalvageCommand(Array.Empty<GearInstanceId>()).GetHashCode()
            .ShouldNotBe(
                new ClaimInboxCommand(Array.Empty<string>()).GetHashCode(),
                "the EqualityContract term in the hand-written GetHashCode — without it two different " +
                "empty-payload commands bucket together in the very cache the replay cache will be.");

        ((GameCommand)new SalvageCommand(Items("a")))
            .Equals(new SalvageCommand(Items("a"))).ShouldBeTrue();

        (new SalvageCommand(Items("a")) == new SalvageCommand(Items("a"))).ShouldBeTrue();
        (new SalvageCommand(Items("a")) != new SalvageCommand(Items("z"))).ShouldBeTrue();
    }

    /// <summary>
    /// <c>sv-SE</c> rather than <c>de-DE</c>: German renders a negative integer with an ordinary
    /// hyphen, so a German test would prove nothing.
    /// </summary>
    [Fact]
    public void A_command_renders_identically_under_any_culture()
    {
        var swedish = new System.Globalization.CultureInfo("sv-SE");

        (-1).ToString(swedish).ShouldNotBe(
            (-1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "under globalization-invariant mode new CultureInfo(\"sv-SE\") silently returns the " +
            "invariant culture and the comparison below would hold over nothing.");

        var fork = new ChooseForkCommand(-1);

        Render(fork, swedish).ShouldBe(Render(fork, System.Globalization.CultureInfo.InvariantCulture));

        Render(fork, System.Globalization.CultureInfo.InvariantCulture)
            .ShouldContain("BranchIndex = -1", Case.Sensitive);

        Render(fork, swedish).ShouldNotContain(
            "−", Case.Sensitive,
            "U+2212 MINUS SIGN is what sv-SE renders a negative integer with. If it appears here the " +
            "hand-written PrintMembers is gone and the synthesized one is back.");

        new SalvageCommand(Items("a", "b")).ToString()
            .ShouldContain("ItemIds = [a, b]", Case.Sensitive,
                "without a PrintMembers a list payload renders the wrapper's type name instead of " +
                "the ids a rejection diagnostic wants.");

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

    /// <summary>
    /// Every list payload is copied on the way in, and the copy does not cast back to the array
    /// behind it — a command whose contents can change after construction is a command whose
    /// idempotency key describes something other than what was applied.
    /// </summary>
    [Fact]
    public void Every_list_payload_is_copied_and_cannot_be_written_through()
    {
        // SALVAGE carries GearInstanceId rather than string, so the probes hand back the non-generic
        // IList every ReadOnlyCollection<T> implements and assert over the element's rendering.
        var probes = new (string Name, Func<string[], System.Collections.IList> Build)[]
        {
            (nameof(SalvageCommand.ItemIds),
                ids => (System.Collections.IList)new SalvageCommand(Items(ids)).ItemIds),
            (nameof(RetuneItemCommand.LockedAffixIds),
                ids => (System.Collections.IList)new RetuneItemCommand(
                    Item("i"), ids, Array.Empty<string>()).LockedAffixIds),
            (nameof(RetuneItemCommand.WishlistAffixIds),
                ids => (System.Collections.IList)new RetuneItemCommand(
                    Item("i"), Array.Empty<string>(), ids).WishlistAffixIds),
            (nameof(ClaimInboxCommand.MessageIds),
                ids => (System.Collections.IList)new ClaimInboxCommand(ids).MessageIds!),
        };

        probes.Length.ShouldBe(4, "14 §2.3 gives SALVAGE, RETUNE_ITEM (twice) and CLAIM_INBOX a list payload.");

        foreach (var (name, build) in probes)
        {
            var callers = new[] { "a", "b" };
            var stored = build(callers);

            callers[0] = "MUTATED";

            stored[0]!.ToString().ShouldBe("a", $"{name} handed back the caller's own array.");

            stored.GetType().IsArray.ShouldBeFalse(
                $"{name}: a bare array behind an IReadOnlyList<T> casts straight back to T[].");

            Should.Throw<NotSupportedException>(() => stored[0] = "MUTATED");
        }
    }

    [Fact]
    public void A_required_list_payload_refuses_null()
    {
        Should.Throw<ArgumentNullException>(() => new SalvageCommand(null!))
            .ParamName.ShouldBe("itemIds");

        Should.Throw<ArgumentNullException>(() => new RetuneItemCommand(Item("i"), null!, Array.Empty<string>()))
            .ParamName.ShouldBe("lockedAffixIds");

        Should.Throw<ArgumentNullException>(() => new RetuneItemCommand(Item("i"), Array.Empty<string>(), null!))
            .ParamName.ShouldBe("wishlistAffixIds");
    }

    // ------------------------------------------------------------------------------------ helpers

    private static GearInstanceId Item(string id) => new(id);

    private static GearInstanceId[] Items(params string[] ids) => ids.Select(Item).ToArray();

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
                "14 §16.3 sliding TTL moves.";
        }
    }

    /// <summary>
    /// The context the real hosts would apply this command with: a seed exactly when
    /// <c>GameRules.RequiresCommandSeed</c> says so — the same predicate <c>InProcessGameHost</c> and
    /// <c>InMemoryGame</c> issue seeds by. Reachability, not determinism, so the seed is arbitrary.
    /// </summary>
    private static GameContext ContextFor(GameCommand command) =>
        SlayIdleRepeat.Core.GameRules.RequiresCommandSeed(command)
            ? Worlds.Drawing(SweepSeed)
            : Worlds.Context;

    private const ulong SweepSeed = 0xC0FFEE_1234_5678UL;

    /// <summary>
    /// One instance of a command type, built from its declared constructor. Reflective rather than
    /// fifty-two hand-written <c>new</c> expressions, which would be a second transcription of the
    /// vocabulary that silently stopped driving whichever command it forgot. Internal so sibling
    /// sweeps drive the same instances.
    /// </summary>
    internal static GameCommand Build(Type commandType)
    {
        // Single, not First: a tie-break between two constructors would silently pick, in a builder
        // that drives the whole vocabulary.
        var constructors = commandType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        if (constructors.Length != 1)
        {
            throw new InvalidOperationException(
                $"{commandType.Name} declares {constructors.Length} public constructors. If a second " +
                "is genuinely wanted, choose here deliberately rather than letting a tie-break pick.");
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

        if (type == typeof(GearInstanceId))
        {
            return new GearInstanceId("x");
        }

        // A nullable slot or family is the SET_FOCUS clear, so the sample is the VALUE rather than
        // null — a null sample would exercise the clearing path and never the naming one.
        if (type == typeof(GearSlot) || type == typeof(GearSlot?))
        {
            return GearSlot.WEAPON;
        }

        if (type == typeof(GearFamily) || type == typeof(GearFamily?))
        {
            return GearFamily.BLADE;
        }

        if (type == typeof(IReadOnlyList<GearInstanceId>))
        {
            return Array.Empty<GearInstanceId>();
        }

        // The EMPTY list, not a fabricated row: 08 §4.3 makes an empty filter mean "sweep nothing",
        // a legal payload the sweep can assert an acceptance on, and a sampled row would have to
        // invent a band and a ceiling with no design document behind it.
        if (type == typeof(IReadOnlyList<AutoSalvageRule>))
        {
            return Array.Empty<AutoSalvageRule>();
        }

        // An optional integer payload. A VALUE rather than null, on the SET_FOCUS precedent above
        // and for the same reason: a null sample would make a row's generic build a request the
        // handler must refuse for a reason unrelated to what the sweep is asking about.
        // ⚠️ DICE_FORGE_CHOOSE's pip count was the one row that reached this arm; no command carries
        // an int? today, so this is a guard with nothing exercising it.
        if (type == typeof(int?))
        {
            return 6;
        }

        throw new InvalidOperationException(
            $"{parameter.Member.DeclaringType?.Name}.{parameter.Name} is a {type.Name}, which this " +
            "builder has no sample for. A payload type nothing here recognises is a new type in the " +
            "vocabulary; 14 §2.3's payload sketches are where it has to be justified first.");
    }
}
