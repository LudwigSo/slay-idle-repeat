using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.BalanceHarness;
using SlayIdleRepeat.Core.Tests.Handlers;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects;

/// <summary>
/// 🔒 The three run-scoped sources — <c>SHRINE_BUFFS</c>, <c>RUN_BUFFS</c>, <c>CURSES</c> — asserted
/// where it matters: on the effect list a FIGHT is handed.
/// </summary>
/// <remarks>
/// The claim is not "the source builds an effect" but "the shrine the player visited changes the
/// hero". Asserted through <c>HeroBuild</c> rather than by constructing a source directly, because
/// a source nothing composes into the build is exactly the state all three of these were in before:
/// authored, tested, and wired to nothing.
/// </remarks>
public sealed class RunModifierEffectSourceTests
{
    private static ContentSnapshot Content => ShippedHarness.Content;

    /// <summary>The effect list a fight is handed for a run holding the given modifiers.</summary>
    private static IReadOnlyList<EffectDefinition> Effects(
        IReadOnlyList<string>? shrineBuffs = null,
        IReadOnlyList<string>? runBuffs = null,
        IReadOnlyList<string>? curses = null,
        int chapterId = 1)
    {
        var world = TileWorlds.OnTile(
            SlayIdleRepeat.Core.Rules.Board.TileKind.Enemy,
            chapterId: chapterId,
            shrineBuffs: shrineBuffs,
            runBuffs: runBuffs,
            curses: curses);

        return HeroBuild.Of(world.Player, world.Run, Content).Effects;
    }

    private static EffectDefinition? Find(IReadOnlyList<EffectDefinition> effects, string prefix) =>
        effects.FirstOrDefault(e => e.Id.StartsWith(prefix, StringComparison.Ordinal));

    // ------------------------------------------------------------------------------ shrine buffs

    [Fact]
    public void A_taken_shrine_buff_reaches_the_fight()
    {
        var effect = Find(Effects(shrineBuffs: ["SHR_ATK"]), "shrine:SHR_ATK");

        effect.ShouldNotBeNull(
            "the run took Sharpened Resolve and the fight was handed nothing for it — which is the " +
            "state every shrine buff was in before this source existed.");
        effect.Op.ShouldBe(EffectOp.STAT_ADD_PCT);
        effect.Stat!.Value.Stat.ShouldBe(StatId.ATK);
        effect.Value.ShouldBe(0.12, "03 §7a.5 authors Sharpened Resolve at +12% ATK.");
    }

    /// <summary>
    /// 🔒 `03` §7a.5 stacks the same buff additively, so a repeat is a SECOND effect — under a
    /// distinct id, because `18` §8 sorts by effect id and two effects sharing one would fall to a
    /// tiebreak instead of both applying.
    /// </summary>
    [Fact]
    public void The_same_shrine_buff_taken_twice_contributes_twice()
    {
        var effects = Effects(shrineBuffs: ["SHR_ATK", "SHR_ATK"]);

        var stacked = effects
            .Where(e => e.Id.StartsWith("shrine:SHR_ATK", StringComparison.Ordinal))
            .ToArray();

        stacked.Length.ShouldBe(2);
        stacked.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count().ShouldBe(
            2, "two stacks sharing one effect id are two effects the resolution order cannot separate.");
    }

    /// <summary>
    /// <c>SHR_HEAL</c> authors <c>stat: null</c> — it is a heal, not a buff — so it contributes no
    /// effect at all rather than a zero-valued one.
    /// </summary>
    [Fact]
    public void The_pure_heal_row_contributes_no_permanent_effect()
    {
        Find(Effects(shrineBuffs: ["SHR_HEAL"]), "shrine:SHR_HEAL").ShouldBeNull();
    }

    /// <summary>
    /// 🔴 The two rows whose authored token is NOT spelled the way the enum spells it —
    /// <c>DR</c> for <c>DR_PCT</c> and <c>GOLD_GAIN</c> for <c>GOLD_PCT</c>. An
    /// <c>Enum.TryParse</c> per call site would have dropped exactly these two.
    /// </summary>
    [Theory]
    [InlineData("SHR_DR", StatId.DR_PCT)]
    [InlineData("SHR_GOLD", StatId.GOLD_PCT)]
    public void The_aliased_stat_tokens_still_reach_the_fight(string buffId, StatId stat)
    {
        var effect = Find(Effects(shrineBuffs: [buffId]), "shrine:" + buffId);

        effect.ShouldNotBeNull(
            buffId + " contributed nothing — its authored stat token is not spelled the way the " +
            "enum spells it, so a plain Enum.TryParse silently drops it and the buff becomes an " +
            "option that looks identical on screen and does nothing.");
        effect.Stat!.Value.Stat.ShouldBe(stat);
    }

    /// <summary>A buff this content version no longer authors is skipped, not thrown on.</summary>
    /// <remarks>
    /// A content rollback across a live run must not leave that run unable to fight — the perk and
    /// loadout sources take the same position for the same reason.
    /// </remarks>
    [Fact]
    public void An_unauthored_buff_is_skipped_rather_than_refused()
    {
        Should.NotThrow(() => Effects(shrineBuffs: ["SHR_NOT_A_THING"]));
    }

    // --------------------------------------------------------------------------------- run buffs

    /// <summary>
    /// `03` §7 makes a run buff FLAT, not a percentage — "applied as <c>FlatAdd</c> in the `05` §1.1
    /// aggregation".
    /// </summary>
    [Fact]
    public void A_bought_run_buff_reaches_the_fight_as_a_flat_add()
    {
        var effect = Find(Effects(runBuffs: ["WHETSTONE"]), "runbuff:WHETSTONE");

        effect.ShouldNotBeNull();
        effect.Op.ShouldBe(EffectOp.STAT_ADD_FLAT);
        effect.Stat!.Value.Stat.ShouldBe(StatId.ATK);
        effect.Value.ShouldBe(12.0, "the chapter-1 magnitude, before any growth.");
    }

    /// <summary>…and it doubles per chapter, because the authored <c>chapterGrowth</c> says so.</summary>
    [Fact]
    public void A_run_buff_scales_with_the_chapter()
    {
        Find(Effects(runBuffs: ["WHETSTONE"], chapterId: 3), "runbuff:WHETSTONE")!
            .Value.ShouldBe(48.0, "12 × 2^(3-1).");
    }

    /// <summary>
    /// 🔒 …and Hawk's Eye does NOT, because Crit is a capped stat and its authored growth is 1.0.
    /// The negative control for the case above: without it, a source that scaled everything by the
    /// same curve would satisfy it.
    /// </summary>
    [Fact]
    public void The_chapter_invariant_run_buff_does_not_scale()
    {
        Find(Effects(runBuffs: ["HAWKS_EYE"], chapterId: 5), "runbuff:HAWKS_EYE")!
            .Value.ShouldBe(0.04, "chapterGrowth is authored as 1.0, so the magnitude is flat.");
    }

    // ----------------------------------------------------------------------------------- curses

    /// <summary>A stat-expressible curse reaches the fight as a negative percentage move.</summary>
    [Fact]
    public void A_stat_curse_reaches_the_fight()
    {
        var effect = Find(Effects(curses: ["CUR_FRACTURED"]), "curse:CUR_FRACTURED");

        effect.ShouldNotBeNull(
            "the run carried Fractured and the fight was handed nothing for it — a curse tile that " +
            "pays its reward and applies no debuff is strictly good, which is the shape this fixes.");
        effect.Stat!.Value.Stat.ShouldBe(StatId.DEF);
        effect.Value.ShouldBe(-0.08, "19 Part E authors Fractured at -8% DEF.");
    }

    /// <summary>
    /// …and a curse whose mechanism does not exist contributes NOTHING rather than an approximation.
    /// </summary>
    /// <remarks>
    /// The negative control for the case above: a source that turned every curse into some stat move
    /// would satisfy it while inventing five debuffs the design never specified.
    /// <c>CurseEffects.UnappliedReason</c> is what names the missing mechanism instead.
    /// </remarks>
    [Fact]
    public void A_curse_with_no_mechanism_contributes_nothing_and_says_why()
    {
        Find(Effects(curses: ["CUR_MARKED"]), "curse:CUR_MARKED").ShouldBeNull();

        CurseEffects.UnappliedReason("CUR_MARKED").ShouldNotBeNullOrWhiteSpace(
            "an unapplied curse names the mechanism it is missing, or it is indistinguishable from " +
            "one nobody noticed.");
    }

    /// <summary>
    /// 🔒 Every one of `19` Part E's nine curses is accounted for: applied as a stat, honoured by
    /// the board, or carrying a written reason it is neither.
    /// </summary>
    /// <remarks>
    /// Stated over the shipped catalogue rather than a list of ids, so a curse authored into the data
    /// fails here rather than becoming a silent no-op.
    /// </remarks>
    [Fact]
    public void Every_authored_curse_is_applied_or_names_its_missing_mechanism()
    {
        var catalogue = CurseTuning.Read(Content);

        catalogue.AvailableFrom(8).Count.ShouldBe(
            9,
            "19 Part E authors twelve curses, less CUR_DIZZY and CUR_LEADFOOT — both removed with " +
            "the reroll and the die's special faces — and less CUR_BLIND, removed with the tile " +
            "preview when the board became permanently visible (16 D42).");

        foreach (var row in catalogue.AvailableFrom(8))
        {
            var applied = CurseEffects.Stat(row.Id) is not null || CurseEffects.IsBoardRule(row.Id);
            var reason = CurseEffects.UnappliedReason(row.Id);

            (applied ^ (reason is not null)).ShouldBeTrue(
                row.Id + " is neither applied nor accounted for — or claims both. Exactly one has " +
                "to be true of every curse the catalogue authors.");
        }
    }
}
