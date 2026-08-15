using System.Globalization;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Content.BoardEvents;

/// <summary>
/// 🔒 `19` Part A — the thirty in-run event cards, read out of
/// <c>content/board_events/board_events.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Read per command out of the command's own <c>ContentSnapshot</c>, never cached statically — the
/// same shape <see cref="MinigameRewardTuning"/> is read in: `30` §3 versions the snapshot per
/// command, so a static cache would serve one command's content to another.
/// </para>
/// <para>
/// ⚠️ <b>Every refusal is a throw, not a rejection.</b> Malformed content is a build-time failure
/// `14` §6 already catches in the Application layer's schema validation; a card that reached this
/// reader broken is a data set that should never have shipped, not a player asking for something
/// illegal.
/// </para>
/// </remarks>
internal sealed class EventCatalogue
{
    /// <summary>The document `19` Part A is transcribed into.</summary>
    internal const string DocumentPath = "content/board_events/board_events.json";

    /// <summary>`19` Part A — the thirty cards.</summary>
    internal const string CardsReference = DocumentPath + "#/cards";

    private readonly IReadOnlyDictionary<string, EventCard> _byId;

    private EventCatalogue(IReadOnlyList<EventCard> all, IReadOnlyDictionary<string, EventCard> byId)
    {
        All = all;
        _byId = byId;
    }

    /// <summary>Every authored card, in the document's order.</summary>
    /// <remarks>
    /// 🔒 The order is load-bearing: <c>EventTileResolver.DrawCard</c> draws by index over the
    /// chapter-eligible subset of this list, so re-ordering the file changes which card every
    /// existing run seed draws.
    /// </remarks>
    internal IReadOnlyList<EventCard> All { get; }

    /// <summary>The card with this id.</summary>
    /// <exception cref="ArgumentException">No card carries this id.</exception>
    internal EventCard Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (_byId.TryGetValue(id, out var card))
        {
            return card;
        }

        throw new ArgumentException(
            "19 Part A authors no event card '" + id + "'. A card id reaching here that the " +
            "catalogue does not carry means a Run persisted a pending card id that this content " +
            "version no longer has — a content rollback across a live run, not a player input.",
            nameof(id));
    }

    /// <summary>The cards a run in <paramref name="chapterId"/> may draw, in the document's order.</summary>
    /// <param name="chapterId">`02` §1's chapter, from 1.</param>
    /// <remarks>
    /// A chapter below 1 is refused rather than answered with an empty list — the same line
    /// <c>CurseTuning.AvailableFrom</c> draws, and for the same reason: an empty answer would reach
    /// <c>EventTileResolver.DrawCard</c> as "the content file lost a band", which is a different and
    /// much more alarming defect than the caller having asked about a chapter that does not exist.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="chapterId"/> is below 1.</exception>
    internal IReadOnlyList<EventCard> AvailableIn(int chapterId)
    {
        if (chapterId < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chapterId),
                chapterId,
                "02 §1 runs chapters from 1 and chapter.schema.json sets \"minimum\": 1. ⚠️ There is " +
                "deliberately no upper bound: content/chapters/ holds chapters 1-2 today and 3-8 are " +
                "M11-02's, so a ceiling here would be a content bound in code (21 §3.1).");
        }

        var eligible = new List<EventCard>(All.Count);

        foreach (var card in All)
        {
            if (card.IsAvailableIn(chapterId))
            {
                eligible.Add(card);
            }
        }

        return eligible;
    }

    /// <summary>Reads the card catalogue. Throws rather than defaulting on anything unusable.</summary>
    /// <param name="content">The version-stamped snapshot the command is reading (`30` §3).</param>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">A pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">A leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static EventCatalogue Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var array = content.Read(CardsReference);
        if (array.Kind != ContentValueKind.Array || array.Items.Count == 0)
        {
            throw new InvalidTunableException(
                CardsReference,
                "19 Part A authors a non-empty card list (thirty as shipped). This document " +
                "authors " + array + ".");
        }

        var cards = new EventCard[array.Items.Count];
        var byId = new Dictionary<string, EventCard>(array.Items.Count, StringComparer.Ordinal);

        for (var i = 0; i < array.Items.Count; i++)
        {
            var card = ReadCard(array.Items[i], CardsReference + "/" + Text(i));

            if (!byId.TryAdd(card.Id, card))
            {
                throw new InvalidTunableException(
                    CardsReference + "/" + Text(i) + "/id",
                    "'" + card.Id + "' is authored twice. 14 §6 makes a duplicate id a build " +
                    "failure: the second card would be drawn twice as often as every other and the " +
                    "first would be unreachable by id — which is how EVENT_CHOOSE finds it.");
            }

            cards[i] = card;
        }

        return new EventCatalogue(Array.AsReadOnly(cards), byId);
    }

    private static EventCard ReadCard(ContentValue entry, string pointer)
    {
        var id = RequiredText(entry, "id", pointer);
        var title = RequiredText(entry, "title", pointer);
        var body = RequiredText(entry, "body", pointer);

        var minChapter = Member(entry, "minChapter", pointer).AsInt32(pointer + "/minChapter");
        var maxChapter = Member(entry, "maxChapter", pointer).AsInt32(pointer + "/maxChapter");

        if (minChapter < 1)
        {
            throw new InvalidTunableException(
                pointer + "/minChapter",
                "02 §1 runs chapters from 1; '" + id + "' opens at " + Text(minChapter) + ".");
        }

        if (maxChapter < minChapter)
        {
            throw new InvalidTunableException(
                pointer + "/maxChapter",
                "'" + id + "' closes at chapter " + Text(maxChapter) + " but opens at " +
                Text(minChapter) + ", so no chapter can draw it at all. 19 Part A's bands are " +
                "1-3, 3-6 and 6-8.");
        }

        var optionsValue = Member(entry, "options", pointer);
        if (optionsValue.Kind != ContentValueKind.Array || optionsValue.Items.Count == 0)
        {
            throw new InvalidTunableException(
                pointer + "/options",
                "'" + id + "' offers no options, so EVENT_CHOOSE could never resolve it. 19 Part A " +
                "gives every card two or three.");
        }

        var options = new EventOption[optionsValue.Items.Count];
        for (var i = 0; i < optionsValue.Items.Count; i++)
        {
            options[i] = ReadOption(optionsValue.Items[i], pointer + "/options/" + Text(i), id);
        }

        return new EventCard(id, title, body, minChapter, maxChapter, Array.AsReadOnly(options));
    }

    private static EventOption ReadOption(ContentValue entry, string pointer, string cardId)
    {
        var label = RequiredText(entry, "label", pointer);

        CurrencyId? costCurrency = null;
        long? costAmount = null;

        if (entry.TryGetMember("cost", out var cost) && cost is not null && !cost.IsUnauthorised)
        {
            var costPointer = pointer + "/cost";
            costCurrency = ParseCurrency(RequiredText(cost, "currency", costPointer), costPointer + "/currency");
            costAmount = Member(cost, "amount", costPointer).AsInt64(costPointer + "/amount");

            if (costAmount <= 0)
            {
                throw new InvalidTunableException(
                    costPointer + "/amount",
                    "A cost is authored as a positive magnitude — its direction is the field's " +
                    "meaning, not its sign, and the handler debits it. '" + cardId + "' authors " +
                    Text(costAmount.Value) + ".");
            }
        }

        var outcomesValue = Member(entry, "outcomes", pointer);
        if (outcomesValue.Kind != ContentValueKind.Array || outcomesValue.Items.Count == 0)
        {
            throw new InvalidTunableException(
                pointer + "/outcomes",
                "'" + label + "' on '" + cardId + "' has no outcomes, so choosing it would resolve " +
                "to nothing at all. An option that deliberately does nothing carries one outcome " +
                "with a NONE effect, which says so.");
        }

        var outcomes = new EventOutcome[outcomesValue.Items.Count];
        var totalWeight = 0.0;

        for (var i = 0; i < outcomesValue.Items.Count; i++)
        {
            outcomes[i] = ReadOutcome(outcomesValue.Items[i], pointer + "/outcomes/" + Text(i), cardId);
            totalWeight += outcomes[i].Weight;
        }

        if (totalWeight <= 0.0)
        {
            throw new InvalidTunableException(
                pointer + "/outcomes",
                "Every outcome of '" + label + "' on '" + cardId + "' has weight zero, so no " +
                "outcome can be drawn.");
        }

        return new EventOption(label, costCurrency, costAmount, Array.AsReadOnly(outcomes));
    }

    private static EventOutcome ReadOutcome(ContentValue entry, string pointer, string cardId)
    {
        var weight = Member(entry, "w", pointer).AsDouble(pointer + "/w");

        if (!double.IsFinite(weight) || weight <= 0.0)
        {
            throw new InvalidTunableException(
                pointer + "/w",
                "An outcome weight is a finite positive number — DeterministicRng.WeightedPick's " +
                "strict walk makes a zero-weight row unreachable wherever it sits, so a " +
                "zero-weighted outcome on '" + cardId + "' is a branch nobody can ever see. This " +
                "document authors " + Text(weight) + ".");
        }

        var effectsValue = Member(entry, "effects", pointer);
        if (effectsValue.Kind != ContentValueKind.Array || effectsValue.Items.Count == 0)
        {
            throw new InvalidTunableException(
                pointer + "/effects",
                "An outcome with no effects is indistinguishable from one nobody finished " +
                "authoring. A deliberate no-op carries a single NONE effect, which says so.");
        }

        var effects = new EventEffect[effectsValue.Items.Count];
        for (var i = 0; i < effectsValue.Items.Count; i++)
        {
            effects[i] = ReadEffect(effectsValue.Items[i], pointer + "/effects/" + Text(i));
        }

        return new EventOutcome(weight, Array.AsReadOnly(effects));
    }

    private static EventEffect ReadEffect(ContentValue entry, string pointer)
    {
        var op = RequiredText(entry, "op", pointer);

        switch (op)
        {
            case "CURRENCY":
            {
                var amount = Member(entry, "amount", pointer).AsInt64(pointer + "/amount");

                // 🔒 Zero is refused for the same reason an empty `effects` array is, three methods
                // below: EventTileResolver.Move skips a zero delta (a CurrencyChanged of 0 would be
                // a misleading row in 21 §8.3's attribution log), so a zero-amount effect resolves
                // to nothing at all and is indistinguishable from a row nobody finished authoring.
                // A deliberate no-op is a NONE effect, which says so.
                if (amount == 0)
                {
                    throw new InvalidTunableException(
                        pointer + "/amount",
                        "A CURRENCY effect moves a non-zero amount — its sign is the direction, a " +
                        "grant or a charge. A zero is dropped by the resolver and would leave an " +
                        "outcome that looks authored and does nothing; a deliberate no-op carries a " +
                        "NONE effect instead.");
                }

                return new EventEffect(
                    EventEffectOp.Currency,
                    ParseCurrency(RequiredText(entry, "currency", pointer), pointer + "/currency"),
                    amount,
                    Member(entry, "chapterScaled", pointer).AsBoolean(pointer + "/chapterScaled"),
                    HpPct: null,
                    CurseId: null,
                    Note: null);
            }

            case "HP_PCT":
            {
                var amount = Member(entry, "amount", pointer).AsDouble(pointer + "/amount");
                if (!double.IsFinite(amount) || amount is < -1.0 or > 1.0)
                {
                    throw new InvalidTunableException(
                        pointer + "/amount",
                        "An HP_PCT is a share of Max HP in [-1,1]: -1 is a full bar spent and 1 a " +
                        "full bar healed. This document authors " + Text(amount) + ".");
                }

                return new EventEffect(
                    EventEffectOp.HpPct, Currency: null, Amount: null, ChapterScaled: false,
                    amount, CurseId: null, Note: null);
            }

            case "CURSE_REWARD":
            {
                var curseId = RequiredText(entry, "curseId", pointer);

                if (!CurseRewards.IsPayable(curseId))
                {
                    throw new InvalidTunableException(
                        pointer + "/curseId",
                        "'" + curseId + "' has no payable reward. ⚠️ CurseRewards carries a narrow, " +
                        "named table for exactly the four chapter-1 curses whose 19 Part E reward " +
                        "is a flat currency amount; the other eight pay percentages and gear-drop " +
                        "chances no system exists to grant. An event card that needs one of those " +
                        "authors an UNSUPPORTED effect instead, which says so.");
                }

                return new EventEffect(
                    EventEffectOp.CurseReward, Currency: null, Amount: null, ChapterScaled: false,
                    HpPct: null, curseId, Note: null);
            }

            case "NONE":
                return new EventEffect(
                    EventEffectOp.None, Currency: null, Amount: null, ChapterScaled: false,
                    HpPct: null, CurseId: null, Note: null);

            case "UNSUPPORTED":
                return new EventEffect(
                    EventEffectOp.Unsupported, Currency: null, Amount: null, ChapterScaled: false,
                    HpPct: null, CurseId: null, RequiredText(entry, "note", pointer));

            default:
                throw new InvalidTunableException(
                    pointer + "/op",
                    "'" + op + "' is not one of the five ops board_events.schema.json authors " +
                    "(CURRENCY, HP_PCT, CURSE_REWARD, NONE, UNSUPPORTED). The vocabulary is closed: " +
                    "a new op is a new resolver branch, not a new content row.");
        }
    }

    private static CurrencyId ParseCurrency(string name, string reference) => name switch
    {
        "GOLD" => CurrencyId.GOLD,
        "CROWNS" => CurrencyId.CROWNS,
        "SOUL_SHARDS" => CurrencyId.SOUL_SHARDS,
        "ENHANCE_STONES" => CurrencyId.ENHANCE_STONES,
        "MERGE_DUST" => CurrencyId.MERGE_DUST,
        "BEAST_FEED" => CurrencyId.BEAST_FEED,
        "HONOR" => CurrencyId.HONOR,
        _ => throw new InvalidTunableException(
            reference,
            "'" + name + "' is not a currency an event card may move. 10 §1 fixes eight currencies " +
            "and ENERGY is deliberately excluded here: its two banks move through Player.SetEnergy " +
            "rather than a wallet row, so a card granting it would need a seam this resolver does " +
            "not have."),
    };

    private static string RequiredText(ContentValue obj, string name, string pointer)
    {
        var reference = pointer + "/" + name;
        var text = Member(obj, name, pointer).AsText(reference);

        return string.IsNullOrWhiteSpace(text)
            ? throw new InvalidTunableException(reference, "'" + name + "' must not be blank.")
            : text;
    }

    private static ContentValue Member(ContentValue obj, string name, string pointer) =>
        obj.TryGetMember(name, out var value) && value is not null
            ? value
            : throw new MissingContentException(pointer + "/" + name, "'" + name + "' resolves to nothing");

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(double value) => value.ToString(CultureInfo.InvariantCulture);
}
