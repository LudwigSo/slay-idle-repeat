using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content.Perks;

/// <summary>
/// <c>PerkEffectText</c> — the token renderer that turns a perk's description template into the
/// sentence a draft card draws, with the tier's own authored numbers in it.
/// </summary>
/// <remarks>
/// 🔒 Every substitution case is driven by a SYNTHETIC catalogue: a case stated over today's data
/// cannot tell a renderer that reads the authored member from one that happens to answer the same
/// number. The shipped catalogue is asserted separately, about a different thing — which perks it
/// cannot render at all.
/// </remarks>
public sealed class PerkEffectTextTests
{
    /// <summary>The id every synthetic fixture authors. One perk is all a token case needs.</summary>
    private const string FixturePerk = "PK_FIXTURE";

    // ------------------------------------------------------------------------------------------
    // One case per token kind, over synthetic data.
    // ------------------------------------------------------------------------------------------

    /// <summary>A bare <c>{value}</c> renders the authored number in the unit it was authored in.</summary>
    [Fact]
    public void A_bare_value_token_renders_the_authored_number()
    {
        var render = Render("Jump {value} tiles.", Effect(("value", Number(10m))));

        render.Text.ShouldBe(
            "Jump 10 tiles.", "a token with no percent sign after it is authored in its display unit");
    }

    /// <summary>…and the negative control: a bare token is NOT multiplied by a hundred.</summary>
    [Fact]
    public void A_token_not_followed_by_a_percent_sign_is_not_multiplied()
    {
        var render = Render("Deal {value} true damage.", Effect(("value", Number(0.02m))));

        render.Text.ShouldBe(
            "Deal 0.02 true damage.",
            "the hundred belongs to the percent sign in the template, not to the token");
    }

    /// <summary>A <c>{value}%</c> renders the authored fraction times a hundred.</summary>
    [Fact]
    public void A_percent_suffixed_value_token_renders_the_authored_fraction_times_a_hundred()
    {
        var render = Render("+{value}% ATK.", Effect(("value", Number(0.12m))));

        render.Text.ShouldBe("+12% ATK.", "a percent-suffixed source is authored as a fraction");
    }

    /// <summary><c>{duration}</c> reads the anchor effect's duration in seconds.</summary>
    [Fact]
    public void A_duration_token_reads_the_anchor_effects_duration_seconds()
    {
        var render = Render(
            "+{value}% ATK for {duration}s.",
            Effect(
                ("value", Number(0.5m)),
                ("duration", Obj(("scope", Text("BATTLE")), ("seconds", Number(8m))))));

        render.Text.ShouldBe("+50% ATK for 8s.", "the seconds are authored under duration, in seconds");
    }

    /// <summary><c>{everyNth}</c> reads the anchor effect's trigger.</summary>
    [Fact]
    public void An_everyNth_token_reads_the_anchor_effects_trigger()
    {
        var render = Render(
            "Every {everyNth}th attack hits twice.",
            Effect(
                ("op", Text("EXTRA_ATTACK")),
                ("trigger", Obj(("kind", Text("ON_ATTACK")), ("everyNth", Number(5m))))));

        render.Text.ShouldBe(
            "Every 5th attack hits twice.",
            "this tier authors no value at all, so the anchor falls back to its first effect");
    }

    /// <summary><c>{interval}</c> reads the anchor effect's trigger too.</summary>
    [Fact]
    public void An_interval_token_reads_the_anchor_effects_trigger()
    {
        var render = Render(
            "Every {interval}s, mark an enemy for +{value}% damage.",
            Effect(
                ("value", Number(0.3m)),
                ("trigger", Obj(("kind", Text("PERIODIC")), ("interval", Number(6m))))));

        render.Text.ShouldBe("Every 6s, mark an enemy for +30% damage.");
    }

    /// <summary><c>{sourceCapPct}</c> reads the anchor effect's own member, and is a fraction.</summary>
    [Fact]
    public void A_sourceCapPct_token_renders_the_authored_fraction_times_a_hundred()
    {
        var render = Render(
            "Overheal becomes a shield, up to {sourceCapPct}% Max HP.",
            Effect(
                ("op", Text("SHIELD")),
                ("value", Number(1.0m)),
                ("sourceCapPct", Number(0.2m))));

        render.Text.ShouldBe("Overheal becomes a shield, up to 20% Max HP.");
    }

    /// <summary>
    /// 🔒 <c>{cap}</c> is the effective value at max steps — <c>value × valueScale.cap</c> — and not
    /// the authored cap on its own.
    /// </summary>
    /// <remarks>
    /// The fixture authors <c>value</c> as 2 rather than 1 precisely so the two answers differ: with
    /// a value of 1 a renderer returning the bare cap would be indistinguishable from a correct one,
    /// and the schema's scaling is <c>value × steps</c> with the steps capped, not the cap alone.
    /// </remarks>
    [Fact]
    public void A_cap_token_renders_value_times_the_scale_cap()
    {
        var render = Render(
            "Convert Gold at run end, max {cap} Crowns.",
            Effect(
                ("op", Text("GRANT_CURRENCY")),
                ("value", Number(2m)),
                ("valueScale", Obj(
                    ("fn", Text("GOLD_HELD")), ("per", Number(10m)), ("cap", Number(500m))))));

        render.Text.ShouldBe(
            "Convert Gold at run end, max 1000 Crowns.",
            "the cap is a step count; the effective value is the value multiplied by it");
    }

    /// <summary>…and a percent-suffixed <c>{cap}</c> multiplies the product, not the cap.</summary>
    [Fact]
    public void A_percent_suffixed_cap_token_renders_value_times_cap_times_a_hundred()
    {
        var render = Render(
            "+{value}% ATK per 1% missing HP, up to +{cap}%.",
            Effect(
                ("value", Number(0.01m)),
                ("valueScale", Obj(
                    ("fn", Text("SELF_MISSING_HP_PCT")),
                    ("per", Number(0.01m)),
                    ("cap", Number(45m))))));

        render.Text.ShouldBe("+1% ATK per 1% missing HP, up to +45%.");
    }

    // ------------------------------------------------------------------------------------------
    // The anchor.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 Every number in one sentence comes from ONE effect — the first carrying a <c>value</c> —
    /// so a sentence never mixes two unrelated effects' numbers and reads as a single claim.
    /// </summary>
    /// <remarks>
    /// The decoy effect is authored FIRST and carries a trigger interval the anchor also carries,
    /// with a different number. A renderer resolving each token against the first effect that
    /// happens to carry it would answer the decoy's 99 here, and would be indistinguishable from a
    /// correct one on every other case in this file.
    /// </remarks>
    [Fact]
    public void Every_token_reads_the_first_effect_carrying_a_value()
    {
        var render = Render(
            "Every {interval}s, deal {value} damage.",
            Effect(
                ("id", Text("FX_DECOY")),
                ("op", Text("SET_TARGET_PRIORITY")),
                ("trigger", Obj(("kind", Text("PERIODIC")), ("interval", Number(99m))))),
            Effect(
                ("id", Text("FX_ANCHOR")),
                ("value", Number(0.5m)),
                ("trigger", Obj(("kind", Text("PERIODIC")), ("interval", Number(7m))))));

        render.Text.ShouldBe(
            "Every 7s, deal 0.5 damage.", "the decoy effect carries no value, so it is not the anchor");
    }

    /// <summary>…and when no effect carries a value, the anchor is the tier's first effect.</summary>
    [Fact]
    public void A_tier_authoring_no_value_anywhere_anchors_on_its_first_effect()
    {
        var render = Render(
            "Every {everyNth}th attack hits twice.",
            Effect(
                ("id", Text("FX_FIRST")),
                ("op", Text("EXTRA_ATTACK")),
                ("trigger", Obj(("kind", Text("ON_ATTACK")), ("everyNth", Number(3m))))),
            Effect(
                ("id", Text("FX_SECOND")),
                ("op", Text("FORCE_CRIT_NEXT")),
                ("charges", Number(1m)),
                ("trigger", Obj(("kind", Text("ON_ATTACK")), ("everyNth", Number(9m))))));

        render.Text.ShouldBe(
            "Every 3th attack hits twice.", "the first effect is the anchor, not the second");
    }

    // ------------------------------------------------------------------------------------------
    // Formatting.
    // ------------------------------------------------------------------------------------------

    /// <summary>Trailing zeros are trimmed rather than drawn.</summary>
    [Theory]
    [InlineData("3.0", "Deal 3 damage.")]
    [InlineData("2.5000", "Deal 2.5 damage.")]
    [InlineData("0.2500", "Deal 0.25 damage.")]
    public void A_number_is_written_with_its_trailing_zeros_trimmed(string authored, string expected)
    {
        var value = decimal.Parse(authored, CultureInfo.InvariantCulture);

        Render("Deal {value} damage.", Effect(("value", Number(value)))).Text.ShouldBe(expected);
    }

    /// <summary>…and to at most four decimal places, the precision this repo rounds simulation values to.</summary>
    [Theory]
    [InlineData("Deal {value} damage.", "Deal 0.1235 damage.")]
    [InlineData("+{value}% ATK.", "+12.3456% ATK.")]
    public void A_number_is_written_to_at_most_four_decimal_places(string template, string expected)
    {
        Render(template, Effect(("value", Number(0.123456m)))).Text.ShouldBe(expected);
    }

    // ------------------------------------------------------------------------------------------
    // 🔒 The hole is never filled.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 A token naming a member the anchor does not carry is UNRESOLVED, not defaulted to zero or
    /// to anything else a reader would take for an authored number.
    /// </summary>
    [Fact]
    public void A_source_the_anchor_effect_does_not_carry_is_unresolved_rather_than_defaulted()
    {
        var render = Render("+{value}% ATK for {duration}s.", Effect(("value", Number(0.5m))));

        render.IsRendered.ShouldBeFalse("the anchor authors no duration, so the sentence cannot be completed");
        render.Text.ShouldBeNull("a partly-substituted sentence is never handed out");
        render.UnresolvedTokens.ShouldBe(["duration"]);
    }

    /// <summary>
    /// 🔒 …and a source only a LATER effect carries is unresolved too, rather than borrowed from it.
    /// </summary>
    /// <remarks>
    /// A renderer that falls back to scanning siblings passes both the decoy case (decoy sits before
    /// the anchor) and the single-effect case (no sibling); only this arrangement separates it.
    /// </remarks>
    [Fact]
    public void A_source_only_a_later_effect_carries_is_unresolved_rather_than_borrowed()
    {
        var render = Render(
            "+{value}% ATK for {duration}s.",
            Effect(("id", Text("FX_ANCHOR")), ("value", Number(0.5m))),
            Effect(
                ("id", Text("FX_SIBLING")),
                ("op", Text("SHIELD")),
                ("duration", Obj(("scope", Text("BATTLE")), ("seconds", Number(4m))))));

        render.Text.ShouldBeNull(
            "the anchor authors no duration. The four seconds belong to a different effect and would " +
            "have been read as this one's.");
        render.UnresolvedTokens.ShouldBe(["duration"]);
    }

    /// <summary>…and a token naming no member of the schema at all is unresolved the same way.</summary>
    [Fact]
    public void A_token_naming_no_field_of_the_schema_is_unresolved()
    {
        var render = Render(
            "50% chance each attack deals x{high}, 50% chance x{low}.",
            Effect(("value", Number(2.0m))));

        render.IsRendered.ShouldBeFalse();
        render.UnresolvedTokens.ShouldBe(["high", "low"]);
    }

    /// <summary>🔒 …and ONE unresolved token withholds the WHOLE sentence, however many siblings resolved.</summary>
    /// <remarks>
    /// This is the case a renderer substituting what it could would pass differently: it would hand
    /// back <c>Below 25% HP: +40% DEF and +{value2}% Damage Reduction.</c> and the card would draw a
    /// token at the player.
    /// </remarks>
    [Fact]
    public void One_unresolved_token_withholds_the_whole_sentence()
    {
        var render = Render(
            "Below 25% HP: +{value}% DEF and +{value2}% Damage Reduction.",
            Effect(("value", Number(0.4m))));

        render.Text.ShouldBeNull("the resolvable half is not handed out on its own");
        render.UnresolvedTokens.ShouldBe(["value2"]);
    }

    /// <summary>The default value of the render type answers nothing rather than an empty list.</summary>
    /// <remarks>
    /// A <c>readonly record struct</c> is default-constructible from outside, and a default reading
    /// as "rendered, with no unresolved tokens" would be a hole in the type's own guarantee.
    /// </remarks>
    [Fact]
    public void The_default_render_refuses_to_answer_its_token_list()
    {
        var uninitialised = default(PerkEffectTextRender);

        uninitialised.IsRendered.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => uninitialised.UnresolvedTokens);
    }

    /// <summary>…and neither door that builds one can build the contradictory shape either.</summary>
    /// <remarks>
    /// A refused render with an empty token list reads exactly like a successful render of nothing,
    /// which is the one shape the type exists to make unreachable.
    /// </remarks>
    [Fact]
    public void Neither_door_builds_a_render_that_is_both_or_neither()
    {
        Should.Throw<ArgumentException>(() => PerkEffectTextRender.Unresolvable([]));
        Should.Throw<ArgumentNullException>(() => PerkEffectTextRender.Rendered(null!));

        var rendered = PerkEffectTextRender.Rendered("done");

        rendered.IsRendered.ShouldBeTrue();
        rendered.UnresolvedTokens.ShouldBeEmpty();

        var refused = PerkEffectTextRender.Unresolvable(["value2"]);

        refused.IsRendered.ShouldBeFalse();
        refused.Text.ShouldBeNull();
        refused.UnresolvedTokens.ShouldBe(["value2"]);
    }

    // ------------------------------------------------------------------------------------------
    // 🔒 The shipped catalogue (steering S4).
    // ------------------------------------------------------------------------------------------

    /// <summary>🔒 Every shipped perk renders its numbers, at every tier it authors.</summary>
    /// <remarks>
    /// <para>
    /// A perk that cannot render says "numbers unavailable" on its draft card — the player is asked
    /// to choose between three cards, one of which will not tell them what it does. The catalogue
    /// authors none, and this is the pin that keeps it that way: a description written against a
    /// token its own effect data cannot answer fails here rather than on a card.
    /// </para>
    /// <para>
    /// A template may only reach for what the anchor effect actually carries — <c>{value}</c>,
    /// <c>{duration}</c>, <c>{everyNth}</c>, <c>{interval}</c>, <c>{sourceCapPct}</c> and the
    /// <c>{cap}</c> product. The two-magnitude sentence ("+X% DEF and +Y% DR") is the shape that
    /// cannot be written, because the anchor is ONE effect and the second clause is a second one.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_shipped_perk_renders_its_numbers_at_every_tier()
    {
        var offenders = ShippedFailures();

        offenders.Keys.OrderBy(id => id, StringComparer.Ordinal).ShouldBeEmpty(
            "a perk joining this set is a description authored against data that cannot answer it, " +
            "and its draft card will silently say its numbers are unavailable: " +
            string.Join(
                ", ",
                offenders.Select(o => o.Key + " (" + string.Join("/", o.Value) + ")")));
    }

    /// <summary>🔒 …and the rendered sentence never leaves a token standing.</summary>
    /// <remarks>
    /// The negative control on the pin above. A renderer that answered every template unchanged
    /// would report no failures at all and draw <c>+{value}% ATK.</c> on every card, which is
    /// exactly the shape the emptiness above cannot tell apart on its own.
    /// </remarks>
    [Fact]
    public void Every_shipped_perk_renders_a_sentence_carrying_no_token()
    {
        var catalogue = PerkCatalogue.Read(ShippedHarness.Content);

        // The subject set is floored by name — a walk derived from the catalogue itself would agree
        // with itself over an empty one.
        var walked = catalogue.All.Select(perk => perk.Id).ToArray();

        walked.ShouldContain("PK_MIGHT");
        walked.ShouldContain("PK_STATIC_CHARGE");
        walked.ShouldContain("PK_OVERFLOW");

        foreach (var perk in catalogue.All)
        {
            for (var tier = 1; tier <= perk.TierCount; tier++)
            {
                var text = PerkEffectText.Render(ShippedHarness.Content, perk.Id, tier).Text;

                text.ShouldNotBeNull(perk.Id + " tier " + tier + " rendered nothing.");
                text.Contains('{', StringComparison.Ordinal).ShouldBeFalse(
                    perk.Id + " tier " + tier + " left a token standing: " + text);
            }
        }
    }

    /// <summary>
    /// …and four shipped sentences, spelled out, one per substitution rule: the percent suffix, the
    /// tier scaling behind it, <c>{everyNth}</c> read off the trigger, and <c>{sourceCapPct}</c>
    /// read off an op-specific key. These move when balance moves — re-state the row rather than
    /// loosening it.
    /// </summary>
    [Theory]
    [InlineData("PK_MIGHT", 1, "+12% ATK.")]
    [InlineData("PK_MIGHT", 3, "+33.6% ATK.")]
    [InlineData("PK_RECOIL_ARC", 1, "Every 5th attack detonates the charge for 80% of ATK to every enemy.")]
    [InlineData("PK_OVERFLOW", 1, "Healing past full becomes a shield, up to 20% Max HP.")]
    public void A_shipped_perk_renders_the_sentence_its_data_authors(string perkId, int tier, string expected)
    {
        PerkEffectText.Render(ShippedHarness.Content, perkId, tier).Text.ShouldBe(expected);
    }

    // ------------------------------------------------------------------------------------------
    // Fixtures.
    // ------------------------------------------------------------------------------------------

    /// <summary>Every shipped perk that cannot be rendered at some tier, and what stopped it.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ShippedFailures()
    {
        var failures = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var perk in PerkCatalogue.Read(ShippedHarness.Content).All)
        {
            var tokens = new List<string>();

            for (var tier = 1; tier <= perk.TierCount; tier++)
            {
                var render = PerkEffectText.Render(ShippedHarness.Content, perk.Id, tier);

                if (render.IsRendered)
                {
                    continue;
                }

                tokens.AddRange(
                    render.UnresolvedTokens.Where(t => !tokens.Contains(t, StringComparer.Ordinal)));
            }

            if (tokens.Count > 0)
            {
                failures[perk.Id] = tokens;
            }
        }

        return failures;
    }

    /// <summary>Renders <paramref name="description"/> against a one-perk, one-tier synthetic catalogue.</summary>
    private static PerkEffectTextRender Render(string description, params ContentValue[] effects) =>
        PerkEffectText.Render(Catalogue(description, effects), FixturePerk, tier: 1);

    /// <summary>A <c>content/perks/perks.json</c> carrying one perk with one tier of these effects.</summary>
    private static ContentSnapshot Catalogue(string description, IReadOnlyList<ContentValue> effects) =>
        new(
            ContentVersion.FromHex(new string('e', ContentVersion.HexLength)),
            [
                new ContentDocument(PerkDocuments.DocumentPath, Obj(
                    ("perks", ContentValue.Array(
                    [
                        Obj(
                            ("id", Text(FixturePerk)),
                            ("name", Text("Fixture")),
                            ("category", Text("OFFENSE")),
                            ("rarity", Text("COMMON")),
                            ("iconId", Text("icon_perk_fixture")),
                            ("description", Text(description)),
                            ("tiers", ContentValue.Array(
                            [
                                Obj(("tier", Number(1m)), ("effects", ContentValue.Array(effects))),
                            ])),
                            ("excludes", ContentValue.Array([])),
                            ("requires", ContentValue.Array([])),
                            ("poolTags", ContentValue.Array([Text("standard")]))),
                    ])))),
            ]);

    /// <summary>One effect, defaulted to the commonest authored shape and overridden by name.</summary>
    private static ContentValue Effect(params (string Name, ContentValue Value)[] members)
    {
        var authored = new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["id"] = Text("FX"),
            ["op"] = Text("STAT_ADD_PCT"),
            ["stat"] = Text("ATK"),
            ["trigger"] = Obj(("kind", Text("ALWAYS"))),
            ["condition"] = ContentValue.Unauthorised,
            ["target"] = Text("SELF"),
        };

        foreach (var (name, value) in members)
        {
            authored[name] = value;
        }

        return ContentValue.Object(authored);
    }

    private static ContentValue Obj(params (string Name, ContentValue Value)[] members) =>
        ContentValue.Object(members.Select(m => new KeyValuePair<string, ContentValue>(m.Name, m.Value)));

    private static ContentValue Text(string value) => ContentValue.Text(value);

    private static ContentValue Number(decimal value) => ContentValue.Number(value);
}
