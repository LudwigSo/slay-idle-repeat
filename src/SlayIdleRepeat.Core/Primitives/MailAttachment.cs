namespace SlayIdleRepeat.Core.Primitives;

/// <summary>One thing an inbox message carries: a type token and how much of it.</summary>
/// <param name="Type">
/// The attachment's type, exactly as the message row spells it — <c>SOUL_SHARDS</c>, <c>ENERGY</c>.
/// Text rather than an enum on purpose: a stored row may name a type this build does not grant, and
/// a parse that threw on one would make a single bad row unreadable for the whole inbox instead of
/// refusing the one attachment by name.
/// </param>
/// <param name="Amount">How much. Positive; <see cref="MailAttachmentKinds"/> refuses the rest.</param>
/// <remarks>
/// 🔒 There is deliberately no <c>chestClass</c>, no <c>count</c> and no item member. v1 grants
/// currencies and Energy and nothing else, and a field for a kind that cannot be granted would be a
/// shape somebody fills in before the system behind it exists — see
/// <see cref="MailAttachmentRefusal"/>, where every absent kind is named with what it is waiting for.
/// </remarks>
public sealed record MailAttachment(string Type, long Amount)
{
    /// <inheritdoc cref="MailAttachment"/>
    public string Type { get; } = IdText.Require(Type, nameof(MailAttachment));
}

/// <summary>What an attachment turns into when it is granted.</summary>
public enum MailAttachmentGrantKind
{
    /// <summary>A balance on the player's wallet.</summary>
    WALLET_CURRENCY = 1,

    /// <summary>Energy: the main bar first, the Energy Reserve for the overflow.</summary>
    ENERGY = 2,
}

/// <summary>
/// 🔒 Why an attachment cannot be granted. Every member is a named absence rather than a failure:
/// the type is real, the system that would receive it is not built, and the member says which.
/// </summary>
/// <remarks>
/// The owners are carried by the architecture suite's inbox-absence register, which expires each of
/// them against the tracker rather than against a comment here.
/// </remarks>
public enum MailAttachmentRefusal
{
    /// <summary>Nothing in the game names this type token at all.</summary>
    UNKNOWN_TYPE = 1,

    /// <summary>
    /// Gold. It is run-scoped and the player has no meta balance of it, so a grant outside a run
    /// has nowhere to land — and landing it in a run would pay a reward into state the next run
    /// discards.
    /// </summary>
    RUN_SCOPED_CURRENCY = 2,

    /// <summary>
    /// A chest, egg or crate. These claim as unopened containers onto a shelf, and the shelf is not
    /// built — pre-opening them here would read pity and Focus at claim time, which is the one thing
    /// a container must not do.
    /// </summary>
    CONTAINER_SHELF_ABSENT = 3,

    /// <summary>
    /// Gear. A rolled item needs a seeded draw, and the nightly auto-grant job is not a command, so
    /// it holds no per-command seed to draw from — a grant path that made one up would be a second,
    /// unreproducible source of loot.
    /// </summary>
    SEEDED_GEAR_GRANT_ABSENT = 4,

    /// <summary>
    /// Set Tokens or Beast Marks. They are non-wallet counters, deliberately absent from
    /// <see cref="CurrencyId"/>, and a wallet slot for them would start reporting them as income.
    /// </summary>
    NO_WALLET_HOME = 5,

    /// <summary>Zero or a negative amount. An attachment that grants nothing is an authoring fault.</summary>
    NON_POSITIVE_AMOUNT = 6,
}

/// <summary>
/// What one attachment resolves to: a grant this build can make, or the named reason it cannot.
/// </summary>
/// <remarks>
/// Exactly one of the two halves is set, and <see cref="MailAttachmentKinds.Resolve"/> is the only
/// thing that builds one — so "resolved but ungrantable" and "grantable but unresolved" are states
/// no caller can construct.
/// </remarks>
public readonly record struct MailAttachmentResolution
{
    private MailAttachmentResolution(
        MailAttachmentGrantKind? kind, CurrencyId? currency, MailAttachmentRefusal? refusal)
    {
        Kind = kind;
        Currency = currency;
        Refusal = refusal;
    }

    /// <summary>What the attachment grants, or <c>null</c> when it was refused.</summary>
    public MailAttachmentGrantKind? Kind { get; }

    /// <summary>The wallet currency, set only for <see cref="MailAttachmentGrantKind.WALLET_CURRENCY"/>.</summary>
    public CurrencyId? Currency { get; }

    /// <summary>Why it cannot be granted, or <c>null</c> when it can.</summary>
    public MailAttachmentRefusal? Refusal { get; }

    /// <summary>Whether this build can grant the attachment.</summary>
    public bool IsGrantable => Refusal is null;

    /// <summary>A wallet currency grant.</summary>
    /// <param name="currency">Which balance moves.</param>
    internal static MailAttachmentResolution Wallet(CurrencyId currency) =>
        new(MailAttachmentGrantKind.WALLET_CURRENCY, currency, refusal: null);

    /// <summary>An Energy grant.</summary>
    internal static MailAttachmentResolution Energy() =>
        new(MailAttachmentGrantKind.ENERGY, currency: null, refusal: null);

    /// <summary>A refusal, by name.</summary>
    /// <param name="refusal">Why the attachment cannot be granted.</param>
    internal static MailAttachmentResolution Refuse(MailAttachmentRefusal refusal) =>
        new(kind: null, currency: null, refusal);
}

/// <summary>
/// 🔒 The closed v1 attachment vocabulary: wallet currencies and Energy are granted, and every other
/// type the design set names is refused here by name rather than silently dropped.
/// </summary>
/// <remarks>
/// <para>
/// This is the one place the vocabulary is decided, and both gates read it: the send path validates
/// a template's attachments against it before a message is ever written, and the claim path resolves
/// each attachment again before granting. Two gates over one table, so a row that reached the store
/// by some other route still cannot pay out.
/// </para>
/// <para>
/// ⚠️ Because no v1 kind grants an item, the inbox's "held, not lost — not enough inventory space"
/// state is unreachable and is deliberately not built. It becomes reachable with the first
/// item-granting kind, which is the same commit that removes one of the refusals below.
/// </para>
/// </remarks>
public static class MailAttachmentKinds
{
    /// <summary>The Energy type token.</summary>
    public const string EnergyType = "ENERGY";

    /// <summary>
    /// Every type token this build grants, in wire order. Named so a caller can present the
    /// vocabulary without re-deriving it from the resolver.
    /// </summary>
    public static IReadOnlyList<string> Grantable { get; } = new[]
    {
        nameof(CurrencyId.CROWNS),
        nameof(CurrencyId.SOUL_SHARDS),
        EnergyType,
        nameof(CurrencyId.ENHANCE_STONES),
        nameof(CurrencyId.MERGE_DUST),
        nameof(CurrencyId.BEAST_FEED),
        nameof(CurrencyId.HONOR),
    };

    /// <summary>What one attachment grants, or the named reason it cannot.</summary>
    /// <param name="attachment">The attachment as the message row spells it.</param>
    /// <returns>A grant, or a refusal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="attachment"/> is null.</exception>
    /// <remarks>
    /// The amount is judged before the type, so an authored <c>0</c> is reported as the authoring
    /// fault it is rather than as whichever type happened to be beside it.
    /// </remarks>
    public static MailAttachmentResolution Resolve(MailAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        if (attachment.Amount <= 0)
        {
            return MailAttachmentResolution.Refuse(MailAttachmentRefusal.NON_POSITIVE_AMOUNT);
        }

        return attachment.Type switch
        {
            EnergyType => MailAttachmentResolution.Energy(),

            nameof(CurrencyId.CROWNS) => MailAttachmentResolution.Wallet(CurrencyId.CROWNS),
            nameof(CurrencyId.SOUL_SHARDS) => MailAttachmentResolution.Wallet(CurrencyId.SOUL_SHARDS),
            nameof(CurrencyId.ENHANCE_STONES) => MailAttachmentResolution.Wallet(CurrencyId.ENHANCE_STONES),
            nameof(CurrencyId.MERGE_DUST) => MailAttachmentResolution.Wallet(CurrencyId.MERGE_DUST),
            nameof(CurrencyId.BEAST_FEED) => MailAttachmentResolution.Wallet(CurrencyId.BEAST_FEED),
            nameof(CurrencyId.HONOR) => MailAttachmentResolution.Wallet(CurrencyId.HONOR),

            nameof(CurrencyId.GOLD) =>
                MailAttachmentResolution.Refuse(MailAttachmentRefusal.RUN_SCOPED_CURRENCY),

            "CHEST" or "EGG" or "CRATE" =>
                MailAttachmentResolution.Refuse(MailAttachmentRefusal.CONTAINER_SHELF_ABSENT),

            "GEAR" =>
                MailAttachmentResolution.Refuse(MailAttachmentRefusal.SEEDED_GEAR_GRANT_ABSENT),

            "SET_TOKENS" or "BEAST_MARKS" =>
                MailAttachmentResolution.Refuse(MailAttachmentRefusal.NO_WALLET_HOME),

            _ => MailAttachmentResolution.Refuse(MailAttachmentRefusal.UNKNOWN_TYPE),
        };
    }
}
