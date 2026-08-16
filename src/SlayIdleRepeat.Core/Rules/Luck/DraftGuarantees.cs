using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Perks;

namespace SlayIdleRepeat.Core.Rules.Luck;

/// <summary>Which of the <c>DRAFT</c> class's rules forced an option into a draft.</summary>
/// <remarks>
/// Reported rather than inferred. Five rules can each floor the pool of a slot, and several can fire
/// at once — so "an option was forced" is not an answer any caller, test or analytics stream can act
/// on. Naming the rule is what makes each one separately checkable.
/// </remarks>
internal enum DraftGuarantee
{
    /// <summary>No Legendary offered by the draft before the authored one.</summary>
    LegendaryPity,

    /// <summary>No Sustain perk owned once the run is past Stage 2.</summary>
    SustainAntiBrick,

    /// <summary>Nothing above Common offered for the authored run of drafts.</summary>
    QualityFloor,

    /// <summary>No owned-perk upgrade offered for the authored run of drafts.</summary>
    UpgradeFamine,
}

/// <summary>The run-scoped <c>DRAFT</c> counters, as they stand before a draft is drawn.</summary>
/// <remarks>
/// Three plain integers rather than entries in the player counter map: the class is scoped per run
/// and authors no counter key at all, so there is no id to form and nothing to store on the profile.
/// The anti-brick and the Codex bias carry no counter — one is a state predicate, the other a weight.
/// </remarks>
/// <param name="DraftsSinceLegendaryOffered">Drafts drawn since one last offered a Legendary.</param>
/// <param name="DraftsWithoutAboveCommon">Consecutive drafts offering nothing above Common.</param>
/// <param name="DraftsWithoutOwnedUpgrade">Consecutive drafts offering no owned-perk upgrade.</param>
internal readonly record struct DraftCounters(
    int DraftsSinceLegendaryOffered,
    int DraftsWithoutAboveCommon,
    int DraftsWithoutOwnedUpgrade)
{
    /// <summary>A run that has drafted nothing yet.</summary>
    internal static DraftCounters Unstarted => new(0, 0, 0);
}

/// <summary>Everything about the run a <c>DRAFT</c> guarantee decides against, beyond the counters.</summary>
/// <param name="Stage">The stage the battle that opened this draft belonged to: 1, 2 or 3.</param>
/// <param name="IsBoss">Whether that battle was the Boss, which belongs to no stage.</param>
/// <param name="OwnsSustainPerk">Whether the run already holds a perk in the Sustain category.</param>
/// <param name="OwnsNonMaxedPerk">Whether the run holds at least one perk below its max tier.</param>
internal readonly record struct DraftDemand(
    int Stage, bool IsBoss, bool OwnsSustainPerk, bool OwnsNonMaxedPerk);

/// <summary>One slot of a draft that a named guarantee has floored.</summary>
/// <remarks>
/// A slot index rather than a bare rarity, because two guarantees firing on one draft must be paid
/// in two different options — a single forced slot would let the higher-priority rule swallow the
/// lower one's obligation and leave its counter unreset.
/// </remarks>
/// <param name="SlotIndex">The option slot this force applies to. Zero-based.</param>
/// <param name="Guarantee">Which rule fired.</param>
/// <param name="RarityAtLeast">The band the slot's pool is floored at, or <c>null</c> where the rule states no band.</param>
/// <param name="Category">The category the slot's pool is narrowed to, or <c>null</c> where the rule states none.</param>
/// <param name="RequiresOwnedUpgrade">Whether the slot must be drawn from the owned-but-not-maxed pool.</param>
internal readonly record struct DraftForce(
    int SlotIndex,
    DraftGuarantee Guarantee,
    PerkRarity? RarityAtLeast,
    PerkCategory? Category,
    bool RequiresOwnedUpgrade);

/// <summary>What one draft actually offered, as the counters read it.</summary>
/// <param name="OfferedLegendary">At least one option was a Legendary.</param>
/// <param name="OfferedAboveCommon">At least one option was above Common.</param>
/// <param name="OfferedOwnedUpgrade">At least one option upgraded an already-owned perk.</param>
internal readonly record struct DraftOffering(
    bool OfferedLegendary, bool OfferedAboveCommon, bool OfferedOwnedUpgrade);

/// <summary>
/// The five <c>DRAFT</c> rules' decision, stated once. Reached only through <c>LuckService</c>.
/// </summary>
/// <remarks>
/// <para>
/// The class states its protection as composition rules rather than as a rarity ladder, so it has no
/// weighted table to floor and does not go through the ladder path at all. It calls the same hard
/// guarantee primitive the ladder path does, from inside this namespace, which is what keeps the
/// decision in one place.
/// </para>
/// <para>
/// Stateless: it takes the counters and answers what they demand, exactly as the ladder path does.
/// </para>
/// </remarks>
internal static class DraftGuarantees
{
    /// <summary>
    /// The stage a run is past when the anti-brick becomes due. Prose in the design documents, not an
    /// authored dial, so it is a constant here rather than a tunable nobody wrote.
    /// </summary>
    private const int LastStageBeforeAntiBrick = 2;

    /// <summary>
    /// Which guarantees fire on the draft about to be drawn, assigned to distinct slots in a fixed
    /// priority order starting at slot 0.
    /// </summary>
    /// <remarks>
    /// Distinct slots so a draft that owes both a Legendary and a Sustain option pays both. Priority,
    /// highest first: Legendary pity, the anti-brick, the quality floor, the upgrade famine. More
    /// guarantees than slots is possible in principle; the lowest-priority ones simply go unpaid this
    /// draft and their counters stay standing.
    /// </remarks>
    /// <param name="rule">The authored dials.</param>
    /// <param name="counters">The run's counters before this draft.</param>
    /// <param name="demand">What the run owns and where it stands.</param>
    /// <param name="optionCount">How many options this draft offers.</param>
    /// <returns>The forced slots, in priority order. Empty when nothing fires.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="optionCount"/> is below 1, or a counter is negative.</exception>
    internal static IReadOnlyList<DraftForce> Forced(
        DraftRule rule, DraftCounters counters, DraftDemand demand, int optionCount) =>
        throw new NotImplementedException(
            "M4-01b Phase 3 owns this body. The five 24 §4.7 rules' decision goes here, and nowhere " +
            "else: the draft engine composes against the forces this answers rather than restating " +
            "any of them.");

    /// <summary>The counters after a draft whose offering is known.</summary>
    /// <remarks>
    /// A draft that offered a Legendary resets the Legendary counter whether or not pity forced it,
    /// on the same argument the ladder path makes: overshooting a guarantee is satisfying it.
    /// </remarks>
    /// <param name="counters">The counters before the draft.</param>
    /// <param name="offering">What the draft offered.</param>
    /// <returns>The counters to store.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A counter is negative.</exception>
    /// <exception cref="OverflowException">A counter would pass <see cref="int.MaxValue"/>.</exception>
    internal static DraftCounters Moved(DraftCounters counters, DraftOffering offering) =>
        throw new NotImplementedException("M4-01b Phase 3 owns this body.");

    /// <summary>
    /// The weight multiplier the Codex bias puts on one perk in the fresh-pool draw.
    /// </summary>
    /// <remarks>
    /// ⚠️ The rule is exact; its input is not. A player-lifetime Codex does not exist yet — <b>M4-11</b>
    /// owns it — so the only "ever drafted" set available today is the run's own drafted perks, a
    /// genuine subset. The rule is therefore correctly implemented against an incomplete input rather
    /// than mis-implemented, and it widens the day M4-11 lands without this body changing.
    /// </remarks>
    /// <param name="rule">The authored dials.</param>
    /// <param name="everDrafted">Whether this perk is known to have been drafted before.</param>
    /// <returns>The multiplier. Never below 1.</returns>
    internal static double CodexWeight(DraftRule rule, bool everDrafted) =>
        throw new NotImplementedException("M4-01b Phase 3 owns this body.");

    /// <summary>
    /// Whether the anti-brick is due: the run holds no Sustain perk and is past Stage 2.
    /// </summary>
    /// <remarks>
    /// Split out from <see cref="Forced"/> so the state predicate — the half that is not a counter —
    /// is separately checkable, and so the stage reading lives beside the constant it uses.
    /// </remarks>
    /// <param name="rule">The authored block. A disabled block is never due.</param>
    /// <param name="demand">What the run owns and where it stands.</param>
    /// <returns><see langword="true"/> when the next draft owes a Sustain option.</returns>
    internal static bool AntiBrickDue(SustainAntiBrickRule rule, DraftDemand demand) =>
        throw new NotImplementedException(
            "M4-01b Phase 3 owns this body: due when the rule is enabled, no Sustain perk is owned, " +
            "and the draft is past " + nameof(LastStageBeforeAntiBrick) + " or is the Boss draft.");
}
