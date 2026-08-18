using System.Text.RegularExpressions;
using SlayIdleRepeat.Application.Services.Content;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// 🔒 The register of gear bonuses the design set <b>describes</b> and the shipped data
/// <b>deliberately leaves unauthored</b> — each carried as a null the content-hole pin already
/// counts, each with the task that closes it, and each expiring by itself in <b>two</b> directions.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not an entry in one of the two registers that already exist.</b> Both of those key
/// on a <c>Core</c> <b>type name</b>: <c>GapRegister</c> expires an entry when
/// <c>Domain.FindInCore(subject)</c> starts answering, and <c>SubjectSetFloorTests.Pending</c> does
/// the same. Every subject here is a <b>hole in authored data</b> — a JSON pointer holding null —
/// and no type arrives when one is filled. Neither mechanism can hold it, and a register whose
/// predicate cannot fire is the thing all three of these exist to prevent.
/// </para>
/// <para>
/// 🔒 <b>Both arms fire, and the second one is the arm the tunable baseline records as missing from
/// itself.</b> An entry goes red when its pointer <em>is</em> authored — the ordinary direction —
/// and it also goes red when the task that owns it <em>ships</em>. The baseline's own note says its
/// <c>closedBy</c> check "asserts only that the id EXISTS. It is therefore satisfied forever by a
/// task that merged three milestones ago and closed nothing", and describes the missing predicate as
/// "read the ✅ out of each tracker row's LAST cell (not the whole line)". That predicate is written
/// here, with a discrimination probe under it, so the shape exists in the repository rather than
/// only in a comment.
/// </para>
/// <para>
/// ⚠️ <b>An entry may name no owner, and that is a finding rather than an omission.</b> Four of these
/// need a mechanism no open tracker row describes — applying a non-combat stat to anything at all, an
/// attacker- or target-conditional damage bucket, and two set bonuses whose magnitude the design set
/// never wrote down. Naming a plausible task for one of those would be steering S6's fabricated value
/// with a task id in place of a number, so they carry <c>null</c> and say what is missing. The data
/// arm still fires on every one of them.
/// </para>
/// </remarks>
public sealed partial class GearAuthoringGapRegisterTests
{
    /// <summary>One deliberately unauthored gear bonus.</summary>
    /// <param name="Pointer">The document pointer that must still hold a null.</param>
    /// <param name="Subject">What the design set calls it, so a failure reads as a sentence.</param>
    /// <param name="Owner">
    /// The tracker task that closes it, or <see langword="null"/> where no open row describes the
    /// mechanism it needs.
    /// </param>
    /// <param name="Why">What is missing. Something a later reader can falsify.</param>
    private sealed record GearGap(string Pointer, string Subject, string? Owner, string Why);

    private const string TrackerRelativePath = "IMPLEMENTATION_TRACKER.md";

    private const string Drops = "tuning/drops.json";

    private const string Sets = "content/sets/sets.json";

    /// <summary>🔒 Every gear bonus the design set describes and this data set does not author.</summary>
    private static readonly GearGap[] Gaps =
    {
        // ── the affix pool ────────────────────────────────────────────────────────────────────
        //
        // Thirteen of the fourteen affixes name a stat and a bucket. This is the one that cannot.
        new(Drops + "#/affixPool/affixes/13/stat",
            "the +X% damage-vs-Elites affix",
            null,
            "Conditional damage. The stat block is fourteen unconditional combat stats and has no " +
            "conditional bucket, and the obvious workaround does not work: a standing STAT_ADD_PCT on " +
            "DMG_PCT gated on TARGET_IS_ELITE is re-evaluated on every re-aggregation, where the " +
            "context carries no current target — the condition evaluator THROWS there rather than " +
            "reading false, so authoring it would end every battle in an exception. No open tracker " +
            "row describes an attacker- or target-conditional bucket, and naming one would be " +
            "inventing an owner."),

        // ── the set bonuses ───────────────────────────────────────────────────────────────────
        new(Sets + "#/sets/0/bonuses/2/effects",
            "Bloodmoon's six-piece bonus, 'lifesteal also applies to pet damage'",
            "M4-07",
            "There are no pets. Nothing in the repository can own a pet, deal pet damage, or aggregate " +
            "a pet aura, and the effect vocabulary has no op meaning 'extend a stat's reach to another " +
            "actor's damage' either — so this is two mechanisms away, not one."),

        new(Sets + "#/sets/1/bonuses/1/effects",
            "Ironvow's four-piece bonus, '-15% damage taken from Elites and Bosses'",
            null,
            "The magnitude is authored; the bucket is not. Damage reduction is one unconditional stat, " +
            "and the ATTACKER_IS_ELITE / ATTACKER_IS_BOSS conditions read false outside a " +
            "hit-reaction context — which is every re-aggregation — so a standing gated DR would be " +
            "silently inert rather than loud. This needs the same conditional bucket the " +
            "damage-vs-Elites affix needs, and the same absence of an owner applies."),

        new(Sets + "#/sets/1/bonuses/2/effects",
            "Ironvow's six-piece bonus, 'once per battle, negate a lethal hit'",
            null,
            "The mechanism exists and the number does not. SURVIVE_LETHAL arms a save and ON_LETHAL " +
            "carries `once`, but the op survives AT a named HP and 08 §3.2 authors none — and " +
            "'negate' arguably means the hit deals nothing, which is a different thing from surviving " +
            "at a fraction. Two unauthored decisions, either of which would be invented here."),

        new(Sets + "#/sets/2/bonuses/1/effects",
            "Fateweave's four-piece bonus, 'Star faces grant a free perk draft'",
            "M4-14",
            "Needs the Star die face, which no run can reach: the roll command carries no payload, so " +
            "a face requiring a player's choice is refused. M4-14 owns the die-face rewrite " +
            "resolution order across talents, mounts, set bonus and pet, which is the row this sits on."),

        new(Sets + "#/sets/2/bonuses/2/effects",
            "Fateweave's six-piece bonus, 'one die face of your choice becomes Star'",
            "M4-14",
            "The same unreachable face as the four-piece, plus a player choice the command vocabulary " +
            "has nowhere to carry. Same owner, same row."),

        new(Sets + "#/sets/3/bonuses/1/effects",
            "Stormcall's four-piece bonus, 'every 5th attack chains to all enemies'",
            null,
            "The trigger exists — ON_ATTACK carries `everyNth`, and the design set writes the 5 — and " +
            "the target exists. What is missing is the damage: 08 §3.2 says the attack 'chains' and " +
            "authors no multiplier for the chained hit, so the one number the effect needs is the one " +
            "nobody wrote. No open row owns authoring it."),

        new(Sets + "#/sets/3/bonuses/2/effects",
            "Stormcall's six-piece bonus, 'attack speed also scales pet ability cooldowns'",
            "M4-07",
            "There are no pets and therefore no pet ability cooldowns. REDUCE_COOLDOWN exists, but " +
            "there is nothing for it to reduce and no way to state 'scaled by the holder's attack " +
            "speed' without a valueScale reading a stat, which the value vocabulary does not offer."),
    };

    /// <summary>
    /// 🔒 The first arm: a registered hole that has been filled fails, so closing one is forced to
    /// delete its entry rather than remembered at some later kickoff.
    /// </summary>
    [Fact]
    public void Every_registered_gear_gap_is_still_a_hole_in_the_shipped_data()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        foreach (var gap in Gaps)
        {
            snapshot.TryRead(gap.Pointer, out var value).ShouldBeTrue(
                $"{gap.Pointer} is where {gap.Subject} would be authored, and the pointer is gone. " +
                "Either the document moved — in which case re-point this entry — or the shape " +
                "changed and this register is now watching nothing.");

            value!.IsUnauthorised.ShouldBeTrue(
                $"{gap.Pointer} now holds a value, so {gap.Subject} has been authored. Delete its " +
                "entry from this register in the same commit, and move the unauthorised-hole counts " +
                "with it.");
        }
    }

    /// <summary>
    /// 🔒 The second arm, and the one the tunable baseline records as missing from itself: an entry
    /// whose owner has <b>shipped</b> fails. Asserting the id merely <em>exists</em> is satisfied
    /// forever by a task that merged three milestones ago and closed nothing.
    /// </summary>
    [Fact]
    public void Every_named_owner_is_a_tracker_task_that_is_still_open()
    {
        var rows = TrackerRows();

        rows.Count.ShouldBeGreaterThan(
            150,
            $"{TrackerRelativePath} lists every milestone task; finding almost none means the row " +
            "pattern broke, not that the tracker emptied — and this rule would then pass over nothing");

        foreach (var gap in Gaps.Where(g => g.Owner is not null))
        {
            var owner = gap.Owner!;

            rows.ContainsKey(owner).ShouldBeTrue(
                $"{gap.Subject} is deferred to {owner}, which is not a task in {TrackerRelativePath}. " +
                "An owner that does not exist is a gap nobody will ever be told to come back to.");

            HasShipped(rows[owner]).ShouldBeFalse(
                $"{gap.Subject} is deferred to {owner}, and {owner} has shipped. Either it authored " +
                "the bonus — in which case this entry should have gone with it — or it did not, and " +
                "the gap now has no owner at all. Both are decisions to take deliberately.");
        }
    }

    /// <summary>
    /// 🔒 The floor under both arms (steering S3), and the discrimination probe under the second
    /// (steering S1): a predicate that answered "open" for everything would pass the rule above over
    /// every entry while tracking nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The probe is stated over two <b>literal</b> task ids of known and opposite status. One of them
    /// — M4-05 — is deliberately <em>not</em> one of this register's owners, so the control cannot be
    /// a restatement of the rule it is controlling; the other is unstarted. Should either row's
    /// status legitimately change, this fails and names the row, which is the correct amount of noise
    /// for a control.
    /// </para>
    /// <para>
    /// ⚠️ <b>It must not be anchored on the task that wrote this register.</b> That id's status is
    /// <c>in flight</c> today and <c>merged</c> the moment this branch lands, so a probe on it would
    /// go red on a healthy repository while reporting a predicate that was working correctly.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_open_predicate_separates_a_shipped_task_from_an_unstarted_one()
    {
        var rows = TrackerRows();

        var declared = TrackerRowIds();

        declared.Except(rows.Keys, StringComparer.Ordinal).ShouldBeEmpty(
            "every task row must yield a status glyph. A row whose notes hold an unescaped pipe used " +
            "to drop out of this lookup and read as permanently open. The expectation is derived " +
            "from the same file rather than pinned to a literal, so this names the offending row at " +
            "any tracker size instead of going red every time a row is added.");

        rows.Count.ShouldBe(
            declared.Count,
            "one status per declared row id, and no more");

        HasShipped(rows["M4-05"]).ShouldBeTrue(
            "M4-05 merged and is marked done, and it owns nothing in this register; a predicate that " +
            "cannot see a shipped task is not watching anything");

        HasShipped(rows["M5-06"]).ShouldBeFalse(
            "M5-06 has not started, and it owns nothing here either — so neither half of this control " +
            "restates the rule it is controlling");
    }

    /// <summary>🔒 The floor under both arms (steering S3): the register still holds its entries.</summary>
    /// <remarks>
    /// Separate from the predicate probe above because these are facts about the register's SHAPE
    /// rather than about the predicate — a name that promised both would deliver whichever failed
    /// first.
    /// </remarks>
    [Fact]
    public void The_register_holds_the_eight_gaps_it_was_written_against()
    {
        Gaps.Length.ShouldBe(
            8,
            "eight gear bonuses are deliberately unauthored: one affix and seven set-bonus " +
            "breakpoints. A register that quietly emptied would report success over nothing.");

        Gaps.ShouldAllBe(g => g.Why.Length > 0, "a gap without a reason is a gap nobody can falsify");

        Gaps.Select(g => g.Pointer).Distinct(StringComparer.Ordinal).Count().ShouldBe(
            Gaps.Length, "two entries on one pointer would untrack the second when the first is filled");
    }

    /// <summary>
    /// Whether a task's status cell says the work landed.
    /// </summary>
    /// <remarks>
    /// Read off the <b>first glyph of the status cell</b>, never the line: a row that is open often
    /// discusses a completed decision in its notes, and a tick anywhere in that prose would read as a
    /// finished task. ✅ is merged and verified, 🔍 is merged and awaiting its milestone review —
    /// both mean the task shipped. Everything else (⬜ unstarted, ⏳ queued, 🔄 in flight, ⛔ blocked,
    /// 🔴 open defect) means it has not.
    /// </remarks>
    private static bool HasShipped(string statusCell)
    {
        var trimmed = statusCell.TrimStart();

        return trimmed.StartsWith(Merged, StringComparison.Ordinal) ||
               trimmed.StartsWith(MergedAwaitingReview, StringComparison.Ordinal);
    }

    /// <summary>The status glyph for a task that merged and was verified.</summary>
    private const string Merged = "✅";

    /// <summary>The status glyph for a task that merged and awaits its milestone review.</summary>
    private const string MergedAwaitingReview = "🔍";

    /// <summary>The glyphs a status cell opens with. The first three mean shipped or not; all seven mark the cell.</summary>
    /// <remarks>
    /// A status cell is recognised BY ITS GLYPH, never by its position, and that is not tidiness.
    /// 🔴 Position was the first implementation and it is wrong on real rows: two of the tracker's
    /// task rows carry an unescaped pipe inside their notes, so the last <c>|</c>-delimited cell is a
    /// fragment of prose. <c>HasShipped</c> then answers "still open" unconditionally for those two —
    /// a second arm that cannot fire, inside the register written to end exactly that.
    /// </remarks>
    private static readonly string[] StatusGlyphs =
        ["✅", "🔍", "⬜", "⏳", "🔄", "⛔", "🔴"];

    /// <summary>Every task row in the tracker, task id to status cell.</summary>
    /// <remarks>
    /// Read from the tracker rather than transcribed here, because a hard-coded copy would go stale
    /// in exactly the silence this register exists to end. A duplicated id keeps the first row, which
    /// is the one the tables are ordered by.
    /// </remarks>
    private static IReadOnlyDictionary<string, string> TrackerRows()
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in File.ReadAllLines(Path.Combine(RepoData.RepositoryRoot, TrackerRelativePath)))
        {
            var match = TrackerRow().Match(line);

            if (!match.Success)
            {
                continue;
            }

            var status = line.Trim().Trim('|').Split('|')
                .Select(cell => cell.Trim())
                .LastOrDefault(cell => StatusGlyphs.Any(g => cell.StartsWith(g, StringComparison.Ordinal)));

            if (status is not null)
            {
                rows.TryAdd(match.Groups[1].Value, status);
            }
        }

        return rows;
    }

    /// <summary>Every task id the tracker declares, read with the same regex the lookup uses.</summary>
    /// <remarks>
    /// Deliberately derived rather than pinned. The literal it replaced was a proxy for "every row
    /// yields a status", and an equality on a number that legitimately grows can only ever be
    /// repaired by raising it -- which is how a guard stops guarding.
    /// </remarks>
    private static IReadOnlyCollection<string> TrackerRowIds()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in File.ReadAllLines(Path.Combine(RepoData.RepositoryRoot, TrackerRelativePath)))
        {
            var match = TrackerRow().Match(line);

            if (match.Success)
            {
                ids.Add(match.Groups[1].Value);
            }
        }

        return ids;
    }

    [GeneratedRegex(@"^\|\s*(M\d+-\d+[a-z]?)\s*\|")]
    private static partial Regex TrackerRow();
}
