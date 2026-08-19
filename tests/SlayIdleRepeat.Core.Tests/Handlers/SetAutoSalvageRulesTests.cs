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
/// All four refusals answer <c>ILLEGAL_STATE</c> (`14` §16.2 authors no finer value), so each rule
/// is built as a PAIR of worlds identical but for the one fact it reads: control accepted, variant
/// refused. Every bound is read off the design set, never restated as a literal.
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
    /// 🔒 An accepted command stores the rows, in order, and replaces whatever was there — a second
    /// filter over the same player, because "the rows are stored" is also true of a handler that
    /// appended.
    /// </summary>
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

    /// <summary>
    /// 🔒 The empty list is a legal filter (`08` §4.3: sweep nothing), driven from a NON-empty one so
    /// the case is about clearing rather than about a player who never set one.
    /// </summary>
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
    /// declared band is accepted. <c>Rarity</c> has no zero member on purpose, so <c>(Rarity)0</c> is
    /// what an uninitialised wire column arrives as.
    /// </summary>
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
    /// Deliberately stricter than <c>AutoSalvageFilter</c>, which reads a PERSISTED row and must
    /// tolerate a repeat; the command is the WRITER, and two ceilings for one band is a screen the
    /// player cannot read back.
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
    /// The CONTROL is the discriminating half: one legal row per band is the largest filter that can
    /// exist once repeats are refused, so it fails a handler whose cap is smaller than the ladder.
    /// The refused side cannot discriminate — by pigeonhole an over-long list repeats a band, so the
    /// repeat rule catches it too (measured: deleting the row-count check left this file green).
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
    /// The accepted pair is the edges of the legal range, not a comfortable middle: an off-by-one at
    /// either end takes away "row switched off" (minLevel) or "sweep whatever it is enhanced to"
    /// (maxLevel + 1), and a probe at +3 would not notice.
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
    /// The rows a command stored are the rows <c>AutoSalvageFilter</c> selects on. Not an end-to-end
    /// claim: nothing applies the filter at run end yet — this shows the two halves fit.
    /// </summary>
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
