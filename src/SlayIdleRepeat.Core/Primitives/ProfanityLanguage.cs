namespace SlayIdleRepeat.Core.Primitives;

/// <summary>The languages a name is filtered in.</summary>
/// <remarks>
/// <para>
/// Two members, and both are authored rather than chosen: `27` §1 is the design set's only profanity
/// specification and it names exactly <em>"EN and DE"</em>, which is also exactly what
/// <c>game-data/loc/</c> holds. A third member is a localisation decision with a word list behind it,
/// not an enum edit.
/// </para>
/// <para>
/// Numbered from 1 with no <c>0</c> member, like every other closed vocabulary here: a refusal
/// carries the language it fired in, so <c>default(ProfanityLanguage)</c> must not read as
/// <see cref="EN"/> and make a German match report itself as an English one.
/// </para>
/// </remarks>
public enum ProfanityLanguage
{
    /// <summary>English. <c>content/profanity/en.json</c>.</summary>
    EN = 1,

    /// <summary>German. <c>content/profanity/de.json</c>.</summary>
    DE = 2,
}
