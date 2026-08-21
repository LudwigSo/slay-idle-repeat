using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Luck;

namespace SlayIdleRepeat.Core.Rules.Perks;

/// <summary>
/// A run's open perk draft, projected into read-only records the Perk Draft screen can draw. The
/// three options are never persisted — they regenerate from the run's committed <c>draft</c> stream
/// position, the same derivation <c>PICK_PERK</c> answers its option index against — so this is the
/// only way anything outside <c>Core</c> can see what a player is being offered.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The derivation is shared with the handler, not restated here.</b> The screen draws what
/// <c>PICK_PERK</c> will act on; a second copy of the draw would be a second chance for the two to
/// disagree, and the disagreement would be a player taking the card to the left of the one they
/// pressed.
/// </para>
/// <para>
/// 🔒 <b>Read-only.</b> Projecting mutates no <c>Run</c> and moves no stream position: the draft
/// stream is reopened at the position the run committed, and the run itself is only ever read
/// through its snapshot.
/// </para>
/// <para>
/// The reroll cost and the skip reward are the authored ones. There is no free-reroll count and no
/// per-draft cap anywhere in the rules layer — the reroll economy is keyed on Gold alone — so
/// nothing here reports one.
/// </para>
/// <para>
/// 🔒 <b>The three <c>DRAFT</c> counters are projected because <c>24</c> §1.1 requires it.</b> Its
/// Visibility rule is a 🔒: every counter is shown to the player, <em>always</em>, as a plain
/// sentence with a real number — <em>"a hidden pity system is indistinguishable from no pity system
/// and buys none of the goodwill it costs to build"</em> — and its Disclosure rule puts every
/// <c>N</c> in §4 on the screen its class belongs to. <c>DRAFT</c>'s screen is S07, so the numbers
/// leave <c>Core</c> here or they are not shown at all. The wording is the screen's; the arithmetic
/// is the rules layer's, because a countdown computed in a presenter is a second authority over a
/// store-policy disclosure.
/// </para>
/// <para>
/// 🔒 <b>Two of the five <c>DRAFT</c> rules carry no counter and appear in no line.</b> The Sustain
/// anti-brick is a state predicate and the Codex bias is a weight — <c>DraftGuarantees</c> says so in
/// its own remarks. Inventing a countdown for either would put a number where the rules have none.
/// </para>
/// </remarks>
public sealed class DraftView
{
    /// <summary>The effect member two perks sharing one names is read as interacting through.</summary>
    /// <remarks>
    /// The one link the authored effect data supports. <c>excludes</c> is empty on every shipped row,
    /// <c>requires</c> names a category's base perk rather than a partner, and <c>poolTags</c> carries
    /// the same token on every row a draft can offer at all — so all three would make every hint
    /// empty; a shared <c>stat</c> would make almost every pair of Offense perks a synergy and mean
    /// nothing.
    /// </remarks>
    private const string StatusMember = "statusId";

    private DraftView(
        IReadOnlyList<DraftOptionView> options,
        IReadOnlyList<DraftGuaranteeView> guarantees,
        long rerollGoldCost,
        long skipGoldReward)
    {
        Options = options;
        Guarantees = guarantees;
        RerollGoldCost = rerollGoldCost;
        SkipGoldReward = skipGoldReward;
    }

    /// <summary>The three options on offer, in the order <c>PICK_PERK</c>'s option index names them.</summary>
    public IReadOnlyList<DraftOptionView> Options { get; }

    /// <summary>
    /// The run's three <c>DRAFT</c> counters and the rung each stands against, in the priority order
    /// the guarantees fire in. Always three entries: a counter standing at zero is still shown, which
    /// is what <c>24</c> §1.1's <em>always</em> means.
    /// </summary>
    public IReadOnlyList<DraftGuaranteeView> Guarantees { get; }

    /// <summary>The Gold <c>REROLL_DRAFT</c> charges, as authored.</summary>
    public long RerollGoldCost { get; }

    /// <summary>The Gold <c>SKIP_DRAFT</c> pays, as authored.</summary>
    public long SkipGoldReward { get; }

    /// <summary>Projects the draft <paramref name="run"/> currently has open, or <c>null</c> when it has none.</summary>
    /// <param name="run">The run whose draft is being drawn.</param>
    /// <param name="content">The loaded content set — the perk catalogue, the pity registry and the draft economy.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="MissingContentException"><paramref name="content"/> is missing a document the draft draws against.</exception>
    public static DraftView? Project(RunSnapshot run, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(content);

        if (!run.DraftPending)
        {
            return null;
        }

        var standing = DraftStanding.Of(run, content);

        // Reopened at the position the run COMMITTED the stream at, and never folded back: the draws
        // this consumes belong to the offer the next command will re-derive from the same index.
        // The demand is captured rather than discarded: it is the same reading of what this run owns
        // that DraftGuarantees.Forced gates the upgrade famine on, so taking it from here is what
        // keeps the line the screen draws and the rule the handler applies from ever disagreeing
        // about whether that guarantee can fire at all.
        var options = CurrentDraft.Draw(
            standing,
            DeterministicRng.OpenAt(run.RunSeed, RngStreams.Draft, CommittedDraftPosition(run)),
            out var demand);

        var catalogue = PerkCatalogue.Read(content);
        var economy = DraftEconomyTuning.Read(content);
        var cards = new DraftOptionView[options.Count];

        for (var slot = 0; slot < cards.Length; slot++)
        {
            var perk = catalogue.Find(options[slot].PerkId);
            var newTier = options[slot].NewTier;

            cards[slot] = new DraftOptionView(
                perk.Id,
                perk.Name,
                perk.Category,
                perk.Rarity,
                perk.IconId,
                options[slot].IsUpgrade,
                newTier,
                PerkEffectText.Render(content, perk.Id, newTier),
                SynergiesWith(content, catalogue, perk.Id, newTier, standing.Owned.Tiers));
        }

        return new DraftView(
            Array.AsReadOnly(cards),
            Standing(LuckTuning.Read(content).Draft, standing.Counters, demand),
            economy.RerollGoldCost,
            economy.SkipGoldReward);
    }

    /// <summary>
    /// The three counter lines, in the order <c>DraftGuarantees.Forced</c> assigns their slots.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>The three rungs are read three different ways, and the difference is not cosmetic.</b>
    /// <c>DraftGuarantees.Forced</c> hands the Legendary pity's authored number to <c>HardPity</c>
    /// unchanged, because <c>24</c> §4.7's <em>"draft #15"</em> names the forced draft's own ordinal;
    /// it hands the quality floor's and the famine's as <c>N + 1</c>, because <em>"3 consecutive
    /// drafts ... force one into the next"</em> counts the drafts that PASS first. Copying one
    /// reading onto all three would print a number one draft out on two of the three lines — and on a
    /// disclosure a store policy requires, an off-by-one is a false statement rather than a rounding.
    /// The readings are taken from that method rather than restated from the document.
    /// </remarks>
    private static IReadOnlyList<DraftGuaranteeView> Standing(
        DraftRule rule, DraftCounters counters, DraftDemand demand) =>
        Array.AsReadOnly<DraftGuaranteeView>(
        [
            Line(
                DraftGuaranteeKind.LegendaryPity,
                counters.DraftsSinceLegendaryOffered,
                rule.LegendaryPityDraftNumber,
                live: true),
            Line(
                DraftGuaranteeKind.QualityFloor,
                counters.DraftsWithoutAboveCommon,
                rule.ConsecutiveDraftsWithoutAboveCommon + 1,
                live: true),

            // Gated on the same fact the rule is: a run whose every owned perk sits at its top tier
            // has no upgrade to be starved of, so a countdown would be promising a draft that cannot
            // arrive. The counter itself is still reported — it is standing, it is simply not due.
            Line(
                DraftGuaranteeKind.UpgradeFamine,
                counters.DraftsWithoutOwnedUpgrade,
                rule.ConsecutiveDraftsWithoutOwnedUpgrade + 1,
                demand.OwnsNonMaxedPerk),
        ]);

    /// <summary>One counter line, with its countdown derived from <c>HardPity</c>'s own contract.</summary>
    /// <remarks>
    /// <c>HardPity.Fires</c> is <c>misses &gt;= everyNth - 1</c>, so a counter standing at
    /// <c>everyNth - 1</c> means the NEXT draft is the forced one. The countdown a player reads is
    /// therefore <c>everyNth - stood</c>, and one is its floor rather than zero: a rung retuned
    /// downwards leaves counters standing above it, and <em>"in 0 drafts"</em> would describe a draft
    /// that has already happened.
    /// </remarks>
    private static DraftGuaranteeView Line(
        DraftGuaranteeKind kind, int draftsStood, int forcedOnDraft, bool live) =>
        new(kind, draftsStood, forcedOnDraft, Math.Max(1, forcedOnDraft - draftsStood), live);

    /// <summary>The <c>draft</c> stream index the run stands at. An unrecorded stream stands at zero.</summary>
    private static ulong CommittedDraftPosition(RunSnapshot run) =>
        run.RngStreamPositions is { } committed &&
        committed.TryGetValue(RngStreams.Draft, out var position)
            ? position
            : 0UL;

    /// <summary>
    /// The owned perks an option interacts with: those whose own owned tier names a status this
    /// option's tier also names, the option itself excluded.
    /// </summary>
    private static IReadOnlyList<string> SynergiesWith(
        ContentSnapshot content,
        PerkCatalogue catalogue,
        string perkId,
        int newTier,
        IReadOnlyDictionary<string, int> owned)
    {
        var offered = StatusesNamedBy(content, perkId, newTier);

        if (offered.Count == 0 || owned.Count == 0)
        {
            return [];
        }

        var sharers = new List<string>(owned.Count);

        foreach (var ownedId in owned.Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            // A card never names itself, and a run can outlive a content version that dropped a perk
            // or shortened it — neither is a synergy, and neither is a reason to throw.
            if (string.Equals(ownedId, perkId, StringComparison.Ordinal) ||
                !catalogue.Contains(ownedId) ||
                owned[ownedId] > catalogue.Find(ownedId).TierCount)
            {
                continue;
            }

            if (StatusesNamedBy(content, ownedId, owned[ownedId]).Overlaps(offered))
            {
                sharers.Add(ownedId);
            }
        }

        return Array.AsReadOnly(sharers.ToArray());
    }

    /// <summary>Every status id one tier of a perk names, anywhere in its effect tree.</summary>
    /// <remarks>
    /// The whole tree rather than the effects' top level: a status a condition tests for is as much a
    /// statement that this perk cares about it as one an op applies.
    /// </remarks>
    private static HashSet<string> StatusesNamedBy(ContentSnapshot content, string perkId, int tier)
    {
        var statuses = new HashSet<string>(StringComparer.Ordinal);

        foreach (var effect in PerkTierEffects.Of(PerkTierEffects.Row(content, perkId), perkId, tier))
        {
            CollectStatuses(effect, statuses);
        }

        return statuses;
    }

    private static void CollectStatuses(ContentValue value, HashSet<string> into)
    {
        if (value.Kind == ContentValueKind.Array)
        {
            foreach (var item in value.Items)
            {
                CollectStatuses(item, into);
            }

            return;
        }

        if (value.Kind != ContentValueKind.Object)
        {
            return;
        }

        foreach (var name in value.MemberNames)
        {
            if (!value.TryGetMember(name, out var member) || member is null)
            {
                continue;
            }

            if (string.Equals(name, StatusMember, StringComparison.Ordinal) &&
                member.Kind == ContentValueKind.Text)
            {
                into.Add(member.AsText());
            }
            else
            {
                CollectStatuses(member, into);
            }
        }
    }
}

/// <summary>One card of an open draft.</summary>
/// <param name="PerkId">The perk offered — the id <c>PICK_PERK</c> lands on the run at this index.</param>
/// <param name="Name">The perk's player-facing name.</param>
/// <param name="Category">The perk's category.</param>
/// <param name="Rarity">The perk's own rarity band, not the band the slot drew.</param>
/// <param name="IconId">The icon asset id.</param>
/// <param name="IsUpgrade">Whether taking this raises an already-owned copy rather than granting a fresh one.</param>
/// <param name="NewTier">
/// The tier taking this option lands on. The badge a card draws is composed from this and
/// <paramref name="IsUpgrade"/> by whatever renders it: the upgrade wording is a translated string,
/// so the words belong to the screen's locale table and only the two facts belong here.
/// </param>
/// <param name="EffectText">
/// The perk's sentence at <paramref name="NewTier"/> with its real numbers substituted, or the
/// tokens that stopped it. Never a half-substituted string.
/// </param>
/// <param name="SynergyPerkIds">
/// Owned perks this option interacts with: those whose own owned tier names a status this option's
/// tier also names. Empty when the data supports no such link, which is not the same as none
/// existing — it is the only interaction the authored effect data can be read for.
/// </param>
public sealed record DraftOptionView(
    string PerkId,
    string Name,
    PerkCategory Category,
    PerkRarity Rarity,
    string IconId,
    bool IsUpgrade,
    int NewTier,
    PerkEffectTextRender EffectText,
    IReadOnlyList<string> SynergyPerkIds);

/// <summary>Which of the <c>DRAFT</c> class's counters one projected line reports.</summary>
/// <remarks>
/// Three members, not five. <c>24</c> §3 gives the class three counters; the Sustain anti-brick and
/// the Codex bias are the other two rules of §4.7 and neither counts anything, so neither can be
/// counted down to.
/// </remarks>
public enum DraftGuaranteeKind
{
    /// <summary>Drafts stood since one last offered a Legendary — <c>24</c> §4.7's standing pity.</summary>
    LegendaryPity,

    /// <summary>Consecutive drafts offering nothing above Common — <c>24</c> §4.7 F1.</summary>
    QualityFloor,

    /// <summary>Consecutive drafts offering no owned-perk upgrade — <c>24</c> §4.7 F3.</summary>
    UpgradeFamine,
}

/// <summary>
/// One <c>DRAFT</c> counter as <c>24</c> §1.1 requires it shown: where it stands, the rung it is
/// counting towards, and how many drafts are left.
/// </summary>
/// <remarks>
/// Every number here is a fact about the run and the authored dials. 🔒 The sentence around them is
/// NOT: <c>24</c> §1.1 asks for <em>"a plain sentence with a real number"</em>, and the words of that
/// sentence are a translated string belonging to the screen's locale table. This record carries only
/// what a presenter cannot compute for itself.
/// </remarks>
/// <param name="Kind">Which counter this line reports.</param>
/// <param name="DraftsStood">
/// Where the counter stands right now — drafts elapsed since it last reset. Never negative, and not
/// bounded by <paramref name="ForcedOnDraft"/>: a rung retuned downwards leaves counters above it.
/// </param>
/// <param name="ForcedOnDraft">
/// The authored rung: the ordinal, counted from the counter's last reset, of the draft the guarantee
/// fires on. This is the <c>N</c> that <c>24</c> §1.1's Disclosure rule requires stated in-game.
/// </param>
/// <param name="DraftsUntilForced">
/// How many drafts including the next one before the guarantee fires. <b>One</b> means the next draft
/// is the forced one; it never reads zero.
/// </param>
/// <param name="Live">
/// Whether the guarantee can fire as the run currently stands. False only for the upgrade famine, and
/// only while the run owns no perk below its top tier — the counter stands, but nothing is due on it.
/// A line drawn as a countdown while this is false would promise a draft that cannot arrive.
/// </param>
public sealed record DraftGuaranteeView(
    DraftGuaranteeKind Kind,
    int DraftsStood,
    int ForcedOnDraft,
    int DraftsUntilForced,
    bool Live);
