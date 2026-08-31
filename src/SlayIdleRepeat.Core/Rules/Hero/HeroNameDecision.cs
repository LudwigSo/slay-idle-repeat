using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Hero;

/// <summary>What the name filter decided about one candidate, as a caller outside Core sees it.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>It deliberately does not carry the matched term.</b> The verdict inside Core does, because a
/// diagnosis there is read by whoever curates the word lists. This one crosses an API boundary and
/// ends up in a response body, a log line and a client toast — and the term it would carry is the slur
/// the filter exists to refuse. Which check fired is the whole answer a caller is owed.
/// </para>
/// <para>
/// ⚠️ The constructor is <c>internal</c> and the type is deliberately not positional: a public one
/// would hand every outside caller a way to state a name was accepted, which is the same hole an
/// unfiltered write path is, reached from the other side.
/// </para>
/// </remarks>
public readonly record struct HeroNameDecision
{
    /// <summary>The only constructor. <c>internal</c>; the filter has already decided.</summary>
    /// <param name="name">The accepted name, or <see langword="null"/> when it was refused.</param>
    /// <param name="refusal">Which check fired, or <see langword="null"/> when it was accepted.</param>
    internal HeroNameDecision(HeroName? name, HeroNameRefusal? refusal)
    {
        Name = name;
        Refusal = refusal;
    }

    /// <summary>The accepted name, or <see langword="null"/> when it was refused.</summary>
    public HeroName? Name { get; }

    /// <summary>Which check fired, or <see langword="null"/> when it was accepted.</summary>
    public HeroNameRefusal? Refusal { get; }
}
