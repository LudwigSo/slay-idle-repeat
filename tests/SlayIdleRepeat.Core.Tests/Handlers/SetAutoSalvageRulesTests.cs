using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Forge;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// <c>SET_AUTO_SALVAGE_RULES</c> — the four rules that refuse a row list, what an accepted one
/// stores, and the rule it finally makes reachable.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>All four refusals answer <c>ILLEGAL_STATE</c>, because `14` §16.2 authors no finer
/// domain-tier value for a malformed configuration.</b> So a case that asserted only the code would
/// pass with three of the four rules deleted. Each is built as a PAIR of worlds whose payloads are
/// identical but for the one fact its rule reads: the control has to be accepted and the variant
/// refused, and nothing else in the payload can account for the difference.
/// </para>
/// <para>
/// 🔒 <b>Every bound asserted here is read off the design set, never restated.</b> The row count
/// comes from <c>Enum.GetValues&lt;Rarity&gt;().Length</c> and the level range from
/// <c>ForgeTuning</c>'s authored <c>minLevel</c>/<c>maxLevel</c> — a literal 5 or 15 in this file
/// would pass just as well against a handler that had invented its own limit and happened to agree.
/// </para>
/// </remarks>
public sealed class SetAutoSalvageRulesTests
{
    /// <summary>
    /// The authored enhancement range the ceiling is validated against, read from the same shipped
    /// tuning the handler reads through <c>Worlds.Context</c>.
    /// </summary>
    private static readonly ForgeTuning Forge = ForgeTuning.Read(TuningDocuments.Shipped);

    /// <summary>Every band the ladder declares — the row-count bound, derived rather than restated.</summary>
    private static readonly Rarity[] Bands = Enum.GetValues<Rarity>();

    // ═══════════════════════════════════════════════════════ what an accepted one stores

    /// <summary>
    /// 🔒 An accepted command stores the rows, in order, and replaces whatever was there.
    /// </summary>
    /// <remarks>
    /// The design set's own example read literally — <em>"salvage all C and B below +3"</em>, which
    /// is where <c>AutoSalvageRule</c>'s shape comes from — and then a second, different filter over
    /// the same player, because "the rows are stored" is also true of a handler that appended.
    /// </remarks>
    [Fact]
    public void An_accepted_filter_replaces_whatever_the_player_had()
    {
        var first = Set(World(), Row(Rarity.C, 3), Row(Rarity.B, 3));

        first.Accepted.ShouldBeTrue("the filter was refused " + first.Rejection + ".");

        first.NewState.Player.AutoSalvageRules.ShouldBe(
            new[] { Row(Rarity.C, 3), Row(Rarity.B, 3) },
            "08 §4.3's own example — 'salvage all C and B below +3' — stored in the order it was sent.");

        var second = Set(first.NewState, Row(Rarity.A, 10));

        second.Accepted.ShouldBeTrue("the second filter was refused " + second.Rejection + ".");

        second.NewState.Player.AutoSalvageRules.ShouldBe(
            new[] { Row(Rarity.A, 10) },
            "the command REPLACES the filter. A handler that appended would leave three rows here, " +
            "and the player would have no way to remove the two they just edited away.");

        first.Events.ShouldBeEmpty(
            "a filter is configuration: it grants nothing and moves no currency.");
    }

    /// <summary>🔒 The empty list is a legal filter, and it is how a player switches the feature off.</summary>
    /// <remarks>
    /// `08` §4.3 makes an empty filter mean "sweep nothing", so refusing it would leave a player who
    /// had ever configured a row unable to stop. Driven from a NON-empty filter, so the case is about
    /// clearing rather than about a player who never set one.
    /// </remarks>
    [Fact]
    public void An_empty_filter_is_accepted_and_clears_the_rows()
    {
        var configured = Set(World(), Row(Rarity.C, 3));
        configured.NewState.Player.AutoSalvageRules.Count.ShouldBe(1, "the premise.");

        var cleared = Set(configured.NewState);

        cleared.Accepted.ShouldBeTrue("an empty filter was refused " + cleared.Rejection + ".");
        cleared.NewState.Player.AutoSalvageRules.ShouldBeEmpty(
            "an empty list sweeps nothing, which is exactly what a player turning the feature off " +
            "is asking for.");
    }

    // ═══════════════════════════════════════════════════════ 1 · a band that is not on the ladder

    /// <summary>
    /// 🔒 A row over a band the rarity ladder does not declare is refused, where the same row over a
    /// declared band is accepted.
    /// </summary>
    /// <remarks>
    /// <c>Rarity</c> has no zero member on purpose, so this is what an uninitialised wire column
    /// arrives as. The pair is the assertion: the ceiling, the row count and the player are identical
    /// on both sides, so only the band can account for the refusal.
    /// </remarks>
    [Fact]
    public void A_row_over_a_band_the_ladder_does_not_declare_is_refused()
    {
        var declared = Set(World(), Row(Rarity.C, 3));
        var undeclared = Set(World(), Row((Rarity)0, 3));

        declared.Accepted.ShouldBeTrue(
            "the control was refused " + declared.Rejection + ". Same player, same ceiling, one row " +
            "on each side — with this failing, the refusal below is not about the band.");

        undeclared.Accepted.ShouldBeFalse(
            "a filter row over a band nothing can be would sweep nothing, forever, invisibly. " +
            "Player.Rehydrate refuses the same row on the way IN; refusing it here is what stops it " +
            "ever being written.");

        undeclared.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);

        undeclared.NewState.Player.AutoSalvageRules.ShouldBeEmpty(
            "a refused SET_AUTO_SALVAGE_RULES changes nothing.");
    }

    // ═══════════════════════════════════════════════════════ 2 · two rows for one band

    /// <summary>
    /// 🔒 Two rows for one band are refused, where two rows for two bands are accepted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pair differs in one token — the second row's band — so neither the row count nor the
    /// ceilings can account for the refusal.
    /// </para>
    /// <para>
    /// ⚠️ <b>This is stricter than <c>AutoSalvageFilter</c>, deliberately.</b> The filter reads a
    /// PERSISTED row and tolerates a repeat, sweeping on either so that neither row is silently
    /// ignored — it has to do something defensible with whatever it finds. The command is the WRITER,
    /// and a filter carrying two ceilings for one band is a screen the player cannot read back: which
    /// of the two they set is decided by array order and nothing tells them.
    /// </para>
    /// </remarks>
    [Fact]
    public void Two_rows_for_one_band_are_refused_where_two_bands_are_accepted()
    {
        var twoBands = Set(World(), Row(Rarity.C, 3), Row(Rarity.B, 5));
        var oneBandTwice = Set(World(), Row(Rarity.C, 3), Row(Rarity.C, 5));

        twoBands.Accepted.ShouldBeTrue(
            "the control was refused " + twoBands.Rejection + ". Two rows on each side and the same " +
            "two ceilings — the ONLY difference below is that the second row names the same band, " +
            "so with this failing the refusal is not about repeats.");

        oneBandTwice.Accepted.ShouldBeFalse(
            "one band cannot have two keep-lines. Which of the two the player set would be decided " +
            "by the order the array happened to be in.");

        oneBandTwice.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    // ═══════════════════════════════════════════════════════ 3 · more rows than there are bands

    /// <summary>
    /// 🔒 A list longer than the rarity ladder is refused, where a list exactly as long as the ladder
    /// is accepted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>The CONTROL is the whole discriminating half of this case, and that is measured rather
    /// than argued.</b> The accepted side carries one legal row per band — the largest filter that
    /// can exist once repeats are refused — so a handler with a cap smaller than the ladder fails
    /// it: tightening the bound by one turned this red. The refused side does <em>not</em>
    /// discriminate, and cannot: by pigeonhole a list longer than the ladder must repeat a band, so
    /// the repeat rule catches it too. MEASURED: deleting the row-count check outright left this
    /// whole file green.
    /// </para>
    /// <para>
    /// The check is kept anyway, and the handler's own comment says why — the bound is stated where
    /// a reader looks for it and derived from the ladder rather than guessed, and it caps the work a
    /// hostile payload can cause before the tuning read. What must not happen is this case being
    /// read as proof that it bites on its own.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_list_longer_than_the_rarity_ladder_is_refused()
    {
        Bands.Length.ShouldBeGreaterThan(
            1, "the ladder has to have bands, or both halves below are the same list.");

        var everyBand = Bands.Select(band => Row(band, 1)).ToArray();

        var atTheBound = Set(World(), everyBand);

        atTheBound.Accepted.ShouldBeTrue(
            "the control was refused " + atTheBound.Rejection + ". One row per band is the largest " +
            "filter that can legally exist, so a handler that refuses it has a cap smaller than the " +
            "ladder — a limit nobody authored.");

        atTheBound.NewState.Player.AutoSalvageRules.Count.ShouldBe(Bands.Length);

        var overTheBound = Set(World(), [.. everyBand, Row(Bands[0], 2)]);

        overTheBound.Accepted.ShouldBeFalse(
            "a list longer than the ladder cannot describe a filter whatever it holds — there is no " +
            "band left for the extra row to be about.");

        overTheBound.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    // ═══════════════════════════════════════════════════════ 4 · a ceiling outside the range

    /// <summary>
    /// 🔒 A ceiling outside `08` §4.2's authored enhancement range is refused, at both ends, where
    /// both ends of the range itself are accepted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four worlds, differing only in one integer. The accepted pair is the <em>edges</em> of the
    /// legal range rather than a comfortable middle: <c>minLevel</c> is how a player keeps a row in
    /// place with it switched off, and <c>maxLevel + 1</c> is how they say "sweep this band whatever
    /// it is enhanced to" — an off-by-one at either end takes one of those away, and a probe at +3
    /// would not notice.
    /// </para>
    /// <para>
    /// The bounds are read from the tuning, so a handler that hard-coded 0 and 16 would still pass —
    /// and would then be wrong the day the range moves, which is what <c>ForgeTuning</c> exists to
    /// prevent. What this case pins is that the handler agrees with the document today.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_ceiling_outside_the_authored_enhancement_range_is_refused_at_both_ends()
    {
        Set(World(), Row(Rarity.C, Forge.MinEnhanceLevel)).Accepted.ShouldBeTrue(
            "the floor of the range was refused. A ceiling of minLevel sweeps nothing, which " +
            "AutoSalvageRule's own remarks make a legal row: it is how a player switches one band " +
            "off without losing its place in the filter.");

        Set(World(), Row(Rarity.C, Forge.MaxEnhanceLevel + 1)).Accepted.ShouldBeTrue(
            "the top of the range was refused. 'Below maxLevel + 1' is how a player says 'sweep this " +
            "band whatever it is enhanced to' — refusing it makes the fully-enhanced item the one " +
            "thing no filter can ever reach.");

        var belowFloor = Set(World(), Row(Rarity.C, Forge.MinEnhanceLevel - 1));

        belowFloor.Accepted.ShouldBeFalse("no item sits below the authored floor.");
        belowFloor.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);

        var aboveCeiling = Set(World(), Row(Rarity.C, Forge.MaxEnhanceLevel + 2));

        aboveCeiling.Accepted.ShouldBeFalse(
            "a ceiling two past the top names a level no item can reach. It means the same sweep as " +
            "maxLevel + 1, which is exactly why it is a client sending a number rather than a " +
            "keep-line the player chose.");
        aboveCeiling.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    // ═══════════════════════════ what the command is FOR: the rule it makes reachable

    /// <summary>
    /// 🔴 <b>The rows a command stored are the rows <c>AutoSalvageFilter</c> selects on</b> — the
    /// first time the rule has been driven from anything but a hand-written fixture.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Player.AutoSalvageRules</c> had no writer until this command, so
    /// <c>Rules.Forge.AutoSalvageFilter</c> — 75 lines of production code with its own suite — could
    /// only ever be reached by a test constructing the rows itself. This case closes the loop the
    /// other way: send the command, then hand the STORED rows to the filter and watch it pick the
    /// item the player asked it to.
    /// </para>
    /// <para>
    /// ⚠️ It is not an end-to-end claim, and must not be read as one. Nothing applies the filter at
    /// run end — that is out of scope for this ruling, and the screen that edits the rows is M9-01's.
    /// What this shows is that the two halves now fit.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_stored_rows_are_the_rows_the_filter_selects_on()
    {
        var stock = Inventories.Holding(
            Inventories.Item("gi_junk", rarity: Rarity.C, enhanceLevel: 0),
            Inventories.Item("gi_keeper", rarity: Rarity.C, enhanceLevel: 9));

        var world = new WorldSlice(
            Worlds.Rehydrated(PlayerSnapshots.With(inventory: stock.ToSnapshot())), null);

        world.Player.AutoSalvageRules.ShouldBeEmpty(
            "the premise: the player has configured nothing, so the filter starts sweeping nothing.");

        var result = Set(world, Row(Rarity.C, 3));

        result.Accepted.ShouldBeTrue("the filter was refused " + result.Rejection + ".");

        var swept = AutoSalvageFilter.Select(
            result.NewState.Player.Inventory, result.NewState.Player.AutoSalvageRules);

        swept.Select(id => id.Value).ShouldBe(
            new[] { "gi_junk" },
            "the +0 C item is below the keep-line the COMMAND set and the +9 one is not. Both are " +
            "the same band, so a filter that swept on the band alone would take both and one that " +
            "read no rows at all would take neither.");
    }

    // ═══════════════════════════════════════════════════════ the worlds

    private static WorldSlice World() =>
        new(Worlds.Rehydrated(PlayerSnapshots.Valid), null);

    private static CommandResult Set(WorldSlice world, params AutoSalvageRule[] rules) =>
        SlayIdleRepeat.Core.GameRules.Apply(
            world, new SetAutoSalvageRulesCommand(rules), Worlds.Context);

    private static AutoSalvageRule Row(Rarity band, int belowEnhanceLevel) =>
        new(band, belowEnhanceLevel);
}
