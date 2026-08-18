using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Model.Snapshots;

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
/// </remarks>
public sealed class DraftView
{
    private DraftView(IReadOnlyList<DraftOptionView> options, long rerollGoldCost, long skipGoldReward)
    {
        Options = options;
        RerollGoldCost = rerollGoldCost;
        SkipGoldReward = skipGoldReward;
    }

    /// <summary>The three options on offer, in the order <c>PICK_PERK</c>'s option index names them.</summary>
    public IReadOnlyList<DraftOptionView> Options { get; }

    /// <summary>The Gold <c>REROLL_DRAFT</c> charges, as authored.</summary>
    public long RerollGoldCost { get; }

    /// <summary>The Gold <c>SKIP_DRAFT</c> pays, as authored.</summary>
    public long SkipGoldReward { get; }

    /// <summary>Projects the draft <paramref name="run"/> currently has open, or <c>null</c> when it has none.</summary>
    /// <param name="run">The run whose draft is being drawn.</param>
    /// <param name="content">The loaded content set — the perk catalogue, the pity registry and the draft economy.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="MissingContentException"><paramref name="content"/> is missing a document the draft draws against.</exception>
    public static DraftView? Project(RunSnapshot run, ContentSnapshot content) =>
        throw new NotImplementedException(
            "M7-07 phase 1 skeleton: the projection is written against the failing cases in " +
            "DraftViewTests and filled in by the implementation phase.");
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
