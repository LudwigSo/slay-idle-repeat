namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// 🔒 The one failure of the `18` §4/§5 evaluation layer: a token was asked to resolve against a
/// context that does not carry its subject.
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
/// Every message begins with <see cref="Marker"/> and names <see cref="Token"/>, so a test can pin
/// <em>which</em> token failed rather than merely that something did (steering S2), and so the whole
/// class of decision is greppable from one string.
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
