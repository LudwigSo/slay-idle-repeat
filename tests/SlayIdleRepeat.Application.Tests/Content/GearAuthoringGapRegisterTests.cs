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
/// ⚠️ <b>An entry may name no owner, and that is a finding rather than an omission.</b> One of these
/// names no open tracker row — Stormcall's four-piece, whose chain-hit multiplier the design set
/// never wrote down. Naming a plausible task for it would be steering S6's fabricated value with a
/// task id in place of a number, so it carries <c>null</c> and says what is missing. The data arm
/// still fires on every entry. (The register held eight entries until M4-16e closed the affix and
/// Ironvow's four-piece against 16 D47's conditional bucket, and Ironvow's six-piece left with
/// 16 D49: NEGATE voids the hit, so the missing number stopped being missing by ceasing to exist —
/// M4-16g.)
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

    private const string Sets = "content/sets/sets.json";

    /// <summary>🔒 Every gear bonus the design set describes and this data set does not author.</summary>
    private static readonly GearGap[] Gaps =
    {
        // ── the set bonuses ───────────────────────────────────────────────────────────────────
        //
        // The affix pool no longer holds an entry: M4-16e (16 D47) authored the damage-vs-Elites
        // affix against the conditional standing-effect bucket, together with Ironvow's four-piece
        // below.
        new(Sets + "#/sets/0/bonuses/2/effects",
            "Bloodmoon's six-piece bonus, 'lifesteal also applies to pet damage'",
            "M4-07",
            "There are no pets. Nothing in the repository can own a pet, deal pet damage, or aggregate " +
            "a pet aura, and the effect vocabulary has no op meaning 'extend a stat's reach to another " +
            "actor's damage' either — so this is two mechanisms away, not one."),


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
            "The trigger exists — ON_ATTACK carries `everyNth`, and the design set writes the 5 — the " +
            "target exists, and since 16 D47 the shape does too. What is still missing is the damage: " +
            "08 §3.2 says the attack 'chains' and authors no multiplier for the chained hit, so the " +
            "one number the effect needs is the one nobody wrote — filling it with 1.0 would be a " +
            "plausible value, not a transcription. No open row owns authoring it."),

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

        // ⚠️ A hardcoded example of an UNSTARTED task expires when that task ships, which is what
        // happened to M5-06 at its own merge. Re-point it rather than deleting the arm: without a
        // false case the predicate could return true for everything and this control would pass.
        HasShipped(rows["M18-08"]).ShouldBeFalse(
            "M18-08 has not started, and it owns nothing here either — so neither half of this control " +
            "restates the rule it is controlling");
    }

    /// <summary>🔒 The floor under both arms (steering S3): the register still holds its entries.</summary>
    /// <remarks>
    /// Separate from the predicate probe above because these are facts about the register's SHAPE
    /// rather than about the predicate — a name that promised both would deliver whichever failed
    /// first.
    /// </remarks>
    [Fact]
    public void The_register_holds_the_five_gaps_still_open()
    {
        Gaps.Length.ShouldBe(
            5,
            "five set-bonus breakpoints are deliberately unauthored — eight entries until M4-16e " +
            "closed the affix and Ironvow's four-piece against 16 D47's conditional bucket, and " +
            "M4-16g closed Ironvow's six-piece against 16 D49's NEGATE. A " +
            "register that quietly emptied would report success over nothing.");

        Gaps.ShouldAllBe(g => g.Why.Length > 0, "a gap without a reason is a gap nobody can falsify");

        Gaps.Select(g => g.Pointer).Distinct(StringComparer.Ordinal).Count().ShouldBe(
            Gaps.Length, "two entries on one pointer would untrack the second when the first is filled");
    }

    /// <summary>
    /// Whether a task's status cell says the work landed. See <see cref="TrackerStatus.HasShipped"/>
    /// for the glyph rule and why position cannot be used.
    /// </summary>
    private static bool HasShipped(string statusCell) => TrackerStatus.HasShipped(statusCell);

    /// <summary>Every task row in the tracker, task id to status cell.</summary>
    private static IReadOnlyDictionary<string, string> TrackerRows() => TrackerStatus.Rows();

    /// <summary>Every task id the tracker declares, read with the same regex the lookup uses.</summary>
    private static IReadOnlyCollection<string> TrackerRowIds() => TrackerStatus.Ids();
}
