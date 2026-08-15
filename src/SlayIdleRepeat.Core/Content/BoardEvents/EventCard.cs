using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Content.BoardEvents;

/// <summary>
/// 🔒 `19` Part A / `03` §5 — one in-run event card a <c>TILE_EVENT</c> can draw.
/// </summary>
/// <remarks>
/// ⚠️ <b>A <em>board</em> event, not a live-ops one.</b> <c>game-data/README.md</c>:
/// <em>"<c>board_events/</c> and <c>liveops_events/</c> are two different things that the design docs
/// both call 'events'. The first is a tile you land on mid-run; the second is a two-week live-ops
/// package."</em> This namespace is the first; `26` §2's packages are M13-06's and share nothing with
/// it but the <c>EVT_</c> prefix.
/// </remarks>
/// <param name="Id">The card id, e.g. <c>EVT_WELL</c>.</param>
/// <param name="Title">`19` Part A's Title column.</param>
/// <param name="Body">`19` Part A's Body column — the flavour line.</param>
/// <param name="MinChapter">The first chapter this card may be drawn in (`19` Part A's A1/A2/A3 bands).</param>
/// <param name="MaxChapter">The last chapter this card may be drawn in. Never below <paramref name="MinChapter"/>.</param>
/// <param name="Options">
/// The two or three options, in the document's order. 🔒 The order is the wire contract:
/// <c>EventChooseCommand.ChoiceIndex</c> names an option by its position here.
/// </param>
internal sealed record EventCard(
    string Id,
    string Title,
    string Body,
    int MinChapter,
    int MaxChapter,
    IReadOnlyList<EventOption> Options)
{
    /// <summary>Whether a run in <paramref name="chapterId"/> may draw this card.</summary>
    internal bool IsAvailableIn(int chapterId) => chapterId >= MinChapter && chapterId <= MaxChapter;
}

/// <summary>One option of an event card — what the player may choose.</summary>
/// <param name="Label">`19` Part A's bolded option text.</param>
/// <param name="CostCurrency">
/// What taking this option costs, or <c>null</c> when it is free. 🔒 Paid by the
/// <c>EVENT_CHOOSE</c> handler <b>before</b> the outcome is drawn, and refused with
/// <c>INSUFFICIENT_FUNDS</c> when the player cannot afford it — never by the resolver, which is only
/// responsible for the outcome.
/// </param>
/// <param name="CostAmount">
/// The cost's magnitude, always positive, or <c>null</c> when there is no cost. Always set exactly
/// when <paramref name="CostCurrency"/> is.
/// </param>
/// <param name="Outcomes">
/// The weighted branches. 🔒 Exactly <b>one</b> weighted layer: where `19` Part A describes a
/// guaranteed cost followed by a split, the guaranteed part is repeated in every branch's effect list
/// rather than nested, so one draw resolves one option.
/// </param>
internal sealed record EventOption(
    string Label,
    CurrencyId? CostCurrency,
    long? CostAmount,
    IReadOnlyList<EventOutcome> Outcomes);

/// <summary>One weighted branch of an option.</summary>
/// <param name="Weight">Its share of the draw off `14` §8.1's <c>events</c> stream. Always positive.</param>
/// <param name="Effects">Applied in order, all of them, once this branch is drawn. Never empty.</param>
internal sealed record EventOutcome(double Weight, IReadOnlyList<EventEffect> Effects);

/// <summary>One effect of one outcome — a row of the closed five-op vocabulary.</summary>
/// <param name="Op">Which of the five this is; every other member's meaning follows from it.</param>
/// <param name="Currency"><see cref="EventEffectOp.Currency"/> only: which currency moves.</param>
/// <param name="Amount"><see cref="EventEffectOp.Currency"/> only: the signed amount, before scaling.</param>
/// <param name="ChapterScaled">
/// <see cref="EventEffectOp.Currency"/> only: whether <paramref name="Amount"/> is multiplied by
/// <c>M(c)</c>. <c>false</c> for the other four ops, where it has no meaning.
/// </param>
/// <param name="HpPct"><see cref="EventEffectOp.HpPct"/> only: the signed share of Max HP.</param>
/// <param name="CurseId"><see cref="EventEffectOp.CurseReward"/> only: whose paired reward is paid.</param>
/// <param name="Note">
/// <see cref="EventEffectOp.Unsupported"/> only: what `19` Part A authored and which milestone owns
/// it. Written for a human reading the content file, never parsed.
/// </param>
internal sealed record EventEffect(
    EventEffectOp Op,
    CurrencyId? Currency,
    long? Amount,
    bool ChapterScaled,
    double? HpPct,
    string? CurseId,
    string? Note);

/// <summary>
/// 🔒 The closed vocabulary an event outcome's effects are drawn from. Extending it is a new resolver
/// branch, not a new content row.
/// </summary>
internal enum EventEffectOp
{
    /// <summary>Move a currency — <c>GOLD</c> on the <c>Run</c>, the other seven on the <c>Player</c>.</summary>
    Currency,

    /// <summary>Heal or cost a share of Max HP, clamped into <c>0..MaxHp</c> by the resolver.</summary>
    HpPct,

    /// <summary>
    /// ⚠️ Pay a curse's authored paired reward (`19` Part E) and <b>nothing else</b> — the curse
    /// itself is not applied or persisted. See <c>CurseRewards</c> and M3-11's <c>Curses</c> gap.
    /// </summary>
    CurseReward,

    /// <summary>A deliberate no-op — `19` Part A's "Walk away", "Leave it", "Decline".</summary>
    None,

    /// <summary>
    /// 🔒 A mechanic `19` Part A authors and <c>Core</c> cannot execute yet. The resolver applies
    /// nothing, which is correct rather than a bug: see the schema's own remarks and steering S6.
    /// </summary>
    Unsupported,
}
