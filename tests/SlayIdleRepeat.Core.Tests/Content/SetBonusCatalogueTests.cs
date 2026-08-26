using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Content.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.BalanceHarness;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// The four SS sets' breakpoint bonuses, read out of <c>content/sets/sets.json</c>.
/// </summary>
/// <remarks>
/// The reader is narrow on purpose — it maps the keys a set bonus authors and refuses any other —
/// because the alternative is a document that says one thing and a game that does another. Most of
/// the cases here are that refusal.
/// </remarks>
public sealed class SetBonusCatalogueTests
{
    private static readonly int[] Breakpoints = [2, 4, 6];

    /// <summary>Every breakpoint at or below the piece count grants, not only the highest.</summary>
    /// <remarks>
    /// The tiers escalate rather than replace, so a four-piece set is still granting its two-piece
    /// bonus. A reader that answered only the highest would quietly delete a bonus from every set
    /// past its first breakpoint.
    /// </remarks>
    [Fact]
    public void Every_breakpoint_at_or_below_the_piece_count_grants()
    {
        var sets = Shipped();

        sets.Granted(GearFamilyAxis.BALANCED, 1).ShouldBeEmpty("no breakpoint reached");
        sets.Granted(GearFamilyAxis.BALANCED, 2).Count.ShouldBe(1, "the two-piece only");
        sets.Granted(GearFamilyAxis.BALANCED, 4).Count.ShouldBe(2, "the two- and the four-piece");
        sets.Granted(GearFamilyAxis.BALANCED, 6).Count.ShouldBe(
            2, "the six-piece is deliberately unauthored, so it adds nothing");
    }

    /// <summary>An unauthored breakpoint grants nothing rather than refusing the whole set.</summary>
    /// <remarks>
    /// Seven of the twelve ship unauthored and a full set is reachable, so a reader that refused a
    /// null would make the strongest loadout in the game unloadable.
    /// </remarks>
    [Fact]
    public void An_unauthored_breakpoint_is_a_bonus_that_grants_nothing()
    {
        var rows = Shipped().Bonuses(GearFamilyAxis.BALANCED);

        rows.Count.ShouldBe(3);
        rows[0].Effects.ShouldNotBeNull("Bloodmoon's two-piece is +10% Lifesteal");
        rows[2].Effects.ShouldBeNull("its six-piece needs pets, which do not exist");
    }

    /// <summary>
    /// Ironvow's six-piece, shipped: 16 D49's value-less <c>NEGATE</c> save on <c>ON_LETHAL</c>,
    /// once per battle. Anchored on the breakpoint row rather than <c>Granted</c> counts so the
    /// four-piece row's own authoring (a sibling task) cannot move this pin.
    /// </summary>
    [Fact]
    public void The_shipped_Ironvow_six_piece_authors_the_D49_NEGATE_save()
    {
        var row = Shipped().Bonuses(GearFamilyAxis.HEAVY)[2];

        row.Pieces.ShouldBe(6);

        var effect = row.Effects.ShouldNotBeNull().ShouldHaveSingleItem();

        effect.Id.ShouldBe("SET_BONUS_HEAVY_6");
        effect.Op.ShouldBe(EffectOp.SURVIVE_LETHAL);
        effect.ValueMode.ShouldBe(ValueMode.NEGATE);
        effect.Value.ShouldBeNull("a voided hit has no HP number, and authoring one would be 16 R6's invented value");
        effect.Target.ShouldBe(EffectTarget.SELF);

        var trigger = effect.Trigger.ShouldNotBeNull();
        trigger.Kind.ShouldBe(TriggerKind.ON_LETHAL);
        trigger.Once.ShouldBe(true, "08 §3.2: ONCE per battle — an unbounded negate never dies");
    }

    /// <summary>A set the document does not author is refused, never answered as empty.</summary>
    [Fact]
    public void A_set_the_document_does_not_author_is_refused()
    {
        var sets = SetBonusCatalogue.Read(
            Snapshot(CoveringSet("BALANCED", StatEffect("SET_X"))), Breakpoints);

        Should.Throw<MissingContentException>(() => sets.Bonuses(GearFamilyAxis.HEAVY))
            .Message.ShouldContain("Every family axis is a set", Case.Sensitive);
    }

    /// <summary>A bonus authored at a piece count the breakpoint ladder does not carry is refused.</summary>
    /// <remarks>
    /// 🔒 The cross-document invariant, and the only layer that can see both halves. Only a
    /// breakpoint on the authored ladder is ever reported as met, so a bonus at three pieces reads as
    /// authored and can never fire — the exact shape a null exists to keep visible, wearing the
    /// clothes of a complete row.
    /// </remarks>
    [Fact]
    public void A_bonus_at_a_piece_count_the_ladder_does_not_carry_is_refused()
    {
        Should.Throw<InvalidTunableException>(
                () => SetBonusCatalogue.Read(
                    Snapshot(SetRow("BALANCED", BonusRow(3, StatEffect("SET_X")))), Breakpoints))
            .Message.ShouldContain("can never fire", Case.Sensitive);
    }

    /// <summary>A breakpoint granting an empty list is refused; null is how "unwritten" is said.</summary>
    [Fact]
    public void A_breakpoint_granting_an_empty_list_is_refused()
    {
        Should.Throw<InvalidTunableException>(
                () => SetBonusCatalogue.Read(
                    Snapshot(SetRow(
                        "BALANCED",
                        ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
                        {
                            ["pieces"] = ContentValue.Number(2),
                            ["effects"] = ContentValue.EmptyArray,
                        }))),
                    Breakpoints))
            .Message.ShouldContain("nobody has written this one", Case.Sensitive);
    }

    /// <summary>A key this reader does not map is refused rather than dropped.</summary>
    /// <remarks>
    /// The difference between a narrow reader and a lossy one: authoring a duration on a set bonus
    /// would otherwise load cleanly and grant a permanent effect instead, with the document and the
    /// game disagreeing and everything green.
    /// </remarks>
    [Fact]
    public void A_key_the_reader_does_not_map_is_refused()
    {
        var effect = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["id"] = ContentValue.Text("SET_X"),
            ["op"] = ContentValue.Text("STAT_ADD_FLAT"),
            ["stat"] = ContentValue.Text("LIFESTEAL"),
            ["value"] = ContentValue.Number(0.1m),
            ["duration"] = ContentValue.Text("BATTLE"),
        });

        Should.Throw<InvalidTunableException>(
                () => SetBonusCatalogue.Read(
                    Snapshot(CoveringSet("BALANCED", effect)), Breakpoints))
            .Message.ShouldContain("'duration' is a key this reader does not map", Case.Sensitive);
    }

    /// <summary>A key the reader does not map INSIDE the trigger is refused too.</summary>
    /// <remarks>
    /// The probe is <c>chance</c> — a real trigger parameter this reader still does not map — so the
    /// nested guard stays proven now that <c>once</c> is mapped for the six-piece save.
    /// </remarks>
    [Fact]
    public void A_trigger_key_the_reader_does_not_map_is_refused()
    {
        var effect = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["id"] = ContentValue.Text("SET_X"),
            ["op"] = ContentValue.Text("STAT_ADD_FLAT"),
            ["stat"] = ContentValue.Text("LIFESTEAL"),
            ["value"] = ContentValue.Number(0.1m),
            ["trigger"] = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
            {
                ["kind"] = ContentValue.Text("ON_ATTACK"),
                ["chance"] = ContentValue.Number(0.5m),
            }),
        });

        var message = Should.Throw<InvalidTunableException>(
                () => SetBonusCatalogue.Read(
                    Snapshot(CoveringSet("BALANCED", effect)), Breakpoints))
            .Message;

        message.ShouldContain("'chance' is a key this reader does not map", Case.Sensitive);
        message.ShouldContain(
            "a set bonus's trigger may carry",
            Case.Sensitive,
            "the nested guard fired, not the one over the effect's own keys");
    }

    /// <summary>
    /// <c>once</c> IS mapped, not merely tolerated: dropping it silently would turn one save per
    /// fight into one every time the hero would die.
    /// </summary>
    [Fact]
    public void A_set_bonus_trigger_carries_once_through_to_the_effect()
    {
        var effect = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["id"] = ContentValue.Text("SET_X"),
            ["op"] = ContentValue.Text("SURVIVE_LETHAL"),
            ["valueMode"] = ContentValue.Text("NEGATE"),
            ["target"] = ContentValue.Text("SELF"),
            ["trigger"] = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
            {
                ["kind"] = ContentValue.Text("ON_LETHAL"),
                ["once"] = ContentValue.True,
            }),
        });

        var read = SetBonusCatalogue.Read(Snapshot(CoveringSet("BALANCED", effect)), Breakpoints)
            .Bonuses(GearFamilyAxis.BALANCED)[0].Effects.ShouldNotBeNull().ShouldHaveSingleItem();

        read.Trigger.ShouldNotBeNull().Once.ShouldBe(true);
    }

    /// <summary>A set authoring fewer rows than the ladder has breakpoints is refused.</summary>
    /// <remarks>
    /// A dropped row is indistinguishable from an unauthored bonus, and telling those two apart is
    /// what the whole document is built on.
    /// </remarks>
    [Fact]
    public void A_set_that_does_not_cover_the_whole_breakpoint_ladder_is_refused()
    {
        Should.Throw<InvalidTunableException>(
                () => SetBonusCatalogue.Read(
                    Snapshot(SetRow("BALANCED", BonusRow(2, StatEffect("SET_A")))), Breakpoints))
            .Message.ShouldContain("different facts", Case.Sensitive);
    }

    /// <summary>An authorised condition on a set bonus is refused.</summary>
    /// <remarks>
    /// <c>condition</c> is a mapped key so a bonus can write the vocabulary's canonical "ungated"
    /// null — which is exactly why an authorised one would otherwise be accepted and then dropped.
    /// The build aggregation re-evaluates conditions outside any attack, where the conditions worth
    /// gating a set bonus on either throw or read false.
    /// </remarks>
    [Fact]
    public void An_authorised_condition_on_a_set_bonus_is_refused()
    {
        var effect = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["id"] = ContentValue.Text("SET_X"),
            ["op"] = ContentValue.Text("STAT_ADD_FLAT"),
            ["stat"] = ContentValue.Text("LIFESTEAL"),
            ["value"] = ContentValue.Number(0.1m),
            ["condition"] = ContentValue.Text("TARGET_IS_ELITE"),
        });

        Should.Throw<InvalidTunableException>(
                () => SetBonusCatalogue.Read(
                    Snapshot(CoveringSet("BALANCED", effect)), Breakpoints))
            .Message.ShouldContain("carries no condition", Case.Sensitive);
    }

    /// <summary>Bonuses authored out of ascending piece order are refused.</summary>
    [Fact]
    public void Bonuses_authored_out_of_ascending_order_are_refused()
    {
        Should.Throw<InvalidTunableException>(
                () => SetBonusCatalogue.Read(
                    Snapshot(SetRow(
                        "BALANCED",
                        BonusRow(4, StatEffect("SET_A")),
                        BonusRow(2, StatEffect("SET_B")))),
                    Breakpoints))
            .Message.ShouldContain("ascending piece order", Case.Sensitive);
    }

    /// <summary>One axis authored twice is refused.</summary>
    [Fact]
    public void One_axis_authored_twice_is_refused()
    {
        Should.Throw<InvalidTunableException>(
                () => SetBonusCatalogue.Read(
                    Snapshot(
                        CoveringSet("BALANCED", StatEffect("SET_A")),
                        CoveringSet("BALANCED", StatEffect("SET_B"))),
                    Breakpoints))
            .Message.ShouldContain("The BALANCED axis is authored twice", Case.Sensitive);
    }

    // ------------------------------------------------------------------------ fixtures

    private static SetBonusCatalogue Shipped() =>
        SetBonusCatalogue.Read(
            ShippedHarness.Content, DropsTuning.Read(ShippedHarness.Content).SetBreakpoints);

    private static ContentSnapshot Snapshot(params ContentValue[] sets) =>
        new(
            ProgressionDocuments.Shipped.Version,
            [
                new ContentDocument(
                    SetBonusCatalogue.DocumentPath,
                    ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
                    {
                        ["sets"] = ContentValue.Array(sets),
                    })),
            ]);

    /// <summary>
    /// One set covering the whole authored ladder: the given effects at the first breakpoint, and
    /// the rest unauthored.
    /// </summary>
    /// <remarks>
    /// The reader refuses a set that does not cover the ladder, so a fixture about anything else has
    /// to cover it — which is the guard doing its job on this file's own author.
    /// </remarks>
    private static ContentValue CoveringSet(string axis, params ContentValue[] firstBreakpoint) =>
        SetRow(
            axis,
            [
                BonusRow(Breakpoints[0], firstBreakpoint),
                Unauthored(Breakpoints[1]),
                Unauthored(Breakpoints[2]),
            ]);

    private static ContentValue Unauthored(int pieces) =>
        ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["pieces"] = ContentValue.Number(pieces),
            ["effects"] = ContentValue.Unauthorised,
        });

    private static ContentValue SetRow(string axis, params ContentValue[] bonuses) =>
        ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["familyAxis"] = ContentValue.Text(axis),
            ["bonuses"] = ContentValue.Array(bonuses),
        });

    private static ContentValue BonusRow(int pieces, params ContentValue[] effects) =>
        ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["pieces"] = ContentValue.Number(pieces),
            ["effects"] = ContentValue.Array(effects),
        });

    private static ContentValue StatEffect(string id) =>
        ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["id"] = ContentValue.Text(id),
            ["op"] = ContentValue.Text(nameof(EffectOp.STAT_ADD_FLAT)),
            ["stat"] = ContentValue.Text(nameof(StatId.LIFESTEAL)),
            ["value"] = ContentValue.Number(0.1m),
        });
}
