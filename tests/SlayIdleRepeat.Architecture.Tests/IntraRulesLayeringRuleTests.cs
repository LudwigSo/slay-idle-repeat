using Mono.Cecil;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 <b>R17 — the layering <em>inside</em> <c>Core/Rules/</c>:
/// <c>Rules.Combat ▶ Rules.Stats ▶ Rules.Effects</c>.</b> <c>Rules.Effects</c> is the bottom and may
/// name neither of the other two.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Why this rule did not exist and had to.</b> `30` §11.4's layering table has a single
/// <c>Rules</c> row and governs nothing <em>inside</em> it, which
/// <c>AccessibilityBoundaryTests.Core_internal_layering_holds</c> faithfully reproduces:
/// <c>Rules.Effects</c> naming <c>Rules.Combat</c> is a <c>Rules</c> type naming a <c>Rules</c> type
/// and passes. M2-05's <c>IEffectActorView</c> flagged exactly this — <em>"which direction that
/// dependency should run … is a milestone-level decision … until it is taken deliberately, a cycle
/// between the two can form with every architecture rule green"</em>. R17 took the decision; this
/// enforces it.
/// </para>
/// <para>
/// <b>What it buys, concretely.</b> The trigger layer fires <em>into</em> the combat log, which lives
/// in <c>Rules.Combat</c>. The natural implementation is a call to
/// <c>CombatLog.AppendRunEffectQueued</c>, and it would compile, pass every existing rule, and put a
/// namespace cycle in the middle of the game's hottest path. What R17 forces instead is a seam the
/// upper layer implements — <c>IRunEffectSink</c> — and this rule is what makes the seam load-bearing
/// rather than a stylistic preference the next edit can quietly bypass.
/// </para>
/// <para>
/// 🔒 A <b>new file</b>, not an edit to M0-08's rule files or to <c>Infrastructure/Domain.cs</c>:
/// M1-12 is in flight on exactly those, and patching a component another agent is scheduled to touch
/// is how M0 broke thirty tests (steering S12). The namespace constants are therefore restated here
/// rather than added to <c>Domain</c>, and <see cref="The_namespaces_R17_governs_are_the_ones_under_Rules"/> pins that
/// restatement against the real tree so it cannot go stale.
/// </para>
/// </remarks>
public sealed class IntraRulesLayeringRuleTests
{
    /// <summary>The bottom of the intra-<c>Rules</c> layering — `18`'s effect DSL interpreter.</summary>
    internal const string EffectsNamespace = "SlayIdleRepeat.Core.Rules.Effects";

    /// <summary>The middle — `05` §1-2's stat block and `18` §8's aggregation.</summary>
    internal const string StatsNamespace = "SlayIdleRepeat.Core.Rules.Stats";

    /// <summary>The top — `05` §3's simulation and §7's combat log.</summary>
    internal const string CombatNamespace = "SlayIdleRepeat.Core.Rules.Combat";

    /// <summary>M1's energy accrual/spend math — outside the Combat/Stats/Effects ordering entirely.</summary>
    internal const string EconomyNamespace = "SlayIdleRepeat.Core.Rules.Economy";

    /// <summary>M3-04's die math — outside the Combat/Stats/Effects ordering entirely, like <see cref="EconomyNamespace"/>.</summary>
    internal const string DiceNamespace = "SlayIdleRepeat.Core.Rules.Dice";

    /// <summary>M3-01's board DAG + generator — outside the Combat/Stats/Effects ordering entirely.</summary>
    internal const string BoardNamespace = "SlayIdleRepeat.Core.Rules.Board";

    /// <summary>M3-06's draft rarity weights/composition seams — outside the ordering entirely, like <see cref="BoardNamespace"/>.</summary>
    internal const string PerksNamespace = "SlayIdleRepeat.Core.Rules.Perks";

    /// <summary>M4-13's event → lifetime-counter table — outside the ordering entirely, like <see cref="PerksNamespace"/>.</summary>
    internal const string FeatsNamespace = "SlayIdleRepeat.Core.Rules.Feats";

    /// <summary>M4-03's `08` gear generation and set-bonus rules — outside the ordering entirely, like <see cref="PerksNamespace"/>.</summary>
    internal const string GearNamespace = "SlayIdleRepeat.Core.Rules.Gear";

    /// <summary>M4-01's `24` §11 pity façade and its guarantee primitives — outside the ordering entirely, like <see cref="PerksNamespace"/>.</summary>
    /// <remarks>
    /// 🔒 The namespace <see cref="Every_namespace_under_Rules_has_a_declared_place_in_R17"/>'s own
    /// remarks named as the next one to arrive, and it has. It is also
    /// <c>LuckRoutingRuleTests</c>' subject namespace, which reads this constant rather than
    /// restating it — one statement of the name for the two rules that quantify over it.
    /// </remarks>
    internal const string LuckNamespace = "SlayIdleRepeat.Core.Rules.Luck";

    /// <remarks>
    /// 🔒 Stated as a <b>table</b>, in <c>AccessibilityBoundaryTests.Core_internal_layering_holds</c>'
    /// shape, rather than as one scan over <c>Rules.Effects</c>. R17 is an ordering of three
    /// namespaces and therefore has <b>two</b> edges: an earlier draft enforced only the bottom one,
    /// so <c>Rules.Stats</c> naming <c>Rules.Combat</c> — the other cycle-forming edge,
    /// <c>Combat → Stats → Combat</c> — passed with the rule's own headline claiming otherwise. It
    /// costs nothing to close today, because <c>Rules/Stats/</c> names nothing in <c>Rules.Combat</c>,
    /// and it will not be free later.
    /// </remarks>
    private static readonly (string Subject, string Forbidden, string Reason)[] ForbiddenEdges =
    {
        (EffectsNamespace, StatsNamespace,
            "R17 puts Rules.Stats ABOVE Rules.Effects: a stat aggregation reads effects, not the " +
            "other way round. 18 §8's resolution order is stated over effects the aggregator collects."),
        (EffectsNamespace, CombatNamespace,
            "R17 puts Rules.Combat at the TOP. A trigger that appended to the combat log directly " +
            "would put a namespace cycle in the game's hottest path — emit through a seam " +
            "Rules.Combat implements (IRunEffectSink)."),
        (StatsNamespace, CombatNamespace,
            "R17 puts Rules.Combat above Rules.Stats. `05` §3.1 has the simulator aggregate stats, " +
            "not the reverse — a stat block that named the simulator would close the cycle from the " +
            "middle rather than from the bottom."),

        // 🔒 M1's Economy namespace surfaced at the M1/M2 merge: R17 was authored over three
        // namespaces on M2's branch, before Rules/Economy/ existed on either side. As of this merge
        // it holds real types (EnergyAccrual/EnergyMath/EnergySpend) with ZERO current coupling in
        // either direction to Combat/Stats/Effects — verified by inspection, not assumed. Rather than
        // guess a relationship that does not exist yet, Economy is pinned OUTSIDE the Combat/Stats/
        // Effects ordering as a fourth, independent leaf: forbidden from reaching UP into any of the
        // three (pure run-economy arithmetic has no business reading the effect DSL or the combat
        // simulator), while the three remain free to consume Economy's math later if a milestone
        // finds a real reason (e.g. an effect scaling by energy) — that direction is a decision for
        // whichever milestone needs it, exactly as R17's own remarks describe for Rules.Luck.
        (EconomyNamespace, EffectsNamespace,
            "Economy is pinned OUTSIDE the Combat/Stats/Effects ordering (see the ForbiddenEdges " +
            "remarks) — energy accrual/spend math has no current reason to read the effect DSL, and " +
            "forbidding this direction closes the same cycle risk R17 closes for Effects itself."),
        (EconomyNamespace, StatsNamespace,
            "Economy is pinned OUTSIDE the Combat/Stats/Effects ordering (see the ForbiddenEdges " +
            "remarks) — energy accrual/spend math has no current reason to read stat aggregation."),
        (EconomyNamespace, CombatNamespace,
            "Economy is pinned OUTSIDE the Combat/Stats/Effects ordering (see the ForbiddenEdges " +
            "remarks) — energy accrual/spend math has no current reason to read the combat simulator."),

        // 🔒 M3-04's Dice namespace, pinned OUTSIDE the ordering the same way Economy is, and for
        // the same reason: FairDiceBag/FaceEffectResolver/DieComposer/RerollEconomy are pure die
        // arithmetic with ZERO current coupling to Combat/Stats/Effects in either direction. The
        // MODIFY_DIE_FACE resolver a future ResolveTileCommand handler calls
        // (DiceForgeUpgradeResolver) reads Content.Dice only, not the effect DSL's resolver layer —
        // 18's interpreter calls INTO the dice system when M3-03 wires TILE_DICE_FORGE, which is the
        // direction these edges leave open by forbidding only the reverse.
        (DiceNamespace, EffectsNamespace,
            "Dice is pinned OUTSIDE the Combat/Stats/Effects ordering (see the ForbiddenEdges " +
            "remarks) — die-face arithmetic has no current reason to read the effect DSL's resolver."),
        (DiceNamespace, StatsNamespace,
            "Dice is pinned OUTSIDE the Combat/Stats/Effects ordering (see the ForbiddenEdges " +
            "remarks) — die-face arithmetic has no current reason to read stat aggregation."),
        (DiceNamespace, CombatNamespace,
            "Dice is pinned OUTSIDE the Combat/Stats/Effects ordering (see the ForbiddenEdges " +
            "remarks) — die-face arithmetic has no current reason to read the combat simulator."),

        // 🔒 M3-01 added Rules/Board/ (the board DAG + GenerateBoard). Verified by inspection, same
        // as Economy above: BoardGenerator's only Core dependency outside its own namespace is
        // Rules.Board itself plus Rng (DeterministicRng, RngStreams) — zero current coupling to
        // Combat/Stats/Effects in either direction. Board is a graph-shape/content-placement
        // concern; it has no business reading combat stats or the effect DSL, and EnemyPower(i)
        // reads Board's linear index as a plain int rather than Board naming Rules.Combat, so the
        // dependency (when M3-03's tile resolvers need it) runs the other way. Pinned as a fourth
        // independent leaf rather than guessed into the Combat/Stats/Effects ordering, for the same
        // reason Economy was.
        (BoardNamespace, EffectsNamespace,
            "Board is pinned OUTSIDE the Combat/Stats/Effects ordering (see the ForbiddenEdges " +
            "remarks) — board generation has no current reason to read the effect DSL."),
        (BoardNamespace, StatsNamespace,
            "Board is pinned OUTSIDE the Combat/Stats/Effects ordering (see the ForbiddenEdges " +
            "remarks) — board generation has no current reason to read stat aggregation."),
        (BoardNamespace, CombatNamespace,
            "Board is pinned OUTSIDE the Combat/Stats/Effects ordering (see the ForbiddenEdges " +
            "remarks) — board generation has no current reason to read the combat simulator."),

        // 🔒 M3-06's Rules/Perks/ (DraftRarityWeights, DraftCompositionRules, PerkDraftEngine),
        // pinned OUTSIDE the ordering the same way Board is, for the same reason: the draft engine's
        // only Core dependencies outside its own namespace are Content.Perks, Model and Rng — zero
        // current coupling to Combat/Stats/Effects in either direction. Drafting a perk chooses an
        // id and a tier; it never evaluates what a tier's effects do (see PerkCatalogueEntry's own
        // remarks for why the DSL is deliberately not read here).
        (PerksNamespace, EffectsNamespace,
            "Perks is pinned OUTSIDE the Combat/Stats/Effects ordering (see the ForbiddenEdges " +
            "remarks) — the draft engine has no current reason to read the effect DSL's resolver."),
        (PerksNamespace, StatsNamespace,
            "Perks is pinned OUTSIDE the Combat/Stats/Effects ordering (see the ForbiddenEdges " +
            "remarks) — the draft engine has no current reason to read stat aggregation."),
        (PerksNamespace, CombatNamespace,
            "Perks is pinned OUTSIDE the Combat/Stats/Effects ordering (see the ForbiddenEdges " +
            "remarks) — the draft engine has no current reason to read the combat simulator."),

        // 🔒 M4-13's Rules/Feats/ (FeatCounterProjection, FeatCounterIncrement), pinned OUTSIDE the
        // ordering the same way Perks is, for the same reason: the projection's only Core
        // dependencies outside its own namespace are Events, Content.Dice and Primitives — zero
        // current coupling to Combat/Stats/Effects in either direction. It is a pure fold over the
        // event list GameRules.Apply has already produced; it never asks how a fight went, only what
        // the fight said happened, and a counter that reached into the simulator to ask again would
        // be counting a second, differently-derived answer.
        //
        // ⚠️ The direction these edges leave OPEN is the one that will eventually be needed: when a
        // combat event lands (28 D2.1's 'Slaughter' category is 20 feats over enemies defeated,
        // crits and overkill), it will be an Events type that Rules.Combat produces and this
        // projection consumes — Events, not Rules.Combat, so no edge here has to move.
        (FeatsNamespace, EffectsNamespace,
            "Feats is pinned OUTSIDE the Combat/Stats/Effects ordering (see the ForbiddenEdges " +
            "remarks) — a counter projection reads the event list, not the effect DSL's resolver."),
        (FeatsNamespace, StatsNamespace,
            "Feats is pinned OUTSIDE the Combat/Stats/Effects ordering (see the ForbiddenEdges " +
            "remarks) — a counter projection has no reason to read stat aggregation."),
        (FeatsNamespace, CombatNamespace,
            "Feats is pinned OUTSIDE the Combat/Stats/Effects ordering (see the ForbiddenEdges " +
            "remarks) — a counter projection reads what the simulation REPORTED, through the event " +
            "list, rather than re-deriving it from the simulator."),

        // 🔒 M4-01's Rules/Luck/ (LuckService, HardPity, SoftPity, MercyAccrual, RarityTable and the
        // resolution records), pinned OUTSIDE the ordering the same way Perks is, for the same
        // reason — and this is the namespace Every_namespace_under_Rules_has_a_declared_place_in_R17
        // was written against by name, so the edges and the first Rules/Luck/ type land together.
        // Verified by inspection, not assumed: the whole namespace's Core dependencies outside
        // itself are Content (LuckTuning and, since M4-01b, Content.Perks' PerkRarity/PerkCategory —
        // the vocabulary the DRAFT class's guarantees floor a pool by), Model (PityCounters),
        // Primitives (SourceClass, Rarity) and Rng (DeterministicRng) — ZERO coupling to
        // Combat/Stats/Effects in either direction, and nothing under those three names a Luck type
        // either. Pity is a draw-shaping concern; it decides which rarity a grant lands on and never
        // evaluates what the grant then does, so it has no business reading the effect DSL, stat
        // aggregation or the tick loop.
        //
        // ⚠️ Content.Perks is CONTENT, not Rules.Perks, and the difference is the whole reason
        // M4-01b's draft guarantees could live here: Content sits beneath Rules, so naming a perk
        // band is a downward read, while Rules.Luck naming Rules.Perks would be a sideways edge with
        // no declaration. The permitted direction is Perks -> Luck, and that is the one the draft
        // engine uses.
        //
        // ⚠️ The reverse direction is deliberately left open, exactly as it is for Board and Perks:
        // `05` §6.2's no-repeat Elite draw is `24` §4.10 B2's rule and lives in
        // Rules/Combat/Enemies/, so Rules.Combat calling INTO the luck primitives one day is the
        // direction these edges permit by forbidding only the other. SubjectSetFloorTests' own
        // IEliteModifierHistory note says the same thing from the other side: the run-scoped history
        // must NOT be implemented on LuckService, "or Rules.Luck ends up naming Rules.Combat and R17
        // has no edge for it". These three edges are that edge.
        (LuckNamespace, EffectsNamespace,
            "Luck is pinned OUTSIDE the Combat/Stats/Effects ordering (see the ForbiddenEdges " +
            "remarks) — a pity guarantee has no current reason to read the effect DSL's resolver."),
        (LuckNamespace, StatsNamespace,
            "Luck is pinned OUTSIDE the Combat/Stats/Effects ordering (see the ForbiddenEdges " +
            "remarks) — a pity guarantee has no current reason to read stat aggregation."),
        (LuckNamespace, CombatNamespace,
            "Luck is pinned OUTSIDE the Combat/Stats/Effects ordering (see the ForbiddenEdges " +
            "remarks) — a pity guarantee has no current reason to read the combat simulator, and " +
            "05 §6.2's run-scoped Elite no-repeat memory belongs on the run controller rather than " +
            "on LuckService for exactly this reason."),

        // 🔒 M4-03's Rules/Gear/ (ItemPower, GearMinting, GearAffixRoller, GearGeneration,
        // GearStatDerivation, SetBonusResolver), pinned OUTSIDE the ordering the same way Luck is,
        // for the same reason — verified by inspection, not assumed: the namespace's Core
        // dependencies outside itself are Content (the gear catalogue, the drop tables, the par
        // table), Model (the gear instance), Primitives, Rng and Rules.Luck (the façade every drop
        // routes through). ZERO coupling to Combat/Stats/Effects in either direction, and nothing
        // under those three names a Gear type either.
        //
        // ⚠️ THE REVERSE DIRECTION IS THE ONE THAT WILL BE WANTED, and it is deliberately left open,
        // exactly as it is for Board, Perks and Luck. `18` §8 step 1 collects effects from gear,
        // affixes and set bonuses, and that collector lives in Rules.Effects — so Rules.Effects (or
        // Rules.Stats) reading a derived gear stat one day is the direction these edges permit by
        // forbidding only the other. What must not happen is the inverse: a gear derivation that
        // reached into the aggregator or the tick loop would put the item's own stats downstream of
        // the fight they are an input to.
        (GearNamespace, EffectsNamespace,
            "Gear is pinned OUTSIDE the Combat/Stats/Effects ordering (see the ForbiddenEdges " +
            "remarks) — deriving an item's stats from what it rolled has no reason to read the " +
            "effect DSL's resolver; the collection runs the other way."),
        (GearNamespace, StatsNamespace,
            "Gear is pinned OUTSIDE the Combat/Stats/Effects ordering (see the ForbiddenEdges " +
            "remarks) — an item contributes TO stat aggregation and must not read it back, or the " +
            "item's own numbers become a function of the aggregate they are an input to."),
        (GearNamespace, CombatNamespace,
            "Gear is pinned OUTSIDE the Combat/Stats/Effects ordering (see the ForbiddenEdges " +
            "remarks) — gear generation and set-bonus counting have no reason to read the combat " +
            "simulator."),
    };

    /// <summary>
    /// 🔒 R17, over `30` §11.4's <c>Rules</c> row — nothing under <c>Rules/Effects/</c> names a type
    /// under <c>Rules/Stats/</c> or <c>Rules/Combat/</c>. `18` §2.5's run-op emission goes through a
    /// seam the upper layer implements.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An IL scan rather than a text grep, for <c>Il</c>'s stated reason: a grep is defeated by a
    /// <c>using</c> alias, a fully-qualified call or an extension method, and it fires inside comments
    /// — and the file that most wants to name <c>CombatLog</c> is the one whose remarks explain at
    /// length why it does not.
    /// </para>
    /// <para>
    /// ⚠️ <b>ONE THING AN IL SCAN CANNOT SEE: a <c>const</c>.</b> Found by making this rule fail on
    /// purpose (steering S1). The first probe reached up with
    /// <c>_ = CombatLog.TicksPerSecond</c> and the rule stayed <b>green</b> — C# folds a
    /// <c>const</c> into the call site at compile time, so no reference to the declaring type reaches
    /// the metadata. Re-probed with a method call, the rule reported both
    /// <c>CombatLog</c> and <c>CombatEvent</c>.
    /// </para>
    /// <para>
    /// This is a real hole and it is recorded rather than papered over — but it is narrow, and the one
    /// place it bites is already closed by other means. <c>Rules.Combat</c>'s consts are the tick
    /// rate, the fight cap, the telegraph band and <c>CombatActor</c>'s id layout; the only two
    /// <c>Rules.Effects</c> has any use for are the first two, which is exactly why
    /// <c>TriggerSchedule</c> declares its own and <c>PeriodicAnchoringTests.The_tick_rate_agrees_
    /// with_the_combat_log</c> compares them. A const borrowed across the boundary would be a
    /// compile-time copy of a number rather than a runtime dependency — the cycle this rule exists to
    /// prevent cannot be built out of them.
    /// </para>
    /// </remarks>
    [Fact]
    public void Rules_Effects_is_the_bottom_of_the_intra_Rules_layering()
    {
        var offenders = new List<string>();

        foreach (var (subject, forbidden, reason) in ForbiddenEdges)
        {
            foreach (var type in Il.TypesUnder(ProductionAssemblies.CoreModule, subject))
            {
                offenders.AddRange(
                    Il.ReferencedTypeNames(type)
                      .Where(referenced => Il.IsUnder(NamespaceOf(referenced), forbidden))
                      .Select(referenced => $"{type.FullName} names {referenced} — {reason}"));
            }
        }

        ArchRule.Empty(
            offenders,
            "R17: the layering inside Core/Rules/ is Rules.Combat -> Rules.Stats -> Rules.Effects, " +
            "and every edge of it runs one way.");
    }

    /// <summary>
    /// 🔒 <b>The floors</b> under R17's subject and target sets — `23` §6, over the namespaces
    /// `30` §11.4 puts beneath <c>Rules</c>. Steering S3: the rule above is "no member of set S names
    /// set F", which passes vacuously when either set empties. Both can — a rename of
    /// <c>Rules/Effects/</c> empties the subject, and a rename of <c>Rules/Combat/</c> empties the
    /// target while the rule stays green over a cycle that now runs to a differently-named namespace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The counts are floors rather than equalities, so adding a type is not a test edit. They are
    /// set below the counts on the commit the rule landed, which
    /// <see cref="The_floors_are_below_the_counts_the_rule_was_written_against"/> pins so the numbers
    /// in this comment cannot go stale (steering S9).
    /// </para>
    /// <para>
    /// 🔒 <b><c>Rules.Effects</c> is floored twice, and it has to be.</b> <c>Il.TypesUnder</c> matches
    /// by namespace <b>prefix</b>, so a single floor over <c>Rules.Effects</c> is satisfied by
    /// <c>Rules/Effects/Triggers/</c> alone: move the whole `18` §4/§5 interpreter out and the rule
    /// still reports a healthy subject set while quantifying over none of it. The second floor is on
    /// the root namespace's <b>own</b> types — the shared roster predicate, the evaluation context,
    /// the two seams — which is what a move would actually empty.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_namespaces_R17_governs_are_the_ones_under_Rules()
    {
        var offenders = new List<string>();

        Floor(offenders, EffectsNamespace, EffectsFloor,
            "the subject set of Rules_Effects_is_the_bottom_of_the_intra_Rules_layering. Empty, it " +
            "reports success over nothing and 18's interpreter is free to reach up into the " +
            "simulator again.");

        Floor(offenders, TriggersNamespace, TriggersFloor,
            "`18` §3's trigger model — the part of the subject set that most wants to name the " +
            "combat log, and the part a prefix floor over Rules.Effects would hide the loss of.");

        Floor(offenders, StatsNamespace, StatsFloor,
            "both the subject of the Stats -> Combat edge and half the forbidden set of the " +
            "Effects -> Stats edge. Empty, two of the three edges govern nothing.");

        Floor(offenders, CombatNamespace, CombatFloor,
            "the forbidden set of two edges — and the one that matters, because the combat log is " +
            "what a trigger most wants to reach.");

        // 🔒 The root namespace's OWN types, not the prefix. See the remarks.
        RootFloor(offenders, EffectsNamespace, EffectsRootFloor,
            "A prefix floor cannot see this shrink, and `18` §4/§5's interpreter moving out is " +
            "exactly what would shrink it.");

        // 🔴 The SAME argument, in the direction it had not been applied. Found by measuring every
        //    floor in this file against the tree: CombatFloor is 4 against 88 types under
        //    Rules.Combat, of which 47 are in Bosses/, Enemies/ and Status/. The combat log — the
        //    thing CombatFloor's own message says it is there for, "the one that matters, because
        //    the combat log is what a trigger most wants to reach" — could be deleted along with
        //    the whole of Rules/Combat/'s root, and a prefix floor of 4 stays satisfied by the boss
        //    engine next door. That is precisely the hazard the remarks above identify for
        //    Rules.Effects and close with EffectsRootFloor; Rules.Combat was left on the bare prefix.
        RootFloor(offenders, CombatNamespace, CombatRootFloor,
            "`05` §7's combat log, `05` §4's attack pipeline and `05` §3's tick loop are declared " +
            "directly here, and they are the forbidden set the Effects -> Combat and Stats -> " +
            "Combat edges exist to protect. A prefix floor over Rules.Combat is satisfied by " +
            "Bosses/, Enemies/ and Status/ alone, so it cannot see them go.");

        // 🔒 The three restated namespace constants are the ones Domain declares. Not editing
        // Domain.cs was deliberate (M1-12 holds it, steering S12), but the two statements can still
        // be pinned to each other — otherwise M1-12 renaming a constant makes R17 govern a namespace
        // that no longer exists, silently.
        offenders.AddRange(
            new[]
            {
                (Restated: StatsNamespace, Declared: Domain.StatsRulesNamespace, Name: nameof(StatsNamespace)),
                (Restated: CombatNamespace, Declared: Domain.CombatRulesNamespace, Name: nameof(CombatNamespace)),
                (Restated: EffectsNamespace, Declared: Domain.RulesNamespace + ".Effects", Name: nameof(EffectsNamespace)),
            }
            .Where(pair => !pair.Restated.Equals(pair.Declared, StringComparison.Ordinal))
            .Select(pair =>
                $"{pair.Name} is '{pair.Restated}' here and '{pair.Declared}' in Domain. R17 would " +
                "then govern a namespace that does not exist, and report success over nothing."));

        ArchRule.Empty(
            offenders,
            "R17's subject and target sets are the ones the layering rule was written against (23 §6).");
    }

    /// <summary>
    /// 🔒 `30` §11.4 / `23` §6 — <b>R17 is a total order over the sub-namespaces of
    /// <c>Core.Rules</c>, so the set it orders must be the set that exists.</b> A fourth
    /// sub-namespace appearing with no declared edge is not
    /// a rule failure today and never becomes one: <see cref="ForbiddenEdges"/> is a hand-written
    /// whitelist of three ordered pairs, and a namespace named in none of them is governed by
    /// nothing, in either direction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is not hypothetical, and the name is already written down.</b>
    /// <c>SubjectSetFloorTests</c>' <c>LuckService</c> entry (M4-01) says in as many words that the
    /// `05` §6.2 no-repeat seam's implementation must not live on <c>LuckService</c> itself <em>"or
    /// <c>Rules.Luck</c> ends up naming <c>Rules.Combat</c> and R17 has no edge for it"</em>. That
    /// sentence is a correct diagnosis of a hole this file did not close: with
    /// <c>Rules/Luck/</c> on disk, <see cref="Rules_Effects_is_the_bottom_of_the_intra_Rules_layering"/>
    /// quantifies over three namespaces that do not include it and stays green over the cycle.
    /// </para>
    /// <para>
    /// 🔒 <b>What this asserts, and what it deliberately does not.</b> It does not guess where a new
    /// namespace belongs — that is a milestone-level decision, exactly as M2-05 said of
    /// <c>IEffectActorView</c>. It asserts only that the decision was <em>taken</em>: every namespace
    /// directly beneath <c>Core.Rules</c> appears in <see cref="ForbiddenEdges"/> as a subject, as a
    /// forbidden target, or both. Adding <c>Rules/Luck/</c> therefore fails here, in a message that
    /// names the pairs to add, rather than passing silently two milestones from now.
    /// </para>
    /// <para>
    /// ⚠️ Stated over the namespaces that <b>hold a type</b>, not over the directories: an empty
    /// folder emits nothing into the assembly and there is nothing for an ordering to govern.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_namespace_under_Rules_has_a_declared_place_in_R17()
    {
        var governed = ForbiddenEdges
            .SelectMany(edge => new[] { edge.Subject, edge.Forbidden })
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // The namespaces that actually exist directly under Core.Rules, by the segment that follows
        // it — Il.TypesUnder is a PREFIX match, so a type in Rules.Combat.Bosses reports "Combat".
        var present = Il.TypesUnder(ProductionAssemblies.CoreModule, Domain.RulesNamespace)
            .Where(t => !Domain.IsCompilerGenerated(t))
            .Select(t => Il.NamespaceOf(t))
            .Where(ns => ns.Length > Domain.RulesNamespace.Length + 1)
            .Select(ns => Domain.RulesNamespace + "." + ns[(Domain.RulesNamespace.Length + 1)..].Split('.')[0])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(ns => ns, StringComparer.Ordinal)
            .ToArray();

        var offenders = present
            .Where(ns => !governed.Contains(ns, StringComparer.Ordinal))
            .Select(ns =>
                $"{ns} holds types but appears in no R17 edge. The layering is a hand-written list of " +
                "ordered pairs, so a namespace named in none of them is unordered against all three " +
                "of Rules.Combat, Rules.Stats and Rules.Effects — a cycle through it passes " +
                "Rules_Effects_is_the_bottom_of_the_intra_Rules_layering with nothing going red. " +
                "Decide where it sits in `30` §11.4's Rules row and add its edges to ForbiddenEdges " +
                "in the same commit; do not widen this rule to ignore it.")
            .ToList();

        // S3 — the other direction. A governed namespace that holds nothing is a rule quantifying
        // over an empty set, which the floors above catch for the three named ones; this catches an
        // edge added for a namespace that was then never created.
        offenders.AddRange(
            governed
                .Where(ns => !present.Contains(ns, StringComparer.Ordinal))
                .Select(ns =>
                    $"{ns} is an R17 edge endpoint but no type is declared under it. The edge governs " +
                    "nothing. If the namespace was renamed, rename it here too."));

        ArchRule.Empty(
            offenders,
            "R17 orders every sub-namespace of Core.Rules that exists, and every namespace it orders " +
            "exists (30 §11.4, R17).");
    }

    /// <summary>
    /// 🔒 `23` §6 — the floors above are below the counts on the commit that wrote them, so none of
    /// them is already breached and reporting a false pass.
    /// </summary>
    /// <remarks>
    /// Steering S9, applied to this file's own numbers: a floor set <em>above</em> the real count
    /// fails loudly, but one set at a number nobody checked is a claim about the tree that was never
    /// verified. This asserts the headroom rather than the exact counts, so adding a type stays free.
    /// </remarks>
    [Fact]
    public void The_floors_are_below_the_counts_the_rule_was_written_against()
    {
        var offenders = new List<string>();

        foreach (var (ns, floor) in new[]
                 {
                     (EffectsNamespace, EffectsFloor),
                     (TriggersNamespace, TriggersFloor),
                     (StatsNamespace, StatsFloor),
                     (CombatNamespace, CombatFloor),
                 })
        {
            var found = Count(ns);

            if (found < floor)
            {
                offenders.Add($"{ns}: {found} types, floor {floor} — already breached");
            }
        }

        ArchRule.Empty(offenders, "Every floor in this file has headroom over the tree it was written against (23 §6).");
    }

    /// <summary>Types under <c>Rules/Effects/</c> and below, the whole `18` interpreter.</summary>
    private const int EffectsFloor = 12;

    /// <summary>Types declared directly in <c>Rules.Effects</c>, not in a sub-namespace.</summary>
    private const int EffectsRootFloor = 6;

    /// <summary>
    /// Types declared directly in <c>Rules.Combat</c>, not in <c>Bosses/</c>, <c>Enemies/</c> or
    /// <c>Status/</c> — `05` §3's loop, §4's pipeline, §4.1's ward pool and §7's log. 41 on the
    /// commit this floor landed; set well below so adding or removing one is not a test edit.
    /// </summary>
    private const int CombatRootFloor = 20;

    /// <summary>`18` §3's trigger model.</summary>
    private const int TriggersFloor = 8;

    /// <summary>`05` §1-2's stat block and `18` §8's aggregation.</summary>
    private const int StatsFloor = 4;

    /// <summary>`05` §7's combat log.</summary>
    private const int CombatFloor = 4;

    /// <summary>`18` §3's trigger model — floored separately. See the remarks above.</summary>
    internal const string TriggersNamespace = "SlayIdleRepeat.Core.Rules.Effects.Triggers";

    private static int Count(string ns) =>
        Il.TypesUnder(ProductionAssemblies.CoreModule, ns).Count(t => !Domain.IsCompilerGenerated(t));

    /// <summary>
    /// The floor over a namespace's <b>own</b> types, excluding everything in a sub-namespace.
    /// </summary>
    /// <remarks>
    /// <c>Il.TypesUnder</c> matches by prefix, so a floor stated with it is satisfied by any one
    /// sub-namespace. This is the companion that watches the root itself.
    /// </remarks>
    private static void RootFloor(List<string> offenders, string ns, int floor, string consequence)
    {
        var found = Il.TypesUnder(ProductionAssemblies.CoreModule, ns)
                      .Count(t => !Domain.IsCompilerGenerated(t) &&
                                  Il.NamespaceOf(t).Equals(ns, StringComparison.Ordinal));

        if (found < floor)
        {
            offenders.Add(
                $"types declared directly in {ns}: found {found}, floor is {floor}. {consequence} " +
                "If this shrank on purpose, lower the floor in the same commit and say why.");
        }
    }

    private static void Floor(List<string> offenders, string ns, int floor, string consequence)
    {
        var found = Count(ns);

        if (found < floor)
        {
            offenders.Add(
                $"types under {ns}: found {found}, floor is {floor}. {consequence} " +
                "If this shrank on purpose, lower the floor in the same commit and say why.");
        }
    }

    /// <summary>The namespace part of a full type name, for a name this suite holds as a string.</summary>
    /// <remarks>
    /// <c>Il.NamespaceOf</c> takes a <see cref="TypeDefinition"/>; the references being scanned are
    /// names, and a nested type's name carries a <c>/</c> that must be cut before the last dot is
    /// meaningful.
    /// </remarks>
    private static string NamespaceOf(string fullName)
    {
        var outer = fullName.Split('/')[0];
        var lastDot = outer.LastIndexOf('.');

        return lastDot < 0 ? string.Empty : outer[..lastDot];
    }
}
