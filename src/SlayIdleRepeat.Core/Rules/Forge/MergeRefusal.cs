namespace SlayIdleRepeat.Core.Rules.Forge;

/// <summary>Why a fusion is not a legal fusion.</summary>
/// <remarks>
/// 🔒 <b>Named rather than collapsed to one "illegal merge".</b> Seven independent rules can refuse
/// a fusion, and every one of them maps onto the same wire rejection — so a caller holding only the
/// wire reason cannot tell them apart, and neither can a test. Naming each one is what lets the tests
/// pin <em>which</em> rule fired instead of merely that something did, and what lets a client tell
/// the player the one thing that is actually wrong with their selection.
/// <para>
/// The rules a fusion can also fail on that are <b>not</b> here are the ones about the player rather
/// than the selection — an id they do not own, an item in overflow, a locked item, a wallet that
/// does not cover the price. Those are the container's and the wallet's answers, and the handler
/// asks them; restating them here would put two owners on one refusal.
/// </para>
/// </remarks>
internal enum MergeRefusal
{
    /// <summary>The selection does not add up to the authored input count, dust included.</summary>
    WRONG_INPUT_COUNT = 1,

    /// <summary>Merge Dust was offered for more slots than the document lets it fill.</summary>
    DUST_SUBSTITUTION_NOT_ALLOWED = 2,

    /// <summary>The same instance was named twice, which would fuse an item with itself.</summary>
    DUPLICATE_INPUT = 3,

    /// <summary>The inputs are not all the same base item.</summary>
    MISMATCHED_ITEM = 4,

    /// <summary>The inputs are not all of the same band.</summary>
    MISMATCHED_RARITY = 5,

    /// <summary>The inputs are not all at the same enhancement level.</summary>
    MISMATCHED_ENHANCE_LEVEL = 6,

    /// <summary>The inputs are already at the top of the ladder, so there is no band to fuse onto.</summary>
    NO_HIGHER_RARITY = 7,
}
