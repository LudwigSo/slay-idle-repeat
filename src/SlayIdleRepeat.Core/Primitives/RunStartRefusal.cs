namespace SlayIdleRepeat.Core.Primitives;

/// <summary>What a refused <c>START_RUN</c> means to the screen that asked for it.</summary>
/// <remarks>
/// Coarser than <see cref="RejectionReason"/> on purpose: a launch button has two sentences for a
/// refusal — "you cannot afford this yet" and "you have not opened this yet" — and which reasons
/// fall under which is a fact about the rules, not about the screen.
/// </remarks>
public enum RunStartRefusal
{
    /// <summary>The two Energy banks together could not cover a run's price.</summary>
    NotEnoughEnergy = 1,

    /// <summary>
    /// The chapter/tier ladder has not opened that stage for this player — either the clear it
    /// demands or the Legend Level it demands is missing.
    /// </summary>
    /// <remarks>
    /// One member for two reasons, and deliberately: the ladder names one requirement per rung and
    /// the domain can only report one of them, so a screen distinguishing them here would be
    /// promising a sentence it has no way to fill in. The chapter select screen lists both.
    /// </remarks>
    StageLocked = 2,

    /// <summary>
    /// A refusal a launch button has no sentence for — a run already open, a run already ended
    /// under it, a rule that is not about starting runs at all.
    /// </summary>
    /// <remarks>
    /// 🔒 Its own member rather than an arm folded into one of the two above: answering "not enough
    /// Energy" to a player who is simply already in a run is a wrong reason shown confidently, which
    /// is exactly what the refusal payload exists to prevent (steering S2).
    /// </remarks>
    NotAStartRefusal = 3,
}

/// <summary>Reads a <see cref="RejectionReason"/> as a <see cref="RunStartRefusal"/>.</summary>
/// <remarks>
/// 🔒 <b>Here, in <c>Core</c>, because this is a statement about the rules and not about a
/// screen.</b> Which reason a rule may answer when a run is refused is the domain's own catalogue,
/// and a caller above the domain that switched on those members itself would be deciding a refusal
/// rather than routing one — the layer above is forbidden from naming a domain-tier reason at all,
/// and <c>Application.Tests</c> scans its source to prove it.
/// </remarks>
public static class RunStartRefusals
{
    /// <summary>Which of the launch button's sentences <paramref name="reason"/> deserves.</summary>
    /// <param name="reason">The reason the domain refused a <c>START_RUN</c> with.</param>
    /// <returns>
    /// The classification. <see cref="RunStartRefusal.NotAStartRefusal"/> for every reason that is
    /// not one of the three <c>Handlers.StartRun</c> can answer with about affordability or the
    /// ladder — including every transport-tier reason, which is a refusal decided before the rules
    /// were ever consulted.
    /// </returns>
    public static RunStartRefusal Of(RejectionReason reason) => reason switch
    {
        RejectionReason.INSUFFICIENT_ENERGY => RunStartRefusal.NotEnoughEnergy,

        RejectionReason.PREREQUISITE_NOT_CLEARED or
        RejectionReason.LEGEND_LEVEL_TOO_LOW => RunStartRefusal.StageLocked,

        // Everything else, and a default rather than an exhaustive arm list: this is not a
        // classification OF the catalogue — it is a reading of the three reasons a run start can be
        // refused for, and a reason added for some other command has nothing to say here.
        _ => RunStartRefusal.NotAStartRefusal,
    };
}
