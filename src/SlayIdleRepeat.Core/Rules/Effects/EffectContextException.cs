namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// 🔒 The one failure type of the `18` §4/§5 evaluation layer, covering three classes: a token asked
/// to resolve against a context that does not carry its subject; a term or tree that is malformed
/// whatever the state is; and the one token that is declared but deliberately not resolved here.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The uniform rule, stated once.</b> `18` §5 authors a degradation for exactly two of its
/// eleven targets, and `18` §4 authors a default for exactly three of its twenty-three conditions:
/// </para>
/// <list type="bullet">
///   <item><c>OTHER_ENEMIES</c> outside an attack context <b>degrades</b> to <c>ALL_ENEMIES</c> (§5).</item>
///   <item><c>OWNER</c> on an actor that is not a summon is <b>skipped</b> — the empty set (§5).</item>
///   <item><c>ATTACKER_IS_ELITE</c>/<c>_BOSS</c>/<c>_SUMMON</c> outside an attacker context are
///   <b>false</b> (§4).</item>
/// </list>
/// <para>
/// For everything else the documents author nothing, and steering S6 is explicit that a hole is not
/// to be filled with a plausible value and <em>"never coerce such a hole to a default at read time;
/// fail loudly"</em>. So:
/// </para>
/// <para>
/// 🔒 <b>A token whose SUBJECT is absent from the context throws this. A token whose subject is
/// present but whose SET is empty resolves to the empty set.</b>
/// </para>
/// <para>
/// The distinction is the whole point. <c>ALL_ENEMIES</c> in a battle whose last enemy just died is
/// an empty set and a correct answer. <c>ATTACKER</c> in a context with no attacker is a question
/// with no answer, and returning the empty set there would be a silent no-op indistinguishable from a
/// hit that landed on nobody — the worst of the three available behaviours, because nothing ever goes
/// red.
/// </para>
/// <para>
/// 🔒 <b>The second class: malformed content.</b> A comparison with nothing to compare against, a
/// <c>not</c> carrying two operands, an <c>all</c> carrying none, an inverted range, an ordering
/// comparator applied to a boolean, a token outside `18` §11's counts — none of these depends on the
/// state at all. <see cref="Token"/> then names the comparator or the node kind that ruled the term
/// malformed rather than a `18` §4 function, which is still the rule that fired (steering S2).
/// </para>
/// <para>
/// 🔒 <b>The third: <c>EffectTarget.RUN</c>.</b> `18` §5 declares it and `18` §2.5 gives its resolver
/// to the run controller, which is M3's. Neither an absent subject nor malformed content — a token
/// that is somebody else's to answer, saying so rather than resolving to nobody.
/// </para>
/// <para>
/// Every message begins with <see cref="Marker"/> and names <see cref="Token"/>, so a test can pin
/// <em>which</em> token failed rather than merely that something did (steering S2), and so the whole
/// class of decision is greppable from one string.
/// </para>
/// <para>
/// ⚠️ <b>Deliberately outside <c>Content.ContentException</c>'s hierarchy.</b> That root is scoped by
/// its own summary to <em>"every fault raised while READING a <c>ContentSnapshot</c>"</em>, and only
/// one of the three classes above is a content fault at all — an absent attacker and an unwired
/// <c>RUN</c> are facts about the evaluation context, not about the data. Widening
/// <c>ContentException</c> to cover them would cost it the precision that makes it useful. M2-02's
/// resolver wanting one <c>catch</c> at the effect boundary is served by this type being the
/// layer's only one.
/// </para>
/// </remarks>
internal sealed class EffectContextException : InvalidOperationException
{
    /// <summary>🔒 The stable prefix every message carries. Grep for it to find every ruling of this kind.</summary>
    internal const string Marker = "EFFECT CONTEXT";

    /// <summary>Raises the failure for a token, naming what was missing and the clause that rules it.</summary>
    /// <param name="token">
    /// The `18` §4 condition function or `18` §5 target that could not resolve, spelled exactly as the
    /// DSL spells it.
    /// </param>
    /// <param name="missing">What the context did not carry, in the reader's terms.</param>
    /// <param name="reference">
    /// Why this is a throw rather than a default — the document clause that authors no answer, or the
    /// milestone that owns the resolver.
    /// </param>
    internal EffectContextException(string token, string missing, string reference)
        : base($"{Marker}: '{token}' cannot resolve — {missing}. {reference}")
    {
        Token = token;
    }

    /// <summary>The token that could not resolve. Pins which rule fired (steering S2).</summary>
    internal string Token { get; }
}
