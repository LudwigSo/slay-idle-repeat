using SlayIdleRepeat.Application.Wire;

namespace SlayIdleRepeat.Client.Game.Net;

/// <summary>
/// The device credential this process is holding, kept in memory and persisted nowhere.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Not written to <c>user://</c>, not written to any settings file, never rendered.</b> The
/// device secret is the anonymous account's root credential and the only sanctioned home for it is
/// the platform keystore, which this repository does not have yet. Inventing a store here would be
/// filling that hole with a plausible value.
/// </para>
/// <para>
/// 🔴 <b>Consequence, stated rather than hidden:</b> every cold start mints a NEW anonymous account,
/// so nothing done through the wire survives a restart. That is why the arm this belongs to is a
/// developer arm and not a shipped one.
/// </para>
/// <para>
/// No interface, deliberately: a seam with one implementation is a seam nothing checks.
/// </para>
/// </remarks>
public sealed class EphemeralDeviceCredentials
{
    /// <summary>Whether a credential has been minted in this process.</summary>
    public bool IsHeld => throw new NotImplementedException();

    /// <summary>The credential held, or <c>null</c> before one has been minted.</summary>
    public WireCredentials? Held => throw new NotImplementedException();

    /// <summary>Takes the credential a fresh registration issued.</summary>
    /// <param name="registration">What the device route answered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registration"/> is null.</exception>
    public void Adopt(WireDeviceRegistration registration) => throw new NotImplementedException();

    /// <summary>Says that this holds a secret rather than printing it.</summary>
    public override string ToString() => throw new NotImplementedException();
}
