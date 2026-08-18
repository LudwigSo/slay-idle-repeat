using System.Globalization;
using System.Text;
using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rules.Board;
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
/// offer, and the only way anything outside <c>Core</c> can see a draft at all.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The load-bearing claim is not "three options exist" — it is that these are the SAME three
/// options <c>PICK_PERK</c> acts on</b> (steering S2). The options are never persisted: they
/// regenerate from the run's committed <c>draft</c> stream position on every command, and the view
/// regenerates them the same way. A view that drew a second, plausible set of three would satisfy
/// every structural case in this file, and a player pressing the middle card would take a perk the
/// card never showed. <see cref="The_option_the_view_shows_at_an_index_is_the_perk_PICK_PERK_lands"/>
/// is the case that says otherwise, and it is stated at more than one index so a view whose order
/// is reversed cannot pass it.
/// </para>
/// <para>
/// ⚠️ <b>Two projections are never compared by record equality</b> (steering S17): the option list
/// is an <c>IReadOnlyList</c> and a synthesized <c>Equals</c> would compare it by reference.
/// <see cref="Canonical"/> is the comparison, and
/// <see cref="Two_different_run_seeds_project_to_different_bytes"/> is the negative control proving
/// it can see a difference at all.
/// </para>
/// </remarks>
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
    /// run — for every index, not just the first.
    /// </summary>
    /// <remarks>
    /// Stated end-to-end through <c>GameRules.Apply</c> rather than against the engine, because the
    /// engine is the half both sides share: a view that re-derived the options correctly and handed
    /// them back in a different ORDER would agree with the engine perfectly and still take the wrong
    /// card. The other two perks are asserted absent for the same reason — "the run holds the perk
    /// the view showed" is satisfied by a view showing all three in any order.
    /// </remarks>
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
    /// …and the effect text is rendered for the tier this option LANDS on, not for the perk's first.
    /// </summary>
    /// <remarks>
    /// The hermetic catalogue authors a different value per tier (0.10 / 0.18 / 0.28 of the same
    /// template), so the tiers render three different sentences and a view rendering tier I for an
    /// upgrade to tier II is visible here. The boss fixture is the one that deterministically offers
    /// an upgrade, which is the only way an option's tier is ever above I.
    /// </remarks>
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

    /// <summary>
    /// 🔒 No member of an option carries a player-facing English word this projection composed
    /// itself. Every such word is a translated string, and one assembled here would reach a German
    /// player in English with no key to translate it by.
    /// </summary>
    /// <remarks>
    /// Stated over the record's own members by reflection rather than over a list of the ones that
    /// exist today, so a member added later is caught by the rule rather than by whoever remembers
    /// it. The two string members that legitimately carry authored text — the perk's name and its
    /// substituted sentence — come out of the content set and are excluded by name.
    /// </remarks>
    [Fact]
    public void No_option_member_carries_wording_this_projection_invented()
    {
        string[] authoredElsewhere = [nameof(DraftOptionView.Name), nameof(DraftOptionView.EffectText)];

        var invented = typeof(DraftOptionView)
            .GetProperties()
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.Name)
            .Where(name => !authoredElsewhere.Contains(name, StringComparer.Ordinal))
            .Where(name => !name.EndsWith("Id", StringComparison.Ordinal))
            .ToArray();

        invented.ShouldBeEmpty(
            "DraftOptionView." + string.Join(", ", invented) + " is a string this projection builds " +
            "that is neither an id nor authored content, so it is a sentence composed in code. The " +
            "screen composes wording from its own locale table; this type carries the facts.");
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
    /// An option names the owned perks it interacts with: those whose own owned tier names a status
    /// this option's tier also names.
    /// </summary>
    /// <remarks>
    /// 🔒 All three arms in one case, because the two negative ones are what make the positive one
    /// mean anything: a hint that named every owned perk, and a hint that named the option itself,
    /// both satisfy "the sharer is listed". The fixture catalogue holds exactly three perks so the
    /// draft is forced to offer all three, which is what makes every arm reachable in one projection
    /// rather than in a seed sweep.
    /// </remarks>
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

        DraftView.Project(state.Run!.ToSnapshot(), Content);

        CanonicalStateWriter.CanonicalBytes(state.Run!.ToSnapshot()).ShouldBe(
            before, "a read-only projection moved something on the run it was drawing.");
    }

    /// <summary>
    /// 🔒 …and the stronger half: a draft that was LOOKED at resolves byte-for-byte like one that
    /// was not.
    /// </summary>
    /// <remarks>
    /// The bytes above are read off the same immutable snapshot the projection was handed, so they
    /// cannot see a projection that consumed draw indices from a stream the next command would then
    /// continue from. Applying the command on both paths and comparing what the run became is what
    /// can.
    /// </remarks>
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
