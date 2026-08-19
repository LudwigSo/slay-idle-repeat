using SlayIdleRepeat.Core.Model;

namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// The run-scoped holdings a build reads: the shrine buffs taken, the run buffs bought, the curses
/// carried, and the chapter their magnitudes are valued at.
/// </summary>
/// <remarks>
/// <para>
/// A reading of the run, not the run itself, for the reason <c>HeroBuild</c>'s explicit-item overload
/// exists at all: the balance harness, a hero-screen preview and a comparison all want to compose a
/// build without a <c>Run</c> to hand, and a parameter typed as the aggregate would shut all three
/// out. <see cref="None"/> is what those callers pass.
/// </para>
/// <para>
/// 🔒 <see cref="None"/> is an EMPTY holding rather than an absent one, on <c>HeroBuild.NoPerks</c>'s
/// precedent and for its reason: the three sources are composed unconditionally, so "this run has no
/// buffs" and "nobody wired the buffs in" cannot look alike from the fight's side. That is exactly
/// the shape the shrine and curse tiles were in before this type existed.
/// </para>
/// </remarks>
/// <param name="ShrineBuffs">The shrine buff ids taken this run, in the order taken. Duplicates stack.</param>
/// <param name="RunBuffs">The run buff ids bought this run, in the order bought. Duplicates stack.</param>
/// <param name="Curses">The curse ids active on this run. Never holds one twice.</param>
/// <param name="ChapterId">
/// The chapter the run is being played in. Read only by the run buffs, whose magnitudes are
/// <c>base × chapterGrowth^(c-1)</c>; the shrine pool and the curse table are chapter-invariant.
/// </param>
internal readonly record struct RunModifiers(
    IReadOnlyList<string> ShrineBuffs,
    IReadOnlyList<string> RunBuffs,
    IReadOnlyList<string> Curses,
    int ChapterId)
{
    /// <summary>The reading of a build that is not inside a run: no buffs, no curses, chapter 1.</summary>
    /// <remarks>
    /// Chapter 1 is the neutral value rather than an arbitrary one: it is the only chapter at which
    /// the run-buff curve's exponent is zero, so the chapter this carries can never scale a
    /// magnitude that is not there.
    /// </remarks>
    internal static RunModifiers None { get; } =
        new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), ChapterId: 1);

    /// <summary>What a run holds, or <see cref="None"/> outside one.</summary>
    internal static RunModifiers Of(Run? run) =>
        run is null
            ? None
            : new RunModifiers(run.ShrineBuffs, run.RunBuffs, run.Curses, run.ChapterId);
}
