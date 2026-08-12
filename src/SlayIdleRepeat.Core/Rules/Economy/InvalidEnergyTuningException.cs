using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Rules.Economy;

/// <summary>
/// Raised when an energy tunable exists and is authorised, but holds a value the `10` §3 / `28` C
/// energy model cannot work with — a Max Energy of zero, a cap below the base, a regeneration
/// interval that is not a positive span.
/// </summary>
/// <remarks>
/// <para>
/// The fourth failure mode of the content read, beside <see cref="MissingContentException"/>
/// (nothing there), <see cref="UnauthorisedTunableException"/> (a deliberate <c>null</c>) and
/// <see cref="ContentTypeMismatchException"/> (the wrong kind). Those three are answered in
/// <c>Content/</c> because they are true of any reader; this one is answered here because
/// "usable by the energy model" is the energy model's question and nothing else's.
/// </para>
/// <para>
/// It derives from <see cref="ContentException"/> deliberately: a composition root that catches
/// the content family at start-up to report a bad data set catches this too, and a nonsensical
/// energy block is exactly that kind of fault. `30` §2.1's "an illegal move is data, never an
/// exception" governs player commands — this is not one. It is a data defect, and `14` §6's whole
/// point is that a silent default in the read path produces a plausible, wrong economy that
/// nothing would ever flag.
/// </para>
/// <para>
/// 🔒 <c>internal</c>, like everything else under <c>Core/Rules/</c> (`30` §11.2). Callers outside
/// <c>Core</c> catch it as a <see cref="ContentException"/>, which is the level they can act on.
/// </para>
/// </remarks>
internal sealed class InvalidEnergyTuningException : ContentException
{
    /// <summary>Creates the exception for the reference that holds the unusable value.</summary>
    /// <param name="reference">The content reference, e.g. <c>tuning/progression.json#/energy/baseMax</c>.</param>
    /// <param name="detail">What is wrong with it, and which document authorises what instead.</param>
    internal InvalidEnergyTuningException(string reference, string detail)
        : base($"Content reference '{reference}' is authorised but unusable by the energy model: {detail}")
    {
        Reference = reference;
    }

    /// <summary>The reference whose value the energy model cannot work with.</summary>
    internal string Reference { get; }
}
