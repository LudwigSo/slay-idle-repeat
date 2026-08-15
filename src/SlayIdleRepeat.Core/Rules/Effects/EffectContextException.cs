namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// The one failure type of the effect evaluation layer: a token whose subject the context doesn't
/// carry, a malformed term/tree, or the one token deliberately left unresolved here.
/// </summary>
/// <remarks>
/// <para>
/// A token whose subject is absent from the context throws this; a token whose subject is present
/// but whose result set is empty resolves to the empty set instead. E.g. <c>ALL_ENEMIES</c> in a
/// battle whose last enemy just died is a correct empty answer, but <c>ATTACKER</c> in a context
/// with no attacker is a question with no answer — returning empty there would be a silent no-op
/// indistinguishable from a hit that landed on nobody.
/// </para>
/// <para>
/// <c>EffectTarget.RUN</c> is declared by the DSL but resolved by the run controller, not here — a
/// token that's somebody else's to answer, saying so rather than resolving to nobody.
/// </para>
/// <para>
/// Deliberately outside <c>Content.ContentException</c>'s hierarchy: that root covers faults from
/// reading a <c>ContentSnapshot</c>, and an absent attacker or unresolved <c>RUN</c> are facts about
/// the evaluation context, not about the data.
/// </para>
/// </remarks>
internal sealed class EffectContextException : InvalidOperationException
{
    /// <summary>The stable prefix every message carries, for greppability.</summary>
    internal const string Marker = "EFFECT CONTEXT";

    /// <summary>Raises the failure for a token, naming what was missing and the clause that rules it.</summary>
    /// <param name="token">The condition function or target that could not resolve, spelled as the DSL spells it.</param>
    /// <param name="missing">What the context did not carry, in the reader's terms.</param>
    /// <param name="reference">Why this is a throw rather than a default.</param>
    internal EffectContextException(string token, string missing, string reference)
        : base($"{Marker}: '{token}' cannot resolve — {missing}. {reference}")
    {
        Token = token;
    }

    /// <summary>The token that could not resolve.</summary>
    internal string Token { get; }
}
