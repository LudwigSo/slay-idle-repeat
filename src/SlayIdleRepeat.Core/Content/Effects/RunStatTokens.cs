namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// The one place a run-scoped tuning document's <c>stat</c> token is turned into a
/// <see cref="StatId"/> — the shrine buff pool, the shop's run buffs and the curse catalogue.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The tokens are NOT all spelled as the enum spells them, and that is the whole reason this
/// type exists.</b> <c>currencies.json</c> authors <c>DR</c> where the enum says <c>DR_PCT</c>, and
/// <c>GOLD_GAIN</c> where it says <c>GOLD_PCT</c>. An <c>Enum.TryParse</c> at each call site would
/// have silently dropped exactly those two rows — Warding Light and Gilded Tongue — leaving two of
/// the ten shrine buffs as options that look identical on screen and do nothing at all.
/// </para>
/// <para>
/// A closed alias table rather than a parse-with-fallback: a token this table does not name is
/// refused, so authoring a new stat into the shrine pool fails loudly here instead of loading
/// cleanly and contributing nothing.
/// </para>
/// </remarks>
public static class RunStatTokens
{
    /// <summary>
    /// The tokens that differ from the enum's own spelling. Everything else parses by name.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, StatId> Aliases =
        new Dictionary<string, StatId>(StringComparer.Ordinal)
        {
            ["DR"] = StatId.DR_PCT,
            ["GOLD_GAIN"] = StatId.GOLD_PCT,
            ["GOLD"] = StatId.GOLD_PCT,
            ["CRIT_CHANCE"] = StatId.CRIT,
            ["HEALING_RECEIVED"] = StatId.HEAL_PCT,
            ["SHOP_PRICE"] = StatId.SHOP_PRICE_PCT,
            ["DROP_RATE"] = StatId.DROP_CHANCE,
        };

    /// <summary>Attempts to read a run-scoped tuning document's <c>stat</c> token.</summary>
    /// <param name="token">The authored token. <c>null</c> answers <c>false</c> — a row that names no stat.</param>
    /// <param name="stat">The stat, when the token names one.</param>
    /// <returns><c>true</c> when the token names one of the 26 stats.</returns>
    public static bool TryParse(string? token, out StatId stat)
    {
        if (token is null)
        {
            stat = default;
            return false;
        }

        if (Aliases.TryGetValue(token, out stat))
        {
            return true;
        }

        // Case-SENSITIVE, like every other id comparison in this codebase: 'atk' is a different
        // token from 'ATK', and accepting both would make the document's own casing meaningless.
        return Enum.TryParse(token, ignoreCase: false, out stat) && Enum.IsDefined(stat);
    }

    /// <summary>Reads a run-scoped tuning document's <c>stat</c> token.</summary>
    /// <param name="token">The authored token.</param>
    /// <param name="reference">The content pointer, for the failure text.</param>
    /// <exception cref="InvalidTunableException">The token names none of the 26 stats.</exception>
    public static StatId Parse(string token, string reference)
    {
        if (TryParse(token, out var stat))
        {
            return stat;
        }

        throw new InvalidTunableException(
            reference,
            "'" + token + "' names none of 18 §2.1's 26 stats, and none of the aliases the run-scoped " +
            "tuning documents use for them. Refused rather than skipped: a buff or curse whose stat " +
            "could not be read would load cleanly, appear on screen with its authored magnitude, and " +
            "change nothing about the fight.");
    }
}
