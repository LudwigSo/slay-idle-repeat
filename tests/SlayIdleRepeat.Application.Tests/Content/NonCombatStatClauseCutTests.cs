using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// 🔒 D48 (`16` A12, ruled 2026-08-20): <c>GOLD_PCT</c>, <c>PET_AURA_PCT</c> and
/// <c>TILE_PREVIEW</c> clauses are cut from shipped perk content until a consumer exists — and this
/// class is the arm that goes red when one is re-authored.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this class exists at all.</b> D48's tracker sentence says "the register's data arm stays
/// armed, so re-authoring one fires the gap test" — and before M4-16f nothing did.
/// <c>GearAuthoringGapRegisterTests</c> watches eight <em>fixed pointers</em> for a null being
/// filled; a clause re-authored into any perk lands at a pointer nobody registered. Probed on
/// 2026-08-26: a <c>GOLD_PCT</c> clause grafted into the shipped catalogue turned exactly three
/// tests red — the 237-effect census and the two unauthorised-hole pins — and an <c>ATK</c> clause
/// turned the <em>same three</em>, so no test distinguished the re-authoring D48 forbids from any
/// other authoring. This class is that missing arm, with its own discrimination probes beside it.
/// </para>
/// <para>
/// <b>Scope is the ruling's scope.</b> D48 cut <em>perk</em> content. The two
/// <c>tuning/drops.json</c> affix rows that name the same stats (<c>AFX_GOLD_GAIN</c>,
/// <c>AFX_PET_AURA_POWER</c>) deliberately STAY — they are the armed side, collected and reported
/// via <c>AggregatedStats.SkippedNonCombatStatEffects</c>, and their stat/op mapping is pinned by
/// <c>GearDocuments</c> against the shipped tuning. Cutting them too would be a second ruling
/// nobody made.
/// </para>
/// <para>
/// <b>How it expires.</b> When a system consumes a non-combat stat from the hero build, re-author
/// the content deliberately and delete (or narrow) this guard in the same commit, citing the ruling
/// that added the consumer. There is no task id to key an expiry on: gold gain has no owning system
/// — D48 removed the dishonest content, not the design hole — and inventing an owner here would be
/// steering S6 with a task id in place of a number. That leaves the reason-went-stale direction
/// unmechanised (steering S4's known limit); kickoffs re-read declared exceptions.
/// </para>
/// <para>
/// <b>The third stat is not this guard's to watch.</b> <c>TILE_PREVIEW</c> was retired from the
/// vocabulary itself — wire value 22, with the whole tile-preview mechanism (`04` §4, `16` D42) —
/// so the schema refuses the clause outright and no content-walk arm could ever see one load. The
/// case below pins that refusal, so the split stays visible: two of D48's stats are schema-legal
/// and cut by ruling; the third cannot be authored at all.
/// </para>
/// </remarks>
public sealed class NonCombatStatClauseCutTests
{
    private const string Document = "content/perks/perks.json";

    /// <summary>
    /// The unique anchor the injection probes graft a clause onto: the first clause of the
    /// catalogue's first row. If the row is renamed, <c>RepoData.SourceWithEdit</c> throws rather
    /// than letting the probes pass over nothing.
    /// </summary>
    private const string Anchor = "\"id\": \"PK_STATIC_CHARGE_T1\",";

    private static ContentSnapshot Data() => ContentLoader.Load(RepoData.Source()).Require();

    /// <summary>
    /// 🔒 The cut itself: no clause in the shipped perk catalogue names a non-combat stat.
    /// </summary>
    [Fact]
    public void Shipped_perk_content_authors_no_clause_naming_a_non_combat_stat()
    {
        var offending = StatClauses(Data()).Where(c => !c.Combat).ToArray();

        offending.ShouldBeEmpty(
            "D48: a non-combat stat clause in a perk is collected, valued, reported in " +
            "AggregatedStats.SkippedNonCombatStatEffects, and applied by nobody — content a player " +
            "can take and feel nothing from. Cut until a consumer exists. If a consumer now exists " +
            "and this authoring is deliberate, delete or narrow this guard in the same commit, " +
            "citing the ruling that added the consumer.");
    }

    /// <summary>
    /// The floor under the walk (steering S3): a traversal that silently found no stat clauses
    /// would pass the cut over nothing.
    /// </summary>
    /// <remarks>
    /// A floor rather than an equality: the exact census (237 effects) is
    /// <c>PerksDataTests.R35_validated_every_embedded_effect_in_the_shipped_perk_catalogue</c>'s
    /// pin, and an equality here would just be a second copy to move on every authoring. 40 is
    /// well under the 57 stat clauses shipped today and well over zero, which is the failure this
    /// floor exists to catch.
    /// </remarks>
    [Fact]
    public void The_walk_sees_the_catalogues_stat_clauses()
    {
        var clauses = StatClauses(Data());

        clauses.Count.ShouldBeGreaterThanOrEqualTo(
            40, "the shipped catalogue authors well over this many stat clauses (57 as of " +
                "2026-08); finding almost none means the walk broke — a renamed member, a " +
                "reshaped row — not that the catalogue emptied");

        clauses.ShouldContain(
            c => c.Stat == nameof(StatId.ATK),
            "a catalogue with no ATK clause at all is not the shipped catalogue");
    }

    /// <summary>
    /// 🔒 The discrimination probes (steering S1), in-suite so they cannot rot: a re-authored
    /// clause on either schema-legal non-combat stat is found, located, and named.
    /// </summary>
    /// <remarks>
    /// Two shapes on purpose — different stat, different op. <c>.Require()</c> is half the point:
    /// the loader ACCEPTS both clauses, which is what keeps the cut a ruling about content rather
    /// than a fact about the schema, and is why this guard must exist. The clause key set mirrors
    /// the cut originals (M3-07's <c>PK_GREED_T1</c> shape) exactly.
    /// </remarks>
    [Theory]
    [InlineData("STAT_ADD_PCT", "GOLD_PCT", "0.25")]
    [InlineData("STAT_ADD_FLAT", "PET_AURA_PCT", "0.05")]
    public void A_re_authored_non_combat_clause_is_found_by_this_guard(
        string op, string stat, string value)
    {
        var snapshot = ContentLoader.Load(
            RepoData.SourceWithEdit(Document, Anchor, Injected(op, stat, value))).Require();

        var found = StatClauses(snapshot).Where(c => !c.Combat).ShouldHaveSingleItem(
            "the injected clause is the only non-combat clause in the edited catalogue");

        found.Stat.ShouldBe(stat, "which stat fired — steering S2");
        found.Pointer.ShouldBe(
            Document + "#/perks/0/tiers/0/effects/0",
            "and where: the graft site, so the guard reports a location a human can open");
    }

    /// <summary>
    /// The negative control (steering S1): a combat clause authored the same way is not this
    /// guard's subject.
    /// </summary>
    /// <remarks>
    /// The catalogue census and the unauthorised-hole pins still move on this edit, exactly as
    /// they move on any authoring — that is their job, not this guard's. This control is what
    /// separates the two: without it, an arm that fired on "a clause was added" would pass every
    /// case above. The <c>ALL_COMBAT</c> case exercises the selector-token branch of the walk: a
    /// token that is schema-legal but not a <c>StatId</c> member, and expands to combat stats.
    /// </remarks>
    [Theory]
    [InlineData("ATK")]
    [InlineData("ALL_COMBAT")]
    public void A_combat_stat_clause_is_not_this_guards_subject(string stat)
    {
        var snapshot = ContentLoader.Load(
            RepoData.SourceWithEdit(Document, Anchor, Injected("STAT_ADD_PCT", stat, "0.05"))).Require();

        StatClauses(snapshot).Where(c => !c.Combat).ShouldBeEmpty(
            "a combat clause is ordinary authoring; a guard that fired on it would be pinning the " +
            "census, which PerksDataTests already owns");
    }

    /// <summary>
    /// 🔒 D48's third stat cannot be re-authored at all: <c>TILE_PREVIEW</c> left the vocabulary
    /// with the tile-preview mechanism (`16` D42, wire value 22 retired), so the loader refuses
    /// the clause this class would otherwise have to watch for.
    /// </summary>
    /// <remarks>
    /// The clause is byte-identical to the accepted <c>GOLD_PCT</c> probe except for the stat
    /// token, so the refusal is about the stat and nothing else.
    /// </remarks>
    [Fact]
    public void A_TILE_PREVIEW_clause_is_refused_by_the_schema_itself()
    {
        Enum.TryParse<StatId>("TILE_PREVIEW", out _).ShouldBeFalse(
            "the stat is retired from the vocabulary, not merely cut from content");

        var result = ContentLoader.Load(
            RepoData.SourceWithEdit(Document, Anchor, Injected("STAT_ADD_PCT", "TILE_PREVIEW", "0.25")));

        result.Snapshot.ShouldBeNull("a retired stat must not load");

        var issue = result.Issues.ShouldHaveSingleItem();

        issue.Code.ShouldBe(ContentIssueCode.SchemaViolation, "which rule fired — steering S2");
        issue.Location.ShouldStartWith(Document, Case.Sensitive, "which document");
        issue.Location.ShouldContain("/effects/", Case.Sensitive, "which embedded effect");
        issue.Message.ShouldContain("TILE_PREVIEW", Case.Sensitive, "and which token was refused");
    }

    /// <summary>One stat-naming clause of the shipped catalogue, located and classified.</summary>
    private sealed record StatClause(string Pointer, string Stat, bool Combat);

    /// <summary>
    /// Every embedded effect clause in the perk catalogue that names a stat, with its JSON pointer
    /// and its combat/non-combat classification — read off <see cref="StatIds.IsCombat"/>, the
    /// same classification the aggregation pipeline skips by, so this guard and the runtime cannot
    /// disagree about what "non-combat" means.
    /// </summary>
    private static IReadOnlyList<StatClause> StatClauses(ContentSnapshot snapshot)
    {
        var root = snapshot.GetDocument(Document).Root;

        root.TryGetMember("perks", out var rows).ShouldBeTrue("the catalogue is a 'perks' array");

        var clauses = new List<StatClause>();

        for (var p = 0; p < rows!.Items.Count; p++)
        {
            rows.Items[p].TryGetMember("tiers", out var tiers).ShouldBeTrue("every row carries tiers");

            for (var t = 0; t < tiers!.Items.Count; t++)
            {
                tiers.Items[t].TryGetMember("effects", out var effects).ShouldBeTrue(
                    "every tier carries an effects array");

                for (var e = 0; e < effects!.Items.Count; e++)
                {
                    if (!effects.Items[e].TryGetMember("stat", out var stat) ||
                        stat!.Kind != ContentValueKind.Text)
                    {
                        continue;
                    }

                    var token = stat.AsText("stat");

                    // The two selector tokens that are schema-legal here but are not StatId
                    // members: ALL_COMBAT (statSelector) expands to the fourteen combat stats, so
                    // it IS a combat clause; HIGHEST_PCT_BONUS (statCopySelector) resolves at copy
                    // time to whichever combat stat leads, so neither is this guard's subject.
                    if (token is "ALL_COMBAT" or "HIGHEST_PCT_BONUS")
                    {
                        clauses.Add(new StatClause(
                            $"{Document}#/perks/{p}/tiers/{t}/effects/{e}", token, Combat: true));
                        continue;
                    }

                    Enum.TryParse<StatId>(token, ignoreCase: false, out var statId).ShouldBeTrue(
                        $"'{token}' at {Document}#/perks/{p}/tiers/{t}/effects/{e} is not a " +
                        "declared StatId — the schema admits a stat the vocabulary does not, " +
                        "and this guard cannot classify what it cannot parse");

                    clauses.Add(new StatClause(
                        $"{Document}#/perks/{p}/tiers/{t}/effects/{e}",
                        token,
                        StatIds.IsCombat(statId)));
                }
            }
        }

        return clauses;
    }

    /// <summary>
    /// The injected probe clause: a complete sibling grafted before the anchor clause, in the cut
    /// originals' exact key set.
    /// </summary>
    private static string Injected(string op, string stat, string value) =>
        "\"id\": \"PK_STATIC_CHARGE_T1_PROBE\", \"op\": \"" + op + "\", \"stat\": \"" + stat +
        "\", \"trigger\": { \"kind\": \"ALWAYS\" }, \"condition\": null, \"target\": \"SELF\", " +
        "\"value\": " + value + " }, { " + Anchor;
}
