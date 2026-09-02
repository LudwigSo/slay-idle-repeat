using System.Globalization;
using System.Text;
using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Luck;
using SlayIdleRepeat.Core.Rules.Perks;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Content.Perks;
using SlayIdleRepeat.Core.Tests.Handlers;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;

namespace SlayIdleRepeat.Core.Tests.Rules.Perks;

/// <summary>
/// <c>DraftView</c> — the narrow public projection of the three options a run currently has on
/// offer. The load-bearing claim is that these are the SAME three options <c>PICK_PERK</c> acts on
/// (steering S2): the options regenerate from the run's committed <c>draft</c> stream position, and
/// a view that derived or ordered them a second way would show the player a card other than the one
/// their finger is on.
/// </summary>
public sealed class DraftViewTests
{
    /// <summary>The content every case that needs the hermetic perk catalogue projects against.</summary>
    private static ContentSnapshot Content => DraftWorlds.Context.Content;

    // ------------------------------------------------------------------------------------------
    // The gate.
    // ------------------------------------------------------------------------------------------

    /// <summary>A run with no draft open has no draft to draw.</summary>
    [Fact]
    public void A_run_with_no_draft_pending_projects_nothing()
    {
        DraftView.Project(RunSnapshots.With(draftPending: false), Content).ShouldBeNull();
    }

    /// <summary>…and an open draft projects exactly the three options a draft always offers.</summary>
    [Fact]
    public void An_open_draft_projects_three_options()
    {
        var view = Projected(DraftWorlds.DraftPendingOn());

        view.Options.Count.ShouldBe(3, "a draft offers three options and this view draws all of them");
    }

    /// <summary>…and the three name three different perks, so an index is a meaningful choice.</summary>
    [Fact]
    public void The_three_options_name_three_different_perks()
    {
        var view = Projected(DraftWorlds.DraftPendingOn());

        view.Options.Select(o => o.PerkId).Distinct(StringComparer.Ordinal).Count().ShouldBe(3);
    }

    // ------------------------------------------------------------------------------------------
    // 🔒 The view and the command are looking at the same draft.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 The perk the view draws at an index is the perk <c>PICK_PERK</c> at that index lands on the
    /// run — for every index. Stated through <c>GameRules.Apply</c> rather than against the engine,
    /// because the engine is the half both sides share: a reordered view would agree with the engine
    /// perfectly and still take the wrong card.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void The_option_the_view_shows_at_an_index_is_the_perk_PICK_PERK_lands(int index)
    {
        var state = DraftWorlds.DraftPendingOn();
        var view = Projected(state);
        var chosen = view.Options[index];

        var picked = SlayIdleRepeat.Core.GameRules.Apply(
            state, new PickPerkCommand(index), DraftWorlds.Context);

        picked.Accepted.ShouldBeTrue("PICK_PERK was refused " + picked.Rejection + " at index " + index);

        var owned = picked.NewState.Run!.ToSnapshot().OwnedPerkTiers!;

        owned.Count.ShouldBe(1, "one pick grants one perk");
        owned.TryGetValue(chosen.PerkId, out var landedTier).ShouldBeTrue(
            "the view drew '" + chosen.PerkId + "' at index " + index + " and PICK_PERK landed " +
            string.Join(", ", owned.Keys) + ". The options are regenerated rather than persisted, so " +
            "a view deriving them a second way — or ordering them a second way — shows the player a " +
            "card other than the one their finger is on.");
        landedTier.ShouldBe(
            chosen.NewTier,
            "the view drew '" + chosen.PerkId + "' at index " + index + " and PICK_PERK landed " +
            string.Join(", ", owned.Keys) + ". The options are regenerated rather than persisted, so " +
            "a view deriving them a second way — or ordering them a second way — shows the player a " +
            "card other than the one their finger is on.");

        foreach (var other in view.Options.Where(o => !string.Equals(o.PerkId, chosen.PerkId, StringComparison.Ordinal)))
        {
            owned.Keys.ShouldNotContain(other.PerkId);
        }
    }

    /// <summary>Every option's catalogue facts are the catalogue's, not the view's own invention.</summary>
    [Fact]
    public void Every_option_carries_the_catalogues_own_facts_about_its_perk()
    {
        var catalogue = PerkCatalogue.Read(Content);

        foreach (var option in Projected(DraftWorlds.DraftPendingOn()).Options)
        {
            var perk = catalogue.Find(option.PerkId);

            option.Name.ShouldBe(perk.Name);
            option.Category.ShouldBe(perk.Category);
            option.Rarity.ShouldBe(perk.Rarity, "the perk's own band, not the band the slot drew");
            option.IconId.ShouldBe(perk.IconId);
        }
    }

    /// <summary>
    /// …and the effect text is rendered for the tier this option LANDS on, not for the perk's first —
    /// the fixture authors a different value per tier, so the three tiers render three sentences.
    /// </summary>
    [Theory]
    [InlineData(1, "+10% Test.")]
    [InlineData(2, "+18% Test.")]
    [InlineData(3, "+28% Test.")]
    public void An_options_effect_text_is_rendered_for_the_tier_it_lands_on(int ownedTier, string expected)
    {
        var state = ownedTier == 1
            ? DraftWorlds.DraftPendingOn()
            : DraftWorlds.DraftPendingOn(
                battleKind: TileKind.Boss,
                ownedPerkTiers: new Dictionary<string, int> { [PerkDocuments.Epic1] = ownedTier - 1 });

        var option = Projected(state).Options.FirstOrDefault(o => o.NewTier == ownedTier);

        option.ShouldNotBeNull("no option lands on tier " + ownedTier + ", so this case asserts nothing");
        option.EffectText.Text.ShouldBe(
            expected, "the card draws the numbers of the tier the player is about to own");
    }

    /// <summary>
    /// An upgrade names the tier it raises to, and names itself an upgrade. Both facts, and no
    /// rendered badge: the upgrade wording is translated, so the numeral and the flag are what this
    /// projection owes and the sentence is the screen's to compose.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void An_upgrade_option_reports_the_tier_it_raises_to(int ownedTier)
    {
        var state = DraftWorlds.DraftPendingOn(
            battleKind: TileKind.Boss,
            ownedPerkTiers: new Dictionary<string, int> { [PerkDocuments.Epic1] = ownedTier });

        var upgrade = Projected(state).Options.FirstOrDefault(o => o.IsUpgrade);

        upgrade.ShouldNotBeNull("the boss fixture owns the one Epic row, so its upgrade is offered");
        upgrade.NewTier.ShouldBe(ownedTier + 1);
    }

    /// <summary>…and a fresh grant lands on tier 1 and is never flagged as an upgrade.</summary>
    [Fact]
    public void A_fresh_grant_lands_on_the_first_tier()
    {
        foreach (var option in Projected(DraftWorlds.DraftPendingOn()).Options)
        {
            option.IsUpgrade.ShouldBeFalse("this run owns nothing, so no option can be an upgrade");
            option.NewTier.ShouldBe(1);
        }
    }

    /// <summary>The offer reports the authored draft economy, read rather than transcribed.</summary>
    [Fact]
    public void The_offer_reports_the_authored_reroll_cost_and_skip_reward()
    {
        var authored = DraftEconomyTuning.Read(Content);
        var view = Projected(DraftWorlds.DraftPendingOn());

        view.RerollGoldCost.ShouldBe(authored.RerollGoldCost);
        view.SkipGoldReward.ShouldBe(authored.SkipGoldReward);
    }

    // ------------------------------------------------------------------------------------------
    // The synergy hint.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// An option names the owned perks it shares a status with. All three arms in one case: a hint
    /// that named every owned perk, and one that named the option itself, both satisfy "the sharer
    /// is listed", so the two negative arms are what make the positive one mean anything.
    /// </summary>
    [Fact]
    public void An_option_names_the_owned_perks_it_shares_a_status_with()
    {
        var view = DraftView.Project(SynergyRun(), SynergyContent)!;

        view.Options.Select(o => o.PerkId).OrderBy(id => id, StringComparer.Ordinal).ShouldBe(
            new[] { StatusReader, StatusWriter, Unrelated }.OrderBy(id => id, StringComparer.Ordinal),
            "the fixture authors three perks and a draft offers three distinct options, so all " +
            "three are on offer and every arm below is reachable");

        Option(view, StatusReader).SynergyPerkIds.ShouldBe(
            [StatusWriter],
            "this option's tier names the same status the owned perk's tier names");

        Option(view, Unrelated).SynergyPerkIds.ShouldBeEmpty(
            "this option names no status at all, so it interacts with nothing the run owns");

        Option(view, StatusWriter).SynergyPerkIds.ShouldBeEmpty(
            "the owned perk is the option, and a card never names itself as its own synergy");
    }

    // ------------------------------------------------------------------------------------------
    // 🔒 Projecting moves nothing.
    // ------------------------------------------------------------------------------------------

    /// <summary>Two projections of one run answer the same three options.</summary>
    [Fact]
    public void Projecting_twice_answers_the_same_options()
    {
        var run = DraftWorlds.DraftPendingOn().Run!.ToSnapshot();

        Canonical(DraftView.Project(run, Content)!).ShouldBe(Canonical(DraftView.Project(run, Content)!));
    }

    /// <summary>…and the negative control: two different seeds do not.</summary>
    /// <remarks>
    /// Without this, a projection that answered a constant would pass the case above and every
    /// determinism claim in this file.
    /// </remarks>
    [Fact]
    public void Two_different_run_seeds_project_to_different_bytes()
    {
        var first = DraftView.Project(DraftPendingWithSeed(0x1111_1111_1111_1111UL), Content)!;
        var second = DraftView.Project(DraftPendingWithSeed(0x2222_2222_2222_2222UL), Content)!;

        Canonical(first).ShouldNotBe(Canonical(second));
    }

    /// <summary>🔒 Projecting mutates nothing on the run — not a counter, not a stream position.</summary>
    [Fact]
    public void Projecting_moves_nothing_on_the_run()
    {
        var state = DraftWorlds.DraftPendingOn(gold: 500);
        var before = CanonicalStateWriter.CanonicalBytes(state.Run!.ToSnapshot());

        DraftView.Project(state.Run.ToSnapshot(), Content);

        CanonicalStateWriter.CanonicalBytes(state.Run.ToSnapshot()).ShouldBe(
            before, "a read-only projection moved something on the run it was drawing.");
    }

    /// <summary>
    /// 🔒 …and the stronger half: a draft that was LOOKED at resolves byte-for-byte like one that was
    /// not. The snapshot bytes above cannot see a projection that consumed draw indices; applying the
    /// command on both paths can.
    /// </summary>
    [Fact]
    public void A_draft_that_was_projected_resolves_exactly_like_one_that_was_not()
    {
        var looked = DraftWorlds.DraftPendingOn(gold: 500);
        DraftView.Project(looked.Run!.ToSnapshot(), Content);

        var blind = DraftWorlds.DraftPendingOn(gold: 500);

        var afterLooking = SlayIdleRepeat.Core.GameRules.Apply(
            looked, new PickPerkCommand(0), DraftWorlds.Context);
        var afterBlind = SlayIdleRepeat.Core.GameRules.Apply(
            blind, new PickPerkCommand(0), DraftWorlds.Context);

        CanonicalStateWriter.CanonicalBytes(afterLooking.NewState.Run!.ToSnapshot()).ShouldBe(
            CanonicalStateWriter.CanonicalBytes(afterBlind.NewState.Run!.ToSnapshot()),
            "looking at a draft changed what taking it did.");
    }

    // ------------------------------------------------------------------------------------------
    // The doors.
    // ------------------------------------------------------------------------------------------

    /// <summary>Neither argument may be null.</summary>
    [Fact]
    public void Project_refuses_a_null_argument()
    {
        Should.Throw<ArgumentNullException>(() => DraftView.Project(null!, Content));
        Should.Throw<ArgumentNullException>(
            () => DraftView.Project(RunSnapshots.With(draftPending: true), null!));
    }

    // ------------------------------------------------------------------------------------------
    // 🔒 24 §1.1 — the three DRAFT counters, shown always and with the right number on each.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Every draft reports all three counters, whatever they stand at. <c>24</c> §1.1's Visibility
    /// rule is <em>always</em>, and a counter standing at zero is the case a "show it when it matters"
    /// implementation would drop.
    /// </summary>
    [Fact]
    public void Every_draft_projects_all_three_counter_lines()
    {
        var view = Projected(DraftWorlds.DraftPendingOn());

        view.Guarantees.Select(g => g.Kind).ShouldBe(
            [
                DraftGuaranteeKind.LegendaryPity,
                DraftGuaranteeKind.QualityFloor,
                DraftGuaranteeKind.UpgradeFamine,
            ],
            "24 §1.1 says every counter is shown always, and DraftGuarantees.Forced assigns its " +
            "slots in this order");
    }

    /// <summary>
    /// 🔒 <b>The three rungs are read three different ways, and this is the case that says so.</b>
    /// <c>luck.json</c> authors 15, 3 and 5. The Legendary pity's number is the forced draft's own
    /// ordinal, so its rung is 15; the quality floor's and the famine's count the drafts that pass
    /// BEFORE the next one is floored, so theirs are 4 and 6. Copying either reading onto all three
    /// fails here — the expected triple is <c>15/4/6</c>, which no single reading produces.
    /// </summary>
    [Fact]
    public void The_three_rungs_take_the_three_readings_DraftGuarantees_takes()
    {
        var view = Projected(DraftWorlds.DraftPendingOn());

        view.Guarantees.Select(g => g.ForcedOnDraft).ShouldBe(
            [15, 4, 6],
            "authored 15/3/5: the pity's N is the forced draft's ordinal, the floor's and the " +
            "famine's are the drafts that pass first, so their rungs are N + 1");
    }

    /// <summary>…and a fresh run's countdown is the rung itself, not the rung less one.</summary>
    [Fact]
    public void A_fresh_run_counts_down_from_the_rung_itself()
    {
        var view = Projected(DraftWorlds.DraftPendingOn());

        view.Guarantees.Select(g => g.DraftsUntilForced).ShouldBe(
            [15, 4, 6],
            "a run that has drafted nothing has the whole ladder ahead of it");
    }

    /// <summary>…and the line reports where the counter actually stands, not only what is left.</summary>
    [Fact]
    public void Each_line_reports_where_its_own_counter_stands()
    {
        var view = ProjectedWith(legendary: 7, aboveCommon: 2, upgrade: 4);

        view.Guarantees.Select(g => g.DraftsStood).ShouldBe([7, 2, 4]);
    }

    /// <summary>…and each countdown is measured against its own counter, never a shared one.</summary>
    [Fact]
    public void Each_countdown_is_measured_against_its_own_counter()
    {
        var view = ProjectedWith(legendary: 7, aboveCommon: 2, upgrade: 4);

        view.Guarantees.Select(g => g.DraftsUntilForced).ShouldBe(
            [8, 2, 2],
            "15-7, 4-2 and 6-4 — three different subtractions, so a line reading another line's " +
            "counter cannot pass");
    }

    /// <summary>
    /// 🔒 <b>The countdown and the rule agree about which draft is the forced one.</b> This is the
    /// load-bearing case (steering S2): the view is not compared against the document but against
    /// <c>DraftGuarantees.Forced</c> itself. At the counter the view says is one draft short, the rule
    /// fires; one draft earlier, it does not. A view off by one in either direction fails one arm.
    /// </summary>
    [Fact]
    public void A_countdown_of_one_is_the_draft_the_rule_actually_forces()
    {
        var rule = LuckTuning.Read(Content).Draft;

        // Owns a perk below its top tier, so all three guarantees — the famine included — are live.
        var demand = new DraftDemand(
            Stage: 1, IsBoss: false, OwnsSustainPerk: false, OwnsNonMaxedPerk: true);

        foreach (var kind in Enum.GetValues<DraftGuaranteeKind>())
        {
            var due = Projected(DraftWorlds.DraftPendingOn()).Guarantees.Single(g => g.Kind == kind);

            Fires(rule, Standing(kind, due.ForcedOnDraft - 1), demand, kind).ShouldBeTrue(
                $"{kind}: the view counts down to draft {due.ForcedOnDraft}, so a counter standing " +
                $"at {due.ForcedOnDraft - 1} must be the one the rule forces");

            Fires(rule, Standing(kind, due.ForcedOnDraft - 2), demand, kind).ShouldBeFalse(
                $"{kind}: and one draft earlier it must not — otherwise the countdown is a draft late");
        }
    }

    /// <summary>…and the countdown a run one short of a rung reports is exactly one.</summary>
    [Fact]
    public void A_counter_one_short_of_its_rung_says_the_next_draft_is_the_forced_one()
    {
        var view = ProjectedWith(legendary: 14, aboveCommon: 3, upgrade: 5);

        view.Guarantees.Select(g => g.DraftsUntilForced).ShouldBe([1, 1, 1]);
    }

    /// <summary>
    /// …and it never reads zero, however high a counter stands. A rung retuned downwards leaves
    /// counters above it, and <em>"in 0 drafts"</em> describes a draft that has already happened.
    /// </summary>
    [Fact]
    public void The_countdown_never_reads_zero_however_high_the_counter_stands()
    {
        var view = ProjectedWith(legendary: 400, aboveCommon: 400, upgrade: 400);

        view.Guarantees.Select(g => g.DraftsUntilForced).ShouldBe([1, 1, 1]);
        view.Guarantees.Select(g => g.DraftsStood).ShouldBe(
            [400, 400, 400], "the standing is reported as it is, never clamped to the rung");
    }

    /// <summary>
    /// 🔒 The upgrade famine is not live while the run owns no perk below its top tier — the same
    /// fact <c>DraftGuarantees.Forced</c> gates it on. A countdown drawn as live here would promise a
    /// forced upgrade that cannot arrive.
    /// </summary>
    [Fact]
    public void The_upgrade_famine_is_not_live_while_the_run_owns_no_upgradable_perk()
    {
        var view = Projected(DraftWorlds.DraftPendingOn());

        view.Guarantees.Single(g => g.Kind == DraftGuaranteeKind.UpgradeFamine).Live.ShouldBeFalse(
            "a run owning nothing has no upgrade to be starved of");
    }

    /// <summary>…and it becomes live the moment the run owns one.</summary>
    [Fact]
    public void The_upgrade_famine_is_live_once_the_run_owns_a_perk_below_its_top_tier()
    {
        var view = Projected(DraftWorlds.DraftPendingOn(
            ownedPerkTiers: new Dictionary<string, int> { [OwnedNonMaxedPerk] = 1 }));

        view.Guarantees.Single(g => g.Kind == DraftGuaranteeKind.UpgradeFamine).Live.ShouldBeTrue();
    }

    /// <summary>
    /// …and the other two are live whatever the run owns, because every draft can offer a Legendary
    /// and every draft can offer something above Common.
    /// </summary>
    [Fact]
    public void The_pity_and_the_quality_floor_are_live_whatever_the_run_owns()
    {
        foreach (var owned in new IReadOnlyDictionary<string, int>?[]
        {
            null,
            new Dictionary<string, int> { [OwnedNonMaxedPerk] = 1 },
        })
        {
            var view = Projected(DraftWorlds.DraftPendingOn(ownedPerkTiers: owned));

            view.Guarantees
                .Where(g => g.Kind != DraftGuaranteeKind.UpgradeFamine)
                .ShouldAllBe(g => g.Live);
        }
    }

    // ------------------------------------------------------------------------------------------
    // Fixtures.
    // ------------------------------------------------------------------------------------------

    /// <summary>The perk that grants the shared status. Owned by the synergy run.</summary>
    private const string StatusWriter = "PK_TEST_STATUS_WRITER";

    /// <summary>The perk that reads the same status. The option whose hint must name the owned one.</summary>
    private const string StatusReader = "PK_TEST_STATUS_READER";

    /// <summary>The perk that names no status at all. The option whose hint must stay empty.</summary>
    private const string Unrelated = "PK_TEST_UNRELATED";

    /// <summary>The status both sharers name. An id, so nothing here keys on a perk.</summary>
    private const string SharedStatus = "RAGE";

    /// <summary>An owned perk below its top tier, which is what makes the upgrade famine live.</summary>
    /// <remarks>
    /// Read off the hermetic catalogue rather than named as a literal: the fixture's rows carry three
    /// tiers each, so tier 1 is below the top and the famine's precondition holds.
    /// </remarks>
    private static string OwnedNonMaxedPerk => PerkCatalogue.Read(Content).All.First(e => e.TierCount > 1).Id;

    /// <summary>A projected draft whose three counters stand where a case needs them.</summary>
    private static DraftView ProjectedWith(int legendary, int aboveCommon, int upgrade) =>
        DraftView.Project(
            RunSnapshots.With(
                draftPending: true,
                draftBattleKind: (int)TileKind.Enemy,
                draftBattleStage: 1,
                draftsSinceLegendaryOffered: legendary,
                draftsWithoutAboveCommon: aboveCommon,
                draftsWithoutOwnedUpgrade: upgrade),
            Content)
        ?? throw new InvalidOperationException("the fixture run has no draft pending");

    /// <summary>One counter standing at a value, with the other two at zero.</summary>
    private static DraftCounters Standing(DraftGuaranteeKind kind, int stood) => kind switch
    {
        DraftGuaranteeKind.LegendaryPity => new DraftCounters(stood, 0, 0),
        DraftGuaranteeKind.QualityFloor => new DraftCounters(0, stood, 0),
        DraftGuaranteeKind.UpgradeFamine => new DraftCounters(0, 0, stood),
        _ => throw new InvalidOperationException("unreachable: " + kind),
    };

    /// <summary>Whether the rule itself forces the named guarantee at these counters.</summary>
    private static bool Fires(
        DraftRule rule, DraftCounters counters, DraftDemand demand, DraftGuaranteeKind kind) =>
        DraftGuarantees.Forced(rule, counters, demand, optionCount: 3)
            .Any(f => f.Guarantee == Expected(kind));

    /// <summary>The rules-layer guarantee one projected kind reports on.</summary>
    private static DraftGuarantee Expected(DraftGuaranteeKind kind) => kind switch
    {
        DraftGuaranteeKind.LegendaryPity => DraftGuarantee.LegendaryPity,
        DraftGuaranteeKind.QualityFloor => DraftGuarantee.QualityFloor,
        DraftGuaranteeKind.UpgradeFamine => DraftGuarantee.UpgradeFamine,
        _ => throw new InvalidOperationException("unreachable: " + kind),
    };

    private static DraftView Projected(WorldSlice state) =>
        DraftView.Project(state.Run!.ToSnapshot(), Content)
        ?? throw new InvalidOperationException("the fixture run has no draft pending");

    private static DraftOptionView Option(DraftView view, string perkId) =>
        view.Options.Single(o => string.Equals(o.PerkId, perkId, StringComparison.Ordinal));

    /// <summary>A run with a draft open on a given seed, for the two determinism cases.</summary>
    private static RunSnapshot DraftPendingWithSeed(ulong runSeed) => RunSnapshots.With(
        runSeed: runSeed,
        draftPending: true,
        draftBattleKind: (int)TileKind.Enemy,
        draftBattleStage: 1);

    /// <summary>
    /// One projection rendered to bytes. Record equality would compare the option list by
    /// REFERENCE, so two unrelated views would differ on nothing that matters.
    /// </summary>
    private static string Canonical(DraftView view)
    {
        var text = new StringBuilder();

        text.Append(view.RerollGoldCost.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(view.SkipGoldReward.ToString(CultureInfo.InvariantCulture)).Append('\n');

        foreach (var option in view.Options)
        {
            text.Append(option.PerkId).Append('|')
                .Append(option.Name).Append('|')
                .Append(option.Category).Append('|')
                .Append(option.Rarity).Append('|')
                .Append(option.IconId).Append('|')
                .Append(option.IsUpgrade).Append('|')
                .Append(option.NewTier.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(option.EffectText.Text ?? "<unrendered>").Append('|')
                .Append(string.Join(",", option.EffectText.Text is null ? option.EffectText.UnresolvedTokens : []))
                .Append('|')
                .Append(string.Join(",", option.SynergyPerkIds))
                .Append('\n');
        }

        return text.ToString();
    }

    /// <summary>A run owning <see cref="StatusWriter"/> at Tier I with a draft open.</summary>
    private static RunSnapshot SynergyRun() => RunSnapshots.With(
        draftPending: true,
        draftBattleKind: (int)TileKind.Enemy,
        draftBattleStage: 1,
        ownedPerkTiers: new Dictionary<string, int> { [StatusWriter] = 1 });

    /// <summary>
    /// The content the synergy case projects against: everything a draft reads, over a three-perk
    /// catalogue in which two rows name one status and the third names none.
    /// </summary>
    private static ContentSnapshot SynergyContent { get; } = BuildSynergyContent();

    private static ContentSnapshot BuildSynergyContent()
    {
        var baseline = InRunIncomeDocuments.Shipped;

        var perks = new ContentDocument(PerkDocuments.DocumentPath, Obj(
            ("perks", ContentValue.Array(
            [
                SynergyPerk(StatusWriter, "OFFENSE", "APPLY_STATUS", SharedStatus),
                SynergyPerk(StatusReader, "DEFENSE", "EXTEND_STATUS", SharedStatus),
                SynergyPerk(Unrelated, "SUSTAIN", "STAT_ADD_PCT", statusId: null),
            ]))));

        return new ContentSnapshot(
            baseline.Version, baseline.DocumentPaths.Select(baseline.GetDocument).Append(perks));
    }

    private static ContentValue SynergyPerk(string id, string category, string op, string? statusId) => Obj(
        ("id", ContentValue.Text(id)),
        ("name", ContentValue.Text(id)),
        ("category", ContentValue.Text(category)),
        ("rarity", ContentValue.Text("COMMON")),
        ("iconId", ContentValue.Text("icon_perk_test")),
        ("description", ContentValue.Text("+{value}% Test.")),
        ("tiers", ContentValue.Array(
        [
            SynergyTier(1, id, 0.10m, op, statusId),
            SynergyTier(2, id, 0.18m, op, statusId),
            SynergyTier(3, id, 0.28m, op, statusId),
        ])),
        ("excludes", ContentValue.Array([])),
        ("requires", ContentValue.Array([])),
        ("poolTags", ContentValue.Array([ContentValue.Text("standard")])));

    private static ContentValue SynergyTier(
        int tier, string id, decimal value, string op, string? statusId)
    {
        var effect = new List<(string Name, ContentValue Value)>
        {
            ("id", ContentValue.Text(id + "_T" + tier.ToString(CultureInfo.InvariantCulture))),
            ("op", ContentValue.Text(op)),
            ("trigger", Obj(("kind", ContentValue.Text("ALWAYS")))),
            ("target", ContentValue.Text("SELF")),
            ("value", ContentValue.Number(value)),
        };

        if (statusId is not null)
        {
            effect.Add(("statusId", ContentValue.Text(statusId)));
        }
        else
        {
            effect.Add(("stat", ContentValue.Text("ATK")));
        }

        return Obj(
            ("tier", ContentValue.Number(tier)),
            ("effects", ContentValue.Array([Obj(effect.ToArray())])));
    }

    private static ContentValue Obj(params (string Name, ContentValue Value)[] members) =>
        ContentValue.Object(members.Select(m => new KeyValuePair<string, ContentValue>(m.Name, m.Value)));
}
